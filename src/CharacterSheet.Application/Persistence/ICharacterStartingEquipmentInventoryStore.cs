using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

public sealed record CharacterStartingEquipmentInventoryItem(
    string SourceGrantKey,
    string? RuleConceptKey,
    string? CustomName,
    int Quantity);

/// <summary>
/// Persists the acquisition boundary between Rules Core starting-equipment grants and ordinary
/// Character-owned inventory. The provenance mapping exists only so re-applying a changed starting
/// equipment selection can update generated occurrences without touching identical manual items.
/// </summary>
public interface ICharacterStartingEquipmentInventoryStore
{
    Task<CharacterSheetRoot?> SynchronizeAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> items,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
