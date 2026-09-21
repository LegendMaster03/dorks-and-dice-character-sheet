using CharacterSheet.Application.Characters;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterStateModelTests
{
    [Fact]
    public void CurrentHitPointsAreCharacterOwnedAndPermitNegativeEditionSpecificState()
    {
        var root = Root();
        var changedAt = DateTimeOffset.UtcNow;

        root.SetCurrentHitPoints(-3, changedAt);

        Assert.Equal(-3, root.CurrentHitPoints);
        Assert.Equal(changedAt, root.UpdatedAt);

        root.SetCurrentHitPoints(null, changedAt.AddMinutes(1));
        Assert.Null(root.CurrentHitPoints);
    }

    [Fact]
    public void InventoryOccurrencesUseStableConceptKeysAndPermitDuplicates()
    {
        var root = Root();

        var first = root.AddInventoryItemOccurrence(
            "  ITEM:ROPE-HEMPEN  ",
            DateTimeOffset.UtcNow);
        var second = root.AddInventoryItemOccurrence(
            "item:rope-hempen",
            DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("item:rope-hempen", first.RuleConceptKey);
        Assert.Equal(first.RuleConceptKey, second.RuleConceptKey);
        Assert.Equal(root.CharacterId, first.CharacterId);
        Assert.Equal(2, root.InventoryItemOccurrences.Count);
    }

    [Fact]
    public void RemovingOneDuplicateInventoryOccurrencePreservesTheOther()
    {
        var root = Root();
        var first = root.AddInventoryItemOccurrence("item:torch", DateTimeOffset.UtcNow);
        var second = root.AddInventoryItemOccurrence("item:torch", DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.True(root.RemoveInventoryItemOccurrence(first.Id, DateTimeOffset.UtcNow.AddMinutes(1)));

        var remaining = Assert.Single(root.InventoryItemOccurrences);
        Assert.Equal(second.Id, remaining.Id);
        Assert.False(root.RemoveInventoryItemOccurrence(first.Id, DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    [Fact]
    public void NotesHaveStableCharacterOwnedIdentityAndPreserveAuthoredContent()
    {
        var root = Root();
        var createdAt = DateTimeOffset.UtcNow;
        var note = root.AddNote("  Keep the leading spaces.\nSecond line.  ", createdAt);

        var updated = root.UpdateNote(
            note.Id,
            "Replacement note",
            createdAt.AddMinutes(1));

        Assert.Same(note, updated);
        Assert.Equal("Replacement note", note.Content);
        Assert.Equal(createdAt, note.CreatedAt);
        Assert.Equal(createdAt.AddMinutes(1), note.UpdatedAt);
        Assert.Equal(root.CharacterId, note.CharacterId);
    }

    [Fact]
    public void BlankNotesAreRejected()
    {
        var root = Root();

        var exception = Assert.Throws<ArgumentException>(() =>
            root.AddNote("   ", DateTimeOffset.UtcNow));

        Assert.Contains("can not be blank", exception.Message, StringComparison.Ordinal);
        Assert.Empty(root.Notes);
    }

    [Fact]
    public void RoutineStateDoesNotCopyRulesMechanicsOrEquipmentUsageState()
    {
        Assert.DoesNotContain(
            typeof(CharacterInventoryItemOccurrence).GetProperties(),
            property => property.Name.Contains("DisplayName", StringComparison.Ordinal)
                || property.Name.Contains("Json", StringComparison.Ordinal)
                || property.Name.Contains("Equipped", StringComparison.Ordinal)
                || property.Name.Contains("Attun", StringComparison.Ordinal)
                || property.Name.Contains("Carried", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(CharacterStateService).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("RulesCore", StringComparison.Ordinal) == true);
    }

    private static CharacterSheetRoot Root() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1));
}
