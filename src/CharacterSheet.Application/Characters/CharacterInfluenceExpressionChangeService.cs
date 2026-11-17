using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterInfluenceExpressionChangeStatus
{
    Applied, InvalidRequest, Unauthenticated, NotFoundOrNotOwned,
    ProjectionUnavailable, ArchivedReadOnly, NotInitialized,
    StateRevisionConflict, LedgerRevisionConflict, SourceUnauthorized,
    NativeContentChanged, AuthoritativeInputsChanged, DuplicateEvent
}

public sealed record CharacterInfluenceExpressionChangeResult(
    CharacterInfluenceExpressionChangeStatus Status,
    long? StateRevision = null, long? LedgerRevision = null,
    string? EffectiveStatus = null, JsonNode? EffectiveProjection = null);

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
        if (saved.Revision != expectedStateRevision)
            return new(CharacterInfluenceExpressionChangeStatus.StateRevisionConflict,
                saved.Revision, saved.LedgerRevision);
        if (saved.LedgerRevision != expectedLedgerRevision)
            return new(CharacterInfluenceExpressionChangeStatus.LedgerRevisionConflict,
                saved.Revision, saved.LedgerRevision);

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
            if (!CharacterNativePrimaryStateAdapter.HasUniqueJsonKeys(
                    document.RootElement))
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
            var nextExpression = JsonSerializer.SerializeToNode(
                verified.NextExpression, Wire) as JsonObject
                ?? throw new JsonException("Invalid expression.");
            if (JsonNode.DeepEquals(nextExpression, previousExpression))
                return new(CharacterInfluenceExpressionChangeStatus.InvalidRequest);

            original["expression"] = nextExpression.DeepClone();
            original["expressionEvidenceId"] = verified.NextExpressionEvidenceId;
            audit = new JsonObject
            {
                ["characterId"] = characterId.ToString("D"),
                ["eventId"] = eventId,
                ["influenceId"] = influenceId,
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

        // A successful durable event is not a successful feature projection.
        // A separate fresh read delegates all mechanics to Rules Core and may
        // legitimately report unresolved when a native authority is absent.
        if (cancellationToken.IsCancellationRequested)
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision);
        try
        {
            var read = await effective.GetAsync(characterId, cancellationToken);
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision,
                read.View?.Status, read.View?.NativeProjection?.DeepClone());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(CharacterInfluenceExpressionChangeStatus.Applied,
                committed.Revision, committed.LedgerRevision);
        }
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
