using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.UnitTests;

public sealed class RulesCoreCharacterProjectionProjectorTests
{
    [Fact]
    public void ProjectionPromotesAuthoritativeCharacterMechanicsWithoutRecalculatingThem()
    {
        var fallback = new CharacterMechanicsPresentationView(
            Competencies: new CompetencyCollectionPresentationView(
                [new CompetencyPresentationView(
                    "skill.arcana",
                    "Arcana",
                    "-",
                    Kind: "skill",
                    Family: "Knowledge",
                    Specialty: "arcana",
                    SupportsRanks: true)]));

        var projection = EmptyProjection() with
        {
            Mechanics =
            [
                Mechanic("ability.strength.score", "ability-score", "Strength Score", 14,
                    contributions: [Contribution("base", "Base Score", 10), Contribution("temp", "Temporary", 4)]),
                Mechanic("ability.strength.modifier", "ability-modifier", "Strength Modifier", 2),
                Mechanic("ability.strength.ordinary-score", "ability-score", "Strength Ordinary Score", 10),
                Mechanic("ability.strength.temporary-score", "ability-score", "Strength Temporary Score", 14),
                Mechanic("ability.strength.temporary-modifier", "ability-modifier", "Strength Temporary Modifier", 2),
                Mechanic("save.fortitude", "saving-throw", "Fortitude", 6),
                Mechanic("defense.ac.total", "defense", "Armor Class", 22,
                    contributions: [Contribution("armor", "Armor", 8), Contribution("dex", "Dexterity", 4)]),
                Mechanic("combat.base-attack-bonus", "combat-value", "Base Attack Bonus", 5),
                Mechanic("proficiency.standard", "proficiency", "Proficiency Bonus", 3),
                Mechanic("health.maximum-hp", "health", "Maximum HP", 44, unit: "hit points"),
                Mechanic("character.size-category", "character-metadata", "Size", text: "Large"),
                Mechanic("competency.skill.arcana", "competency", "Arcana", 9,
                    contributions: [Contribution("ability", "Intelligence", 4), Contribution("training", "Training", 5)]),
                Mechanic("spellcasting.class.wizard.save-dc", "spell-save-dc", "Wizard Save DC", 15),
                Mechanic("spellcasting.class.wizard.attack", "spell-attack", "Wizard Spell Attack", 7),
                Mechanic("spellcasting.class.warlock.save-dc", "spell-save-dc", "Warlock Save DC", 14),
                Mechanic("spellcasting.class.warlock.attack", "spell-attack", "Warlock Spell Attack", 6),
                Mechanic("attack.weapon.long-sword", "attack-bonus", "Longsword Attack", 8)
            ],
            Movement =
            [
                new RulesCoreCharacterMovementModeView(
                    "movement.walk", "Walk", "resolved", 30, "feet", [], Provenance)
            ],
            Resources =
            [
                Resource("resource.hit-die.class.wizard", "Wizard Hit Dice", maximum: 7),
                Resource("resource.spell-points", "Spell Points", current: 8, maximum: 14),
                Resource("resource.pact-slot.class.warlock.level-3", "Pact Slots", current: 1, maximum: 2)
            ],
            Actions =
            [
                new RulesCoreCharacterActionView(
                    "action.attack.long-sword",
                    "Longsword",
                    "action",
                    "resolved",
                    "attack.weapon.long-sword",
                    "1d8+4",
                    "slashing",
                    "5 ft.",
                    "5 ft.",
                    "one target",
                    null,
                    null,
                    [],
                    Provenance)
            ],
            Features =
            [
                new RulesCoreCharacterFeatureView(
                    "feature.second-wind",
                    "Second Wind",
                    "class-feature",
                    "resolved",
                    "class.fighter",
                    [],
                    Provenance,
                    GrantingSourceKind: "class",
                    AcquisitionLevel: 1)
            ],
            Spellcasting =
            [
                new RulesCoreCharacterSpellcastingView(
                    "spellcasting.class.wizard",
                    "Wizard Spellcasting",
                    "resolved",
                    "intelligence",
                    "spell-points",
                    "spellcasting.class.wizard.save-dc",
                    "spellcasting.class.wizard.attack",
                    [],
                    [],
                    Provenance),
                new RulesCoreCharacterSpellcastingView(
                    "spellcasting.class.warlock",
                    "Warlock Pact Magic",
                    "resolved",
                    "charisma",
                    "pact-magic",
                    "spellcasting.class.warlock.save-dc",
                    "spellcasting.class.warlock.attack",
                    [],
                    [],
                    Provenance)
            ],
            Choices =
            [
                new RulesCoreCharacterChoiceView(
                    "spellcasting.resource-system",
                    "spellcasting",
                    "Spellcasting Resource System",
                    "single-select",
                    "choice-required",
                    [
                        new RulesCoreCharacterChoiceOptionView(
                            "spell-slots",
                            "Spell Slots",
                            null),
                        new RulesCoreCharacterChoiceOptionView(
                            "spell-points",
                            "Spell Points",
                            null)
                    ],
                    "spell-points",
                    "house.spellcasting-resource",
                    Provenance)
            ],
            Conflicts =
            [
                new RulesCoreCharacterProjectionConflictView(
                    "conflict.fixture",
                    "fixture",
                    "Fixture conflict",
                    ["defense.ac.total"],
                    ["class.fighter"])
            ]
        };

        var state = new CharacterStateView(
            Guid.NewGuid(),
            false,
            null,
            new CharacterDeathSavesView(0, 0),
            [],
            [],
            [],
            [
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.CompetencyRank,
                    "skill.arcana",
                    5,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
            ]);

        var mechanics = CharacterPresentationProjector.ProjectCharacterRules(
            fallback,
            projection,
            state);

        var strength = Assert.Single(mechanics.AbilityValues!);
        Assert.Equal("strength", strength.Key);
        Assert.Equal(14, strength.EffectiveValue);
        Assert.Equal(2, Assert.Single(strength.RelatedValues!, value => value.Key == "modifier").EffectiveValue);
        Assert.Equal(2, strength.Breakdown!.Count);

        Assert.Equal(6, Assert.Single(mechanics.SavingThrows!).EffectiveValue);
        var armorClass = Assert.Single(mechanics.Defenses!.Values, value => value.Key == "defense.ac.total");
        Assert.Equal(22, armorClass.EffectiveValue);
        Assert.Equal(2, armorClass.Breakdown!.Count);
        Assert.Equal("defense.ac.total", mechanics.Defenses.PrimaryKey);
        Assert.Equal(5, Assert.Single(mechanics.CombatFundamentals!).EffectiveValue);
        Assert.Equal(3, Assert.Single(mechanics.Training!).EffectiveValue);

        var hitPoints = Assert.Single(mechanics.HealthTracks!, value => value.Role == "hit-points");
        Assert.Equal(44, hitPoints.Maximum);
        var hitDice = Assert.Single(mechanics.HealthTracks!, value => value.Role == "hit-dice");
        Assert.Equal(7, hitDice.Maximum);

        var arcana = Assert.Single(mechanics.Competencies!.Entries);
        Assert.Equal(9, arcana.EffectiveValue);
        Assert.Equal(5, arcana.Ranks);
        Assert.Equal("Knowledge", arcana.Family);
        Assert.Equal(2, arcana.Breakdown!.Count);

        Assert.Equal(30, Assert.Single(mechanics.Movement!).EffectiveValue);
        Assert.Equal(8, Assert.Single(mechanics.Actions!).AttackOrCheck!.EffectiveValue);
        Assert.Equal("Large", Assert.Single(mechanics.CharacterMetadata!).EffectiveValue);
        Assert.Equal("Second Wind", Assert.Single(mechanics.Features!).Label);

        var wizard = Assert.Single(mechanics.SpellcastingProfiles!, value => value.Key == "spellcasting.class.wizard");
        Assert.Equal("spell-points", wizard.ResourceSystem!.Value);
        Assert.Equal(14, Assert.Single(wizard.Resources!).Maximum);
        Assert.Equal(15, wizard.SaveDc!.EffectiveValue);
        Assert.Equal(7, wizard.SpellAttack!.EffectiveValue);

        var warlock = Assert.Single(mechanics.SpellcastingProfiles!, value => value.Key == "spellcasting.class.warlock");
        Assert.Equal("pact-magic", warlock.ResourceSystem!.Value);
        Assert.Equal(2, Assert.Single(warlock.Resources!).Maximum);

        var choice = Assert.Single(mechanics.RuleChoices!);
        Assert.Equal("spellcasting.resource-system", choice.ChoiceKey);
        Assert.Equal("spell-points", choice.SelectedValue);
        Assert.Equal(["Spell Slots", "Spell Points"], choice.Options.Select(value => value.DisplayName).ToArray());
        Assert.Equal("Fixture conflict", Assert.Single(mechanics.ProjectionConflicts!).Message);
    }

    [Fact]
    public void UniversalCompetencyPresentationUsesImplementationMechanicForValueAndRankState()
    {
        var fallback = new CharacterMechanicsPresentationView(
            Competencies: new CompetencyCollectionPresentationView(
                [new CompetencyPresentationView(
                    "competency.arcana",
                    "Arcana",
                    "-",
                    Kind: "skill",
                    SupportsRanks: true,
                    MechanicKeys: ["competency.skill.arcana"],
                    CompatibilityMechanicKeys: ["competency.skill.knowledge-arcana"],
                    RankInputKey: "skill.arcana")]));

        var projection = EmptyProjection() with
        {
            Mechanics =
            [
                Mechanic(
                    "competency.skill.arcana",
                    "competency",
                    "Arcana",
                    9,
                    contributions:
                    [
                        Contribution("ability", "Intelligence", 4),
                        Contribution("ranks", "Ranks", 5)
                    ])
            ]
        };
        var state = new CharacterStateView(
            Guid.NewGuid(),
            false,
            null,
            new CharacterDeathSavesView(0, 0),
            [],
            [],
            [],
            [
                new CharacterRulesInputStateView(
                    Guid.NewGuid(),
                    CharacterRulesInputKinds.CompetencyRank,
                    "skill.arcana",
                    5,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow)
            ]);

        var mechanics = CharacterPresentationProjector.ProjectCharacterRules(
            fallback,
            projection,
            state);

        var arcana = Assert.Single(mechanics.Competencies!.Entries);
        Assert.Equal("competency.arcana", arcana.Key);
        Assert.Equal(9, arcana.EffectiveValue);
        Assert.Equal(5, arcana.Ranks);
        Assert.Equal("skill.arcana", arcana.RankInputKey);
        Assert.Equal(2, arcana.Breakdown!.Count);
    }

    [Fact]
    public void UniversalCompetencyDoesNotChooseArbitrarilyBetweenResolvedImplementations()
    {
        var fallback = new CharacterMechanicsPresentationView(
            Competencies: new CompetencyCollectionPresentationView(
                [new CompetencyPresentationView(
                    "competency.alchemy",
                    "Alchemy",
                    "-",
                    Kind: "specialized-skill",
                    Family: "Craft",
                    MechanicKeys:
                    [
                        "competency.skill.alchemy",
                        "competency.tool.alchemists-supplies"
                    ])]));

        var projection = EmptyProjection() with
        {
            Mechanics =
            [
                Mechanic("competency.skill.alchemy", "competency", "Alchemy Skill", 8),
                Mechanic("competency.tool.alchemists-supplies", "competency", "Alchemist's Supplies", 5)
            ]
        };

        var mechanics = CharacterPresentationProjector.ProjectCharacterRules(
            fallback,
            projection);

        var alchemy = Assert.Single(mechanics.Competencies!.Entries);
        Assert.Equal("competency.alchemy", alchemy.Key);
        Assert.Equal("-", alchemy.EffectiveValue);
        Assert.Null(alchemy.Breakdown);
    }

    [Fact]
    public void AbilityProjectionUsesAxisKeysForStandardAndAdditionalAbilities()
    {
        var projection = EmptyProjection() with
        {
            Mechanics =
            [
                Mechanic("ability.strength.score", "ability-score", "Strength Score", 14),
                Mechanic("ability.honor.score", "ability-score", "Honor Score", 12)
            ]
        };

        var mechanics = CharacterPresentationProjector.ProjectCharacterRules(
            null,
            projection);

        Assert.Equal(
            ["strength", "honor"],
            mechanics.AbilityValues!.Select(value => value.Key).ToArray());
    }

    [Fact]
    public void EquipmentDefinitionsJoinCharacterOwnedOccurrencesWithoutCollapsingDuplicates()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var state = new CharacterStateView(
            Guid.NewGuid(),
            false,
            null,
            new CharacterDeathSavesView(0, 0),
            [
                new CharacterInventoryItemOccurrenceView(
                    firstId,
                    "item.long-sword",
                    DateTimeOffset.UtcNow,
                    Quantity: 1,
                    IsCarried: true,
                    IsEquipped: true,
                    IsAttuned: false),
                new CharacterInventoryItemOccurrenceView(
                    secondId,
                    "item.long-sword",
                    DateTimeOffset.UtcNow.AddSeconds(1),
                    Quantity: 2,
                    IsCarried: false,
                    IsEquipped: false,
                    IsAttuned: false)
            ],
            [],
            []);

        var projection = EmptyProjection() with
        {
            Equipment =
            [
                new RulesCoreCharacterEquipmentDefinitionView(
                    "equipment.item.long-sword",
                    "item.long-sword",
                    "Longsword",
                    "resolved",
                    "weapon",
                    "martial",
                    null,
                    4m,
                    "lb.",
                    null,
                    null,
                    false,
                    null,
                    ["versatile"],
                    Provenance)
            ]
        };

        var mechanics = CharacterPresentationProjector.ProjectCharacterRules(
            null,
            projection,
            state);

        var occurrences = mechanics.Inventory!.ItemOccurrences!;
        Assert.Equal(2, occurrences.Count);
        Assert.Contains(occurrences, value => value.OccurrenceId == firstId);
        Assert.Contains(occurrences, value => value.OccurrenceId == secondId);
        Assert.All(
            occurrences,
            value =>
            {
                Assert.Contains(value.Facts!, fact =>
                    fact.Key == "item-type" && fact.Value == "weapon");
                Assert.Contains(value.Facts!, fact =>
                    fact.Key == "equipment-category" && fact.Value == "martial");
                Assert.Contains(value.Facts!, fact =>
                    fact.Key == "weight" && fact.Value == "4 lb.");
                Assert.Contains(value.Facts!, fact =>
                    fact.Key == "requires-attunement" && fact.Value == "No");
                Assert.Contains(value.Facts!, fact =>
                    fact.Key == "properties" && fact.Value == "versatile");
            });
    }

    private static RulesCoreCharacterRulesProjectionView EmptyProjection() =>
        new(
            "global",
            null,
            1,
            new DateTimeOffset(2026, 9, 22, 20, 20, 0, TimeSpan.Zero),
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);

    private static RulesCoreCharacterResolvedMechanicView Mechanic(
        string key,
        string kind,
        string name,
        int? numeric = null,
        string? text = null,
        string? unit = null,
        IReadOnlyList<RulesCoreCharacterMechanicContributionView>? contributions = null) =>
        new(
            key,
            kind,
            name,
            "resolved",
            numeric,
            text,
            unit,
            [],
            [],
            [],
            [],
            contributions ?? [],
            Provenance);

    private static RulesCoreCharacterMechanicContributionView Contribution(
        string key,
        string label,
        int value) =>
        new(key, label, "add", value, null, null, Provenance);

    private static RulesCoreCharacterResourceView Resource(
        string key,
        string label,
        int? current = null,
        int? maximum = null) =>
        new(key, label, "resolved", current, maximum, null, [], Provenance);

    private static readonly RulesCoreCharacterMechanicProvenanceView Provenance =
        new([], [], []);
}
