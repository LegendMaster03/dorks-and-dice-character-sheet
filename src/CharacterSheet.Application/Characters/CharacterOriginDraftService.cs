using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginDraftStatus
{
    Ready, NotFoundOrNotOwned, Unauthenticated, ProjectionUnavailable,
    SheetNotInitialized, ArchivedReadOnly, InvalidRequest, RevisionConflict,
    RulesCoreUnavailable
}

public sealed record CharacterOriginDraftView(
    Guid CharacterId, string RulesetId, string? Fingerprint,
    JsonElement Proposal, long Revision, DateTimeOffset UpdatedAt,
    bool Active = false);

public sealed record CharacterOriginDraftResult(
    CharacterOriginDraftStatus Status,
    CharacterOriginDraftView? Draft = null,
    JsonNode? Preview = null);

/// <summary>
/// Persists untrusted Origin proposals only after authenticated user access and
/// authoritative native preview. It does not authorize a draft or grant mechanics.
/// </summary>
public sealed class CharacterOriginDraftService(
    ISiteCharacterAccessGateway characterAccess,
    ICharacterBuildStore buildStore,
    ICharacterOriginDraftStore draftStore,
    INativeRulesCoreGateway rulesCore,
    TimeProvider timeProvider)
{
    public async Task<CharacterOriginDraftResult> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var access = await characterAccess.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        var denied = Denied(access.Status);
        if (denied is not null) return new(denied.Value);
        if (access.Character?.CharacterId != characterId)
            return new(CharacterOriginDraftStatus.ProjectionUnavailable);
        if (await buildStore.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginDraftStatus.SheetNotInitialized);
        var draft = await draftStore.GetAsync(characterId, cancellationToken);
        return new(CharacterOriginDraftStatus.Ready,
            draft is null ? null : ToView(draft));
    }

    public async Task<CharacterOriginDraftResult> SaveAsync(
        Guid characterId, string rulesetId, string? fingerprint,
        JsonElement proposal, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var access = await characterAccess.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        var denied = Denied(access.Status);
        if (denied is not null) return new(denied.Value);
        if (access.Character?.CharacterId != characterId)
            return new(CharacterOriginDraftStatus.ProjectionUnavailable);
        if (!access.Character!.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterOriginDraftStatus.ArchivedReadOnly);
        if (await buildStore.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginDraftStatus.SheetNotInitialized);
        if (expectedRevision < 0 || expectedRevision == long.MaxValue ||
            proposal.ValueKind != JsonValueKind.Object)
            return new(CharacterOriginDraftStatus.InvalidRequest);

        string validRuleset;
        string? validFingerprint;
        string validProposal;
        try
        {
            validRuleset = CharacterOriginDraftState.NormalizeRulesetId(rulesetId);
            validFingerprint = CharacterOriginDraftState.NormalizeFingerprint(fingerprint);
            validProposal = CharacterOriginDraftState.ValidateProposalJson(
                proposal.GetRawText());
        }
        catch (ArgumentException)
        {
            return new(CharacterOriginDraftStatus.InvalidRequest);
        }

        // Ask Rules Core to evaluate the exact proposal. A 200 response does not
        // imply authorization; invalid and unresolved results remain drafts.
        // The bridge rejects forged prior profiles, history and table decisions.
        JsonNode preview;
        try
        {
            preview = await rulesCore.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = validRuleset,
                    fingerprint = validFingerprint,
                    proposal
                }), cancellationToken);
        }
        catch (RulesCoreGatewayException error)
        {
            return new(error.StatusCode is 400 or 409
                ? CharacterOriginDraftStatus.InvalidRequest
                : CharacterOriginDraftStatus.RulesCoreUnavailable);
        }
        if (preview is not JsonObject ||
            preview["previewOnly"]?.GetValue<bool>() != true ||
            preview["result"] is null)
            return new(CharacterOriginDraftStatus.RulesCoreUnavailable);

        var saved = await draftStore.SaveAsync(characterId, validRuleset,
            validFingerprint, validProposal, expectedRevision,
            timeProvider.GetUtcNow(), cancellationToken);
        return saved is null
            ? new(CharacterOriginDraftStatus.RevisionConflict)
            : new(CharacterOriginDraftStatus.Ready, ToView(saved), preview);
    }

    private static CharacterOriginDraftView ToView(CharacterOriginDraftState state) =>
        new(state.CharacterId, state.RulesetId, state.Fingerprint,
            JsonSerializer.Deserialize<JsonElement>(state.ProposalJson),
            state.Revision, state.UpdatedAt);

    private static CharacterOriginDraftStatus? Denied(SiteCharacterAccessStatus status) =>
        status switch
        {
            SiteCharacterAccessStatus.Authorized => null,
            SiteCharacterAccessStatus.NotFoundOrNotOwned =>
                CharacterOriginDraftStatus.NotFoundOrNotOwned,
            SiteCharacterAccessStatus.Unauthenticated =>
                CharacterOriginDraftStatus.Unauthenticated,
            _ => CharacterOriginDraftStatus.ProjectionUnavailable
        };
}
