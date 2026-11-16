using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// First source-verified native ledger integration: initialize a previously
/// uninitialized, direct biological Origin from its still-current committed
/// source draft and the owner's current Class/ability progression. A changed
/// draft, mixed adjudication, historical Origin, or disputed Class source
/// remains unsupported. Spending, rest and advancement are deliberately not
/// authorized here: their runtime events still require trusted acquisition
/// and applicability evidence.
/// </summary>
public sealed class NativeDirectInnateSpellLedgerEvaluator(
    ICharacterOriginDraftStore drafts,
    CharacterBuildService builds,
    CharacterStateService characterStates,
    INativeRulesCoreGateway core) : ITrustedInnateSpellLedgerEvaluator
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly string[] Abilities =
        ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];

    public async Task<TrustedInnateSpellLedgerCandidate?> CalculateAsync(
        TrustedInnateSpellLedgerEvaluation input, CancellationToken cancellationToken)
    {
        // Deliberate one-action capability, NOT a generic verified native
        // transition. The coordinator has already verified Site owner+actor.
        if (input.Action != CharacterInnateSpellLedgerAction.Initialize ||
            input.CurrentLedger is not null ||
            input.GrantId is not null ||
            input.SiteActorId == Guid.Empty ||
            input.CommittedOrigin.CharacterId != input.CharacterId ||
            input.CommittedOrigin.SourceDraftRevision is not > 0)
            return null;

        var origin = input.CommittedOrigin;
        var draft = await drafts.GetAsync(input.CharacterId, cancellationToken);
        // The original draft fingerprint is an audit hint: initial native
        // activation deliberately allows that hint to be stale and commits
        // the re-evaluated latest catalog fingerprint. The *committed* Origin
        // fingerprint is the authoritative source for this new calculation.
        if (draft is null || draft.CharacterId != input.CharacterId ||
            draft.Revision != origin.SourceDraftRevision ||
            draft.RulesetId != origin.RulesetId)
            return null;

        using var proposal = JsonDocument.Parse(draft.ProposalJson);
        if (!CharacterOriginInitialActivationService.TryInitialProposal(
                proposal.RootElement, out var proposalId, out _, out _, out _, out _) ||
            proposalId is null)
            return null;

        using var storedProfile = JsonDocument.Parse(origin.ProfileJson);
        if (storedProfile.RootElement.ValueKind != JsonValueKind.Object ||
            !storedProfile.RootElement.TryGetProperty("proposalId", out var storedProposalId) ||
            storedProposalId.ValueKind != JsonValueKind.String ||
            storedProposalId.GetString() != proposalId ||
            CharacterInnateSpellLedgerState.ReadProfileId(origin.ProfileJson) !=
                "origin:" + proposalId)
            return null;

        var build = await builds.GetAsync(input.CharacterId, cancellationToken);
        var state = await characterStates.GetAsync(input.CharacterId, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready ||
            build.View is not { ReadOnly: false } sheet ||
            state.Status != CharacterStateAccessStatus.Ready ||
            state.View is not { ReadOnly: false } current)
            return null;

        var scores = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var ability in sheet.BaseAbilityScoreInputs)
        {
            if (!Abilities.Contains(ability.AbilityKey, StringComparer.Ordinal) ||
                // The native kernel bounds transported scores at 0..1000;
                // do not introduce an unrelated first-party game cap here.
                ability.Score is < 0 or > 1000 ||
                !scores.TryAdd(ability.AbilityKey, ability.Score))
                return null;
        }
        if (scores.Count != Abilities.Length)
            return null;

        // Only native Class occurrences are currently covered by this
        // bootstrap slice. Do not guess how a custom/prestige progression,
        // shared class level, or unresolved occurrence affects the total.
        var progression = sheet.ProgressionEntries
            .OrderBy(x => x.Ordinal ?? int.MaxValue)
            .ThenBy(x => x.Id).ToArray();
        if (progression.Length is 0 or > 40 ||
            progression.Any(x =>
                x.Kind != CharacterBuildAdvancementKinds.Class ||
                x.Level is not > 0 or > 1000 ||
                !x.RuleConceptKey.StartsWith("class.", StringComparison.Ordinal) ||
                x.Id == Guid.Empty))
            return null;
        var choices = NativeCharacterOptionSelections.FromState(current);
        if (choices.HasUnmappedChoices)
            return null;

        var gains = (current.HitPointGains ?? [])
            .Where(x => x.ClassLevel > 0 && x.HitDieValue >= 0)
            .GroupBy(x => x.AdvancementOccurrenceId.ToString("D") + ":" +
                x.ClassLevel, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last().HitDieValue,
                StringComparer.Ordinal);

        var metadata = await core.GetNativeRulesetAsync(cancellationToken);
        if (ReadInt(metadata, "contractVersion") != 1 ||
            ReadText(metadata, "rulesetId") != origin.RulesetId ||
            ReadText(metadata, "fingerprint") != origin.Fingerprint ||
            ReadText(metadata["capabilities"], "innateSpellLedgerBootstrap") !=
                "source-matched-preview")
            return null;

        var request = JsonSerializer.SerializeToElement(new
        {
            rulesetId = origin.RulesetId,
            fingerprint = origin.Fingerprint,
            proposal = proposal.RootElement,
            committedProfile = storedProfile.RootElement,
            character = new
            {
                rulesetId = origin.RulesetId,
                fingerprint = origin.Fingerprint,
                abilityScores = scores,
                advancements = progression.Select(x => new
                {
                    classId = x.RuleConceptKey,
                    level = x.Level!.Value,
                    occurrenceId = x.Id.ToString("D")
                }).ToArray(),
                selectedOptions = choices.Ids,
                hitPointGains = gains.Count == 0 ? null : gains
            }
        }, Wire);
        JsonNode answer;
        try
        {
            answer = await core.PreviewDirectInnateSpellLedgerBootstrapAsync(
                request, cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return null;
        }
        if (ReadInt(answer, "contractVersion") != 1 ||
            ReadText(answer, "rulesetId") != origin.RulesetId ||
            ReadText(answer, "fingerprint") != origin.Fingerprint ||
            ReadBool(answer, "previewOnly") != true ||
            ReadBool(answer, "committedOriginMatched") != true ||
            ReadText(answer["result"], "status") != "resolved" ||
            ReadBool(answer["result"], "previewOnly") != true ||
            answer["result"]?["state"] is not JsonObject stateJson ||
            ReadText(stateJson, "originProfileId") != "origin:" + proposalId ||
            ReadText(stateJson, "proposalId") != proposalId ||
            ReadText(stateJson, "rulesetId") != origin.RulesetId ||
            ReadText(stateJson, "catalogFingerprint") != origin.Fingerprint ||
            ReadLong(stateJson, "revision") != 0 ||
            answer["result"]?["appliedCapacityDecisionIds"] is JsonArray { Count: > 0 })
            return null;

        // This is only a candidate; the coordinator + PostgreSQL perform
        // create-once CAS. No user-supplied JSON is accepted as entitlement.
        var ledgerJson = stateJson.ToJsonString();
        try
        {
            CharacterInnateSpellLedgerState.ValidateLedgerJson(ledgerJson,
                origin.RulesetId, origin.Fingerprint, "origin:" + proposalId,
                proposalId, 0);
        }
        catch (ArgumentException)
        {
            return null;
        }
        return new TrustedInnateSpellLedgerCandidate(ledgerJson,
            "origin:" + proposalId, proposalId,
            "native-direct:" + origin.Fingerprint);
    }

    private static string? ReadText(JsonNode? node, string key) =>
        node is JsonObject obj &&
        obj[key] is JsonValue value &&
        value.TryGetValue<string>(out var text)
            ? text : null;

    private static int? ReadInt(JsonNode? node, string key) =>
        node is JsonObject obj &&
        obj[key] is JsonValue value &&
        value.TryGetValue<int>(out var number)
            ? number : null;

    private static long? ReadLong(JsonNode? node, string key) =>
        node is JsonObject obj &&
        obj[key] is JsonValue value &&
        value.TryGetValue<long>(out var number)
            ? number : null;

    private static bool? ReadBool(JsonNode? node, string key) =>
        node is JsonObject obj &&
        obj[key] is JsonValue value &&
        value.TryGetValue<bool>(out var boolean)
            ? boolean : null;
}
