using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterInfluenceExpressionChangeStatus
{
    Applied, InvalidRequest, Unauthenticated, NotFoundOrNotOwned,
    ProjectionUnavailable, ArchivedReadOnly, NotInitialized,
    StateRevisionConflict, LedgerRevisionConflict, SourceUnauthorized,
    NativeContentChanged, RulesCoreUnavailable, AuthoritativeInputsChanged, DuplicateEvent,
    PreviouslyCommitted, EventIdentityConflict
}

/// <summary>
/// Outcome of the optional, post-commit mechanics read. A durable receipt is
/// independent of this projection and can be acknowledged without it.
/// </summary>
public enum CharacterInfluenceProjectionOutcome
{
    NotRequested, Available, Unresolved, Unavailable, Failed, Canceled
}

public sealed record CharacterInfluenceExpressionChangeResult(
    CharacterInfluenceExpressionChangeStatus Status,
    long? StateRevision = null, long? LedgerRevision = null,
    string? EffectiveStatus = null, JsonNode? EffectiveProjection = null,
    string? EventId = null,
    CharacterInfluenceProjectionOutcome ProjectionOutcome =
        CharacterInfluenceProjectionOutcome.NotRequested);

/// <summary>
/// Output of an independently trusted source policy after it has checked
/// the exact acquired influence, current expression, source event and actor
/// eligibility. Receipt strings supplied by a caller are never authority.
/// </summary>
public sealed record TrustedInfluenceExpressionChange(
    IReadOnlyDictionary<string, string> NextExpression,
    string NextExpressionEvidenceId,
    string AuthorizationEvidenceId);

/// <summary>
/// An authorized backend implementation must independently check source/DM
/// evidence and expression eligibility. No production implementation or
/// browser endpoint is registered until those authored rules exist.
/// </summary>
public interface ITrustedCharacterInfluenceExpressionSource
{
    Task<TrustedInfluenceExpressionChange?> PrepareAsync(
        Guid characterId, SiteCharacterProjection authorizedCharacter,
        CharacterSupernaturalState committed, string influenceId,
        string intentId, string eventId, CancellationToken cancellationToken);
}

/// <summary>
/// Internal-only, revisioned expression change. Retains the acquired influence
/// and its independently earned progression while changing only source-verified
/// expression and evidence. A fresh Rules Core effective-state read is requested
/// AFTER commit; an unavailable verifier yields no playable projection.
/// </summary>
public sealed class CharacterInfluenceExpressionChangeService(
    ISiteCharacterAccessGateway site,
    ISiteFreshCharacterAccessGateway freshSite,
    ICharacterSupernaturalStateStore store,
    ITrustedCharacterInfluenceExpressionSource source,
    INativeRulesCoreGateway rules,
    CharacterEffectiveStateService effective)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterInfluenceExpressionChangeResult> ApplyAsync(
        Guid characterId, long expectedStateRevision, long expectedLedgerRevision,
        string influenceId, string intentId, string eventId,
        CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || expectedStateRevision < 1 ||
            expectedStateRevision == long.MaxValue || expectedLedgerRevision < 0 ||
            string.IsNullOrWhiteSpace(influenceId) || influenceId.Length > 160 ||
            string.IsNullOrWhiteSpace(intentId) || intentId.Length > 160 ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 160)
            return new(CharacterInfluenceExpressionChangeStatus.InvalidRequest);

        // The request-scoped Tool Host ticket is not a live Site ownership
        // check. Read Site authority before inspecting the private state.
        var firstLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (firstLive.Status != SiteCharacterAccessStatus.Authorized ||
            firstLive.Character?.CharacterId != characterId)
            return new(FromSite(firstLive.Status));
        if (!firstLive.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterInfluenceExpressionChangeStatus.ArchivedReadOnly);

        var initial = await site.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (initial.Status != SiteCharacterAccessStatus.Authorized ||
            initial.Character?.CharacterId != characterId ||
            !SameSiteScope(firstLive.Character, initial.Character))
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);

        var saved = await store.GetAsync(characterId, cancellationToken);
        if (saved is null)
            return new(CharacterInfluenceExpressionChangeStatus.NotInitialized);
        if (saved.ContractVersion != CharacterSupernaturalState.CurrentContractVersion)
            return new(CharacterInfluenceExpressionChangeStatus.NativeContentChanged);
        if (saved.Revision != expectedStateRevision ||
            saved.LedgerRevision != expectedLedgerRevision)
        {
            // A response may be lost after PostgreSQL committed. Recover only
            // from a durable receipt matching this exact original operation,
            // with independently checked current Site access. Do not ask the
            // source to decide again or repeat the write.
            var previouslyCommitted = await RecoverCommittedAsync(characterId,
                expectedStateRevision, expectedLedgerRevision, influenceId,
                intentId, eventId, cancellationToken);
            if (previouslyCommitted is not null)
                return previouslyCommitted;
            return saved.Revision != expectedStateRevision
                ? new(CharacterInfluenceExpressionChangeStatus.StateRevisionConflict,
                    saved.Revision, saved.LedgerRevision)
                : new(CharacterInfluenceExpressionChangeStatus.LedgerRevisionConflict,
                    saved.Revision, saved.LedgerRevision);
        }

        var before = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (before is null)
            return new(CharacterInfluenceExpressionChangeStatus.NotInitialized);

        JsonArray influences;
        JsonObject original;
        string previousEvidence;
        JsonNode previousExpression;
        try
        {
            using var document = JsonDocument.Parse(saved.PersistentInfluencesJson);
            if (!HasUnambiguousInfluenceKeys(document.RootElement))
                return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);

            influences = JsonNode.Parse(saved.PersistentInfluencesJson) as JsonArray
                ?? throw new JsonException("Influence ownership must be an array.");
            if (influences.Count > 512 ||
                influences.Any(x => x is not JsonObject ||
                    x["id"] is not JsonValue))
                return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
            var matching = influences.OfType<JsonObject>().Where(x =>
                x["id"]?.GetValue<string>() == influenceId).ToArray();
            if (matching.Length != 1 ||
                influences.OfType<JsonObject>().Select(x => x["id"]!.GetValue<string>())
                    .Distinct(StringComparer.Ordinal).Count() != influences.Count)
                return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);

            original = matching[0];
            previousEvidence = original["expressionEvidenceId"]?.GetValue<string>()
                ?? throw new JsonException("Missing expression authority.");
            previousExpression = original["expression"]?.DeepClone()
                ?? throw new JsonException("Missing expression.");
            if (previousExpression is not JsonObject ||
                string.IsNullOrWhiteSpace(previousEvidence))
                return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
        }

        TrustedInfluenceExpressionChange? verified;
        try
        {
            verified = await source.PrepareAsync(characterId, initial.Character,
                saved, influenceId, intentId, eventId, cancellationToken);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
        }

        if (verified?.NextExpression is null ||
            verified.NextExpression.Count > 128 ||
            verified.NextExpression.Any(x => string.IsNullOrWhiteSpace(x.Key) ||
                x.Key.Length > 160 || string.IsNullOrWhiteSpace(x.Value) ||
                x.Value.Length > 1024) ||
            string.IsNullOrWhiteSpace(verified.NextExpressionEvidenceId) ||
            verified.NextExpressionEvidenceId.Length > 320 ||
            verified.NextExpressionEvidenceId == previousEvidence ||
            string.IsNullOrWhiteSpace(verified.AuthorizationEvidenceId) ||
            verified.AuthorizationEvidenceId.Length > 320)
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);

        JsonObject audit;
        try
        {
            // Web serializer settings have case-insensitive property binding.
            // Expression dimensions are source-defined ordinal keys; build an
            // explicitly ordinal JSON object instead of using those settings.
            var nextExpression = new JsonObject(new JsonNodeOptions
            {
                PropertyNameCaseInsensitive = false
            });
            foreach (var (dimension, value) in verified.NextExpression)
                nextExpression.Add(dimension, value);
            if (JsonNode.DeepEquals(nextExpression, previousExpression))
                return new(CharacterInfluenceExpressionChangeStatus.InvalidRequest);

            original["expression"] = nextExpression.DeepClone();
            original["expressionEvidenceId"] = verified.NextExpressionEvidenceId;
            audit = new JsonObject
            {
                ["characterId"] = characterId.ToString("D"),
                ["eventId"] = eventId,
                ["influenceId"] = influenceId,
                // The source's distinct intent is needed to authenticate a
                // later event-ID retry without asking for a second decision.
                ["intentId"] = intentId,
                ["previousExpressionEvidenceId"] = previousEvidence,
                ["nextExpressionEvidenceId"] = verified.NextExpressionEvidenceId,
                ["previousExpression"] = previousExpression,
                ["nextExpression"] = nextExpression
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
        }

        var after = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (after != before)
            return new(CharacterInfluenceExpressionChangeStatus.AuthoritativeInputsChanged);

        // A valid saved catalog pin is not evidence that the referenced
        // native rules are still current. No alternate local mechanics engine
        // or trust in the source's echoed fingerprint is introduced.
        try
        {
            var native = await rules.GetNativeRulesetAsync(cancellationToken);
            if (native["contractVersion"]?.GetValue<int>() != 1 ||
                native["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                native["fingerprint"]?.GetValue<string>() != saved.Fingerprint)
                return new(CharacterInfluenceExpressionChangeStatus.NativeContentChanged);
        }
        catch (Exception error) when (error is RulesCoreGatewayException or
            JsonException or InvalidOperationException or FormatException)
        {
            return new(CharacterInfluenceExpressionChangeStatus.RulesCoreUnavailable);
        }

        // These are fresh Site checks, not a cross-service commit fence.
        // Site R3 remains an external dependency, not solved here.
        var finalLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (finalLive.Status != SiteCharacterAccessStatus.Authorized ||
            finalLive.Character?.CharacterId != characterId)
            return new(FromSite(finalLive.Status));
        if (!finalLive.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterInfluenceExpressionChangeStatus.ArchivedReadOnly);
        var finalCached = await site.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (finalCached.Status != SiteCharacterAccessStatus.Authorized ||
            finalCached.Character?.CharacterId != characterId ||
            !SameSiteScope(firstLive.Character, finalLive.Character) ||
            !SameSiteScope(finalLive.Character, finalCached.Character) ||
            !SameSiteScope(initial.Character, finalCached.Character))
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);

        CharacterSupernaturalWriteResult committed;
        try
        {
            committed = await store.CommitVerifiedAsync(
                new VerifiedSupernaturalStateWrite(
                    characterId, saved.RulesetId, saved.Fingerprint,
                    saved.AlternateSheetsJson, influences.ToJsonString(Wire),
                    saved.LedgerJson, expectedStateRevision,
                    expectedLedgerRevision, expectedLedgerRevision, before,
                    eventId, verified.AuthorizationEvidenceId, false,
                    DateTimeOffset.UtcNow, IsExpressionChange: true,
                    ExpressionChangeJson: audit.ToJsonString(Wire)),
                cancellationToken);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterInfluenceExpressionChangeStatus.SourceUnauthorized);
        }

        if (committed.Status != CharacterSupernaturalWriteStatus.Applied)
        {
            // Two same-event requests may pass the initial read before either
            // writes. A transaction-level duplicate or serialization conflict
            // must also consult the authoritative receipt after the losing
            // transaction is disposed, without repeating the write.
            if (committed.Status is CharacterSupernaturalWriteStatus.DuplicateEvent
                or CharacterSupernaturalWriteStatus.RevisionConflict
                or CharacterSupernaturalWriteStatus.LedgerRevisionConflict)
            {
                var recovered = await RecoverCommittedAsync(characterId,
                    expectedStateRevision, expectedLedgerRevision, influenceId,
                    intentId, eventId, cancellationToken);
                if (recovered is not null)
                    return recovered;
            }
            return committed.Status switch
            {
                CharacterSupernaturalWriteStatus.DuplicateEvent =>
                    new(CharacterInfluenceExpressionChangeStatus.DuplicateEvent,
                        committed.Revision, committed.LedgerRevision),
                CharacterSupernaturalWriteStatus.RevisionConflict =>
                    new(CharacterInfluenceExpressionChangeStatus.StateRevisionConflict,
                        committed.Revision, committed.LedgerRevision),
                CharacterSupernaturalWriteStatus.LedgerRevisionConflict =>
                    new(CharacterInfluenceExpressionChangeStatus.LedgerRevisionConflict,
                        committed.Revision, committed.LedgerRevision),
                CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged =>
                    new(CharacterInfluenceExpressionChangeStatus.AuthoritativeInputsChanged),
                _ => new(CharacterInfluenceExpressionChangeStatus.NotInitialized)
            };
        }

        // A successful durable event is not a successful feature projection.
        // A separate fresh read delegates all mechanics to Rules Core and may
        // legitimately report unresolved when a native authority is absent.
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision, EventId: eventId,
                ProjectionOutcome: CharacterInfluenceProjectionOutcome.Canceled);
        try
        {
            var read = await effective.GetAsync(characterId, cancellationToken);
            var projection = read.View?.NativeProjection?.DeepClone();
            var outcome = read.View is null
                ? CharacterInfluenceProjectionOutcome.Unavailable
                : projection is null
                    ? CharacterInfluenceProjectionOutcome.Unresolved
                    : CharacterInfluenceProjectionOutcome.Available;
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision,
                read.View?.Status, projection, eventId, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision, EventId: eventId,
                ProjectionOutcome: CharacterInfluenceProjectionOutcome.Canceled);
        }
        catch (Exception error)
        {
            // The authoritative write has already committed. Whether the
            // failure is a known dependency outage or a programming defect,
            // log it and retain the durable receipt instead of throwing a
            // false write failure or attempting another write.
            System.Diagnostics.Trace.TraceError(
                $"Post-commit influence projection failed for character {characterId}, event {eventId}, revision {committed.Revision}: {error}");
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision, EventId: eventId,
                ProjectionOutcome: CharacterInfluenceProjectionOutcome.Failed);
        }
    }

    private async Task<CharacterInfluenceExpressionChangeResult?> RecoverCommittedAsync(
        Guid characterId, long expectedStateRevision, long expectedLedgerRevision,
        string influenceId, string intentId, string eventId, CancellationToken cancellationToken)
    {
        var receipt = (await store.GetEventsAsync(characterId, cancellationToken))
            .SingleOrDefault(x => x.EventId == eventId);
        if (receipt is null)
            return null;
        if (!MatchesCommittedOperation(receipt, characterId,
                expectedStateRevision, expectedLedgerRevision,
                influenceId, intentId, eventId))
            return new(CharacterInfluenceExpressionChangeStatus.EventIdentityConflict);

        // The source of the revision is the original immutable receipt, not
        // the current state which may already have advanced again.
        return new(CharacterInfluenceExpressionChangeStatus.PreviouslyCommitted,
            receipt.CommittedRevision, receipt.LedgerRevision, EventId: eventId);
    }

    /// <summary>
    /// Validate the persisted operation identity, including the intent that
    /// older seven-field receipts did not record. Those older receipts remain
    /// valid history but cannot prove a replay of a specific intent.
    /// </summary>
    private static bool MatchesCommittedOperation(
        CharacterSupernaturalEventReceipt receipt, Guid characterId,
        long expectedStateRevision, long expectedLedgerRevision,
        string influenceId, string intentId, string eventId)
    {
        if (receipt.CharacterId != characterId ||
            receipt.Kind != "expression-change" ||
            receipt.EventId != eventId ||
            receipt.CommittedRevision != expectedStateRevision + 1 ||
            receipt.LedgerRevision != expectedLedgerRevision ||
            string.IsNullOrWhiteSpace(receipt.AuthorizationEvidenceId) ||
            receipt.ExpressionChangeJson is null ||
            receipt.TransitionRequestJson is not null)
            return false;

        try
        {
            using var document = JsonDocument.Parse(receipt.ExpressionChangeJson);
            var audit = document.RootElement;
            var fields = new HashSet<string>(StringComparer.Ordinal)
            {
                "characterId", "eventId", "influenceId", "intentId",
                "previousExpressionEvidenceId", "nextExpressionEvidenceId",
                "previousExpression", "nextExpression"
            };
            if (audit.ValueKind != JsonValueKind.Object ||
                audit.EnumerateObject().Count() != fields.Count ||
                audit.EnumerateObject().Any(x => !fields.Remove(x.Name)) ||
                fields.Count != 0 ||
                !IsText(audit, "characterId", characterId.ToString("D")) ||
                !IsText(audit, "eventId", eventId) ||
                !IsText(audit, "influenceId", influenceId) ||
                !IsText(audit, "intentId", intentId) ||
                !audit.TryGetProperty("previousExpressionEvidenceId", out var prior) ||
                !audit.TryGetProperty("nextExpressionEvidenceId", out var next) ||
                prior.ValueKind != JsonValueKind.String ||
                next.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(prior.GetString()) ||
                string.IsNullOrWhiteSpace(next.GetString()) ||
                prior.GetString()!.Length > 320 || next.GetString()!.Length > 320 ||
                prior.GetString() == next.GetString() ||
                !audit.TryGetProperty("previousExpression", out var before) ||
                !audit.TryGetProperty("nextExpression", out var after) ||
                !ValidExpression(before) || !ValidExpression(after) ||
                JsonElement.DeepEquals(before, after))
                return false;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsText(JsonElement audit, string name, string value) =>
        audit.TryGetProperty(name, out var element) &&
        element.ValueKind == JsonValueKind.String &&
        element.GetString() == value;

    private static bool ValidExpression(JsonElement expression) =>
        expression.ValueKind == JsonValueKind.Object &&
        expression.EnumerateObject().Count() <= 128 &&
        expression.EnumerateObject().All(x =>
            !string.IsNullOrWhiteSpace(x.Name) && x.Name.Length <= 160 &&
            x.Value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(x.Value.GetString()) &&
            x.Value.GetString()!.Length <= 1024);

    /// <summary>
    /// Model property names use web JSON's case-insensitive binding, but
    /// source-authored expression dimensions and owned choice keys have exact
    /// ordinal identity. Reject ambiguous model fields without collapsing two
    /// legitimately distinct expression dimensions.
    /// </summary>
    private static bool HasUnambiguousInfluenceKeys(
        JsonElement value, string? parentField = null)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().All(item =>
                HasUnambiguousInfluenceKeys(item));
        if (value.ValueKind != JsonValueKind.Object)
            return true;

        var isSourceMap = parentField is not null &&
            (parentField.Equals("expression", StringComparison.OrdinalIgnoreCase) ||
             parentField.Equals("requiredExpression", StringComparison.OrdinalIgnoreCase) ||
             parentField.Equals("choices", StringComparison.OrdinalIgnoreCase));
        var names = new HashSet<string>(isSourceMap
            ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) ||
                !HasUnambiguousInfluenceKeys(property.Value, property.Name))
                return false;
        return true;
    }

    private static CharacterInfluenceExpressionChangeStatus FromSite(
        SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Unauthenticated =>
            CharacterInfluenceExpressionChangeStatus.Unauthenticated,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            CharacterInfluenceExpressionChangeStatus.NotFoundOrNotOwned,
        _ => CharacterInfluenceExpressionChangeStatus.ProjectionUnavailable
    };

    private static bool SameSiteScope(
        SiteCharacterProjection expected, SiteCharacterProjection actual) =>
        expected.CharacterId == actual.CharacterId &&
        expected.LifecycleState == actual.LifecycleState &&
        expected.ArchivedAt == actual.ArchivedAt &&
        expected.CampaignIds.Count == expected.CampaignIds.Distinct().Count() &&
        actual.CampaignIds.Count == actual.CampaignIds.Distinct().Count() &&
        expected.CampaignIds.ToHashSet().SetEquals(actual.CampaignIds);
}
