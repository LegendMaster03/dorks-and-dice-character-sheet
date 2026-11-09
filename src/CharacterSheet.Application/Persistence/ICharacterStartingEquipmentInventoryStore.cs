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
/// Issues starting equipment once into ordinary Character inventory and currency.
/// The receipt prevents duplicate awards; it does not track or synchronize acquired items.
/// </summary>
public static class CharacterStartingEquipmentReceipt
{
    public const string Key = "starting-equipment.received";
}

/// <summary>
/// Records one-time starting-equipment grants and receipts while acquired items become ordinary
/// character-owned inventory.
/// </summary>
public interface ICharacterStartingEquipmentInventoryStore
{
    Task<CharacterSheetRoot?> ApplyOnceAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> items,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> currencies,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}