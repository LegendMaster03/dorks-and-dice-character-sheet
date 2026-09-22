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
            ],
            [
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.Choice,
                    "spellcasting.resource-system",
                    null,
                    null,
                    "spell-points",
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.CompetencyRank,
                    "skill.climb",
                    4,
                    null,
                    null,
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.KnownSpell,
                    "spell.magic-missile",
                    null,
                    null,
                    null,
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.Resource,
                    "resource.spell-points",
                    9,
                    null,
                    null,
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.IntegerFact,
                    "combat.initiative.other",
                    2,
                    null,
                    null,
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.BooleanFact,
                    "character.flat-footed",
                    null,
                    true,
                    null,
                    Now,
                    Now),
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.StringFact,
                    "character.size-category",
                    null,
                    null,
                    "large",
                    Now,
                    Now)
            ],
            [
                new CharacterHitPointGainStateView(
                    Guid.NewGuid(),
                    classId,
                    1,
                    10,
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
                Now,
                Quantity: 1,
                IsCarried: true,
                IsEquipped: true,
                IsAttuned: false)],
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
        Assert.Equal(["item.long-sword"], request.EquippedItemConceptKeys);
        Assert.Equal(["spell.magic-missile"], request.KnownSpellConceptKeys);
        Assert.Equal(4, request.CompetencyRanks!["skill.climb"]);
        Assert.Equal(
            "spell-points",
            Assert.Single(request.Choices!).Value);
        Assert.Equal(9, request.CurrentResources["resource.spell-points"]);
        Assert.Equal(2, request.IntegerFacts!["combat.initiative.other"]);
        Assert.True(request.BooleanFacts!["character.flat-footed"]);
        Assert.Equal("large", request.StringFacts!["character.size-category"]);
        var hpGain = Assert.Single(request.HitPointGains!);
        Assert.Equal("class.fighter", hpGain.ConceptKey);
        Assert.Equal(1, hpGain.ClassLevel);
        Assert.Equal(10, hpGain.HitDieValue);
        Assert.Equal(classId.ToString("D"), hpGain.OccurrenceKey);
    }
}
