using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Persistence boundary for Character-owned builder decisions. Rule definitions remain in Rules Core;
/// this store persists only stable Rules Core concept references and Character-owned progression state.
/// </summary>
public interface ICharacterBuildStore
{
    Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> ClearFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetStartingClassAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> ClearStartingClassAsync(
        Guid characterId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> ClearSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
