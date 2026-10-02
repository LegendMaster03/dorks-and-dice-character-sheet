using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterAdvancementLevelSafetyTests
{
    [Fact]
    public void AdvancementLevelAboveTwentyIsAllowedWithoutACharacterSheetGameRuleCap()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);

        root.SetAdvancementLevel(fighter.Id, 30, now.AddMinutes(1));

        Assert.Equal(30, fighter.Level);
    }

    [Fact]
    public void TechnicalSafetyCeilingIsHighAndStillRejectsPathologicalLevels()
    {
        Assert.Equal(1_000_000, CharacterAdvancementEntry.MaxSupportedLevel);

        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);

        root.SetAdvancementLevel(
            fighter.Id,
            CharacterAdvancementEntry.MaxSupportedLevel,
            now.AddMinutes(1));
        Assert.Equal(CharacterAdvancementEntry.MaxSupportedLevel, fighter.Level);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.SetAdvancementLevel(
                fighter.Id,
                CharacterAdvancementEntry.MaxSupportedLevel + 1,
                now.AddMinutes(2)));
    }
}
