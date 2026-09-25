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

    Task<CharacterSheetRoot?> SetAdvancementProgressAsync(
        Guid characterId,
        int? value,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetCurrencyBalanceAsync(
        Guid characterId,
        string currencyKey,
        long amount,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveCurrencyBalanceAsync(
        Guid characterId,
        string currencyKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetProfileAsync(
        Guid characterId,
        string? alignment,
        string? deity,
        string? age,
        string? height,
        string? weight,
        string? appearance,
        string? personalityTraits,
        string? ideals,
        string? bonds,
        string? flaws,
        string? backstory,
        string? alliesAndOrganizations,
        string? symbol,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetCurrentHitPointsAsync(
        Guid characterId,
        int? currentHitPoints,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetDeathSavesAsync(
        Guid characterId,
        int successes,
        int failures,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> ApplyIntegerStateMutationsAsync(
        Guid characterId,
        IReadOnlyList<CharacterIntegerStateMutation> mutations,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> AddInventoryItemOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> AddCustomInventoryItemOccurrenceAsync(
        Guid characterId,
        string customName,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> UpdateInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        int quantity,
        bool isCarried,
        bool isEquipped,
        bool isAttuned,
        Guid? containerOccurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> SetHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
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

    Task<CharacterSheetRoot?> AddConditionAsync(
        Guid characterId,
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> UpdateConditionAsync(
        Guid characterId,
        Guid conditionId,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);

    Task<CharacterSheetRoot?> RemoveConditionAsync(
        Guid characterId,
        Guid conditionId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
