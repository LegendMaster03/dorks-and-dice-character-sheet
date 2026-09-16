using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterBuildModelTests
{
    [Fact]
    public void RaceSpeciesSelectionUsesStableConceptKeyAndReplacesDeterministically()
    {
        var root = Root();
        var first = root.SetFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            "race:elf",
            DateTimeOffset.UtcNow);
        var replacement = root.SetFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            "race:dwarf",
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Same(first, replacement);
        Assert.Single(root.FoundationalSelections);
        Assert.Equal("race:dwarf", replacement.RuleConceptKey);
        Assert.DoesNotContain("Elf", replacement.RuleConceptKey, StringComparison.Ordinal);
    }

    [Fact]
    public void RaceSpeciesSelectionCanBeCleared()
    {
        var root = Root();
        root.SetFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            "race:elf",
            DateTimeOffset.UtcNow);

        Assert.True(root.ClearFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Empty(root.FoundationalSelections);
        Assert.False(root.ClearFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void StartingClassIsFirstClassProgressionEntryAndReplacementDoesNotDuplicateIt()
    {
        var root = Root();
        var first = root.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);
        var replacement = root.SetStartingClass("class:wizard", DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Same(first, replacement);
        var entry = Assert.Single(root.AdvancementEntries);
        Assert.Equal(CharacterAdvancementKind.Class, entry.Kind);
        Assert.Equal(0, entry.Ordinal);
        Assert.Null(entry.ParentAdvancementEntryId);
        Assert.Equal("class:wizard", entry.RuleConceptKey);
    }

    [Fact]
    public void StartingClassCanBeCleared()
    {
        var root = Root();
        root.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);

        Assert.True(root.ClearStartingClass(DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Empty(root.AdvancementEntries);
    }

    [Fact]
    public void ProgressionKindsRemainDistinctAndPermitFutureMultipleClassAndPrestigeEntries()
    {
        var root = Root();
        var startingClass = root.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);
        var secondClass = root.AddAdvancement(
            CharacterAdvancementKind.Class,
            "class:wizard",
            1,
            null,
            DateTimeOffset.UtcNow.AddMinutes(1));
        var prestigeOne = root.AddAdvancement(
            CharacterAdvancementKind.PrestigeClass,
            "prestigeClass:arcane-archer",
            2,
            null,
            DateTimeOffset.UtcNow.AddMinutes(2));
        var prestigeTwo = root.AddAdvancement(
            CharacterAdvancementKind.PrestigeClass,
            "prestigeClass:loremaster",
            3,
            null,
            DateTimeOffset.UtcNow.AddMinutes(3));
        var subclass = root.AddAdvancement(
            CharacterAdvancementKind.Subclass,
            "subclass:evoker",
            null,
            startingClass.Id,
            DateTimeOffset.UtcNow.AddMinutes(4));
        var feat = root.AddAdvancement(
            CharacterAdvancementKind.Feat,
            "feat:alert",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.Equal(2, root.AdvancementEntries.Count(value => value.Kind == CharacterAdvancementKind.Class));
        Assert.Equal(2, root.AdvancementEntries.Count(value => value.Kind == CharacterAdvancementKind.PrestigeClass));
        Assert.Equal(CharacterAdvancementKind.Class, secondClass.Kind);
        Assert.Equal(CharacterAdvancementKind.PrestigeClass, prestigeOne.Kind);
        Assert.Equal(CharacterAdvancementKind.PrestigeClass, prestigeTwo.Kind);
        Assert.Equal(CharacterAdvancementKind.Subclass, subclass.Kind);
        Assert.Equal(startingClass.Id, subclass.ParentAdvancementEntryId);
        Assert.Equal(CharacterAdvancementKind.Feat, feat.Kind);
    }

    [Fact]
    public void MultipleFeatOccurrencesCanShareRulesCoreIdentityWithoutSharingCharacterOwnedIdentity()
    {
        var root = Root();
        var first = root.AddAdvancement(
            CharacterAdvancementKind.Feat,
            "feat:flavor-choice",
            null,
            null,
            DateTimeOffset.UtcNow);
        var second = root.AddAdvancement(
            CharacterAdvancementKind.Feat,
            "feat:flavor-choice",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.RuleConceptKey, second.RuleConceptKey);
    }

    [Fact]
    public void PersistenceEntitiesDoNotRequireDisplayNameOrMechanicalJson()
    {
        var root = Root();
        var race = root.SetFoundationalSelection(
            CharacterFoundationalSelectionCategory.RaceSpecies,
            "race:elf",
            DateTimeOffset.UtcNow);
        var startingClass = root.SetStartingClass("class:wizard", DateTimeOffset.UtcNow);

        Assert.Equal("race:elf", race.RuleConceptKey);
        Assert.Equal("class:wizard", startingClass.RuleConceptKey);
        Assert.DoesNotContain(
            typeof(CharacterFoundationalRuleSelection).GetProperties(),
            property => property.Name.Contains("DisplayName", StringComparison.Ordinal)
                || property.Name.Contains("Json", StringComparison.Ordinal));
        Assert.DoesNotContain(
            typeof(CharacterAdvancementEntry).GetProperties(),
            property => property.Name.Contains("DisplayName", StringComparison.Ordinal)
                || property.Name.Contains("Json", StringComparison.Ordinal));
    }

    private static CharacterSheetRoot Root() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1));
}
