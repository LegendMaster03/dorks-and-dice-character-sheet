using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class SubclassSelectionModelTests
{
    [Fact]
    public void SubclassSelectionBelongsToClassAndReplacesWithoutDuplicate()
    {
        var root = Root();
        var startingClass = root.SetStartingClass("class.wizard", DateTimeOffset.UtcNow);

        var first = root.SetSubclassForClass(
            startingClass.Id,
            "subclass.wizard.evocation",
            DateTimeOffset.UtcNow.AddMinutes(1));
        var replacement = root.SetSubclassForClass(
            startingClass.Id,
            "subclass.wizard.abjuration",
            DateTimeOffset.UtcNow.AddMinutes(2));

        Assert.Same(first, replacement);
        Assert.Equal(CharacterAdvancementKind.Subclass, replacement.Kind);
        Assert.Equal(startingClass.Id, replacement.ParentAdvancementEntryId);
        Assert.Equal("subclass.wizard.abjuration", replacement.RuleConceptKey);
        Assert.Single(root.AdvancementEntries.Where(value => value.Kind == CharacterAdvancementKind.Subclass));
    }

    [Fact]
    public void SubclassCanNotBelongToPrestigeClassOrUnrelatedAdvancement()
    {
        var root = Root();
        root.SetStartingClass("class.wizard", DateTimeOffset.UtcNow);
        var prestigeClass = root.AddAdvancement(
            CharacterAdvancementKind.PrestigeClass,
            "prestigeclass.loremaster",
            1,
            null,
            DateTimeOffset.UtcNow.AddMinutes(1));

        var exception = Assert.Throws<InvalidOperationException>(() => root.SetSubclassForClass(
            prestigeClass.Id,
            "subclass.wizard.evocation",
            DateTimeOffset.UtcNow.AddMinutes(2)));

        Assert.Contains("Class advancement", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            root.AdvancementEntries,
            value => value.Kind == CharacterAdvancementKind.Subclass);
    }

    [Fact]
    public void GeneralAdvancementRequiresSubclassParentToBeClassAndUnique()
    {
        var root = Root();
        var startingClass = root.SetStartingClass("class.wizard", DateTimeOffset.UtcNow);
        root.AddAdvancement(
            CharacterAdvancementKind.Subclass,
            "subclass.wizard.evocation",
            null,
            startingClass.Id,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => root.AddAdvancement(
            CharacterAdvancementKind.Subclass,
            "subclass.wizard.abjuration",
            null,
            startingClass.Id,
            DateTimeOffset.UtcNow.AddMinutes(2)));
        Assert.Throws<InvalidOperationException>(() => root.AddAdvancement(
            CharacterAdvancementKind.Subclass,
            "subclass.wizard.abjuration",
            null,
            null,
            DateTimeOffset.UtcNow.AddMinutes(3)));
    }

    [Fact]
    public void ReplacingOrClearingClassRemovesItsSubclassSelection()
    {
        var root = Root();
        var startingClass = root.SetStartingClass("class.wizard", DateTimeOffset.UtcNow);
        root.SetSubclassForClass(
            startingClass.Id,
            "subclass.wizard.evocation",
            DateTimeOffset.UtcNow.AddMinutes(1));

        root.SetStartingClass("class.fighter", DateTimeOffset.UtcNow.AddMinutes(2));
        Assert.DoesNotContain(
            root.AdvancementEntries,
            value => value.Kind == CharacterAdvancementKind.Subclass);

        root.SetSubclassForClass(
            startingClass.Id,
            "subclass.fighter.champion",
            DateTimeOffset.UtcNow.AddMinutes(3));
        Assert.True(root.ClearStartingClass(DateTimeOffset.UtcNow.AddMinutes(4)));
        Assert.Empty(root.AdvancementEntries);
    }

    private static CharacterSheetRoot Root() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1));
}
