using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterRulesProjectionRequestBuilderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 19, 50, 0, TimeSpan.Zero);

    [Fact]
    public void ProjectsOnlyExplicitCharacterOwnedFactsAndPreservesAdvancementLevels()
    {
        var speciesId = Guid.NewGuid();
        var classId = Guid.NewGuid();
        var subclassId = Guid.NewGuid();
        var featId = Guid.NewGuid();
        var build = new CharacterBuildView(
            Guid.NewGuid(),
            "BuildInProgress",
            false,
            [new FoundationalRuleSelectionView(
                speciesId,
                CharacterBuildSelectionCategories.RaceSpecies,
                "race.elf",
                Now,
                Now)],
            [new BaseAbilityScoreInputView(
                Guid.NewGuid(),
                "strength",
                16,
                Now,
                Now)],
            [
                new CharacterAdvancementEntryView(
                    classId,
                    0,
                    CharacterBuildAdvancementKinds.Class,
                    "class.fighter",
                    null,
                    Now,
                    Now,
                    7),
                new CharacterAdvancementEntryView(
                    subclassId,
                    null,
                    CharacterBuildAdvancementKinds.Subclass,
                    "subclass.champion",
                    classId,
                    Now,
                    Now),
                new CharacterAdvancementEntryView(
                    featId,
                    null,
                    CharacterBuildAdvancementKinds.Feat,
                    "feat.alert",
                    null,
                    Now,
                    Now)
            ]);

        var state = new CharacterStateView(
            build.CharacterId,
            false,
            12,
            new CharacterDeathSavesView(1, 2),
            [new CharacterInventoryItemOccurrenceView(
                Guid.NewGuid(),
                "item.long-sword",
                Now)],
            [],
            [
                new CharacterConditionOccurrenceView(
                    Guid.NewGuid(),
                    "condition.enlarged",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Now,
                    Now),
                new CharacterConditionOccurrenceView(
                    Guid.NewGuid(),
                    null,
                    "Custom reminder",
                    null,
                    null,
                    null,
                    null,
                    null,
                    Now,
                    Now)
            ]);

        var request = CharacterRulesProjectionRequestBuilder.Build(build, state);

        Assert.Equal(16, request.BaseAbilityScores!["strength"]);
        Assert.Equal(2, request.Advancements!.Count);
        Assert.Contains(
            request.Advancements,
            value => value.ConceptKey == "class.fighter"
                && value.Level == 7
                && value.OccurrenceKey == classId.ToString("D")
                && value.ParentConceptKey is null);
        Assert.Contains(
            request.Advancements,
            value => value.ConceptKey == "subclass.champion"
                && value.Level == 7
                && value.OccurrenceKey == subclassId.ToString("D")
                && value.ParentConceptKey == "class.fighter");
        Assert.Equal(4, request.SelectedConcepts!.Count);
        Assert.Contains(
            request.SelectedConcepts,
            value => value.ConceptKey == "class.fighter"
                && value.OccurrenceKey == classId.ToString("D"));
        Assert.Equal(1, request.CurrentResources!["resource.death-save.successes"]);
        Assert.Equal(2, request.CurrentResources["resource.death-save.failures"]);
        Assert.Equal(["condition.enlarged"], request.ConditionKeys);
        Assert.Equal(["item.long-sword"], request.ItemConceptKeys);
        Assert.Null(request.EquippedItemConceptKeys);
        Assert.Null(request.KnownSpellConceptKeys);
        Assert.Null(request.Choices);
    }
}
