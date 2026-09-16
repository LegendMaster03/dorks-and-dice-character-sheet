using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterIdentityTests
{
    [Fact]
    public void RootUsesSiteCharacterIdWithoutGeneratingAnotherIdentity()
    {
        var characterId = Guid.NewGuid();

        var root = new CharacterSheetRoot(characterId);

        Assert.Equal(characterId, root.CharacterId);
    }

    [Fact]
    public void RootRejectsEmptySiteCharacterId()
    {
        Assert.Throws<ArgumentException>(() => new CharacterSheetRoot(Guid.Empty));
    }
}
