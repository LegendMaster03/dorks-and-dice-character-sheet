using System.Security.Cryptography;
using System.Text;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Applies the starting-equipment grants produced by the current Rules Core Character projection.
/// Rules Core owns which Class/Background rule is effective and what that rule grants; Character
/// Sheet only persists the resulting Character-owned inventory and currency once all genuine
/// Character choices are resolved.
/// </summary>
public sealed class CharacterStartingEquipmentService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    ICharacterStateStore stateStore,
    IRulesCoreGateway rulesCore,
    TimeProvider timeProvider)
{
    private const string StartingEquipmentChoiceKind = "starting-equipment";
    private const string ItemGrantKind = "starting-equipment-item";
    private const string CustomGrantKind = "starting-equipment-custom";
    private const string CurrencyGrantKind = "starting-equipment-currency";

    public async Task<CharacterStateResult> ApplyAsync(
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
        if (buildResult.View.ReadOnly || stateResult.View.ReadOnly)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.ArchivedReadOnly);
        }

        RulesCoreCharacterRulesProjectionView projection;
        try
        {
            projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
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
                StartingEquipmentChoiceKind,
                StringComparison.OrdinalIgnoreCase))
            .Where(value => !string.Equals(
                value.State,
                "resolved",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (unresolvedChoices.Length > 0)
        {
            throw new InvalidOperationException(
                "Complete all starting-equipment choices before adding starting equipment to the Character.");
        }

        var sourceConflicts = projection.Conflicts
            .Where(value => value.ConflictKey.Contains(
                "starting-equipment",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sourceConflicts.Length > 0)
        {
            throw new InvalidOperationException(sourceConflicts[0].Message);
        }

        var grants = projection.Grants
            .Where(IsStartingEquipmentGrant)
            .OrderBy(value => value.GrantKey, StringComparer.Ordinal)
            .ToArray();
        if (grants.Length == 0)
        {
            throw new InvalidOperationException(
                "The resolved Class and Background rules do not currently produce starting-equipment grants.");
        }

        var additions = grants
            .Where(value => string.Equals(value.Kind, ItemGrantKind, StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => value.TargetKey, StringComparer.Ordinal)
            .Select(group => new CharacterInventoryAddition(
                group.Key,
                CustomName: null,
                group.Count()))
            .Concat(
                grants
                    .Where(value => string.Equals(
                        value.Kind,
                        CustomGrantKind,
                        StringComparison.OrdinalIgnoreCase))
                    .GroupBy(
                        value => (value.TargetKey, value.DisplayName),
                        StartingEquipmentCustomGrantComparer.Instance)
                    .Select(group => new CharacterInventoryAddition(
                        RuleConceptKey: null,
                        group.Key.DisplayName,
                        group.Count())))
            .ToArray();

        var currencyAdditions = grants
            .Where(value => string.Equals(
                value.Kind,
                CurrencyGrantKind,
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(value => value.TargetKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key.Trim().ToLowerInvariant(),
                group => checked((long)group.Count()),
                StringComparer.Ordinal);

        var fingerprint = Fingerprint(grants);
        var persisted = await stateStore.ApplyStartingEquipmentAsync(
            characterId,
            additions,
            currencyAdditions,
            fingerprint,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (persisted is null)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.SheetNotInitialized);
        }

        return await stateService.GetAsync(characterId, cancellationToken);
    }

    private static bool IsStartingEquipmentGrant(RulesCoreCharacterGrantView value) =>
        string.Equals(value.Kind, ItemGrantKind, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value.Kind, CustomGrantKind, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value.Kind, CurrencyGrantKind, StringComparison.OrdinalIgnoreCase);

    private static string Fingerprint(IReadOnlyList<RulesCoreCharacterGrantView> grants)
    {
        var canonical = string.Join(
            '\n',
            grants.Select(value =>
                $"{value.GrantKey}|{value.Kind}|{value.TargetKey}|{value.DisplayName}|{value.SourceConceptKey}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static CharacterStateAccessStatus MapBuildStatus(CharacterBuildAccessStatus status) =>
        status switch
        {
            CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterStateAccessStatus.NotFoundOrNotOwned,
            CharacterBuildAccessStatus.ProjectionUnavailable => CharacterStateAccessStatus.ProjectionUnavailable,
            CharacterBuildAccessStatus.Unauthenticated => CharacterStateAccessStatus.Unauthenticated,
            CharacterBuildAccessStatus.SheetNotInitialized => CharacterStateAccessStatus.SheetNotInitialized,
            CharacterBuildAccessStatus.ArchivedReadOnly => CharacterStateAccessStatus.ArchivedReadOnly,
            _ => CharacterStateAccessStatus.ProjectionUnavailable
        };

    private sealed class StartingEquipmentCustomGrantComparer
        : IEqualityComparer<(string TargetKey, string DisplayName)>
    {
        public static readonly StartingEquipmentCustomGrantComparer Instance = new();

        public bool Equals(
            (string TargetKey, string DisplayName) x,
            (string TargetKey, string DisplayName) y) =>
            string.Equals(x.TargetKey, y.TargetKey, StringComparison.Ordinal)
            && string.Equals(x.DisplayName, y.DisplayName, StringComparison.Ordinal);

        public int GetHashCode((string TargetKey, string DisplayName) obj) =>
            HashCode.Combine(obj.TargetKey, obj.DisplayName);
    }
}
