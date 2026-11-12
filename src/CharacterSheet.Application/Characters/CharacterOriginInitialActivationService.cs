using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginActivationStatus
{
    Applied,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    SheetNotInitialized,
    ArchivedReadOnly,
    DraftMissing,
    DraftRevisionConflict,
    OriginAlreadyCommitted,
    LegacySelectionConflict,
    NotAuthorizedByNativeRules,
    RulesCoreUnavailable
}

public sealed record CharacterOriginActivationResult(
    CharacterOriginActivationStatus Status,
    long? Revision = null);

/// <summary>
/// Only initial construction under an executable first-party native method.
/// No browser-supplied proposal, grant, history, actor role or ruling enters
/// this operation: the saved draft is reread and Rules Core reevaluates it.
/// A Rite, form, mixed DM override, or replacement needs a distinct future
/// authority path. This operation never modifies independent entitlements.
/// </summary>
public sealed class CharacterOriginInitialActivationService(
    ISiteCharacterAccessGateway site,
    ICharacterBuildStore builds,
    ICharacterOriginDraftStore drafts,
    ICharacterAuthorizedOriginStore origins,
    INativeRulesCoreGateway rulesCore,
    TimeProvider clock)
{
    public async Task<CharacterOriginActivationResult> ApplyAsync(
        Guid characterId, long expectedDraftRevision,
        CancellationToken cancellationToken = default)
    {
        var access = await site.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        switch (access.Status)
        {
            case SiteCharacterAccessStatus.Unauthenticated:
                return new(CharacterOriginActivationStatus.Unauthenticated);
            case SiteCharacterAccessStatus.NotFoundOrNotOwned:
                return new(CharacterOriginActivationStatus.NotFoundOrNotOwned);
            case SiteCharacterAccessStatus.Authorized:
                break;
            default:
                return new(CharacterOriginActivationStatus.ProjectionUnavailable);
        }
        if (access.Character?.CharacterId != characterId)
            return new(CharacterOriginActivationStatus.ProjectionUnavailable);
        if (!access.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterOriginActivationStatus.ArchivedReadOnly);
        if (expectedDraftRevision < 1)
            return new(CharacterOriginActivationStatus.DraftRevisionConflict);
        if (await builds.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginActivationStatus.SheetNotInitialized);
        if (await origins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginActivationStatus.OriginAlreadyCommitted);

        var draft = await drafts.GetAsync(characterId, cancellationToken);
        if (draft is null)
            return new(CharacterOriginActivationStatus.DraftMissing);
        if (draft.Revision != expectedDraftRevision)
            return new(CharacterOriginActivationStatus.DraftRevisionConflict);

        using var proposed = JsonDocument.Parse(draft.ProposalJson);
        if (!TryInitialProposal(proposed.RootElement, out var proposalId,
            out var methodRuleId, out var methodId, out var speciesId,
            out var ancestryId))
            return new(CharacterOriginActivationStatus.NotAuthorizedByNativeRules);

        // The current native catalog is authoritative. A stored fingerprint
        // is an audit hint and does not freeze eligibility across rule changes.
        JsonNode preview;
        try
        {
            preview = await rulesCore.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = draft.RulesetId,
                    fingerprint = draft.Fingerprint,
                    proposal = proposed.RootElement
                }), cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterOriginActivationStatus.RulesCoreUnavailable);
        }

        using var response = JsonDocument.Parse(preview.ToJsonString());
        if (!TryAuthorizedNativeResult(response.RootElement, draft.RulesetId,
            proposalId!, methodRuleId!, methodId!, speciesId, ancestryId,
            out var latestFingerprint, out var authorizedProfile))
            return new(CharacterOriginActivationStatus.NotAuthorizedByNativeRules);

        // The one completed construction receipt is derived from the reviewed
        // Core response, not an event supplied by the browser. The database
        // independently checks the current draft revision, absence of a
        // previously committed Origin and absence of legacy Species selections.
        var committed = await origins.CommitVerifiedAsync(
            new VerifiedOriginPersistenceCommand(
                characterId, draft.RulesetId, latestFingerprint!,
                authorizedProfile!, 0, draft.Revision,
                "construction:" + proposalId, methodRuleId!, methodId!,
                "first-party", "native-catalog:" + latestFingerprint,
                clock.GetUtcNow()), cancellationToken);

        return committed.Status switch
        {
            AuthorizedOriginStoreStatus.Applied =>
                new(CharacterOriginActivationStatus.Applied, committed.Revision),
            AuthorizedOriginStoreStatus.DraftRevisionConflict =>
                new(CharacterOriginActivationStatus.DraftRevisionConflict),
            AuthorizedOriginStoreStatus.LegacyOriginSelectionConflict =>
                new(CharacterOriginActivationStatus.LegacySelectionConflict),
            AuthorizedOriginStoreStatus.SheetNotInitialized =>
                new(CharacterOriginActivationStatus.SheetNotInitialized),
            _ => new(CharacterOriginActivationStatus.OriginAlreadyCommitted)
        };
    }

    private static bool TryInitialProposal(JsonElement proposal,
        out string? id, out string? ruleId, out string? methodId,
        out string? speciesId, out string? ancestryId)
    {
        id = ruleId = methodId = speciesId = ancestryId = null;
        if (proposal.ValueKind != JsonValueKind.Object ||
            !TryRead(proposal, "intent", out var intent) ||
            !EnumIs(intent, "Direct", 0) ||
            !TryRead(proposal, "method", out var method) ||
            method.ValueKind != JsonValueKind.Object)
            return false;
        id = ReadString(proposal, "id");
        ruleId = ReadString(method, "ruleId");
        methodId = ReadString(method, "methodId");
        speciesId = ReadString(proposal, "selectedSpeciesId");
        ancestryId = ReadString(proposal, "selectedAncestryId");
        // A proposal ID also forms the immutable event ID. Make sure the
        // persistence guard accepts it before asking native evaluation.
        return !string.IsNullOrWhiteSpace(id) &&
            ("construction:" + id).Length <= 240 &&
            !string.IsNullOrWhiteSpace(ruleId) &&
            !string.IsNullOrWhiteSpace(methodId) &&
            (!string.IsNullOrWhiteSpace(speciesId) ||
             !string.IsNullOrWhiteSpace(ancestryId));
    }

    private static bool TryAuthorizedNativeResult(JsonElement response,
        string rulesetId, string proposalId, string methodRuleId,
        string methodId, string? speciesId, string? ancestryId,
        out string? fingerprint, out string? profileJson)
    {
        fingerprint = profileJson = null;
        if (!BoolIs(response, "previewOnly", true) ||
            ReadString(response, "rulesetId") != rulesetId ||
            string.IsNullOrWhiteSpace(fingerprint = ReadString(response, "fingerprint")) ||
            !TryRead(response, "result", out var result) ||
            !TryRead(result, "status", out var status) ||
            !EnumIs(status, "Authorized", 4) ||
            !TryRead(result, "profile", out var profile) ||
            profile.ValueKind != JsonValueKind.Object ||
            !TryRead(result, "issues", out var issues) ||
            issues.ValueKind != JsonValueKind.Array ||
            issues.GetArrayLength() != 0)
            return false;

        if (ReadString(profile, "id") != "origin:" + proposalId ||
            ReadString(profile, "proposalId") != proposalId ||
            !TryRead(profile, "adjudication", out var adjudication) ||
            !TryRead(adjudication, "adjudication", out var adjudicationKind) ||
            !EnumIs(adjudicationKind, "Supported", 0) ||
            ReadString(adjudication, "nativeRuleReference") != methodRuleId ||
            !IsEmptyOrAbsent(profile, "independentGrants") ||
            !IsEmptyOrAbsent(profile, "persistentInfluences") ||
            !IsEmptyOrAbsent(profile, "independentGrantApplicability") ||
            !IsEmptyOrAbsent(profile, "otherActiveTemporaryTransformations") ||
            !IsNullOrAbsent(profile, "activeTemporaryTransformation") ||
            !IsNullOrAbsent(profile, "temporaryTransformationPrecedence") ||
            !IsNullOrAbsent(profile, "effectiveConfigurationBasis") ||
            !TryRead(profile, "underlying", out var underlying) ||
            !TryRead(profile, "current", out var current) ||
            underlying.ValueKind != JsonValueKind.Object ||
            !JsonElement.DeepEquals(underlying, current) ||
            ReadString(underlying, "speciesId") != speciesId ||
            ReadString(underlying, "ancestryId") != ancestryId ||
            !TryRead(profile, "history", out var history) ||
            history.ValueKind != JsonValueKind.Array ||
            history.GetArrayLength() != 1)
            return false;

        var completed = history[0];
        if (ReadString(completed, "id") != "construction:" + proposalId ||
            !TryRead(completed, "stage", out var stage) ||
            !EnumIs(stage, "Completed", 2) ||
            !BoolIs(completed, "replacesConfiguration", false) ||
            !TryRead(completed, "method", out var completedMethod) ||
            ReadString(completedMethod, "ruleId") != methodRuleId ||
            ReadString(completedMethod, "methodId") != methodId ||
            !IsNullOrAbsent(completed, "tableDecisionId") ||
            !IsNullOrAbsent(adjudication, "tableDecisionId"))
            return false;

        profileJson = profile.GetRawText();
        return true;
    }

    private static bool TryRead(JsonElement source, string name,
        out JsonElement value)
    {
        value = default;
        return source.ValueKind == JsonValueKind.Object &&
            source.TryGetProperty(name, out value);
    }

    private static string? ReadString(JsonElement source, string name) =>
        TryRead(source, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool BoolIs(JsonElement source, string name, bool expected) =>
        TryRead(source, name, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static bool EnumIs(JsonElement value, string name, int ordinal) =>
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), name, StringComparison.OrdinalIgnoreCase) ||
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number) && number == ordinal;

    private static bool IsNullOrAbsent(JsonElement source, string name) =>
        !TryRead(source, name, out var value) ||
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;

    private static bool IsEmptyOrAbsent(JsonElement source, string name) =>
        IsNullOrAbsent(source, name) ||
        TryRead(source, name, out var value) &&
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;
}
