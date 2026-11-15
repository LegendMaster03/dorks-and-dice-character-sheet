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
            (!command.IsTransition && command.NextLedgerRevision != command.ExpectedLedgerRevision))
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
            command.NextLedgerRevision, command.IsTransition ? "transition" : "acquisition",
            command.AuthorizationEvidenceId, command.RecordedAt);
        ValidateLedger(command);

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
                command.NextLedgerRevision, command.IsTransition ? "transition" : "acquisition",
                command.AuthorizationEvidenceId, command.RecordedAt));

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
        var origin = await db.AuthorizedOrigins.AsNoTracking()
            .Where(x => x.CharacterId == id)
            .Select(x => new { x.RulesetId, x.Fingerprint, x.ProfileJson,
                x.EvaluatedProjectionJson, x.Revision }).SingleOrDefaultAsync(ct);
        var originEvents = await db.OriginEventReceipts.AsNoTracking()
            .Where(x => x.CharacterId == id).OrderBy(x => x.CommittedRevision)
            .Select(x => new { x.EventId, x.CommittedRevision, x.AuthorizationKind,
                x.AuthorizationEvidenceId, x.ApprovedDecisionsJson }).ToArrayAsync(ct);

        var canonical = JsonSerializer.Serialize(new {
            root, classes, selections, abilities, rules, conditions, hpGains, items, origin, originEvents
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
