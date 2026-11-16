using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterVerifiedOriginUnderlyingStatus
{
    VerifiedPartial,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    SheetNotInitialized,
    OriginUnavailable,
    NativeContentChanged,
    SourceUnverified,
    AuthoritativeInputsChanged,
    RulesCoreUnavailable
}

public sealed record CharacterVerifiedOriginUnderlyingResult(
    CharacterVerifiedOriginUnderlyingStatus Status,
    long? OriginRevision = null,
    JsonNode? Underlying = null,
    JsonNode? Current = null);

/// <summary>
/// Authorized read-only coordination for the existing Origin Underlying model.
/// The saved profile is only a candidate: Rules Core must consult an independent
/// host authority provider for source occurrences, committed provenance and DM
/// evidence, and must review the profile against the current native catalog.
/// This service never issues an inherited grant, builds a new Origin, or
/// activates a supernatural form. Missing production authority fails closed.
/// </summary>
public sealed class CharacterVerifiedOriginUnderlyingService(
    CharacterOriginStateService origins,
    ICharacterSupernaturalStateStore ownerStore,
    INativeRulesCoreGateway rules,
    ISiteFreshCharacterAccessGateway freshSite)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterVerifiedOriginUnderlyingResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty)
            return new(CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned);

        // A per-request Tool Host snapshot can be stale. Require a separate
        // Site database read before inspecting any protected Origin row.
        var liveAccess = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (liveAccess.Status != SiteCharacterAccessStatus.Authorized ||
            liveAccess.Character?.CharacterId != characterId)
            return new(MapLiveStatus(liveAccess.Status));

        // The cached access remains necessary to load the Character Sheet
        // stores, but it must agree with independently refreshed Site state.
        var initialAccess = await origins.GetAsync(characterId, cancellationToken);
        if (initialAccess.Status != CharacterOriginReadStatus.Ready)
            return new(initialAccess.Status switch
            {
                CharacterOriginReadStatus.Unauthenticated =>
                    CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
                CharacterOriginReadStatus.NotFoundOrNotOwned =>
                    CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                CharacterOriginReadStatus.SheetNotInitialized =>
                    CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized,
                _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
            });

        if (!SameSiteState(liveAccess.Character!, initialAccess))
            return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);

        // Reload inside the digest window, so an Origin change between
        // initial Site access and fingerprint acquisition is not certified.
        var before = await ownerStore.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        var origin = await origins.GetAsync(characterId, cancellationToken);
        if (origin.Status != CharacterOriginReadStatus.Ready)
            return new(origin.Status switch
            {
                CharacterOriginReadStatus.Unauthenticated =>
                    CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
                CharacterOriginReadStatus.NotFoundOrNotOwned =>
                    CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                CharacterOriginReadStatus.SheetNotInitialized =>
                    CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized,
                _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
            });
        if (before is null)
            return new(CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized);
        if (origin.SavedState is not { } saved)
            return new(CharacterVerifiedOriginUnderlyingStatus.OriginUnavailable);
        if (saved.CharacterId != characterId || saved.Revision < 1 ||
            !HasContiguousOriginReceipts(
                characterId, saved.Revision, origin.CompletedEvents))
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

        try
        {
            var native = await rules.GetNativeRulesetAsync(cancellationToken);
            if (native["contractVersion"]?.GetValue<int>() != 1 ||
                native["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                native["fingerprint"]?.GetValue<string>() != saved.Fingerprint)
                return new(CharacterVerifiedOriginUnderlyingStatus.NativeContentChanged);

            using var profile = JsonDocument.Parse(saved.ProfileJson);
            if (profile.RootElement.ValueKind != JsonValueKind.Object ||
                !profile.RootElement.TryGetProperty("underlying", out var underlying) ||
                !profile.RootElement.TryGetProperty("current", out var current) ||
                underlying.ValueKind != JsonValueKind.Object ||
                current.ValueKind != JsonValueKind.Object)
                return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

            var response = await rules.VerifyNativeOriginUnderlyingAsync(
                JsonSerializer.SerializeToElement(new
                {
                    characterId,
                    originRevision = saved.Revision,
                    rulesetId = saved.RulesetId,
                    fingerprint = saved.Fingerprint,
                    profile = profile.RootElement
                }, Wire), cancellationToken);

            // Requery Site's live ownership/lifecycle DB after the remote
            // Core evaluation. Unlike re-reading the same introspection
            // snapshot, this notices an archive, campaign disconnect or
            // changed owner between the two service requests. This remains
            // a read-time check, not an atomic cross-service commit fence.
            var finalLive = await freshSite.GetFreshAuthorizedCharacterAsync(
                characterId, cancellationToken);
            if (finalLive.Status != SiteCharacterAccessStatus.Authorized ||
                finalLive.Character?.CharacterId != characterId)
                return new(MapLiveStatus(finalLive.Status));
            if (!SameSiteState(liveAccess.Character!, finalLive))
                return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);

            var finalAccess = await origins.GetAsync(characterId, cancellationToken);
            if (finalAccess.Status != CharacterOriginReadStatus.Ready)
                return new(finalAccess.Status switch
                {
                    CharacterOriginReadStatus.Unauthenticated =>
                        CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
                    CharacterOriginReadStatus.NotFoundOrNotOwned =>
                        CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                    CharacterOriginReadStatus.SheetNotInitialized =>
                        CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized,
                    _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
                });
            if (!SameSiteState(finalLive.Character!, finalAccess))
                return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);
            if (finalAccess.SavedState is not { } finalOrigin ||
                finalOrigin.Revision != saved.Revision ||
                finalOrigin.RulesetId != saved.RulesetId ||
                finalOrigin.Fingerprint != saved.Fingerprint ||
                !string.Equals(finalOrigin.ProfileJson, saved.ProfileJson,
                    StringComparison.Ordinal))
                return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);

            // The host's verdict does not excuse a concurrent mutation or
            // inconsistent returned configuration. Compare the exact source
            // structures, not a species ID, ancestry label or display name.
            var after = await ownerStore.GetAuthoritativeInputsFingerprintAsync(
                characterId, cancellationToken);
            if (after is null || before != after)
                return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);
            if (response["contractVersion"]?.GetValue<int>() != 1 ||
                response["supportedScope"]?.GetValue<string>() !=
                    "verified-origin-underlying" ||
                response["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                response["fingerprint"]?.GetValue<string>() != saved.Fingerprint ||
                response["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                response["originRevision"]?.GetValue<long>() != saved.Revision ||
                response["status"]?.GetValue<string>() != "partial" ||
                response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                response["underlying"] is not JsonObject verifiedUnderlying ||
                response["current"] is not JsonObject verifiedCurrent ||
                !JsonElement.DeepEquals(underlying,
                    JsonSerializer.SerializeToElement(verifiedUnderlying, Wire)) ||
                !JsonElement.DeepEquals(current,
                    JsonSerializer.SerializeToElement(verifiedCurrent, Wire)))
                return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

            return new(CharacterVerifiedOriginUnderlyingStatus.VerifiedPartial,
                saved.Revision, verifiedUnderlying.DeepClone(), verifiedCurrent.DeepClone());
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode == 409 && error.ErrorCode == "native_content_changed")
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.NativeContentChanged);
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode is 400 or 401 or 403 or 409 or 422)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.RulesCoreUnavailable);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);
        }
    }
    /// <summary>
    /// Correlates the committed Origin row with its separately persisted
    /// ordered receipt history. Receipt continuity is a consistency guard,
    /// not proof that a native procedure or DM approval was authorized;
    /// the independent Rules Core source verifier remains mandatory.
    /// </summary>
    private static bool HasContiguousOriginReceipts(Guid characterId,
        long revision, IReadOnlyList<CharacterOriginEventReceipt>? receipts)
    {
        if (receipts is null || receipts.Count != revision)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < receipts.Count; index++)
        {
            var receipt = receipts[index];
            if (receipt is null ||
                receipt.CharacterId != characterId ||
                receipt.CommittedRevision != index + 1L ||
                string.IsNullOrWhiteSpace(receipt.EventId) ||
                !ids.Add(receipt.EventId) ||
                string.IsNullOrWhiteSpace(receipt.MethodRuleId) ||
                string.IsNullOrWhiteSpace(receipt.MethodId) ||
                string.IsNullOrWhiteSpace(receipt.AuthorizationKind) ||
                string.IsNullOrWhiteSpace(receipt.AuthorizationEvidenceId) ||
                receipt.ApprovalActorUserId.HasValue !=
                    receipt.ApprovalCampaignId.HasValue ||
                receipt.ApprovalActorUserId.HasValue !=
                    (receipt.ApprovedDecisionsJson is not null))
                return false;
        }
        return true;
    }

    private static CharacterVerifiedOriginUnderlyingStatus MapLiveStatus(
        SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Unauthenticated =>
            CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
        _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
    };

    private static bool SameSiteState(
        SiteCharacterProjection expected, SiteCharacterAccessResult actual) =>
        actual.Status == SiteCharacterAccessStatus.Authorized &&
        actual.Character is { } current &&
        SameSiteState(expected, current);

    private static bool SameSiteState(
        SiteCharacterProjection expected, SiteCharacterProjection actual) =>
        expected.CharacterId == actual.CharacterId &&
        expected.LifecycleState == actual.LifecycleState &&
        expected.ArchivedAt == actual.ArchivedAt &&
        expected.CampaignIds.Count == expected.CampaignIds.Distinct().Count() &&
        actual.CampaignIds.Count == actual.CampaignIds.Distinct().Count() &&
        expected.CampaignIds.ToHashSet().SetEquals(actual.CampaignIds);

}
