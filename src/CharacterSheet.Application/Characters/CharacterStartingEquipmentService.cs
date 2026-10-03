using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Materializes resolved Rules Core starting-equipment grants into ordinary Character-owned
/// inventory and currency state. This is an acquisition boundary, not a live mirror: materialized
/// state remains ordinary mutable Character state. Stable Core grant provenance makes explicit
/// re-application idempotent and isolates generated state from identical manual acquisitions.
/// </summary>
public sealed class CharacterStartingEquipmentService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    IRulesCoreGateway rulesCoreGateway,
    ICharacterStartingEquipmentInventoryStore inventoryStore,
    TimeProvider timeProvider)
{
    private const string ChoiceKind = "starting-equipment";
    private const string MechanicKind = "starting-equipment";
    private const string ItemGrantKind = "starting-equipment-item";
    private const string CustomGrantKind = "starting-equipment-custom";
    private const string CurrencyGrantKind = "starting-equipment-currency";

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
                    ? "Resolve the remaining starting-equipment choice before applying starting equipment."
                    : $"Resolve the {unresolvedChoices.Length} remaining starting-equipment choices before applying starting equipment.");
        }

        var unresolvedRolls = projection.Mechanics
            .Where(value => string.Equals(
                value.Kind,
                MechanicKind,
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(value => value.RequiredRolls)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (unresolvedRolls.Length > 0)
        {
            throw new InvalidOperationException(
                unresolvedRolls.Length == 1
                    ? "Resolve the remaining starting-equipment roll before applying starting equipment."
                    : $"Resolve the {unresolvedRolls.Length} remaining starting-equipment rolls before applying starting equipment.");
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
                    ? "Resolve the starting-equipment rules conflict before applying starting equipment."
                    : $"Resolve the {equipmentConflicts.Length} starting-equipment rules conflicts before applying starting equipment.");
        }

        var desiredItems = ProjectInventoryItems(projection.Grants);
        var desiredCurrencies = ProjectCurrencyGrants(projection.Grants);
        var root = await inventoryStore.SynchronizeAsync(
            characterId,
            desiredItems,
            desiredCurrencies,
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