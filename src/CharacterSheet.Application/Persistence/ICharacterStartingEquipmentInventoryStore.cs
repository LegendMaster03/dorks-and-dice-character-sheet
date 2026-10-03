using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

public sealed record CharacterStartingEquipmentInventoryItem(
    string SourceGrantKey,
    string? RuleConceptKey,
    string? CustomName,
    int Quantity);

public sealed record CharacterStartingEquipmentCurrencyGrant(
    string SourceGrantKey,
    string CurrencyKey,
    long Amount);

/// <summary>
/// Persists the acquisition boundary between Rules Core starting-equipment grants and ordinary
/// Character-owned inventory/currency. Provenance exists only so re-applying a changed starting
/// equipment selection can update generated state without touching identical manually acquired
/// items or losing later currency changes.
/// </summary>
public interface ICharacterStartingEquipmentInventoryStore
{
    Task<CharacterSheetRoot?> SynchronizeAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> items,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> currencies,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}