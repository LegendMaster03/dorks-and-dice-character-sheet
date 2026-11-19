using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL serialization of complete, previously authorized supernatural
/// state. The root character lock, input fingerprint check, ledger CAS and unique
/// event receipt are a single transaction. No client-facing write exists.
/// </summary>
public sealed class PostgresCharacterSupernaturalStateStore(CharacterSheetDbContext db)
    : ICharacterSupernaturalStateStore
{
    public Task<CharacterSupernaturalState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default) =>
        db.SupernaturalStates.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);

    public async Task<IReadOnlyList<CharacterSupernaturalEventReceipt>> GetEventsAsync(
        Guid characterId, CancellationToken cancellationToken = default) =>
        await db.SupernaturalEventReceipts.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .OrderBy(x => x.CommittedRevision).ToArrayAsync(cancellationToken);

    public Task<string?> GetAuthoritativeInputsFingerprintAsync(
        Guid characterId, CancellationToken cancellationToken = default) =>
        FingerprintAsync(characterId, cancellationToken);

    public async Task<CharacterSupernaturalWriteResult> CommitVerifiedAsync(
        VerifiedSupernaturalStateWrite command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CharacterId == Guid.Empty || command.ExpectedRevision < 0 ||
            command.ExpectedRevision == long.MaxValue || command.ExpectedLedgerRevision < 0 ||
            command.NextLedgerRevision < 0 ||
            (command.IsTransition && (command.ExpectedLedgerRevision == long.MaxValue ||
                command.NextLedgerRevision != command.ExpectedLedgerRevision + 1)) ||
            (!command.IsTransition && command.NextLedgerRevision != command.ExpectedLedgerRevision) ||
            (command.IsExpressionChange && (command.IsTransition || command.ExpectedRevision == 0)) ||
            (command.IsProgression && (command.IsTransition || command.IsExpressionChange ||
                command.ExpectedRevision == 0)))
            throw new ArgumentException("Invalid supernatural write revision.", nameof(command));

        // Shape checks never confer entitlement. The application must have
        // independently verified the exact content and all authority inputs.
        var next = new CharacterSupernaturalState(command.CharacterId,
            command.RulesetId, command.Fingerprint, command.AlternateSheetsJson,
            command.PersistentInfluencesJson, command.LedgerJson,
            command.NextLedgerRevision, command.ExpectedDependencyFingerprint,
            command.RecordedAt);
        _ = new CharacterSupernaturalEventReceipt(command.CharacterId,
            command.EventId, command.ExpectedRevision + 1,
            command.NextLedgerRevision, ReceiptKind(command),
            command.AuthorizationEvidenceId, command.RecordedAt,
            command.TransitionRequestJson, command.ExpressionChangeJson);
        ValidateLedger(command);
        ValidateTransitionAudit(command);
        ValidateExpressionChangeAudit(command);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            // All supernatural commit paths take the canonical character lock
            // first, matching the established authorized-Origin lock ordering.
            var root = await db.CharacterSheets.FromSqlInterpolated(
                $"""SELECT * FROM character_sheet_roots WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (root is null)
                return new(CharacterSupernaturalWriteStatus.SheetNotInitialized);

            var fingerprint = await FingerprintAsync(command.CharacterId, cancellationToken);
            if (fingerprint != command.ExpectedDependencyFingerprint.ToLowerInvariant())
                return new(CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged);

            var existing = await db.SupernaturalStates.FromSqlInterpolated(
                $"""SELECT * FROM character_supernatural_states WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

            if (await db.SupernaturalEventReceipts.AsNoTracking().AnyAsync(
                x => x.CharacterId == command.CharacterId && x.EventId == command.EventId,
                cancellationToken))
                return new(CharacterSupernaturalWriteStatus.DuplicateEvent,
                    existing?.Revision, existing?.LedgerRevision);

            if ((existing?.Revision ?? 0) != command.ExpectedRevision)
                return new(CharacterSupernaturalWriteStatus.RevisionConflict,
                    existing?.Revision, existing?.LedgerRevision);

            if ((existing?.LedgerRevision ?? 0) != command.ExpectedLedgerRevision)
                return new(CharacterSupernaturalWriteStatus.LedgerRevisionConflict,
                    existing?.Revision, existing?.LedgerRevision);

            // Even a matching ledger revision does not prove that the stored
            // processed-event history matches the immutable database receipts.
            // Validate both source-of-truth records under the character lock
            // before permitting another acquisition or form transition.
            var previousReceipts = await db.SupernaturalEventReceipts.AsNoTracking()
                .Where(x => x.CharacterId == command.CharacterId)
                .OrderBy(x => x.CommittedRevision)
                .ToArrayAsync(cancellationToken);
            ValidateCommittedReceiptHistory(existing, previousReceipts);

            // Compare the proposed snapshot to what is actually persisted under
            // the same lock. Revision checks alone can not detect rewritten
            // acquisition history or dropped manifestation event identities.
            CharacterSupernaturalHistoryGuard.Validate(existing, command);

            if (existing is null)
            {
                db.SupernaturalStates.Add(next);
            }
            else
            {
                var updated = await db.SupernaturalStates
                    .Where(x => x.CharacterId == command.CharacterId &&
                        x.Revision == command.ExpectedRevision &&
                        x.LedgerRevision == command.ExpectedLedgerRevision)
                    .ExecuteUpdateAsync(change => change
                        .SetProperty(x => x.RulesetId, next.RulesetId)
                        .SetProperty(x => x.Fingerprint, next.Fingerprint)
                        .SetProperty(x => x.AlternateSheetsJson, next.AlternateSheetsJson)
                        .SetProperty(x => x.PersistentInfluencesJson, next.PersistentInfluencesJson)
                        .SetProperty(x => x.LedgerJson, next.LedgerJson)
                        .SetProperty(x => x.LedgerRevision, next.LedgerRevision)
                        .SetProperty(x => x.DependencyFingerprint, next.DependencyFingerprint)
                        .SetProperty(x => x.Revision, command.ExpectedRevision + 1)
                        .SetProperty(x => x.UpdatedAt, command.RecordedAt),
                        cancellationToken);
                if (updated != 1)
                    return new(CharacterSupernaturalWriteStatus.RevisionConflict);
            }

            db.SupernaturalEventReceipts.Add(new CharacterSupernaturalEventReceipt(
                command.CharacterId, command.EventId, command.ExpectedRevision + 1,
                command.NextLedgerRevision, ReceiptKind(command),
                command.AuthorizationEvidenceId, command.RecordedAt,
                command.TransitionRequestJson, command.ExpressionChangeJson));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(CharacterSupernaturalWriteStatus.Applied,
                command.ExpectedRevision + 1, command.NextLedgerRevision);
        }
        catch (PostgresException error) when (error.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
        {
            db.ChangeTracker.Clear();
            return new(CharacterSupernaturalWriteStatus.RevisionConflict);
        }
        catch (InvalidOperationException error) when (
            error.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
        {
            // EF's Npgsql execution strategy wraps a concurrent serialization
            // failure during FOR UPDATE before this method reaches SaveChanges.
            // The losing request must receive a stable conflict, not HTTP 500.
            db.ChangeTracker.Clear();
            return new(CharacterSupernaturalWriteStatus.RevisionConflict);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation)
        {
            db.ChangeTracker.Clear();
            return new(CharacterSupernaturalWriteStatus.RevisionConflict);
        }
    }


    /// <summary>
    /// Ensures that immutable event receipts are an exact history of the
    /// previously committed ledger, including acquisition-only revisions.
    /// This is deliberately limited to structural continuity. It does not
    /// turn a receipt or a stored evidence string into an acquisition or DM
    /// entitlement, which still requires an independent source verifier.
    /// </summary>
    private static void ValidateCommittedReceiptHistory(
        CharacterSupernaturalState? existing,
        IReadOnlyList<CharacterSupernaturalEventReceipt> receipts)
    {
        if (existing is null)
        {
            if (receipts.Count != 0)
                throw new ArgumentException(
                    "A missing supernatural owner has orphaned event receipts.");
            return;
        }

        if (existing.Revision != receipts.Count)
            throw new ArgumentException(
                "Supernatural state revisions do not match immutable event receipts.");

        var transitionEvents = new List<string>();
        // Expression audits are independent per influence; acquisition receipts
        // predate expression snapshots and cannot supply a fabricated baseline.
        var expressionHistory = new Dictionary<string, ExpressionAudit>(StringComparer.Ordinal);
        long ledgerRevision = 0;
        for (var index = 0; index < receipts.Count; index++)
        {
            var receipt = receipts[index];
            if (receipt.CharacterId != existing.CharacterId ||
                receipt.CommittedRevision != index + 1)
                throw new ArgumentException(
                    "Supernatural event receipt revisions are noncontiguous.");

            switch (receipt.Kind)
            {
                case "transition":
                    if (receipt.ExpressionChangeJson is not null)
                        throw new ArgumentException("A transition receipt cannot contain an influence expression audit.");
                    if (ledgerRevision == long.MaxValue)
                        throw new ArgumentException(
                            "Supernatural ledger revision overflow.");
                    ledgerRevision++;
                    transitionEvents.Add(receipt.EventId);
                    break;
                case "expression-change":
                    if (receipt.TransitionRequestJson is not null ||
                        receipt.ExpressionChangeJson is null)
                        throw new ArgumentException("An expression change requires its immutable audit.");
                    var audited = ParseExpressionAudit(receipt.ExpressionChangeJson,
                        receipt.CharacterId, receipt.EventId);
                    if (expressionHistory.TryGetValue(audited.InfluenceId, out var prior) &&
                        (!JsonElement.DeepEquals(prior.NextExpression, audited.PreviousExpression) ||
                         prior.NextEvidenceId != audited.PreviousEvidenceId))
                        throw new ArgumentException(
                            "Historical influence expression or evidence chain is discontinuous.");
                    expressionHistory[audited.InfluenceId] = audited;
                    break;
                case "acquisition":
                case "progression":
                    if (receipt.ExpressionChangeJson is not null)
                        throw new ArgumentException("An acquisition cannot contain an expression-change audit.");
                    if (receipt.TransitionRequestJson is not null)
                        throw new ArgumentException(
                            "An acquisition receipt can not contain a transition step audit.");
                    break;
                default:
                    throw new ArgumentException(
                        "The supernatural receipt has an unknown event kind.");
            }

            if (receipt.LedgerRevision != ledgerRevision)
                throw new ArgumentException(
                    "Supernatural event receipt ledger revisions are not sequential.");
        }

        if (ledgerRevision != existing.LedgerRevision)
            throw new ArgumentException(
                "Supernatural ledger revision conflicts with committed event history.");

        // Acquisitions can append earned progression, but cannot rewrite the
        // last expression. The current snapshot must agree with the most
        // recent expression receipt for each affected influence.
        if (expressionHistory.Count != 0)
        {
            using var influenceDocument = JsonDocument.Parse(existing.PersistentInfluencesJson);
            var current = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var influence in influenceDocument.RootElement.EnumerateArray())
            {
                if (influence.ValueKind != JsonValueKind.Object ||
                    !influence.TryGetProperty("id", out var id) ||
                    id.ValueKind != JsonValueKind.String ||
                    !current.TryAdd(id.GetString()!, influence))
                    throw new ArgumentException("Malformed current influence identity.");
            }

            foreach (var (id, last) in expressionHistory)
            {
                if (!current.TryGetValue(id, out var influence) ||
                    !influence.TryGetProperty("expression", out var expression) ||
                    !influence.TryGetProperty("expressionEvidenceId", out var evidence) ||
                    evidence.ValueKind != JsonValueKind.String ||
                    !JsonElement.DeepEquals(expression, last.NextExpression) ||
                    evidence.GetString() != last.NextEvidenceId)
                    throw new ArgumentException(
                        "Latest historical expression receipt does not match current influence state.");
            }
        }

        using var document = JsonDocument.Parse(existing.LedgerJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("characterId", out var owner) ||
            owner.ValueKind != JsonValueKind.String ||
            owner.GetString() != existing.CharacterId.ToString("D") ||
            !root.TryGetProperty("revision", out var storedRevision) ||
            storedRevision.ValueKind != JsonValueKind.Number ||
            !storedRevision.TryGetInt64(out var revision) ||
            revision != ledgerRevision ||
            !root.TryGetProperty("processedEventIds", out var journal) ||
            journal.ValueKind != JsonValueKind.Array ||
            !journal.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String
                ? x.GetString() : null).SequenceEqual(transitionEvents, StringComparer.Ordinal))
            throw new ArgumentException(
                "The supernatural ledger does not match its immutable transition receipts.");
    }

    /// <summary>
    /// Validate the complete source-verified transition request supplied by the
    /// application after successful Core planning. This is durable event-step
    /// auditing, not a second implementation of native transition legality.
    /// Historical receipts stay nullable because their steps can not be
    /// reconstructed from an event ID or a final ledger alone.
    /// </summary>
    private static string ReceiptKind(VerifiedSupernaturalStateWrite command) =>
        command.IsTransition ? "transition" :
        command.IsExpressionChange ? "expression-change" :
        command.IsProgression ? "progression" : "acquisition";

    /// <summary>
    /// Validate exact event scope before entering the database transaction.
    /// The history guard compares these values to the locked prior and new
    /// snapshots. Neither layer substitutes for independent source authority.
    /// </summary>
    /// <summary>
    /// The optional intent is recorded by the coordinator for replay-safe
    /// acknowledgement. Older internal receipts without intent remain valid
    /// history but cannot prove a retry with a particular intent.
    /// </summary>
    private sealed record ExpressionAudit(
        string InfluenceId, string? IntentId,
        string PreviousEvidenceId, string NextEvidenceId,
        JsonElement PreviousExpression, JsonElement NextExpression);

    private static ExpressionAudit ParseExpressionAudit(
        string json, Guid characterId, string eventId)
    {
        using var document = JsonDocument.Parse(CharacterSupernaturalState.CheckJson(
            json, JsonValueKind.Object, "expressionChange"));
        var root = document.RootElement;
        RejectDuplicateJsonKeys(root);
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "characterId", "eventId", "influenceId", "intentId",
            "previousExpressionEvidenceId", "nextExpressionEvidenceId",
            "previousExpression", "nextExpression"
        };
        var fields = root.EnumerateObject().ToArray();
        if (fields.Length is < 7 or > 8 ||
            fields.Any(field => !expected.Contains(field.Name)) ||
            !root.TryGetProperty("characterId", out var owner) ||
            owner.ValueKind != JsonValueKind.String ||
            owner.GetString() != characterId.ToString("D") ||
            !root.TryGetProperty("eventId", out var recordedEvent) ||
            recordedEvent.ValueKind != JsonValueKind.String ||
            recordedEvent.GetString() != eventId ||
            !root.TryGetProperty("influenceId", out var target) ||
            target.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(target.GetString()) ||
            target.GetString()!.Length > 160 ||
            !root.TryGetProperty("previousExpressionEvidenceId", out var beforeEvidence) ||
            beforeEvidence.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(beforeEvidence.GetString()) ||
            beforeEvidence.GetString()!.Length > 320 ||
            !root.TryGetProperty("nextExpressionEvidenceId", out var afterEvidence) ||
            afterEvidence.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(afterEvidence.GetString()) ||
            afterEvidence.GetString()!.Length > 320 ||
            beforeEvidence.GetString() == afterEvidence.GetString() ||
            !root.TryGetProperty("previousExpression", out var before) ||
            !root.TryGetProperty("nextExpression", out var after))
            throw new ArgumentException("Malformed historical influence expression audit.");

        string? intentId = null;
        if (root.TryGetProperty("intentId", out var intent))
        {
            if (intent.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(intent.GetString()) ||
                intent.GetString()!.Length > 160)
                throw new ArgumentException("Invalid expression intent identity.");
            intentId = intent.GetString();
        }
        else if (fields.Length != 7)
            throw new ArgumentException("An expression audit has an ambiguous identity.");

        CharacterSupernaturalHistoryGuard.ValidateExpressionDimensions(before);
        CharacterSupernaturalHistoryGuard.ValidateExpressionDimensions(after);
        if (JsonElement.DeepEquals(before, after))
            throw new ArgumentException("An expression audit cannot record an unchanged expression.");

        return new ExpressionAudit(target.GetString()!, intentId,
            beforeEvidence.GetString()!, afterEvidence.GetString()!,
            before.Clone(), after.Clone());
    }

    private static void ValidateExpressionChangeAudit(VerifiedSupernaturalStateWrite command)
    {
        if (!command.IsExpressionChange)
        {
            if (command.ExpressionChangeJson is not null)
                throw new ArgumentException("Only an expression-change event may carry an expression audit.");
            return;
        }
        if (command.TransitionRequestJson is not null ||
            command.ExpressionChangeJson is null)
            throw new ArgumentException("An influence expression change requires a separate immutable audit.");

        _ = ParseExpressionAudit(command.ExpressionChangeJson,
            command.CharacterId, command.EventId);
    }

    private static void ValidateTransitionAudit(VerifiedSupernaturalStateWrite command)
    {
        if (!command.IsTransition)
        {
            if (command.TransitionRequestJson is not null)
                throw new ArgumentException(
                    "An acquisition can not masquerade as a manifestation transition.");
            return;
        }

        if (command.TransitionRequestJson is null)
            throw new ArgumentException(
                "A new manifestation transition requires its verified ordered step audit.");
        var json = CharacterSupernaturalState.CheckJson(command.TransitionRequestJson,
            JsonValueKind.Object, "transitionRequest");
        using var document = JsonDocument.Parse(json);
        var request = document.RootElement;
        RejectDuplicateJsonKeys(request);
        if (!request.TryGetProperty("characterId", out var character) ||
            character.ValueKind != JsonValueKind.String ||
            character.GetString() != command.CharacterId.ToString("D") ||
            !request.TryGetProperty("expectedRevision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetInt64(out var oldRevision) ||
            oldRevision != command.ExpectedLedgerRevision ||
            !request.TryGetProperty("eventId", out var eventId) ||
            eventId.ValueKind != JsonValueKind.String ||
            eventId.GetString() != command.EventId ||
            !request.TryGetProperty("steps", out var steps) ||
            steps.ValueKind != JsonValueKind.Array ||
            steps.GetArrayLength() is < 1 or > 512)
            throw new ArgumentException(
                "A committed manifestation audit must match its character, event and prior ledger revision.");

        foreach (var step in steps.EnumerateArray())
        {
            if (step.ValueKind != JsonValueKind.Object ||
                !step.TryGetProperty("action", out var action) ||
                !KnownAction(action) ||
                !step.TryGetProperty("targetInstanceId", out var target) ||
                target.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(target.GetString()) ||
                !step.TryGetProperty("ruleOccurrence", out var rule) ||
                rule.ValueKind != JsonValueKind.Object ||
                !rule.TryGetProperty("occurrencePath", out var path) ||
                path.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(path.GetString()) ||
                !rule.TryGetProperty("source", out var source) ||
                source.ValueKind != JsonValueKind.Object ||
                !source.TryGetProperty("id", out var sourceId) ||
                sourceId.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(sourceId.GetString()) ||
                !source.TryGetProperty("kind", out var sourceKind) ||
                sourceKind.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) ||
                !step.TryGetProperty("basis", out var basis) ||
                basis.ValueKind != JsonValueKind.Object ||
                !basis.TryGetProperty("adjudication", out var adjudication) ||
                adjudication.ValueKind is not (JsonValueKind.Number or JsonValueKind.String) ||
                !step.TryGetProperty("authorizationEvidenceId", out var evidence) ||
                evidence.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(evidence.GetString()))
                throw new ArgumentException(
                    "A committed manifestation audit must retain every authorized step.");
        }
    }

    private static void RejectDuplicateJsonKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateJsonKeys(item);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new ArgumentException(
                        "The verified transition audit contains ambiguous duplicate JSON keys.");
                RejectDuplicateJsonKeys(property.Value);
            }
        }
    }

    private static bool KnownAction(JsonElement action) =>
        action.ValueKind == JsonValueKind.Number &&
            action.TryGetInt32(out var number) && number is >= 0 and <= 3 ||
        action.ValueKind == JsonValueKind.String &&
            action.GetString() is "Activate" or "End" or "Suppress" or "Restore";

    private static void ValidateLedger(VerifiedSupernaturalStateWrite command)
    {
        using var ledger = JsonDocument.Parse(command.LedgerJson);
        var root = ledger.RootElement;
        if (!root.TryGetProperty("characterId", out var character) ||
            character.ValueKind != JsonValueKind.String ||
            character.GetString() != command.CharacterId.ToString("D") ||
            !root.TryGetProperty("revision", out var revision) ||
            revision.ValueKind != JsonValueKind.Number ||
            !revision.TryGetInt64(out var number) || number != command.NextLedgerRevision ||
            !root.TryGetProperty("instances", out var instances) ||
            instances.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("resources", out var resources) ||
            resources.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("processedEventIds", out var events) ||
            events.ValueKind != JsonValueKind.Array ||
            (command.IsTransition && !events.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String &&
                value.GetString() == command.EventId)))
            throw new ArgumentException("The proposed manifestation ledger does not match its verified event.");
        // ID collisions must not survive a persisted snapshot. Core performs
        // the deeper graph, provenance and exact authority checks.
        UniqueIds(instances, "manifestation instances");
        UniqueIds(resources, "manifestation resources");
        using var sheets = JsonDocument.Parse(command.AlternateSheetsJson);
        using var influences = JsonDocument.Parse(command.PersistentInfluencesJson);
        UniqueIds(sheets.RootElement, "alternate sheets");
        UniqueIds(influences.RootElement, "persistent influences");
    }

    private static void UniqueIds(JsonElement values, string field)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !found.Add(id.GetString()!))
                throw new ArgumentException($"Duplicate or invalid {field} identity.");
        }
    }

    /// <summary>
    /// Hash actual authoritative DB rows rather than the manifestation ledger.
    /// Recomputed inside SERIALIZABLE commit to reject independently changed
    /// Class, Origin, equipment, HP, condition, rules input and related facts.
    /// Callers must bracket external Rules Core planning with two equal reads.
    /// </summary>
    private async Task<string?> FingerprintAsync(Guid id, CancellationToken ct)
    {
        var root = await db.CharacterSheets.AsNoTracking()
            .Where(x => x.CharacterId == id)
            .Select(x => new { x.SchemaVersion, x.CurrentHitPoints, x.AdvancementProgress,
                x.DeathSaveSuccesses, x.DeathSaveFailures })
            .SingleOrDefaultAsync(ct);
        if (root is null) return null;

        var classes = await db.CharacterAdvancementEntries.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Kind, x.RuleConceptKey, x.Ordinal,
                x.ParentAdvancementEntryId, x.Level, x.UpdatedAt }).ToArrayAsync(ct);
        var selections = await db.FoundationalRuleSelections.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Category, x.RuleConceptKey }).ToArrayAsync(ct);
        var abilities = await db.BaseAbilityScoreInputs.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.AbilityKey, x.Score, x.UpdatedAt }).ToArrayAsync(ct);
        var rules = await db.CharacterRulesInputs.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Kind, x.Key, x.IntegerValue, x.BooleanValue,
                x.TextValue, x.UpdatedAt }).ToArrayAsync(ct);
        var conditions = await db.CharacterConditions.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RuleConceptKey, x.Level, x.CounterCurrent,
                x.CounterMaximum, x.Duration, x.UpdatedAt }).ToArrayAsync(ct);
        var hpGains = await db.CharacterHitPointGains.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.AdvancementOccurrenceId, x.ClassLevel,
                x.HitDieValue, x.UpdatedAt }).ToArrayAsync(ct);
        var items = await db.InventoryItemOccurrences.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RuleConceptKey, x.CustomName, x.Quantity,
                x.IsCarried, x.IsEquipped, x.IsAttuned, x.ContainerOccurrenceId,
                x.UpdatedAt }).ToArrayAsync(ct);
        var currency = await db.CharacterCurrencyBalances.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.CurrencyKey, x.Amount, x.UpdatedAt })
            .ToArrayAsync(ct);
        var origin = await db.AuthorizedOrigins.AsNoTracking()
            .Where(x => x.CharacterId == id)
            .Select(x => new { x.RulesetId, x.Fingerprint, x.ProfileJson,
                x.EvaluatedProjectionJson, x.SourceDraftRevision,
                x.Revision }).SingleOrDefaultAsync(ct);
        var originEvents = await db.OriginEventReceipts.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.CommittedRevision)
            // Every field identifying the authorized method, DM scope or
            // independently recorded decision must participate in the
            // primary dependency digest. An unchanged Origin row revision
            // does not protect against tampering with a separate receipt.
            .Select(x => new { x.EventId, x.CommittedRevision,
                x.MethodRuleId, x.MethodId, x.AuthorizationKind,
                x.AuthorizationEvidenceId, x.ApprovalActorUserId,
                x.ApprovalCampaignId, x.ApprovedDecisionsJson,
                x.CreatedAt }).ToArrayAsync(ct);

        var canonical = JsonSerializer.Serialize(new {
            root, classes, selections, abilities, rules, conditions, hpGains, items, currency,
            origin, originEvents
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
