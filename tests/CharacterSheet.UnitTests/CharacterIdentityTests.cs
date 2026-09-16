using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterIdentityTests
{
    [Fact]
    public void RootUsesSiteCharacterIdWithoutGeneratingAnotherIdentity()
    {
        var characterId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var root = new CharacterSheetRoot(characterId, createdAt);

        Assert.Equal(characterId, root.CharacterId);
        Assert.Equal(CharacterSheetRoot.CurrentSchemaVersion, root.SchemaVersion);
        Assert.Equal(CharacterBuilderStatus.BuildInProgress, root.BuilderStatus);
        Assert.Equal(createdAt, root.CreatedAt);
        Assert.Equal(createdAt, root.UpdatedAt);
    }

    [Fact]
    public void RootRejectsEmptySiteCharacterId()
    {
        Assert.Throws<ArgumentException>(() => new CharacterSheetRoot(Guid.Empty, DateTimeOffset.UtcNow));
    }
}
