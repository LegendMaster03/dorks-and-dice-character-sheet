using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginMixedActivationStatus
{
    Applied,
    Unauthenticated,
    DmScopeDenied,
    SiteProjectionUnavailable,
    InvalidSelection,
    DraftMissing,
    DraftRevisionConflict,
    OriginAlreadyCommitted,
    SheetNotInitialized,
    LegacySelectionConflict,
    NativeSnapshotUnpinned,
    NativeContentChanged,
    NotAuthorizedByNativeRules,
    RulesCoreUnavailable
}

public sealed record CharacterOriginMixedActivationResult(
    CharacterOriginMixedActivationStatus Status, long? Revision = null);

/// <summary>
/// One intentionally narrow production vertical slice: an authenticated DM
/// approves selected Human/Elf native offers and commits the construction.
/// This is a table-specific outcome, NOT an automatic inheritance policy.
/// The browser supplies only candidate IDs and expected draft revision.
/// Current catalog evidence, actual decisions, actor and trusted scopes are
/// assembled server-side and checked again by Rules Core.
/// </summary>
public sealed class CharacterOriginMixedActivationService(
    ISiteOriginDmAccessGateway dmAccess,
    ICharacterBuildStore builds,
    ICharacterOriginDraftStore drafts,
    ICharacterAuthorizedOriginStore origins,
    INativeRulesCoreGateway rulesCore,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public async Task<CharacterOriginMixedActivationResult> ApplyAsync(
        Guid characterId, Guid campaignId, long expectedDraftRevision,
        IReadOnlyList<string>? selectedCandidateIds,
        CancellationToken cancellationToken = default)
    {
        // No privileged draft or Character Sheet reads before the Site's
        // exact DM + active campaign-character association is verified.
        var scope = await dmAccess.CheckAsync(campaignId, characterId, cancellationToken);
        if (scope.Status != SiteOriginDmAccessStatus.Authorized)
            return new(scope.Status switch
            {
                SiteOriginDmAccessStatus.Unauthenticated =>
                    CharacterOriginMixedActivationStatus.Unauthenticated,
                SiteOriginDmAccessStatus.NotAuthorized =>
                    CharacterOriginMixedActivationStatus.DmScopeDenied,
                _ => CharacterOriginMixedActivationStatus.SiteProjectionUnavailable
            });
        if (scope.ActorUserId is not Guid actor || actor == Guid.Empty ||
            scope.CampaignId != campaignId || scope.CharacterId != characterId)
            return new(CharacterOriginMixedActivationStatus.SiteProjectionUnavailable);

        if (expectedDraftRevision < 1 || selectedCandidateIds is null ||
            selectedCandidateIds.Count is < 1 or > 32 ||
            selectedCandidateIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 180) ||
            selectedCandidateIds.Distinct(StringComparer.Ordinal).Count() !=
                selectedCandidateIds.Count)
            return new(CharacterOriginMixedActivationStatus.InvalidSelection);
        if (await builds.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginMixedActivationStatus.SheetNotInitialized);
        if (await origins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginMixedActivationStatus.OriginAlreadyCommitted);

        var draft = await drafts.GetAsync(characterId, cancellationToken);
        if (draft is null)
            return new(CharacterOriginMixedActivationStatus.DraftMissing);
        if (draft.Revision != expectedDraftRevision)
            return new(CharacterOriginMixedActivationStatus.DraftRevisionConflict);
        // DM adjudication must be pinned to a concrete reviewed native snapshot.
        // Draft editing may omit a fingerprint; activating a table ruling may not.
        if (draft.Fingerprint is null)
            return new(CharacterOriginMixedActivationStatus.NativeSnapshotUnpinned);

        using var parsedDraft = JsonDocument.Parse(draft.ProposalJson);
        if (!TryGetPlainHumanElfDraft(parsedDraft.RootElement, out var proposalId,
            out var methodRuleId, out var methodId))
            return new(CharacterOriginMixedActivationStatus.InvalidSelection);

        JsonNode preview;
        try
        {
            preview = await rulesCore.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = draft.RulesetId,
                    fingerprint = draft.Fingerprint,
                    proposal = parsedDraft.RootElement
                }, WireJson), cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterOriginMixedActivationStatus.RulesCoreUnavailable);
        }

        // A changed native rule is not necessarily unsafe for ordinary
        // preview, but a DM approval must be reviewed against the same
        // mechanics that will be committed.
        if (preview["contentChangedSinceRequest"]?.ToJsonString() != "false" ||
            preview["fingerprint"]?.ToJsonString() != JsonSerializer.Serialize(draft.Fingerprint))
            return new(CharacterOriginMixedActivationStatus.NativeContentChanged);
        if (preview["rulesetId"]?.ToJsonString() != JsonSerializer.Serialize(draft.RulesetId) ||
            preview["previewOnly"]?.ToJsonString() != "true" ||
            preview["result"]?["evaluatedProposal"]?["candidates"] is not JsonArray offered)
            return new(CharacterOriginMixedActivationStatus.RulesCoreUnavailable);

        // A malformed or duplicate Core candidate identifier must not cause
        // a server error or silently choose the last duplicate.
        var nativeOffers = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var item in offered)
        {
            if (item is not JsonObject candidate ||
                candidate["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<string>(out var id) ||
                string.IsNullOrWhiteSpace(id) ||
                !nativeOffers.TryAdd(id, candidate))
                return new(CharacterOriginMixedActivationStatus.RulesCoreUnavailable);
        }
        var choices = new JsonArray();
        var decisions = new JsonArray();
        var evidenceId = "origin-dm:" + Guid.NewGuid().ToString("N");
        var actorId = actor.ToString("D");
        var campaignScopeId = campaignId.ToString("D");

        for (var i = 0; i < selectedCandidateIds.Count; i++)
        {
            var id = selectedCandidateIds[i];
            if (!nativeOffers.TryGetValue(id, out var candidate) ||
                candidate["identity"] is not JsonObject identity ||
                identity["occurrence"]?["source"]?["kind"]?.ToJsonString() is not "0" ||
                candidate["contributorId"] is null ||
                candidate["requiredChoices"] is JsonArray { Count: > 0 })
                return new(CharacterOriginMixedActivationStatus.InvalidSelection);

            var selection = new JsonObject
            {
                ["id"] = "selected-" + i,
                ["candidateId"] = id
            };
            if (candidate.TryGetPropertyValue("offeredValue", out var value) && value is not null)
                selection["selectedValue"] = value.DeepClone();
            choices.Add(selection);
            decisions.Add(NewDecision("candidate-" + i, "candidate", id,
                JsonSerializer.SerializeToNode(new { eligibility = "eligible" })!,
                actorId, campaignScopeId, evidenceId));
        }
        decisions.Add(NewDecision("identity", "configuration", proposalId!,
            JsonSerializer.SerializeToNode(new { speciesId = "custom.human-elf" })!,
            actorId, campaignScopeId, evidenceId));

        // Reconstruct the proposal exclusively from selected, validated native
        // source references held in the draft. No client-supplied adjudication,
        // parent character snapshot, prior profile, or grant is forwarded.
        var approvedProposal = new JsonObject
        {
            ["id"] = proposalId,
            ["intent"] = 1,
            ["method"] = new JsonObject
            {
                ["ruleId"] = methodRuleId,
                ["methodId"] = methodId
            },
            ["contributors"] = JsonNode.Parse(
                parsedDraft.RootElement.GetProperty("contributors").GetRawText()),
            ["candidates"] = new JsonArray(),
            ["manifestations"] = choices,
            ["tableDecisions"] = decisions
        };

        JsonNode evaluation;
        try
        {
            evaluation = await rulesCore.EvaluateApprovedMixedOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = draft.RulesetId,
                    fingerprint = draft.Fingerprint,
                    characterId,
                    campaignId,
                    approvalEvidenceId = evidenceId,
                    proposal = approvedProposal
                }, WireJson), cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterOriginMixedActivationStatus.RulesCoreUnavailable);
        }

        if (evaluation["contentChangedSinceRequest"]?.ToJsonString() != "false" ||
            evaluation["fingerprint"]?.ToJsonString() != JsonSerializer.Serialize(draft.Fingerprint))
            return new(CharacterOriginMixedActivationStatus.NativeContentChanged);
        using var evaluated = JsonDocument.Parse(evaluation.ToJsonString());
        if (!TryVerifiedMixedResult(evaluated.RootElement, characterId,
            actorId, campaignScopeId, evidenceId, draft.RulesetId,
            proposalId!, selectedCandidateIds, out var fingerprint, out var profileJson))
            return new(CharacterOriginMixedActivationStatus.NotAuthorizedByNativeRules);

        // Rules Core owns these mechanical interpretations. Reject mismatched
        // projection/committed source occurrence data before saving either.
        if (!evaluated.RootElement.TryGetProperty("intrinsics", out var intrinsics) ||
            !evaluated.RootElement.TryGetProperty("vision", out var vision) ||
            VerifiedOriginIntrinsicReader.Read(intrinsics, profileJson!) is null)
            return new(CharacterOriginMixedActivationStatus.NotAuthorizedByNativeRules);
        var projectionJson = JsonSerializer.Serialize(new
        {
            rulesetId = draft.RulesetId,
            fingerprint,
            intrinsics,
            vision
        }, WireJson);

        // The immutable audit and authoritative profile enter PostgreSQL in
        // one transaction. A raced owner draft revision or prior activation
        // leaves the current authorized character state unchanged.
        var committed = await origins.CommitVerifiedAsync(
            new VerifiedOriginPersistenceCommand(
                characterId, draft.RulesetId, fingerprint!, profileJson!, 0,
                draft.Revision, "construction:" + proposalId,
                methodRuleId!, methodId!, "campaign-dm", evidenceId,
                clock.GetUtcNow(), actor, campaignId, decisions.ToJsonString(),
                projectionJson),
            cancellationToken);
        return committed.Status switch
        {
            AuthorizedOriginStoreStatus.Applied =>
                new(CharacterOriginMixedActivationStatus.Applied, committed.Revision),
            AuthorizedOriginStoreStatus.DraftRevisionConflict =>
                new(CharacterOriginMixedActivationStatus.DraftRevisionConflict),
            AuthorizedOriginStoreStatus.LegacyOriginSelectionConflict =>
                new(CharacterOriginMixedActivationStatus.LegacySelectionConflict),
            AuthorizedOriginStoreStatus.SheetNotInitialized =>
                new(CharacterOriginMixedActivationStatus.SheetNotInitialized),
            _ => new(CharacterOriginMixedActivationStatus.OriginAlreadyCommitted)
        };
    }

    private static JsonNode NewDecision(string id, string targetKind,
        string targetId, JsonNode outcome, string actor, string campaign,
        string evidence) =>
        JsonSerializer.SerializeToNode(new
        {
            id,
            authority = new { actorId = actor, authorizationEvidenceId = evidence },
            scope = new { kind = "campaign", id = campaign },
            target = new { kind = targetKind, id = targetId },
            effectiveOutcome = outcome
        }, WireJson)!;

    internal static bool TryGetPlainHumanElfDraft(JsonElement root,
        out string? proposalId, out string? ruleId, out string? methodId)
    {
        proposalId = ruleId = methodId = null;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryProperty(root, "id", out var id) ||
            id.ValueKind != JsonValueKind.String ||
            (proposalId = id.GetString()) is not { Length: > 0 and <= 180 } ||
            !TryProperty(root, "intent", out var intent) ||
            intent.ValueKind != JsonValueKind.Number ||
            !intent.TryGetInt32(out var intentValue) || intentValue != 1 ||
            !TryProperty(root, "method", out var method) ||
            (ruleId = String(method, "ruleId")) != "rule.creature-origination" ||
            (methodId = String(method, "methodId")) != "biological" ||
            !Null(root, "selectedSpeciesId") ||
            !Null(root, "selectedAncestryId") ||
            !Empty(root, "candidates") || !Empty(root, "manifestations") ||
            !TryProperty(root, "contributors", out var contributors) ||
            contributors.ValueKind != JsonValueKind.Array ||
            contributors.GetArrayLength() != 2)
            return false;
        // This is a deliberately narrow acceptance slice. Additional species
        // pairings must receive their own authorizations, never this default.
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var slots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contributor in contributors.EnumerateArray())
        {
            var slot = String(contributor, "id");
            if (slot is not ("parent-one" or "parent-two") ||
                !slots.Add(slot) ||
                contributor.EnumerateObject().Any(field => field.Name is not (
                    "id" or "role" or "source" or "manifestedProfile" or "historicalSourceIds")) ||
                !TryProperty(contributor, "role", out var role) ||
                !Numeric(role, "kind", 0) ||
                role.EnumerateObject().Any(field => field.Name != "kind") ||
                !TryProperty(contributor, "source", out var source) ||
                !Numeric(source, "kind", 0) ||
                source.EnumerateObject().Any(field => field.Name is not ("kind" or "id" or "version")) ||
                !Null(source, "version") ||
                !sources.Add(String(source, "id") ?? string.Empty) ||
                !Null(contributor, "manifestedProfile") ||
                !Null(contributor, "historicalSourceIds"))
                return false;
        }
        if (!sources.SetEquals(["species.human", "species.elf"]))
            return false;
        // Do not carry any existing table decision, saved history, prior
        // entitlements, transformation, relationship, or custom input as fact.
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not (
                "id" or "intent" or "method" or "contributors" or "candidates" or
                "manifestations" or "selectedSpeciesId" or "selectedAncestryId"))
                return false;
        }
        return true;
    }

    private static bool TryVerifiedMixedResult(
        JsonElement response, Guid characterId, string actorId,
        string campaignId, string evidenceId, string rulesetId,
        string proposalId, IReadOnlyList<string> selected, out string? fingerprint,
        out string? profileJson)
    {
        fingerprint = profileJson = null;
        if (!TryProperty(response, "result", out var result) ||
            !Bool(response, "previewOnly", true) ||
            String(response, "rulesetId") != rulesetId ||
            String(response, "dmActorId") != actorId ||
            String(response, "campaignId") != campaignId ||
            String(response, "characterId") != characterId.ToString("D") ||
            String(response, "approvalEvidenceId") != evidenceId ||
            string.IsNullOrWhiteSpace(fingerprint = String(response, "fingerprint")) ||
            !Numeric(result, "status", 4) ||
            !TryProperty(result, "issues", out var issues) ||
            issues.ValueKind != JsonValueKind.Array || issues.GetArrayLength() != 0 ||
            !TryProperty(result, "profile", out var profile) ||
            profile.ValueKind != JsonValueKind.Object ||
            String(profile, "id") != "origin:" + proposalId ||
            String(profile, "proposalId") != proposalId ||
            !TryProperty(profile, "adjudication", out var adjudication) ||
            !Numeric(adjudication, "adjudication", 3) ||
            !TryProperty(profile, "history", out var history) ||
            history.ValueKind != JsonValueKind.Array || history.GetArrayLength() != 1 ||
            String(history[0], "id") != "construction:" + proposalId ||
            !Numeric(history[0], "stage", 2) ||
            !TryProperty(profile, "underlying", out var underlying) ||
            !TryProperty(profile, "current", out var current) ||
            !JsonElement.DeepEquals(underlying, current) ||
            String(underlying, "speciesId") != "custom.human-elf" ||
            !TryProperty(underlying, "inherentGrants", out var grants) ||
            grants.ValueKind != JsonValueKind.Array ||
            grants.GetArrayLength() != selected.Count ||
            !Empty(profile, "independentGrants") ||
            !Empty(profile, "persistentInfluences") ||
            !Empty(profile, "otherActiveTemporaryTransformations") ||
            !Empty(profile, "independentGrantApplicability") ||
            !Null(profile, "activeTemporaryTransformation") ||
            !Null(profile, "temporaryTransformationPrecedence") ||
            !Null(profile, "effectiveConfigurationBasis"))
            return false;
        var selectedSet = selected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants.EnumerateArray())
        {
            if (!TryProperty(grant, "sourceCandidateIds", out var ids) ||
                ids.ValueKind != JsonValueKind.Array ||
                ids.GetArrayLength() != 1 ||
                ids[0].ValueKind != JsonValueKind.String ||
                !selectedSet.Contains(ids[0].GetString()!) ||
                !seen.Add(ids[0].GetString()!) ||
                !TryProperty(grant, "authorization", out var basis) ||
                !Numeric(basis, "adjudication", 3))
                return false;
        }
        if (!seen.SetEquals(selectedSet)) return false;
        profileJson = profile.GetRawText();
        return true;
    }

    private static bool TryProperty(JsonElement parent, string key,
        out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(key, out value);
    }

    private static string? String(JsonElement parent, string name) =>
        TryProperty(parent, name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Numeric(JsonElement parent, string name, int expected) =>
        TryProperty(parent, name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var actual) &&
        actual == expected;

    private static bool Bool(JsonElement parent, string name, bool expected) =>
        TryProperty(parent, name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean() == expected;

    private static bool Null(JsonElement parent, string name) =>
        !TryProperty(parent, name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static bool Empty(JsonElement parent, string name) =>
        !TryProperty(parent, name, out var value) ||
        value.ValueKind == JsonValueKind.Null ||
        value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0;
}
