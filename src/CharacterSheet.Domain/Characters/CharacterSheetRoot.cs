namespace CharacterSheet.Domain.Characters;

public enum CharacterBuilderStatus
{
    BuildInProgress = 1
}

/// <summary>
/// Root of Character Sheet-owned state. CharacterId is exactly the canonical identifier issued by
/// the Dorks & Dice Site; Character Sheet has no second character ID.
/// </summary>
public sealed class CharacterSheetRoot
{
    public const int CurrentSchemaVersion = 1;

    private CharacterSheetRoot()
    {
    }

    public CharacterSheetRoot(Guid characterId, DateTimeOffset createdAt)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        CharacterId = characterId;
        SchemaVersion = CurrentSchemaVersion;
        BuilderStatus = CharacterBuilderStatus.BuildInProgress;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid CharacterId { get; private set; }

    public int SchemaVersion { get; private set; }

    public CharacterBuilderStatus BuilderStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
