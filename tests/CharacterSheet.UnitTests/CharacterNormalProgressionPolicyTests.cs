using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterNormalProgressionPolicyTests
{
    [Fact]
    public void AggregateCharacterLevelHasNoUniversalGameRuleCeiling()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);
        root.SetAdvancementLevel(fighter.Id, 25, now.AddMinutes(1));

        CharacterNormalProgressionPolicy.EnsureNewProgressionAllowed(
            root.AdvancementEntries,
            CharacterAdvancementKind.Class);

        var wizard = root.AddAdvancement(
            CharacterAdvancementKind.Class,
            "class:wizard",
            1,
            null,
            now.AddMinutes(2));
        CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
            root.AdvancementEntries,
            wizard,
            2);

        Assert.Equal(26, CharacterNormalProgressionPolicy.TotalCharacterLevel(root.AdvancementEntries));
    }

    [Fact]
    public void PrestigeClassLevelsContributeToAggregateCharacterLevelWithoutCreatingACeiling()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);
        root.SetAdvancementLevel(fighter.Id, 20, now.AddMinutes(1));
        var prestige = root.AddAdvancement(
            CharacterAdvancementKind.PrestigeClass,
            "prestige-class:duelist",
            1,
            null,
            now.AddMinutes(2));
        root.SetAdvancementLevel(prestige.Id, 10, now.AddMinutes(3));

        Assert.Equal(30, CharacterNormalProgressionPolicy.TotalCharacterLevel(root.AdvancementEntries));
        CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
            root.AdvancementEntries,
            fighter,
            21);
    }

    [Fact]
    public void ProgressionPolicyRetainsTechnicalLevelIntegrityWithoutInventingGameRules()
    {
        var now = DateTimeOffset.UtcNow;
        var root = new CharacterSheetRoot(Guid.NewGuid(), now);
        var fighter = root.SetStartingClass("class:fighter", now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
                root.AdvancementEntries,
                fighter,
                0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterNormalProgressionPolicy.EnsureCharacterLevelChangeAllowed(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CharacterNormalProgressionPolicy.EnsureCharacterLevelChangeAllowed(1, -1));
    }
}
