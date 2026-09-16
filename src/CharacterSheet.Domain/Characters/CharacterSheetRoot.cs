namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Root of Character Sheet-owned state. The identifier is exactly the canonical
/// CharacterId issued by the Dorks & Dice Site; Character Sheet has no second character ID.
/// </summary>
public sealed class CharacterSheetRoot
{
    public CharacterSheetRoot(Guid characterId)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        CharacterId = characterId;
    }

    public Guid CharacterId { get; }
}
