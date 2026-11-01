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
/// state remains ordinary mutable Character state. Stable Core grant provenance makes explicit
/// repeated requests idempotent. Awarded equipment is subsequently ordinary inventory.
/// </summary>
public sealed class CharacterStartingEquipmentService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    INativeRulesCoreGateway nativeCore,
    ICharacterStartingEquipmentInventoryStore inventoryStore,
    TimeProvider timeProvider)
{
    private const string ItemGrantKind = "starting-equipment-item";
    private const string CustomGrantKind = "starting-equipment-custom";
    private const string CurrencyGrantKind = "starting-equipment-currency";

    public async Task<CharacterStateResult> MaterializeInventoryAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(characterId, cancellationToken);
        if (preview.Status != CharacterStateAccessStatus.Ready ||
            preview.Projection is null)
            return new CharacterStateResult(preview.Status);
        var snapshot = await stateService.GetAsync(characterId, cancellationToken);
        if (snapshot.Status != CharacterStateAccessStatus.Ready ||
            snapshot.View is null)
            return snapshot;
        if (snapshot.View.ReadOnly)
            return new CharacterStateResult(CharacterStateAccessStatus.ArchivedReadOnly);
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

            var classHistory = buildResult.View.ProgressionEntries;
            if (classHistory.Count == 0 || classHistory.Any(entry =>
                entry.Kind != CharacterBuildAdvancementKinds.Class ||
                entry.Level is not > 0 ||
                !entry.RuleConceptKey.StartsWith("class.", StringComparison.Ordinal)))
                return new(CharacterStateAccessStatus.ProjectionUnavailable);

            var selected = (stateResult.View.RulesInputs ?? [])
                .Where(entry => entry.Kind == CharacterRulesInputKinds.Choice &&
                    entry.Key.StartsWith("starting-equipment.", StringComparison.Ordinal) &&
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

    internal static IReadOnlyList<CharacterStartingEquipmentInventoryItem> ProjectInventoryItems(
        IReadOnlyList<RulesCoreCharacterGrantView> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        return grants
            .Where(value =>
                string.Equals(value.Kind, ItemGrantKind, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value.Kind, CustomGrantKind, StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => new
            {
                SourceGrantKey = GrantFamilyKey(value.GrantKey),
                Kind = value.Kind.ToLowerInvariant(),
                TargetKey = value.TargetKey,
                DisplayName = value.DisplayName
            })
            .Select(group =>
            {
                var first = group.First();
                return string.Equals(first.Kind, ItemGrantKind, StringComparison.OrdinalIgnoreCase)
                    ? new CharacterStartingEquipmentInventoryItem(
                        group.Key.SourceGrantKey,
                        first.TargetKey,
                        CustomName: null,
                        group.Count())
                    : new CharacterStartingEquipmentInventoryItem(
                        group.Key.SourceGrantKey,
                        RuleConceptKey: null,
                        first.DisplayName,
                        group.Count());
            })
            .OrderBy(value => value.SourceGrantKey, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> ProjectCurrencyGrants(
        IReadOnlyList<RulesCoreCharacterGrantView> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        return grants
            .Where(value => string.Equals(
                value.Kind,
                CurrencyGrantKind,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => new
            {
                SourceGrantKey = GrantFamilyKey(value.GrantKey),
                CurrencyKey = value.TargetKey
            })
            .Select(group => new CharacterStartingEquipmentCurrencyGrant(
                group.Key.SourceGrantKey,
                group.Key.CurrencyKey,
                group.LongCount()))
            .OrderBy(value => value.SourceGrantKey, StringComparer.Ordinal)
            .ToArray();
    }

    private static string GrantFamilyKey(string grantKey)
    {
        if (string.IsNullOrWhiteSpace(grantKey))
        {
            throw new ArgumentException("Rules Core starting-equipment grant key can not be blank.");
        }

        var normalized = grantKey.Trim();
        var finalSeparator = normalized.LastIndexOf('.');
        if (finalSeparator > 0
            && int.TryParse(normalized[(finalSeparator + 1)..], out _))
        {
            return normalized[..finalSeparator];
        }
        return normalized;
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