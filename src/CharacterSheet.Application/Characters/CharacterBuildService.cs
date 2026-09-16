using CharacterSheet.Application.Persistence;
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
    public const string RaceSpecies = "raceSpecies";
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
    DateTimeOffset UpdatedAt);

public sealed record CharacterBuildView(
    Guid CharacterId,
    string BuilderStatus,
    bool ReadOnly,
    IReadOnlyList<FoundationalRuleSelectionView> FoundationalSelections,
    IReadOnlyList<CharacterAdvancementEntryView> ProgressionEntries);

public sealed record CharacterBuildResult(
    CharacterBuildAccessStatus Status,
    CharacterBuildView? View = null);

public sealed class CharacterBuildService(
    ISiteCharacterAccessGateway siteCharacterAccess,
    ICharacterBuildStore buildStore,
    TimeProvider timeProvider)
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

    public Task<CharacterBuildResult> SetRaceSpeciesAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.SetFoundationalSelectionAsync(
                characterId,
                CharacterFoundationalSelectionCategory.RaceSpecies,
                ruleConceptKey,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> ClearRaceSpeciesAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.ClearFoundationalSelectionAsync(
                characterId,
                CharacterFoundationalSelectionCategory.RaceSpecies,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> SetStartingClassAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.SetStartingClassAsync(
                characterId,
                ruleConceptKey,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> ClearStartingClassAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.ClearStartingClassAsync(
                characterId,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> SetSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.SetSubclassAsync(
                characterId,
                classAdvancementEntryId,
                ruleConceptKey,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterBuildResult> ClearSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => buildStore.ClearSubclassAsync(
                characterId,
                classAdvancementEntryId,
                changedAt,
                token),
            cancellationToken);

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
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            new(CharacterBuildAccessStatus.NotFoundOrNotOwned),
        SiteCharacterAccessStatus.ProjectionUnavailable =>
            new(CharacterBuildAccessStatus.ProjectionUnavailable),
        SiteCharacterAccessStatus.Unauthenticated =>
            new(CharacterBuildAccessStatus.Unauthenticated),
        _ => new(CharacterBuildAccessStatus.ProjectionUnavailable)
    };

    private static CharacterBuildView ToView(CharacterSheetRoot root, bool readOnly) =>
        new(
            root.CharacterId,
            root.BuilderStatus.ToString(),
            readOnly,
            root.FoundationalSelections
                .OrderBy(value => value.Category)
                .ThenBy(value => value.Id)
                .Select(value => new FoundationalRuleSelectionView(
                    value.Id,
                    MapCategory(value.Category),
                    value.RuleConceptKey,
                    value.CreatedAt,
                    value.UpdatedAt))
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
                    value.UpdatedAt))
                .ToArray());

    private static string MapCategory(CharacterFoundationalSelectionCategory category) => category switch
    {
        CharacterFoundationalSelectionCategory.RaceSpecies => CharacterBuildSelectionCategories.RaceSpecies,
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
