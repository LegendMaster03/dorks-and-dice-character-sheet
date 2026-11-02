using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

public sealed record NativeStartingEquipmentPreviewResult(
    CharacterStateAccessStatus Status, JsonNode? Projection = null);

/// <summary>
/// Materializes resolved Rules Core starting-equipment grants into ordinary Character-owned
/// inventory and currency state. This is an acquisition boundary, not a live mirror: materialized
/// state remains ordinary mutable Character state. A server-owned receipt makes
/// repeat requests idempotent even when the active ruleset is later revised.
/// </summary>
public sealed class CharacterStartingEquipmentService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    INativeRulesCoreGateway nativeCore,
    ICharacterStartingEquipmentInventoryStore inventoryStore,
    TimeProvider timeProvider)
{
    public async Task<CharacterStateResult> MaterializeInventoryAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        // Starting equipment is an initial acquisition, not a live inventory
        // projection. A repeated call must not require the original ruleset
        // definition to remain available or unchanged after the first award.
        var snapshot = await stateService.GetAsync(characterId, cancellationToken);
        if (snapshot.Status != CharacterStateAccessStatus.Ready ||
            snapshot.View is null)
            return snapshot;
        if (snapshot.View.ReadOnly)
            return new CharacterStateResult(CharacterStateAccessStatus.ArchivedReadOnly);
        if (snapshot.View.RulesInputs?.Any(input =>
            input.Kind == CharacterRulesInputKinds.BooleanFact &&
            input.Key == CharacterStartingEquipmentReceipt.Key &&
            input.BooleanValue == true) == true)
            return snapshot;
        var preview = await PreviewAsync(characterId, cancellationToken);
        if (preview.Status != CharacterStateAccessStatus.Ready ||
            preview.Projection is null)
            return new CharacterStateResult(preview.Status);
        var projection = preview.Projection;
        if (projection["status"]?.GetValue<string>() != "resolved" ||
            projection["grants"] is not JsonArray grants ||
            projection["issues"] is not JsonArray issues || issues.Count != 0)
            return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
        try
        {
            var items = new List<CharacterStartingEquipmentInventoryItem>();
            var currencies = new List<CharacterStartingEquipmentCurrencyGrant>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var grant in grants)
            {
                if (grant is not JsonObject)
                    return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
                var key = grant["grantKey"]?.GetValue<string>();
                var kind = grant["kind"]?.GetValue<string>();
                var target = grant["targetKey"]?.GetValue<string>();
                var name = grant["displayName"]?.GetValue<string>();
                var quantity = grant["quantity"]?.GetValue<int>();
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(target) ||
                    quantity is not > 0 || !keys.Add(key))
                    return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
                switch (kind)
                {
                    case "item" when target.StartsWith("item.", StringComparison.Ordinal):
                        items.Add(new(key, target, null, quantity.Value));
                        break;
                    case "custom" when !string.IsNullOrWhiteSpace(name):
                        items.Add(new(key, null, name, quantity.Value));
                        break;
                    case "currency":
                        currencies.Add(new(key, target, quantity.Value));
                        break;
                    default:
                        return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
                }
            }
            var root = await inventoryStore.ApplyOnceAsync(
                characterId, items, currencies, timeProvider.GetUtcNow(), cancellationToken);
            return root is null
                ? new CharacterStateResult(CharacterStateAccessStatus.SheetNotInitialized)
                : await stateService.GetAsync(characterId, cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
        }
    }

    /// <summary>
    /// Returns non-mutating, native-only package choices and grant status.
    /// Unavailable content is a valid preview result but never an award.
    /// </summary>
    public async Task<NativeStartingEquipmentPreviewResult> PreviewAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready ||
            buildResult.View is null)
            return new(MapBuildStatus(buildResult.Status));

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status != CharacterStateAccessStatus.Ready ||
            stateResult.View is null)
            return new(stateResult.Status);

        try
        {
            var metadata = await nativeCore.GetNativeRulesetAsync(cancellationToken);
            if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                metadata["capabilities"]?["startingEquipmentResolution"]?.GetValue<string>()
                    != "authored-grants-only")
                return new(CharacterStateAccessStatus.ProjectionUnavailable);
            var rulesetId = metadata["rulesetId"]?.GetValue<string>();
            var fingerprint = metadata["fingerprint"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(rulesetId) ||
                string.IsNullOrWhiteSpace(fingerprint))
                return new(CharacterStateAccessStatus.ProjectionUnavailable);

            // Feats, subclasses, prestige tracks, and future advancement kinds
            // coexist in a Character history; only Class occurrences supply
            // the initial equipment selection and must be sent to Core.
            var classHistory = buildResult.View.ProgressionEntries
                .Where(entry => entry.Kind == CharacterBuildAdvancementKinds.Class)
                .OrderBy(entry => entry.Ordinal ?? int.MaxValue)
                .ThenBy(entry => entry.Id)
                .ToArray();
            if (classHistory.Length == 0 || classHistory.Any(entry =>
                entry.Kind != CharacterBuildAdvancementKinds.Class ||
                entry.Level is not > 0 ||
                !entry.RuleConceptKey.StartsWith("class.", StringComparison.Ordinal)))
                return new(CharacterStateAccessStatus.ProjectionUnavailable);

            // A class can be changed while its rules remain under development.
            // Old package selections for replaced class occurrences are not
            // selections for the current initial class and cannot block it.
            var activeChoiceKey = "starting-equipment." + classHistory[0].Id.ToString("D");
            var selected = (stateResult.View.RulesInputs ?? [])
                .Where(entry => entry.Kind == CharacterRulesInputKinds.Choice &&
                    entry.Key == activeChoiceKey &&
                    entry.TextValue is not null)
                .ToDictionary(entry => entry.Key, entry => entry.TextValue!,
                    StringComparer.Ordinal);
            var request = JsonSerializer.SerializeToElement(new
            {
                rulesetId, fingerprint,
                advancements = classHistory.Select(entry => new
                {
                    classId = entry.RuleConceptKey,
                    level = entry.Level!.Value,
                    occurrenceId = entry.Id.ToString("D")
                }).ToArray(),
                selectedPackages = selected
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var projection = await nativeCore.ResolveNativeStartingEquipmentAsync(
                request, cancellationToken);
            if (projection["contractVersion"]?.GetValue<int>() != 1 ||
                projection["rulesetId"]?.GetValue<string>() != rulesetId ||
                projection["fingerprint"]?.GetValue<string>() != fingerprint ||
                projection["status"]?.GetValue<string>() is not
                    ("resolved" or "needs-choices" or "unavailable") ||
                projection["grants"] is not JsonArray ||
                projection["choices"] is not JsonArray ||
                projection["issues"] is not JsonArray)
                return new(CharacterStateAccessStatus.ProjectionUnavailable);

            return new(CharacterStateAccessStatus.Ready, projection);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterStateAccessStatus.ProjectionUnavailable);
        }
    }

    private static CharacterStateAccessStatus MapBuildStatus(
        CharacterBuildAccessStatus status) => status switch
    {
        CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterStateAccessStatus.NotFoundOrNotOwned,
        CharacterBuildAccessStatus.ProjectionUnavailable => CharacterStateAccessStatus.ProjectionUnavailable,
        CharacterBuildAccessStatus.Unauthenticated => CharacterStateAccessStatus.Unauthenticated,
        CharacterBuildAccessStatus.SheetNotInitialized => CharacterStateAccessStatus.SheetNotInitialized,
        CharacterBuildAccessStatus.ArchivedReadOnly => CharacterStateAccessStatus.ArchivedReadOnly,
        _ => CharacterStateAccessStatus.ProjectionUnavailable
    };
}