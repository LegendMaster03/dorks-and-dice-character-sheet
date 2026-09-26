using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterBuildAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized,
    ArchivedReadOnly
}

public static class CharacterBuildSelectionCategories
{
    public const string Species = "species";
    public const string Subspecies = "subspecies";
    public const string LegacyRaceSpecies = "raceSpecies";
    public const string Background = "background";
    public const string Deity = "deity";
}

public static class CharacterBuildAdvancementKinds
{
    public const string Class = "class";
    public const string Subclass = "subclass";
    public const string PrestigeClass = "prestigeClass";
    public const string Feat = "feat";
}

public sealed record FoundationalRuleSelectionView(
    Guid Id,
    string Category,
    string RuleConceptKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterAdvancementEntryView(
    Guid Id,
    int? Ordinal,
    string Kind,
    string RuleConceptKey,
    Guid? ParentAdvancementEntryId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int? Level = null);

public sealed record BaseAbilityScoreInputView(
    Guid Id,
    string AbilityKey,
    int Score,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterBuildView(
    Guid CharacterId,
    string BuilderStatus,
    bool ReadOnly,
    IReadOnlyList<FoundationalRuleSelectionView> FoundationalSelections,
    IReadOnlyList<BaseAbilityScoreInputView> BaseAbilityScoreInputs,
    IReadOnlyList<CharacterAdvancementEntryView> ProgressionEntries);

public sealed record CharacterBuildResult(
    CharacterBuildAccessStatus Status,
    CharacterBuildView? View = null);

public sealed class CharacterBuildService(
    ISiteCharacterAccessGateway siteCharacterAccess,
    ICharacterBuildStore buildStore,
    TimeProvider timeProvider,
    IRulesCoreGateway? rulesCore = null)
{
    public async Task<CharacterBuildResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var root = await buildStore.GetAsync(characterId, cancellationToken);
        if (root is null)
        {
            return new CharacterBuildResult(CharacterBuildAccessStatus.SheetNotInitialized);
        }

        return Ready(root, access.Character!);
    }

    public Task<CharacterBuildResult> SetSpeciesAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            async (changedAt, token) =>
            {
                var normalizedSpeciesKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
                var current = await buildStore.GetAsync(characterId, token);
                var previousSpecies = current?.FoundationalSelections.FirstOrDefault(value =>
                    value.Category is CharacterFoundationalSelectionCategory.Species
                        or CharacterFoundationalSelectionCategory.RaceSpecies);
                var currentSubspecies = current?.FoundationalSelections.FirstOrDefault(value =>
                    value.Category == CharacterFoundationalSelectionCategory.Subspecies);

                if (previousSpecies is not null
                    && !string.Equals(previousSpecies.RuleConceptKey, normalizedSpeciesKey, StringComparison.Ordinal)
                    && currentSubspecies is not null
                    && !await IsSubspeciesCompatibleWithSpeciesAsync(
                        currentSubspecies.RuleConceptKey,
                        normalizedSpeciesKey,
                        token))
                {
                    _ = await buildStore.ClearFoundationalSelectionAsync(
                        characterId,
                        CharacterFoundationalSelectionCategory.Subspecies,
                        changedAt,
                        token);
                }

                _ = await buildStore.ClearFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    changedAt,
                    token);
                return await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.Species,
                    normalizedSpeciesKey,
                    changedAt,
                    token);
            },
            cancellationToken);

    public Task<CharacterBuildResult> ClearSpeciesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            async (changedAt, token) =>
            {
                _ = await buildStore.ClearFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.Subspecies,
                    changedAt,
                    token);
                _ = await buildStore.ClearFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    changedAt,
                    token);
                return await buildStore.ClearFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.Species,
                    changedAt,
                    token);
            },
            cancellationToken);

    public Task<CharacterBuildResult> SetSubspeciesAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            async (changedAt, token) =>
            {
                var root = await buildStore.GetAsync(characterId, token)
                    ?? throw new InvalidOperationException("Character Sheet is not initialized.");
                var species = root.FoundationalSelections.FirstOrDefault(value =>
                    value.Category is CharacterFoundationalSelectionCategory.Species
                        or CharacterFoundationalSelectionCategory.RaceSpecies);
                if (species is null)
                {
                    throw new InvalidOperationException(
                        "Choose a Species before selecting a Subspecies.");
                }

                var normalizedSubspeciesKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
                if (!await IsSubspeciesCompatibleWithSpeciesAsync(
                        normalizedSubspeciesKey,
                        species.RuleConceptKey,
                        token))
                {
                    throw new InvalidOperationException(
                        "The selected Subspecies does not belong to the selected Species.");
                }

                return await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.Subspecies,
                    normalizedSubspeciesKey,
                    changedAt,
                    token);
            },
            cancellationToken);

    public Task<CharacterBuildResult> ClearSubspeciesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.ClearFoundationalSelectionAsync(
                characterId,
                CharacterFoundationalSelectionCategory.Subspecies,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> SetRaceSpeciesAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        SetSpeciesAsync(characterId, ruleConceptKey, cancellationToken);

    public Task<CharacterBuildResult> ClearRaceSpeciesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        ClearSpeciesAsync(characterId, cancellationToken);

    public Task<CharacterBuildResult> SetBackgroundAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetFoundationalSelectionAsync(
            characterId, CharacterFoundationalSelectionCategory.Background, ruleConceptKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> ClearBackgroundAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.ClearFoundationalSelectionAsync(
            characterId, CharacterFoundationalSelectionCategory.Background, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> SetDeityAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetFoundationalSelectionAsync(
            characterId, CharacterFoundationalSelectionCategory.Deity, ruleConceptKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> ClearDeityAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.ClearFoundationalSelectionAsync(
            characterId, CharacterFoundationalSelectionCategory.Deity, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> SetBaseAbilityScoreInputAsync(
        Guid characterId,
        string abilityKey,
        int score,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetBaseAbilityScoreInputAsync(
            characterId, abilityKey, score, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> ClearBaseAbilityScoreInputAsync(
        Guid characterId,
        string abilityKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.ClearBaseAbilityScoreInputAsync(
            characterId, abilityKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> SetStartingClassAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetStartingClassAsync(
            characterId, ruleConceptKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> ClearStartingClassAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.ClearStartingClassAsync(
            characterId, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> SetSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetSubclassAsync(
            characterId, classAdvancementEntryId, ruleConceptKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> ClearSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.ClearSubclassAsync(
            characterId, classAdvancementEntryId, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> SetAdvancementLevelAsync(
        Guid characterId,
        Guid advancementEntryId,
        int level,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.SetAdvancementLevelAsync(
            characterId, advancementEntryId, level, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> AddFeatOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.AddFeatOccurrenceAsync(
            characterId, ruleConceptKey, changedAt, token), cancellationToken);

    public Task<CharacterBuildResult> RemoveFeatOccurrenceAsync(
        Guid characterId,
        Guid featAdvancementEntryId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(characterId, (changedAt, token) => buildStore.RemoveFeatOccurrenceAsync(
            characterId, featAdvancementEntryId, changedAt, token), cancellationToken);

    private async Task<bool> IsSubspeciesCompatibleWithSpeciesAsync(
        string subspeciesConceptKey,
        string speciesConceptKey,
        CancellationToken cancellationToken)
    {
        if (rulesCore is null)
        {
            return false;
        }

        var normalizedSubspecies = CharacterRuleReference.NormalizeConceptKey(subspeciesConceptKey);
        var normalizedSpecies = CharacterRuleReference.NormalizeConceptKey(speciesConceptKey);
        var resolved = await rulesCore.ResolveGlobalRulesAsync(
            [normalizedSubspecies, normalizedSpecies],
            cancellationToken);
        if (!resolved.TryGetValue(normalizedSubspecies, out var subspecies)
            || !resolved.TryGetValue(normalizedSpecies, out var species)
            || !IsCanonicalEntityType(subspecies.EntityType, "subspecies", "subrace")
            || !IsCanonicalEntityType(species.EntityType, "species", "race"))
        {
            return false;
        }

        return (subspecies.Relationships ?? [])
            .Any(relationship =>
                string.Equals(relationship.Kind, "parent-species", StringComparison.Ordinal)
                && IsCanonicalEntityType(relationship.RelatedEntityType, "species", "race")
                && string.Equals(relationship.RelatedConceptKey, normalizedSpecies, StringComparison.Ordinal));
    }

    private static bool IsCanonicalEntityType(string actual, string canonical, string legacy) =>
        string.Equals(actual, canonical, StringComparison.OrdinalIgnoreCase)
        || string.Equals(actual, legacy, StringComparison.OrdinalIgnoreCase);

    private async Task<CharacterBuildResult> MutateAsync(
        Guid characterId,
        Func<DateTimeOffset, CancellationToken, Task<CharacterSheetRoot?>> mutation,
        CancellationToken cancellationToken)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var character = access.Character!;
        if (!character.AllowsOrdinaryEditingByLifecycle)
        {
            return new CharacterBuildResult(CharacterBuildAccessStatus.ArchivedReadOnly);
        }

        var root = await mutation(timeProvider.GetUtcNow(), cancellationToken);
        if (root is null)
        {
            return new CharacterBuildResult(CharacterBuildAccessStatus.SheetNotInitialized);
        }

        return Ready(root, character);
    }

    private static CharacterBuildResult Ready(CharacterSheetRoot root, SiteCharacterProjection character) =>
        new(CharacterBuildAccessStatus.Ready, ToView(root, !character.AllowsOrdinaryEditingByLifecycle));

    private static CharacterBuildResult? MapDeniedAccess(SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Authorized => null,
        SiteCharacterAccessStatus.NotFoundOrNotOwned => new(CharacterBuildAccessStatus.NotFoundOrNotOwned),
        SiteCharacterAccessStatus.ProjectionUnavailable => new(CharacterBuildAccessStatus.ProjectionUnavailable),
        SiteCharacterAccessStatus.Unauthenticated => new(CharacterBuildAccessStatus.Unauthenticated),
        _ => new(CharacterBuildAccessStatus.ProjectionUnavailable)
    };

    private static CharacterBuildView ToView(CharacterSheetRoot root, bool readOnly)
    {
        var foundational = root.FoundationalSelections
            .GroupBy(value => value.Category == CharacterFoundationalSelectionCategory.RaceSpecies
                ? CharacterFoundationalSelectionCategory.Species
                : value.Category)
            .Select(group => group
                .OrderByDescending(value => value.Category == CharacterFoundationalSelectionCategory.Species)
                .ThenByDescending(value => value.UpdatedAt)
                .First())
            .OrderBy(value => value.Category)
            .ThenBy(value => value.Id)
            .Select(value => new FoundationalRuleSelectionView(
                value.Id,
                MapCategory(value.Category),
                value.RuleConceptKey,
                value.CreatedAt,
                value.UpdatedAt))
            .ToArray();

        return new CharacterBuildView(
            root.CharacterId,
            root.BuilderStatus.ToString(),
            readOnly,
            foundational,
            root.BaseAbilityScoreInputs
                .OrderBy(value => value.AbilityKey, StringComparer.Ordinal)
                .ThenBy(value => value.Id)
                .Select(value => new BaseAbilityScoreInputView(
                    value.Id, value.AbilityKey, value.Score, value.CreatedAt, value.UpdatedAt))
                .ToArray(),
            root.AdvancementEntries
                .OrderBy(value => value.Ordinal ?? int.MaxValue)
                .ThenBy(value => value.Kind)
                .ThenBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => new CharacterAdvancementEntryView(
                    value.Id,
                    value.Ordinal,
                    MapKind(value.Kind),
                    value.RuleConceptKey,
                    value.ParentAdvancementEntryId,
                    value.CreatedAt,
                    value.UpdatedAt,
                    value.Level))
                .ToArray());
    }

    private static string MapCategory(CharacterFoundationalSelectionCategory category) => category switch
    {
        CharacterFoundationalSelectionCategory.RaceSpecies => CharacterBuildSelectionCategories.Species,
        CharacterFoundationalSelectionCategory.Species => CharacterBuildSelectionCategories.Species,
        CharacterFoundationalSelectionCategory.Subspecies => CharacterBuildSelectionCategories.Subspecies,
        CharacterFoundationalSelectionCategory.Background => CharacterBuildSelectionCategories.Background,
        CharacterFoundationalSelectionCategory.Deity => CharacterBuildSelectionCategories.Deity,
        _ => throw new InvalidOperationException($"Unsupported foundational selection category '{category}'.")
    };

    private static string MapKind(CharacterAdvancementKind kind) => kind switch
    {
        CharacterAdvancementKind.Class => CharacterBuildAdvancementKinds.Class,
        CharacterAdvancementKind.Subclass => CharacterBuildAdvancementKinds.Subclass,
        CharacterAdvancementKind.PrestigeClass => CharacterBuildAdvancementKinds.PrestigeClass,
        CharacterAdvancementKind.Feat => CharacterBuildAdvancementKinds.Feat,
        _ => throw new InvalidOperationException($"Unsupported advancement kind '{kind}'.")
    };
}
