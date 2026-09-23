using CharacterSheet.Application.Characters;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterStateModelTests
{
    [Fact]
    public void CurrencyBalancesAreGenericCharacterOwnedStateWithoutDenominationRules()
    {
        var root = Root();
        var now = DateTimeOffset.UtcNow;

        var gold = root.SetCurrencyBalance("  GP  ", 125, now);
        Assert.Equal("gp", gold.CurrencyKey);
        Assert.Equal(125, gold.Amount);

        var updated = root.SetCurrencyBalance("gp", -10, now.AddMinutes(1));
        Assert.Same(gold, updated);
        Assert.Equal(-10, updated.Amount);

        root.SetCurrencyBalance("third-party-scrip", 7, now.AddMinutes(2));
        Assert.Equal(2, root.CurrencyBalances.Count);
        Assert.True(root.RemoveCurrencyBalance("GP", now.AddMinutes(3)));
        Assert.Single(root.CurrencyBalances);
        Assert.False(root.RemoveCurrencyBalance("gp", now.AddMinutes(4)));
    }

    [Fact]
    public void ProfileStateIsCharacterOwnedFlexibleAndNormalizesBlankFields()
    {
        var root = Root();
        var now = DateTimeOffset.UtcNow;

        var profile = root.SetProfile(
            "  Unaligned by choice  ",
            "  The Traveler  ",
            "  34  ",
            "  5 ft. 11 in.  ",
            "  180 lb.  ",
            "  Scar over the left eyebrow.  ",
            "  Curious.  ",
            "  Freedom.  ",
            "  Old adventuring company.  ",
            "  Impatient.  ",
            "  A long-form history.  ",
            "  The Cartographers Guild.  ",
            "  Silver compass rose.  ",
            now);

        Assert.Equal("Unaligned by choice", profile.Alignment);
        Assert.Equal("The Traveler", profile.Deity);
        Assert.Equal("34", profile.Age);
        Assert.Equal("A long-form history.", profile.Backstory);

        var updated = root.SetProfile(
            null, "   ", null, null, null, null, null, null, null, null, null, null, null,
            now.AddMinutes(1));

        Assert.Same(profile, updated);
        Assert.Null(updated.Alignment);
        Assert.Null(updated.Deity);
        Assert.Null(updated.Backstory);
        Assert.Equal(now.AddMinutes(1), updated.UpdatedAt);
    }

    [Fact]
    public void ProfileStateEnforcesOnlyTechnicalTextBounds()
    {
        var root = Root();

        Assert.Throws<ArgumentException>(() => root.SetProfile(
            new string('a', CharacterProfileState.MaxShortTextLength + 1),
            null, null, null, null, null, null, null, null, null, null, null, null,
            DateTimeOffset.UtcNow));
    }

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
    public void DeathSavesAreCharacterOwnedBoundedRuntimeState()
    {
        var root = Root();
        var changedAt = DateTimeOffset.UtcNow;

        root.SetDeathSaves(2, 1, changedAt);

        Assert.Equal(2, root.DeathSaveSuccesses);
        Assert.Equal(1, root.DeathSaveFailures);
        Assert.Equal(changedAt, root.UpdatedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.SetDeathSaves(-1, 0, changedAt.AddMinutes(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.SetDeathSaves(0, 4, changedAt.AddMinutes(1)));
        Assert.Equal(2, root.DeathSaveSuccesses);
        Assert.Equal(1, root.DeathSaveFailures);
    }

    [Fact]
    public void IntegerStateMutationBatchValidatesBeforeApplyingAnyChange()
    {
        var root = Root();
        var now = DateTimeOffset.UtcNow;
        root.SetCurrentHitPoints(10, now);
        root.SetDeathSaves(1, 0, now);
        root.SetRulesInput(
            CharacterRulesInputKind.Resource,
            "resource.focus",
            5,
            null,
            null,
            now);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.ApplyIntegerStateMutations(
                [
                    new CharacterIntegerStateMutation(
                        CharacterIntegerStateMutationTarget.CurrentHitPoints,
                        CharacterIntegerStateMutationOperation.Adjust,
                        4),
                    new CharacterIntegerStateMutation(
                        CharacterIntegerStateMutationTarget.Resource,
                        CharacterIntegerStateMutationOperation.Expend,
                        2,
                        "resource.focus"),
                    new CharacterIntegerStateMutation(
                        CharacterIntegerStateMutationTarget.DeathSaveFailures,
                        CharacterIntegerStateMutationOperation.Set,
                        4)
                ],
                now.AddMinutes(1)));

        Assert.Equal(10, root.CurrentHitPoints);
        Assert.Equal(0, root.DeathSaveFailures);
        Assert.Equal(
            5,
            Assert.Single(
                root.RulesInputs,
                value => value.Kind == CharacterRulesInputKind.Resource
                    && value.Key == "resource.focus").IntegerValue);
    }

    [Fact]
    public void IntegerStateMutationBatchSupportsAdjustExpendAndSetWithoutInventingMissingValues()
    {
        var root = Root();
        var now = DateTimeOffset.UtcNow;
        root.SetCurrentHitPoints(10, now);
        root.SetRulesInput(
            CharacterRulesInputKind.Resource,
            "resource.focus",
            5,
            null,
            null,
            now);

        root.ApplyIntegerStateMutations(
            [
                new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.CurrentHitPoints,
                    CharacterIntegerStateMutationOperation.Adjust,
                    4),
                new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.Resource,
                    CharacterIntegerStateMutationOperation.Expend,
                    2,
                    "resource.focus"),
                new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.DeathSaveSuccesses,
                    CharacterIntegerStateMutationOperation.Set,
                    0)
            ],
            now.AddMinutes(1));

        Assert.Equal(14, root.CurrentHitPoints);
        Assert.Equal(0, root.DeathSaveSuccesses);
        Assert.Equal(
            3,
            Assert.Single(
                root.RulesInputs,
                value => value.Kind == CharacterRulesInputKind.Resource
                    && value.Key == "resource.focus").IntegerValue);

        Assert.Throws<InvalidOperationException>(() =>
            root.ApplyIntegerStateMutations(
                [new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.Resource,
                    CharacterIntegerStateMutationOperation.Adjust,
                    1,
                    "resource.missing")],
                now.AddMinutes(2)));
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
    public void AdvancementLevelUsesTechnicalSafetyCeilingAndPrunesStaleHitPointGains()
    {
        var root = Root();
        var createdAt = DateTimeOffset.UtcNow;
        var fighter = root.SetStartingClass("class:fighter", createdAt);

        root.SetAdvancementLevel(fighter.Id, 3, createdAt.AddSeconds(1));
        root.SetHitPointGain(fighter.Id, 1, 10, createdAt.AddSeconds(2));
        root.SetHitPointGain(fighter.Id, 2, 6, createdAt.AddSeconds(3));
        root.SetHitPointGain(fighter.Id, 3, 5, createdAt.AddSeconds(4));

        root.SetAdvancementLevel(fighter.Id, 2, createdAt.AddMinutes(1));

        Assert.Equal(2, fighter.Level);
        Assert.Equal(2, root.HitPointGains.Count);
        Assert.DoesNotContain(root.HitPointGains, value => value.ClassLevel > 2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.SetAdvancementLevel(
                fighter.Id,
                CharacterAdvancementEntry.MaxSupportedLevel + 1,
                createdAt.AddMinutes(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.SetHitPointGain(
                fighter.Id,
                3,
                5,
                createdAt.AddMinutes(3)));

        root.SetStartingClass("class:wizard", createdAt.AddMinutes(4));
        Assert.Empty(root.HitPointGains);
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
    public void ConditionsSupportRulesDefinedCustomLeveledAndCountedState()
    {
        var root = Root();
        var createdAt = DateTimeOffset.UtcNow;
        var exhaustion = root.AddCondition(
            "condition:exhaustion",
            null,
            level: 2,
            counterCurrent: null,
            counterMaximum: null,
            duration: null,
            notes: null,
            createdAt);
        var custom = root.AddCondition(
            null,
            "  Burning  ",
            level: null,
            counterCurrent: 2,
            counterMaximum: 5,
            duration: "3 rounds",
            notes: "Custom table condition",
            createdAt.AddSeconds(1));

        Assert.Equal("condition:exhaustion", exhaustion.RuleConceptKey);
        Assert.Equal(2, exhaustion.Level);
        Assert.Equal("Burning", custom.CustomName);
        Assert.Equal(2, custom.CounterCurrent);
        Assert.Equal(5, custom.CounterMaximum);

        root.UpdateCondition(
            exhaustion.Id,
            customName: null,
            level: 3,
            counterCurrent: null,
            counterMaximum: null,
            duration: "Until rest",
            notes: null,
            createdAt.AddMinutes(1));
        Assert.Equal(3, exhaustion.Level);
        Assert.Equal("Until rest", exhaustion.Duration);
    }

    [Fact]
    public void ConditionIdentityRequiresEitherRuleOrCustomNameButNotBoth()
    {
        var root = Root();
        Assert.Throws<ArgumentException>(() => root.AddCondition(
            null, null, null, null, null, null, null, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => root.AddCondition(
            "condition:prone", "Prone", null, null, null, null, null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void InventoryOccurrenceOwnsMutableUsageStateButDoesNotCopyRuleDefinitions()
    {
        var root = Root();
        var bag = root.AddInventoryItemOccurrence("item:backpack", DateTimeOffset.UtcNow);
        var sword = root.AddInventoryItemOccurrence(
            "item:longsword",
            DateTimeOffset.UtcNow.AddSeconds(1));

        Assert.Equal(1, sword.Quantity);
        Assert.True(sword.IsCarried);
        Assert.False(sword.IsEquipped);
        Assert.False(sword.IsAttuned);
        Assert.Null(sword.ContainerOccurrenceId);

        root.UpdateInventoryItemOccurrence(
            sword.Id,
            quantity: 2,
            isCarried: true,
            isEquipped: true,
            isAttuned: true,
            bag.Id,
            DateTimeOffset.UtcNow.AddMinutes(1));

        Assert.Equal(2, sword.Quantity);
        Assert.True(sword.IsEquipped);
        Assert.True(sword.IsAttuned);
        Assert.Equal(bag.Id, sword.ContainerOccurrenceId);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            root.UpdateInventoryItemOccurrence(
                sword.Id, 0, true, false, false, null, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            root.UpdateInventoryItemOccurrence(
                bag.Id, 1, true, false, false, sword.Id, DateTimeOffset.UtcNow));

        Assert.DoesNotContain(
            typeof(CharacterInventoryItemOccurrence).GetProperties(),
            property => property.Name.Contains("DisplayName", StringComparison.Ordinal)
                || property.Name.Contains("Json", StringComparison.Ordinal)
                || property.Name.Contains("Weight", StringComparison.Ordinal)
                || property.Name.Contains("Armor", StringComparison.Ordinal)
                || property.Name.Contains("Damage", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(CharacterStateService).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name?.StartsWith("RulesCore", StringComparison.Ordinal) == true);
    }

    private static CharacterSheetRoot Root() =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1));
}
