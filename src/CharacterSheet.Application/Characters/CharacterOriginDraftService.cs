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
    OriginAlreadyCommitted, RulesCoreUnavailable
}

public sealed record CharacterOriginDraftView(
    Guid CharacterId, string RulesetId, string? Fingerprint,
    JsonElement Proposal, long Revision, DateTimeOffset UpdatedAt,
    bool Active = false);

public sealed record CharacterOriginDraftResult(
    CharacterOriginDraftStatus Status,
    CharacterOriginDraftView? Draft = null,
    JsonNode? Preview = null, JsonNode? ConstructPreview = null);

/// <summary>
/// Persists untrusted Origin proposals only after authenticated user access and
/// authoritative native preview. It does not authorize a draft or grant mechanics.
/// </summary>
public sealed class CharacterOriginDraftService(
    ISiteCharacterAccessGateway characterAccess,
    ICharacterBuildStore buildStore,
    ICharacterOriginDraftStore draftStore,
    ICharacterAuthorizedOriginStore authorizedOrigins,
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
        if (await authorizedOrigins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginDraftStatus.OriginAlreadyCommitted);
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
        var isConstruct = false;
        try
        {
            // The assembly is part of the same revisioned draft, but is not a
            // member of Rules Core's ordinary Origin proposal contract.
            if (proposal.TryGetProperty("constructAssembly", out var assembly))
            {
                if (validFingerprint is null || !IsInitialConstruct(proposal, assembly))
                    return new(CharacterOriginDraftStatus.InvalidRequest);
                isConstruct = true;
                var methods = await rulesCore.DiscoverNativeOriginMethodsAsync(
                    validFingerprint, null, cancellationToken);
                if (methods is not JsonObject || methods["fingerprint"]?.ToJsonString() != JsonSerializer.Serialize(validFingerprint) ||
                    methods["rulesetId"]?.ToJsonString() != JsonSerializer.Serialize(validRuleset) ||
                    methods["previewOnly"]?.ToJsonString() != "true" || methods["methods"] is not JsonArray discovered)
                    return new(CharacterOriginDraftStatus.RulesCoreUnavailable);
                var constructMethods = discovered.OfType<JsonObject>().Where(item =>
                    item["reference"] is JsonObject reference &&
                    reference["ruleId"]?.ToJsonString() == "\"rule.creature-origination\"" &&
                    reference["methodId"]?.ToJsonString() == "\"construct-assembly\"" &&
                    item["support"]?.ToJsonString() == "\"assembly-preview\"").ToArray();
                if (constructMethods.Length != 1)
                    return new(CharacterOriginDraftStatus.RulesCoreUnavailable);
                var allowance = constructMethods[0]["initialConstructionAllowance"];
                if (allowance is not null && (allowance is not JsonValue value ||
                    !value.TryGetValue<int>(out var amount) || amount < 0))
                    return new(CharacterOriginDraftStatus.RulesCoreUnavailable);
                if (!assembly.TryGetProperty("initialAllowance", out var submitted) ||
                    !JsonElement.DeepEquals(submitted, JsonSerializer.SerializeToElement(allowance)))
                    return new(CharacterOriginDraftStatus.InvalidRequest);
                var nativeProposal = JsonNode.Parse(validProposal)!.AsObject();
                nativeProposal.Remove("constructAssembly");
                preview = await rulesCore.PreviewNativeConstructAsync(
                    JsonSerializer.SerializeToElement(new
                    {
                        rulesetId = validRuleset, fingerprint = validFingerprint,
                        proposal = nativeProposal, assembly
                    }), cancellationToken);
            }
            else
            {
                // A Construct without an assembly must not be validated via
                // the ordinary Origin route and mistaken for a complete draft.
                if (IsConstructMethod(proposal))
                    return new(CharacterOriginDraftStatus.InvalidRequest);
                preview = await rulesCore.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = validRuleset,
                    fingerprint = validFingerprint,
                    proposal
                }), cancellationToken);
            }
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

        if ((isConstruct || proposal.TryGetProperty("biologicalTransmission", out _)) &&
            (validFingerprint is null || preview["rulesetId"]?.ToJsonString() != JsonSerializer.Serialize(validRuleset) ||
            preview["fingerprint"]?.ToJsonString() != JsonSerializer.Serialize(validFingerprint) ||
            preview["contentChangedSinceRequest"]?.ToJsonString() == "true"))
            return new(CharacterOriginDraftStatus.InvalidRequest);

        var saved = await draftStore.SaveAsync(characterId, validRuleset,
            validFingerprint, validProposal, expectedRevision,
            timeProvider.GetUtcNow(), cancellationToken);
        if (saved is null)
        {
            // A concurrent native activation may have committed while Core
            // was previewing this draft. The store serializes both operations
            // under the root lock, so distinguish an immutable active Origin
            // from a stale revision without guessing from the preview.
            return new(await authorizedOrigins.GetAsync(characterId, cancellationToken) is not null
                ? CharacterOriginDraftStatus.OriginAlreadyCommitted
                : CharacterOriginDraftStatus.RevisionConflict);
        }
        return new(CharacterOriginDraftStatus.Ready, ToView(saved),
            isConstruct ? null : preview, isConstruct ? preview : null);
    }

    private static bool IsConstructMethod(JsonElement proposal) =>
        proposal.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.Object &&
        method.TryGetProperty("ruleId", out var rule) && rule.ValueKind == JsonValueKind.String &&
        rule.GetString() == "rule.creature-origination" &&
        method.TryGetProperty("methodId", out var id) && id.ValueKind == JsonValueKind.String &&
        id.GetString() == "construct-assembly";

    // Only player-editable initial assembly input is accepted here. Operating
    // context, verified resources, and existing state require separate authority.
    private static bool IsInitialConstruct(JsonElement proposal, JsonElement assembly) =>
        IsConstructMethod(proposal) &&
        proposal.TryGetProperty("intent", out var intent) && intent.ValueKind == JsonValueKind.String &&
        intent.GetString() == "Custom" &&
        proposal.TryGetProperty("contributors", out var contributors) && contributors.ValueKind == JsonValueKind.Array &&
        contributors.GetArrayLength() == 0 &&
        proposal.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array &&
        candidates.GetArrayLength() == 0 &&
        assembly.ValueKind == JsonValueKind.Object &&
        assembly.TryGetProperty("initialConstruction", out var initial) && initial.ValueKind == JsonValueKind.True &&
        assembly.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array &&
        assembly.EnumerateObject().All(field => field.Name is "components" or "initialAllowance" or "initialConstruction" or "powerConnections") &&
        components.EnumerateArray().All(component => component.ValueKind == JsonValueKind.Object &&
            component.EnumerateObject().All(field => field.Name is "instanceId" or "definitionId" or "materials" or
                "connectedTo" or "enabled" or "modeId" or "designId" or "enhancementIds" or "powerSourceId" or
                "parentInstanceId" or "mountInterfaceId" or "constructionVariantId" or "protectedRegionId"));

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
