using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginMixedReviewStatus
{
    Ready,
    Unauthenticated,
    NotAuthorized,
    SiteProjectionUnavailable,
    SheetNotInitialized,
    OriginAlreadyCommitted,
    DraftMissing,
    InvalidDraft,
    NativeSnapshotUnpinned,
    NativeContentChanged,
    RulesCoreUnavailable
}

public sealed record CharacterOriginMixedReviewCandidate(
    string Id,
    string ContributorId,
    string SourceId,
    string OccurrencePath,
    string Kind,
    string? IntrinsicSlotId,
    JsonElement? OfferedValue,
    bool RequiresChoices);

public sealed record CharacterOriginMixedReviewResult(
    CharacterOriginMixedReviewStatus Status,
    long? DraftRevision = null,
    string? RulesetId = null,
    string? Fingerprint = null,
    string? ProposalId = null,
    IReadOnlyList<CharacterOriginMixedReviewCandidate>? Candidates = null);

/// <summary>
/// Narrow DM-only reader for another owner's proposed Human–Elf Origin. It
/// reveals only source/offer identities needed to approve table decisions,
/// never the owner's full sheet, inventory, class, or private character state.
/// A campaign role alone is not enough: the Site must authorize the exact
/// active campaign–character pair before any draft is read.
/// </summary>
public sealed class CharacterOriginMixedReviewService(
    ISiteOriginDmAccessGateway siteDm,
    ICharacterBuildStore builds,
    ICharacterOriginDraftStore drafts,
    ICharacterAuthorizedOriginStore origins,
    INativeRulesCoreGateway rulesCore)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CharacterOriginMixedReviewResult> ReviewAsync(
        Guid characterId, Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var authorized = await siteDm.CheckAsync(
            campaignId, characterId, cancellationToken);
        if (authorized.Status != SiteOriginDmAccessStatus.Authorized)
            return new(authorized.Status switch
            {
                SiteOriginDmAccessStatus.Unauthenticated =>
                    CharacterOriginMixedReviewStatus.Unauthenticated,
                SiteOriginDmAccessStatus.NotAuthorized =>
                    CharacterOriginMixedReviewStatus.NotAuthorized,
                _ => CharacterOriginMixedReviewStatus.SiteProjectionUnavailable
            });
        if (authorized.ActorUserId is not Guid actor || actor == Guid.Empty ||
            authorized.CampaignId != campaignId ||
            authorized.CharacterId != characterId)
            return new(CharacterOriginMixedReviewStatus.SiteProjectionUnavailable);

        if (await builds.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginMixedReviewStatus.SheetNotInitialized);
        if (await origins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginMixedReviewStatus.OriginAlreadyCommitted);
        var draft = await drafts.GetAsync(characterId, cancellationToken);
        if (draft is null)
            return new(CharacterOriginMixedReviewStatus.DraftMissing);

        using var doc = JsonDocument.Parse(draft.ProposalJson);
        if (!CharacterOriginMixedActivationService.TryGetPlainHumanElfDraft(
            doc.RootElement, out var proposalId, out _, out _))
            return new(CharacterOriginMixedReviewStatus.InvalidDraft);
        if (draft.Fingerprint is null)
            return new(CharacterOriginMixedReviewStatus.NativeSnapshotUnpinned);

        JsonNode response;
        try
        {
            response = await rulesCore.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = draft.RulesetId,
                    fingerprint = draft.Fingerprint,
                    proposal = doc.RootElement
                }, JsonOptions), cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterOriginMixedReviewStatus.RulesCoreUnavailable);
        }

        if (response["contentChangedSinceRequest"]?.ToJsonString() != "false" ||
            response["fingerprint"]?.ToJsonString() != JsonSerializer.Serialize(draft.Fingerprint))
            return new(CharacterOriginMixedReviewStatus.NativeContentChanged);
        if (response["previewOnly"]?.ToJsonString() != "true" ||
            response["rulesetId"]?.ToJsonString() != JsonSerializer.Serialize(draft.RulesetId) ||
            response["result"]?["evaluatedProposal"]?["candidates"] is not JsonArray offers ||
            offers.Count > 256)
            return new(CharacterOriginMixedReviewStatus.RulesCoreUnavailable);

        var rows = new List<CharacterOriginMixedReviewCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in offers)
        {
            if (item is not JsonObject candidate ||
                candidate["identity"] is not JsonObject identity ||
                identity["occurrence"] is not JsonObject occurrence ||
                occurrence["source"] is not JsonObject source)
                return new(CharacterOriginMixedReviewStatus.RulesCoreUnavailable);
            if (!Text(candidate["id"], out var id) ||
                !seen.Add(id!) ||
                !Text(candidate["contributorId"], out var contributor) ||
                !Text(source["id"], out var sourceId) ||
                sourceId is not ("species.human" or "species.elf") ||
                source["kind"]?.ToJsonString() != "0" ||
                !Text(occurrence["occurrencePath"], out var path) ||
                identity["kind"] is null)
                return new(CharacterOriginMixedReviewStatus.RulesCoreUnavailable);

            var requiresChoices = candidate["requiredChoices"] is JsonArray
                { Count: > 0 };
            var offered = candidate["offeredValue"] is { } value
                ? JsonSerializer.SerializeToElement(value, JsonOptions)
                : (JsonElement?)null;
            rows.Add(new(id!, contributor!, sourceId!, path!,
                identity["kind"]!.ToJsonString(),
                Text(identity["intrinsicSlotId"], out var slot) ? slot : null,
                offered, requiresChoices));
        }

        return new(CharacterOriginMixedReviewStatus.Ready,
            draft.Revision, draft.RulesetId, draft.Fingerprint,
            proposalId, rows);
    }

    private static bool Text(JsonNode? node, out string? value)
    {
        value = null;
        return node is JsonValue text &&
            text.TryGetValue<string>(out value) &&
            !string.IsNullOrWhiteSpace(value);
    }
}
