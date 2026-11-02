using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public sealed record NativeCharacterResourceActionRequest(
    string ResourceId, int? Spend = null, string? Rest = null);

public enum NativeCharacterResourceActionStatus
{
    Applied, NotFoundOrNotOwned, Unauthenticated, SheetNotInitialized,
    ArchivedReadOnly, Unavailable, Conflict, Unsupported
}

public sealed record NativeCharacterResourceActionResult(
    NativeCharacterResourceActionStatus Status,
    JsonNode? Resolution = null,
    CharacterStateView? State = null,
    string? Message = null);

/// <summary>
/// Orchestrates a single native resource transition. The caller supplies the action,
/// never the current value, character facts, ruleset, or calculated maximum.
/// Full short/long-rest procedures (HP, hit dice, conditions) remain separate.
/// </summary>
public sealed class NativeCharacterResourceService(
    CharacterBuildService builds,
    CharacterStateService states,
    INativeRulesCoreGateway core)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex NativeId = new(
        "^[a-z0-9-]+(?:\\.[a-z0-9-]+)+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<NativeCharacterResourceActionResult> ResolveAsync(
        Guid characterId, NativeCharacterResourceActionRequest action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (string.IsNullOrWhiteSpace(action.ResourceId) ||
            action.ResourceId.Length > 160 ||
            !action.ResourceId.StartsWith("resource.", StringComparison.Ordinal) ||
            !NativeId.IsMatch(action.ResourceId) ||
            (action.Spend.HasValue == (action.Rest is not null)) ||
            action.Spend is <= 0 ||
            (action.Rest is not null && action.Rest is not ("short-rest" or "long-rest")))
            throw new ArgumentException("Specify one supported native resource spend or recovery action.");

        var build = await builds.GetAsync(characterId, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null)
            return new(MapBuildStatus(build.Status));

        var saved = await states.GetAsync(characterId, cancellationToken);
        if (saved.Status != CharacterStateAccessStatus.Ready || saved.View is null)
            return new(MapStateStatus(saved.Status));
        if (build.View.ReadOnly || saved.View.ReadOnly)
            return new(NativeCharacterResourceActionStatus.ArchivedReadOnly);

        var currentInputs = saved.View.RulesInputs ?? [];
        var matching = currentInputs.Where(input =>
            input.Kind == CharacterRulesInputKinds.Resource &&
            input.Key == action.ResourceId).ToArray();
        if (matching.Length != 1 || matching[0].IntegerValue is not int current)
            return new(NativeCharacterResourceActionStatus.Unsupported,
                Message: "Configure a current resource value on the Character Sheet before spending or recovering it.");
        var progression = build.View.ProgressionEntries
            .OrderBy(entry => entry.Ordinal ?? int.MaxValue).ThenBy(entry => entry.Id)
            .ToArray();
        if (progression.Length == 0 || progression.Any(entry =>
            entry.Kind != CharacterBuildAdvancementKinds.Class ||
            entry.Level is not > 0 ||
            !entry.RuleConceptKey.StartsWith("class.", StringComparison.Ordinal) ||
            !NativeId.IsMatch(entry.RuleConceptKey)))
            return new(NativeCharacterResourceActionStatus.Unsupported,
                Message: "This progression does not yet have a complete native class resource evaluator.");

        var choices = NativeCharacterOptionSelections.FromState(saved.View);
        if (choices.HasUnmappedChoices)
            return new(NativeCharacterResourceActionStatus.Unsupported,
                Message: "Character rule choices have not all been mapped to native identities.");

        try
        {
            var metadata = await core.GetNativeRulesetAsync(cancellationToken);
            var rulesetId = Text(metadata, "rulesetId");
            var fingerprint = Text(metadata, "fingerprint");
            if (Int(metadata, "contractVersion") != 1 ||
                string.IsNullOrWhiteSpace(rulesetId) ||
                fingerprint is null || !Regex.IsMatch(fingerprint, "^[0-9a-f]{64}$") ||
                metadata["capabilities"]?["resourceResolution"]?.GetValue<bool>() != true)
                throw new RulesCoreGatewayException("Rules Core does not advertise native resource evaluation.");

            var resources = currentInputs
                .Where(input => input.Kind == CharacterRulesInputKinds.Resource &&
                    input.IntegerValue is not null)
                .GroupBy(input => input.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(input => input.UpdatedAt)
                        .First().IntegerValue!.Value, StringComparer.Ordinal);
            var abilities = build.View.BaseAbilityScoreInputs
                .GroupBy(entry => entry.AbilityKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => group.OrderByDescending(entry => entry.UpdatedAt).First().Score,
                    StringComparer.Ordinal);
            var hitPoints = (saved.View.HitPointGains ?? [])
                .Where(gain => gain.ClassLevel > 0 && gain.HitDieValue >= 0)
                .GroupBy(gain => gain.AdvancementOccurrenceId.ToString("D") + ":" + gain.ClassLevel,
                    StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last().HitDieValue,
                    StringComparer.Ordinal);
            var character = JsonSerializer.SerializeToElement(new
            {
                rulesetId,
                fingerprint,
                abilityScores = abilities,
                advancements = progression.Select(entry => new {
                    classId = entry.RuleConceptKey, level = entry.Level!.Value,
                    occurrenceId = entry.Id.ToString("D")
                }).ToArray(),
                selectedOptions = choices.Ids,
                currentResources = resources,
                hitPointGains = hitPoints.Count == 0 ? null : hitPoints
            }, JsonOptions);

            var projected = await core.ProjectNativeCharacterAsync(character, cancellationToken);
            if (Text(projected, "rulesetId") != rulesetId ||
                Text(projected, "fingerprint") != fingerprint ||
                Int(projected, "contractVersion") != 1)
                throw new RulesCoreGatewayException("The native resource projection snapshot changed.");
            if (Text(projected, "status") == "invalid" ||
                projected["resources"] is not JsonArray list)
                return new(NativeCharacterResourceActionStatus.Unsupported,
                    Message: "The native Character projection does not resolve this resource.");

            var matchingResources = list.Where(resource =>
                Text(resource, "id") == action.ResourceId).Take(2).ToArray();
            if (matchingResources.Length != 1 ||
                Int(matchingResources[0], "current") != current ||
                Int(matchingResources[0], "maximum") is not int maximum ||
                maximum < 0 || current < 0 || current > maximum)
                return new(NativeCharacterResourceActionStatus.Unsupported,
                    Message: "The current value or maximum is not resolved by native Rules Core.");

            var rest = action.Rest switch {
                "short-rest" => (int?)0,
                "long-rest" => (int?)1,
                _ => null
            };
            var actionRequest = JsonSerializer.SerializeToElement(new {
                character, resourceId = action.ResourceId, current,
                spend = action.Spend, rest
            }, JsonOptions);
            var result = await core.ResolveNativeResourceAsync(
                actionRequest, cancellationToken);
            var next = Int(result, "current");
            if (Text(result, "status") != "resolved" ||
                Text(result, "resourceId") != action.ResourceId ||
                Int(result, "maximum") != maximum ||
                next is not int changed || changed < 0 || changed > maximum)
                return new(NativeCharacterResourceActionStatus.Unsupported,
                    Message: "Rules Core did not resolve a valid native resource transition.");

            var applied = await states.ApplyIntegerStateMutationsAsync(
                characterId, [new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.Resource,
                    CharacterIntegerStateMutationOperation.Set,
                    changed, action.ResourceId, ExpectedValue: current)],
                cancellationToken);
            if (applied.Status != CharacterStateAccessStatus.Ready || applied.View is null)
                return new(MapStateStatus(applied.Status));
            return new(NativeCharacterResourceActionStatus.Applied, result, applied.View);
        }
        catch (RulesCoreGatewayException exception)
        {
            return new(exception.StatusCode == 409
                ? NativeCharacterResourceActionStatus.Conflict
                : exception.StatusCode == 422
                    ? NativeCharacterResourceActionStatus.Unsupported
                    : NativeCharacterResourceActionStatus.Unavailable,
                Message: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new(NativeCharacterResourceActionStatus.Conflict,
                Message: exception.Message);
        }
    }

    private static string? Text(JsonNode? node, string key) =>
        node is JsonObject obj ? obj[key]?.GetValue<string>() : null;

    private static int? Int(JsonNode? node, string key) =>
        node is JsonObject obj && obj[key] is JsonValue value &&
        value.TryGetValue<int>(out var number) ? number : null;

    private static NativeCharacterResourceActionStatus MapBuildStatus(CharacterBuildAccessStatus status) =>
        status switch {
            CharacterBuildAccessStatus.NotFoundOrNotOwned =>
                NativeCharacterResourceActionStatus.NotFoundOrNotOwned,
            CharacterBuildAccessStatus.Unauthenticated =>
                NativeCharacterResourceActionStatus.Unauthenticated,
            CharacterBuildAccessStatus.SheetNotInitialized =>
                NativeCharacterResourceActionStatus.SheetNotInitialized,
            CharacterBuildAccessStatus.ArchivedReadOnly =>
                NativeCharacterResourceActionStatus.ArchivedReadOnly,
            _ => NativeCharacterResourceActionStatus.Unavailable
        };

    private static NativeCharacterResourceActionStatus MapStateStatus(CharacterStateAccessStatus status) =>
        status switch {
            CharacterStateAccessStatus.NotFoundOrNotOwned =>
                NativeCharacterResourceActionStatus.NotFoundOrNotOwned,
            CharacterStateAccessStatus.Unauthenticated =>
                NativeCharacterResourceActionStatus.Unauthenticated,
            CharacterStateAccessStatus.SheetNotInitialized =>
                NativeCharacterResourceActionStatus.SheetNotInitialized,
            CharacterStateAccessStatus.ArchivedReadOnly =>
                NativeCharacterResourceActionStatus.ArchivedReadOnly,
            _ => NativeCharacterResourceActionStatus.Unavailable
        };
}
