using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterNormalProgressionPolicyTests
{
    [Fact]
    public void NormalProgressionAllowsCharacterLevelTwentyButNotTwentyOne()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);
        root.SetAdvancementLevel(fighter.Id, 19, now.AddMinutes(1));

        CharacterNormalProgressionPolicy.EnsureNewProgressionAllowed(
            root.AdvancementEntries,
            CharacterAdvancementKind.Class);

        var wizard = root.AddAdvancement(
            CharacterAdvancementKind.Class,
            "class:wizard",
            1,
            null,
            now.AddMinutes(2));
        Assert.Equal(20, CharacterNormalProgressionPolicy.TotalCharacterLevel(root.AdvancementEntries));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
                root.AdvancementEntries,
                wizard,
                2));
        Assert.Contains("maximum total level of 20", exception.Message);
    }

    [Fact]
    public void PrestigeClassLevelsCountTowardTheSameCharacterLevelCeiling()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);
        root.SetAdvancementLevel(fighter.Id, 10, now.AddMinutes(1));
        var prestige = root.AddAdvancement(
            CharacterAdvancementKind.PrestigeClass,
            "prestige-class:duelist",
            1,
            null,
            now.AddMinutes(2));
        root.SetAdvancementLevel(prestige.Id, 10, now.AddMinutes(3));

        Assert.Equal(20, CharacterNormalProgressionPolicy.TotalCharacterLevel(root.AdvancementEntries));
        Assert.Throws<InvalidOperationException>(() =>
            CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
                root.AdvancementEntries,
                fighter,
                11));
    }

    [Fact]
    public void ExistingEpicRecordCanBeCorrectedDownwardButCanNotIncreaseFurther()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);
        root.SetAdvancementLevel(fighter.Id, 25, now.AddMinutes(1));

        CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
            root.AdvancementEntries,
            fighter,
            24);

        Assert.Throws<InvalidOperationException>(() =>
            CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
                root.AdvancementEntries,
                fighter,
                26));
    }
}
