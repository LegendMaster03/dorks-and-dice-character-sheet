using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Persistence boundary for routine Character-owned state that is neither a Rules Core definition
/// nor calculated/effective Character state.
/// </summary>
public interface ICharacterStateStore
{
    Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetCurrentHitPointsAsync(
        Guid characterId,
        int? currentHitPoints,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> AddInventoryItemOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> AddNoteAsync(
        Guid characterId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> UpdateNoteAsync(
        Guid characterId,
        Guid noteId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveNoteAsync(
        Guid characterId,
        Guid noteId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
