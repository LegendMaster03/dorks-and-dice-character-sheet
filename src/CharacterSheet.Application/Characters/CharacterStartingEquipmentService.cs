using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Materializes resolved Rules Core starting-equipment item grants into ordinary Character-owned
/// inventory. This is an acquisition boundary, not a live mirror: inventory remains ordinary
/// mutable Character state after materialization. Stable Core grant provenance makes explicit
/// re-application idempotent and isolates generated occurrences from identical manual inventory.
/// </summary>
public sealed class CharacterStartingEquipmentService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    IRulesCoreGateway rulesCoreGateway,
    ICharacterStartingEquipmentInventoryStore inventoryStore,
    TimeProvider timeProvider)
{
    private const string ChoiceKind = "starting-equipment";
    private const string ItemGrantKind = "starting-equipment-item";
    private const string CustomGrantKind = "starting-equipment-custom";

    public async Task<CharacterStateResult> MaterializeInventoryAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
        {
            return new CharacterStateResult(MapBuildStatus(buildResult.Status));
        }

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status != CharacterStateAccessStatus.Ready || stateResult.View is null)
        {
            return stateResult;
        }
        if (stateResult.View.ReadOnly || buildResult.View.ReadOnly)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.ArchivedReadOnly);
        }

        RulesCoreCharacterRulesProjectionView projection;
        try
        {
            projection = await rulesCoreGateway.ResolveGlobalCharacterMechanicsAsync(
                CharacterRulesProjectionRequestBuilder.Build(
                    buildResult.View,
                    stateResult.View),
                cancellationToken);
        }
        catch (RulesCoreGatewayException)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.ProjectionUnavailable);
        }

        var unresolvedChoices = projection.Choices
            .Where(value => string.Equals(
                value.Kind,
                ChoiceKind,
                StringComparison.OrdinalIgnoreCase))
            .Where(value => !string.Equals(
                value.State,
                "resolved",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unresolvedChoices.Length > 0)
        {
            throw new InvalidOperationException(
                unresolvedChoices.Length == 1
                    ? "Resolve the remaining starting-equipment choice before adding starting items to Inventory."
                    : $"Resolve the {unresolvedChoices.Length} remaining starting-equipment choices before adding starting items to Inventory.");
        }

        var equipmentConflicts = projection.Conflicts
            .Where(value => value.ConflictKey.Contains(
                "starting-equipment",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (equipmentConflicts.Length > 0)
        {
            throw new InvalidOperationException(
                equipmentConflicts.Length == 1
                    ? "Resolve the starting-equipment rules conflict before adding starting items to Inventory."
                    : $"Resolve the {equipmentConflicts.Length} starting-equipment rules conflicts before adding starting items to Inventory.");
        }

        var desiredItems = ProjectInventoryItems(projection.Grants);
        var root = await inventoryStore.SynchronizeAsync(
            characterId,
            desiredItems,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (root is null)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.SheetNotInitialized);
        }

        return await stateService.GetAsync(characterId, cancellationToken);
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
