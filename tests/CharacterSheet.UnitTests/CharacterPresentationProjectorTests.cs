using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.UnitTests;

public sealed class CharacterPresentationProjectorTests
{
    [Fact]
    public void AdvancementPreservesParentDuplicateOccurrencesUnavailableReferencesAndNoProgression()
    {
        var parentId = Guid.NewGuid();
        var subclassId = Guid.NewGuid();
        var featOneId = Guid.NewGuid();
        var featTwoId = Guid.NewGuid();
        var build = Build(
            new CharacterAdvancementEntryView(parentId, 0, "class", "class:wizard", null, Now, Now),
            new CharacterAdvancementEntryView(subclassId, null, "subclass", "subclass:evocation", parentId, Now, Now),
            new CharacterAdvancementEntryView(featOneId, null, "feat", "feat:alert", null, Now, Now),
            new CharacterAdvancementEntryView(featTwoId, null, "feat", "feat:alert", null, Now, Now),
            new CharacterAdvancementEntryView(Guid.NewGuid(), null, "prestigeClass", "prestige:missing", null, Now, Now));
        var resolved = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal)
        {
            ["class:wizard"] = Rule("class:wizard", "class", "Wizard"),
            ["subclass:evocation"] = Rule("subclass:evocation", "subclass", "Evocation"),
            ["feat:alert"] = Rule("feat:alert", "feat", "Alert")
        };

        var projected = CharacterPresentationProjector.ProjectAdvancement(build, resolved);

        Assert.Equal(5, projected.Occurrences.Count);
        var subclass = Assert.Single(projected.Occurrences, value => value.OccurrenceId == subclassId);
        Assert.Equal(parentId, subclass.ParentOccurrenceId);
        Assert.Equal(2, projected.Occurrences.Count(value => value.ConceptKey == "feat:alert"));
        Assert.Equal(
            2,
            projected.Occurrences.Where(value => value.ConceptKey == "feat:alert")
                .Select(value => value.OccurrenceId)
                .Distinct()
                .Count());
        var unavailable = Assert.Single(projected.Occurrences, value => value.ConceptKey == "prestige:missing");
        Assert.Equal("Unavailable rule reference", unavailable.DisplayName);
        Assert.Null(unavailable.SourceAttributions);
        Assert.All(projected.Occurrences, value =>
            Assert.DoesNotContain("Level", value.DisplayName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            Assert.Single(projected.Occurrences, value => value.ConceptKey == "class:wizard").SourceAttributions!,
            value => value.Label == "Fixture Rules");
    }

    [Fact]
    public void AdvancementProjectsOwnedLevelsAndSubclassUsesParentClassLevel()
    {
        var parentId = Guid.NewGuid();
        var subclassId = Guid.NewGuid();
        var prestigeId = Guid.NewGuid();
        var build = Build(
            new CharacterAdvancementEntryView(
                parentId,
                0,
                CharacterBuildAdvancementKinds.Class,
                "class:wizard",
                null,
                Now,
                Now,
                7),
            new CharacterAdvancementEntryView(
                subclassId,
                null,
                CharacterBuildAdvancementKinds.Subclass,
                "subclass:evocation",
                parentId,
                Now,
                Now),
            new CharacterAdvancementEntryView(
                prestigeId,
                8,
                CharacterBuildAdvancementKinds.PrestigeClass,
                "prestige:loremaster",
                null,
                Now,
                Now,
                3));
        var resolved = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal)
        {
            ["class:wizard"] = Rule("class:wizard", "class", "Wizard"),
            ["subclass:evocation"] = Rule("subclass:evocation", "subclass", "Evocation"),
            ["prestige:loremaster"] = Rule("prestige:loremaster", "class", "Loremaster")
        };

        var projected = CharacterPresentationProjector.ProjectAdvancement(build, resolved);

        var wizard = Assert.Single(projected.Occurrences, value => value.OccurrenceId == parentId);
        Assert.Equal("Level", wizard.Progression!.Label);
        Assert.Equal(7, wizard.Progression.Value);
        Assert.Equal("Level 7", wizard.Progression.FormattedValue);

        var subclass = Assert.Single(projected.Occurrences, value => value.OccurrenceId == subclassId);
        Assert.Equal("Parent Level", subclass.Progression!.Label);
        Assert.Equal(7, subclass.Progression.Value);
        Assert.Equal("Parent Level 7", subclass.Progression.FormattedValue);

        var prestige = Assert.Single(projected.Occurrences, value => value.OccurrenceId == prestigeId);
        Assert.Equal(3, prestige.Progression!.Value);
    }

    [Fact]
    public void CompetencyProjectionUsesConceptIdentityMetadataAndOnlyEffectiveDeriveParentRelationships()
    {
        var hide = Competency(
            "competency.skill.hide",
            "skill.hide",
            "Hide",
            specialty: null,
            relationships: [
                new RulesCoreMechanicRelationshipView(
                    "stealth",
                    "composite",
                    "competency.skill.stealth",
                    ["competency.skill.hide", "competency.skill.move-silently"],
                    "average-floor",
                    "components-to-parent",
                    "derive-parent",
                    true,
                    [])
            ]);
        var move = Competency("competency.skill.move-silently", "skill.move-silently", "Move Silently");
        var stealth = Competency("competency.skill.stealth", "skill.stealth", "Stealth");
        var craft = Competency(
            "competency.skill.craft",
            "skill.craft",
            "Craft",
            family: "Craft",
            isFamily: true);
        var craftAlchemy = Competency(
            "competency.skill.craft-alchemy",
            "skill.craft-alchemy",
            "Craft (Alchemy)",
            family: "Craft",
            specialty: "alchemy",
            competencyKind: "specialized-skill");
        var independent = Competency(
            "competency.skill.perception",
            "skill.perception",
            "Perception",
            relationships: [
                new RulesCoreMechanicRelationshipView(
                    "perception",
                    "composite",
                    "competency.skill.perception",
                    ["competency.skill.listen", "competency.skill.spot"],
                    "average-floor",
                    "components-to-parent",
                    "independent-parent",
                    true,
                    [])
            ]);
        var catalog = Catalog(hide, move, stealth, craft, craftAlchemy, independent);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        Assert.NotNull(mechanics.Competencies);
        var entries = mechanics.Competencies.Entries;
        var family = Assert.Single(entries, value => value.Key == "skill.craft");
        Assert.True(family.IsFamily);
        Assert.Equal("Craft", family.Family);
        var specialized = Assert.Single(entries, value => value.Key == "skill.craft-alchemy");
        Assert.Equal("-", specialized.EffectiveValue);
        Assert.False(specialized.IsFamily);
        Assert.Equal("specialized-skill", specialized.Kind);
        Assert.Equal("Craft", specialized.Family);
        Assert.Equal("alchemy", specialized.Specialty);
        Assert.Equal("intelligence", specialized.GoverningAbility);
        Assert.True(specialized.SupportsRanks);
        Assert.True(specialized.SupportsClassSkillState);
        Assert.True(specialized.SupportsTrainingState);
        Assert.True(specialized.TrainedOnly);
        Assert.True(specialized.ArmorCheckPenalty?.Applies);
        var relationship = Assert.Single(mechanics.Competencies.Relationships!);
        Assert.Equal("skill.stealth", relationship.ParentKey);
        Assert.Equal(["skill.hide", "skill.move-silently"], relationship.ComponentKeys);
        Assert.Equal("average-floor", relationship.Composition);
        Assert.Equal("derive-parent", relationship.ResolutionKind);
        Assert.DoesNotContain(
            mechanics.Competencies.Relationships!,
            value => value.ParentKey == "skill.perception");
        Assert.Null(mechanics.AbilityValues);
    }

    [Fact]
    public void UniversalCompetencyCatalogCollapsesImplementationsAndPopulatesFamilies()
    {
        var craft = Competency(
            "competency.skill.craft",
            "skill.craft",
            "Craft",
            family: "Craft",
            isFamily: true);
        var craftAlchemy = Competency(
            "competency.skill.craft-alchemy",
            "skill.craft-alchemy",
            "Craft (alchemy)",
            family: "Craft",
            specialty: "alchemy",
            competencyKind: "specialized-skill");
        var alchemyTools = Competency(
            "competency.tool.alchemists-supplies",
            "tool.alchemists-supplies",
            "Alchemist's Supplies",
            competencyKind: "tool");

        var universal =
            new RulesCoreUniversalCompetencyView[]
            {
                new(
                    "competency.craft",
                    "craft",
                    "Craft",
                    "Craft",
                    true,
                    null,
                    ["competency.alchemy", "competency.blacksmithing"],
                    ["competency.skill.craft"],
                    [],
                    ["Craft"],
                    [],
                    [],
                    [],
                    []),
                new(
                    "competency.alchemy",
                    "alchemy",
                    "Alchemy",
                    "Craft",
                    false,
                    "competency.alchemy.training",
                    [],
                    ["competency.skill.craft-alchemy", "competency.tool.alchemists-supplies"],
                    ["competency.skill.alchemy"],
                    ["Alchemy", "Craft (alchemy)", "Alchemist's Supplies"],
                    [],
                    [
                        new RulesCoreCompetencyFacetView(
                            "skill",
                            [],
                            true,
                            true,
                            true,
                            ["competency.skill.craft-alchemy"]),
                        new RulesCoreCompetencyFacetView(
                            "tool",
                            [],
                            false,
                            false,
                            true,
                            ["competency.tool.alchemists-supplies"])
                    ],
                    [],
                    []),
                new(
                    "competency.blacksmithing",
                    "blacksmithing",
                    "Blacksmithing",
                    "Craft",
                    false,
                    "competency.blacksmithing.training",
                    [],
                    [],
                    ["competency.skill.craft-blacksmithing"],
                    ["Blacksmithing", "Craft (blacksmithing)"],
                    [],
                    [],
                    [],
                    [])
            };
        var catalog = new RulesCoreMechanicsCatalogView(
            "global",
            null,
            1,
            Now,
            [craft, craftAlchemy, alchemyTools],
            universal);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        Assert.NotNull(mechanics.Competencies);
        Assert.Equal(3, mechanics.Competencies.Entries.Count);

        var family = Assert.Single(
            mechanics.Competencies.Entries,
            value => value.Key == "competency.craft");
        Assert.True(family.IsFamily);
        Assert.Equal("family", family.Kind);
        Assert.Equal(
            ["competency.alchemy", "competency.blacksmithing"],
            family.ChildCompetencyKeys);

        var alchemy = Assert.Single(
            mechanics.Competencies.Entries,
            value => value.Key == "competency.alchemy");
        Assert.Equal("Alchemy", alchemy.Label);
        Assert.Equal("specialized-skill", alchemy.Kind);
        Assert.Equal("Craft", alchemy.Family);
        Assert.Equal("competency.alchemy.training", alchemy.SharedTrainingKey);
        Assert.Equal("skill.craft-alchemy", alchemy.RankInputKey);
        Assert.True(alchemy.SupportsRanks);
        Assert.Equal(2, alchemy.Facets!.Count);
        Assert.Equal(
            ["competency.skill.craft-alchemy", "competency.tool.alchemists-supplies"],
            alchemy.MechanicKeys);
        Assert.DoesNotContain(
            mechanics.Competencies.Entries,
            value => value.Key == "skill.craft-alchemy"
                || value.Key == "tool.alchemists-supplies");

        var blacksmithing = Assert.Single(
            mechanics.Competencies.Entries,
            value => value.Key == "competency.blacksmithing");
        Assert.Equal("Blacksmithing", blacksmithing.Label);
        Assert.Equal("specialized-skill", blacksmithing.Kind);
        Assert.Equal("Craft", blacksmithing.Family);
        Assert.Equal("intelligence", blacksmithing.GoverningAbility);
        Assert.Equal("-", blacksmithing.EffectiveValue);
        Assert.Null(blacksmithing.RankInputKey);
    }

    [Fact]
    public void UniversalCompetencyMechanicsContractIsAuthoritativeForPresentation()
    {
        var source = Competency(
            "competency.skill.example",
            "skill.example",
            "Example",
            governingAbility: "charisma");

        var universal = new RulesCoreUniversalCompetencyView(
            "competency.example",
            "example",
            "Example",
            null,
            false,
            "competency.example.training",
            [],
            ["competency.skill.example"],
            [],
            ["Example"],
            [],
            [],
            [],
            [],
            new RulesCoreUniversalCompetencyMechanicsView(
                new RulesCoreUniversalGoverningAbilityView(
                    "fixed",
                    "wis",
                    ["wisdom"]),
                SupportsRanks: true,
                SupportsClassSkillState: false,
                SupportsTrainingState: true,
                TrainedOnly: false,
                ArmorCheckPenaltyApplies: false,
                EvaluationProfileKeys: ["ranked-skill"],
                EvaluationKinds: ["competency-profile"],
                CanEvaluate: true));

        var catalog = new RulesCoreMechanicsCatalogView(
            "global",
            null,
            1,
            Now,
            [source],
            [universal]);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        var example = Assert.Single(mechanics.Competencies!.Entries);
        Assert.Equal("wisdom", example.GoverningAbility);
        Assert.True(example.SupportsRanks);
        Assert.False(example.SupportsClassSkillState);
        Assert.True(example.SupportsTrainingState);
        Assert.False(example.TrainedOnly);
        Assert.False(example.ArmorCheckPenalty?.Applies);
    }

    [Fact]
    public void UniversalCompetencyGoverningAbilitiesNormalizeAliasesAndPreserveRealConflicts()
    {
        var animalHandlingLegacy = Competency(
            "competency.skill.handle-animal",
            "skill.handle-animal",
            "Handle Animal",
            governingAbility: "cha");
        var animalHandlingModern = Competency(
            "competency.skill.animal-handling",
            "skill.animal-handling",
            "Animal Handling",
            governingAbility: "wisdom");
        var medicineLegacy = Competency(
            "competency.skill.heal",
            "skill.heal",
            "Heal",
            governingAbility: "wis");
        var medicineModern = Competency(
            "competency.skill.medicine",
            "skill.medicine",
            "Medicine",
            governingAbility: "wisdom");

        var universal =
            new RulesCoreUniversalCompetencyView[]
            {
                new(
                    "competency.animal-handling",
                    "animal-handling",
                    "Animal Handling",
                    null,
                    false,
                    "competency.animal-handling.training",
                    [],
                    ["competency.skill.handle-animal", "competency.skill.animal-handling"],
                    [],
                    ["Handle Animal", "Animal Handling"],
                    [],
                    [],
                    [],
                    []),
                new(
                    "competency.medicine",
                    "medicine",
                    "Medicine",
                    null,
                    false,
                    "competency.medicine.training",
                    [],
                    ["competency.skill.heal", "competency.skill.medicine"],
                    [],
                    ["Heal", "Medicine"],
                    [],
                    [],
                    [],
                    [])
            };
        var catalog = new RulesCoreMechanicsCatalogView(
            "global",
            null,
            1,
            Now,
            [animalHandlingLegacy, animalHandlingModern, medicineLegacy, medicineModern],
            universal);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        var animalHandling = Assert.Single(
            mechanics.Competencies!.Entries,
            value => value.Key == "competency.animal-handling");
        Assert.Equal("wisdom / charisma", animalHandling.GoverningAbility);

        var medicine = Assert.Single(
            mechanics.Competencies.Entries,
            value => value.Key == "competency.medicine");
        Assert.Equal("wisdom", medicine.GoverningAbility);
    }

    [Fact]
    public void ExplicitEmptyUniversalCompetencyCatalogDoesNotFallBackToSourceShapedRows()
    {
        var arcana = Competency(
            "competency.skill.arcana",
            "skill.arcana",
            "Arcana");

        var catalog = new RulesCoreMechanicsCatalogView(
            "global",
            null,
            1,
            Now,
            [arcana],
            []);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        Assert.NotNull(mechanics.Competencies);
        Assert.Empty(mechanics.Competencies.Entries);
    }

    [Fact]
    public void AdvancementKindDoesNotNeedToMatchResolvedRuleEntityType()
    {
        var occurrenceId = Guid.NewGuid();
        var build = Build(new CharacterAdvancementEntryView(
            occurrenceId,
            null,
            "position",
            "position.acquisitions-documancer",
            null,
            Now,
            Now));
        var resolved = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal)
        {
            ["position.acquisitions-documancer"] = Rule(
                "position.acquisitions-documancer",
                "charoption",
                "Documancer")
        };

        var projected = CharacterPresentationProjector.ProjectAdvancement(build, resolved);

        var occurrence = Assert.Single(projected.Occurrences);
        Assert.Equal(occurrenceId, occurrence.OccurrenceId);
        Assert.Equal("position", occurrence.Kind);
        Assert.Equal("position.acquisitions-documancer", occurrence.ConceptKey);
        Assert.Equal("Documancer", occurrence.DisplayName);
    }

    [Fact]
    public void CompositeCheckRelationshipProjectsGenericProcedureWithoutSourceSpecificLogic()
    {
        var assessment = Check("check.harvesting.assessment", "Assessment");
        var carving = Check("check.harvesting.carving", "Carving");
        var total = new RulesCoreMechanicView(
            "check.harvesting.total",
            "check",
            "Harvesting",
            null,
            true,
            new RulesCoreMechanicApplicabilityView("always", true, [], null),
            "sum",
            false,
            [],
            [new RulesCoreMechanicRelationshipView(
                "check-composite.harvesting",
                "composite-check",
                "check.harvesting.total",
                ["check.harvesting.assessment", "check.harvesting.carving"],
                "sum",
                "components-to-parent",
                null,
                true,
                [])],
            [],
            null,
            null,
            [],
            []);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(assessment, carving, total),
            null);

        var procedure = Assert.Single(mechanics.Procedures!);
        Assert.Equal("check.harvesting.total", procedure.Key);
        Assert.Equal("Harvesting", procedure.Name);
        Assert.Equal(
            ["check.harvesting.assessment", "check.harvesting.carving"],
            procedure.Components.Select(value => value.Key).ToArray());
        Assert.NotNull(procedure.Result);
        Assert.Equal("-", procedure.Result.Value);
        Assert.All(procedure.Components, component =>
        {
            Assert.NotNull(component.EffectiveModifierOrResult);
            Assert.Equal("-", component.EffectiveModifierOrResult.EffectiveValue);
        });
    }

    [Fact]
    public void ExternalPublicRulesAreMarkedSupplementalAndKeepRequiredCreatorCredit()
    {
        var source = new RulesCoreMechanicSourceAttributionView(
            null,
            null,
            "Fixture Publisher",
            null,
            null,
            "fixture-public-rules",
            "Fixture Public Rules",
            "5e",
            "public-release",
            new DateOnly(2026, 9, 19),
            "fixture-public-reference",
            "Fixture public reference",
            "https://example.test/public-rules",
            true,
            true);
        var applicability = new RulesCoreMechanicApplicabilityView(
            "external-public-rules",
            true,
            [],
            null);
        var assessment = Check("check.external.assessment", "External Assessment") with
        {
            Applicability = applicability,
            SourceAttributions = [source]
        };
        var carving = Check("check.external.carving", "External Carving") with
        {
            Applicability = applicability,
            SourceAttributions = [source]
        };
        var total = new RulesCoreMechanicView(
            "check.external.total",
            "check",
            "External Procedure",
            null,
            true,
            applicability,
            "sum",
            false,
            [],
            [new RulesCoreMechanicRelationshipView(
                "check-composite.external",
                "composite-check",
                "check.external.total",
                ["check.external.assessment", "check.external.carving"],
                "sum",
                "components-to-parent",
                null,
                true,
                [])],
            [],
            null,
            null,
            [],
            [source]);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(assessment, carving, total),
            null);

        Assert.All(mechanics.Checks!, check => Assert.True(check.Supplemental));
        var procedure = Assert.Single(mechanics.Procedures!);
        Assert.True(procedure.Supplemental);
        var attribution = Assert.Single(procedure.SourceAttributions!);
        Assert.Equal("Fixture Public Rules", attribution.Label);
        Assert.Contains("Rules by Fixture Publisher", attribution.Detail ?? string.Empty);
        Assert.Equal("https://example.test/public-rules", attribution.OfficialUrl);
        Assert.Equal("Official rules", attribution.LinkLabel);
        Assert.True(attribution.PresentationRequired);
    }

    [Fact]
    public void UnevaluatedSavingThrowDefinitionsRemainVisibleWithDashValues()
    {
        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(
                SavingThrow("save.fortitude", "Fortitude Save"),
                SavingThrow("save.reflex", "Reflex Save"),
                SavingThrow("save.will", "Will Save")),
            null);

        Assert.Collection(
            mechanics.SavingThrows!,
            save =>
            {
                Assert.Equal("save.fortitude", save.Key);
                Assert.Equal("Fortitude Save", save.Label);
                Assert.Equal("-", save.EffectiveValue);
                Assert.Null(save.GoverningAbility);
            },
            save =>
            {
                Assert.Equal("save.reflex", save.Key);
                Assert.Equal("Reflex Save", save.Label);
                Assert.Equal("-", save.EffectiveValue);
                Assert.Null(save.GoverningAbility);
            },
            save =>
            {
                Assert.Equal("save.will", save.Key);
                Assert.Equal("Will Save", save.Label);
                Assert.Equal("-", save.EffectiveValue);
                Assert.Null(save.GoverningAbility);
            });
    }

    [Fact]
    public void UnevaluatedDefenseCombatAndResourceDefinitionsRemainVisibleWithDashValues()
    {
        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(
                UnevaluatedMechanic("defense.ac.touch", "defense", "Touch Armor Class"),
                UnevaluatedMechanic("defense.ac.flat-footed", "defense", "Flat-Footed Armor Class"),
                UnevaluatedMechanic("defense.spell-resistance", "defense", "Spell Resistance"),
                UnevaluatedMechanic("defense.damage-reduction", "defense", "Damage Reduction", "none", false),
                UnevaluatedMechanic("combat.base-attack-bonus", "combat-value", "Base Attack Bonus"),
                UnevaluatedMechanic("combat.grapple", "combat-value", "Grapple Modifier"),
                UnevaluatedMechanic("resource.nonlethal-damage", "resource", "Nonlethal Damage"),
                UnevaluatedMechanic("resource.hit-dice", "resource", "Hit Dice")),
            null);

        Assert.Equal(
            ["defense.ac.touch", "defense.ac.flat-footed", "defense.spell-resistance", "defense.damage-reduction"],
            mechanics.Defenses!.Values.Select(value => value.Key).ToArray());
        Assert.All(mechanics.Defenses.Values, value => Assert.Equal("-", value.EffectiveValue));

        Assert.Equal(
            ["combat.base-attack-bonus", "combat.grapple"],
            mechanics.CombatFundamentals!.Select(value => value.Key).ToArray());
        Assert.All(mechanics.CombatFundamentals, value => Assert.Equal("-", value.EffectiveValue));

        Assert.Collection(
            mechanics.HealthTracks!,
            nonlethal =>
            {
                Assert.Equal("resource.nonlethal-damage", nonlethal.Key);
                Assert.Equal("nonlethal-damage", nonlethal.Role);
                Assert.Equal("-", nonlethal.Current);
            },
            hitDice =>
            {
                Assert.Equal("resource.hit-dice", hitDice.Key);
                Assert.Equal("hit-dice", hitDice.Role);
                Assert.Equal("-", hitDice.Current);
            });
    }

    [Fact]
    public void InspirationAndPersistentSupportValuesProjectWithoutFrontendSpecificFormulas()
    {
        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(
                UnevaluatedMechanic("resource.heroic-inspiration", "resource", "Heroic Inspiration"),
                UnevaluatedMechanic("passive.awareness", "passive-value", "Awareness"),
                UnevaluatedMechanic("training.armor.light", "training", "Light Armor"),
                UnevaluatedMechanic("proficiency.weapon.simple", "proficiency", "Simple Weapons"),
                UnevaluatedMechanic("sense.darkvision", "sense", "Darkvision")),
            null);

        Assert.NotNull(mechanics.Inspiration);
        Assert.Equal("resource.heroic-inspiration", mechanics.Inspiration.Key);
        Assert.Equal("-", mechanics.Inspiration.EffectiveValue);
        Assert.Single(mechanics.PassiveValues!);
        Assert.Equal(2, mechanics.Training!.Count);
        Assert.Single(mechanics.Senses!);
        Assert.Null(mechanics.HealthTracks);
    }

    [Fact]
    public void MovementMechanicsProjectEveryBackendDefinedModeWithoutFrontendAssumptions()
    {
        var mechanics = CharacterPresentationProjector.ProjectMechanics(
            Catalog(
                UnevaluatedMechanic("movement.walk", "movement", "Walk"),
                UnevaluatedMechanic("movement.swim", "movement", "Swim"),
                UnevaluatedMechanic("movement.climb", "movement", "Climb"),
                UnevaluatedMechanic("movement.fly", "movement", "Fly")),
            null);

        Assert.Equal(
            ["movement.walk", "movement.swim", "movement.climb", "movement.fly"],
            mechanics.Movement!.Select(value => value.Key).ToArray());
        Assert.All(mechanics.Movement, value => Assert.Equal("-", value.EffectiveValue));
    }

    [Fact]
    public void MissingCharacterInputsAndCapabilitiesAreNeverSubmittedAsZero()
    {
        var mechanic = new RulesCoreMechanicView(
            "save.fortitude",
            "saving-throw",
            "Fortitude Save",
            null,
            true,
            new RulesCoreMechanicApplicabilityView(
                "character-capability",
                true,
                ["save.fortitude"],
                null),
            "sum",
            true,
            [new RulesCoreMechanicInputView(
                "abilityContribution",
                "integer",
                "derived",
                true,
                true,
                null,
                null,
                null)],
            [],
            [],
            null,
            null,
            [],
            []);
        var request = CharacterPresentationProjector.BuildSafeAutomaticEvaluations(Catalog(mechanic));
        Assert.Empty(request.Evaluations);

        var noCapabilityButMissingInput = mechanic with
        {
            MechanicKey = "combat.example",
            Applicability = mechanic.Applicability with { RequiredCapabilityKeys = [] }
        };
        request = CharacterPresentationProjector.BuildSafeAutomaticEvaluations(Catalog(noCapabilityButMissingInput));
        Assert.Empty(request.Evaluations);

        var defaultedCharacterState = new RulesCoreMechanicView(
            "combat.defaulted-character-state",
            "combat-value",
            "Defaulted Character State",
            null,
            true,
            new RulesCoreMechanicApplicabilityView("always", false, [], null),
            "sum",
            true,
            [new RulesCoreMechanicInputView(
                "otherModifier",
                "integer",
                "character-state",
                false,
                true,
                0,
                null,
                null)],
            [],
            [],
            null,
            null,
            [],
            []);
        request = CharacterPresentationProjector.BuildSafeAutomaticEvaluations(Catalog(defaultedCharacterState));
        Assert.Empty(request.Evaluations);
    }

    [Fact]
    public void RulesCoreEvaluatedValuesAreProjectedWithoutCharacterSheetArithmetic()
    {
        var mechanic = new RulesCoreMechanicView(
            "combat.example",
            "combat-value",
            "Example Combat Value",
            null,
            true,
            new RulesCoreMechanicApplicabilityView("always", false, [], null),
            "sum",
            true,
            [],
            [],
            [],
            null,
            null,
            [],
            []);
        var batch = new RulesCoreMechanicsBatchEvaluationView(
            "global",
            null,
            1,
            Now,
            [new RulesCoreMechanicBatchEvaluationItemView(
                "combat.example",
                new RulesCoreMechanicEvaluationView(
                    "combat.example",
                    "sum",
                    7,
                    null,
                    null,
                    true,
                    []))]);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(Catalog(mechanic), batch);

        var value = Assert.Single(mechanics.CombatFundamentals!);
        Assert.Equal(7, value.EffectiveValue);
    }

    private static CharacterBuildView Build(params CharacterAdvancementEntryView[] entries) =>
        new(
            Guid.NewGuid(),
            "BuildInProgress",
            false,
            [],
            [new BaseAbilityScoreInputView(Guid.NewGuid(), "strength", 18, Now, Now)],
            entries);

    private static RulesCoreResolvedRuleSummaryView Rule(string key, string type, string name) =>
        new(
            key,
            type,
            name,
            2,
            name,
            "SRD",
            "fixture",
            "Fixture Rules",
            "3.5e",
            "3.5e");

    private static RulesCoreMechanicView Check(string mechanicKey, string displayName) =>
        new(
            mechanicKey,
            "check",
            displayName,
            null,
            true,
            new RulesCoreMechanicApplicabilityView("always", true, [], null),
            "sum",
            false,
            [],
            [],
            [],
            new RulesCoreMechanicCheckView(
                new RulesCoreCheckAbilityView("rule-resolved", null, []),
                new RulesCoreCheckCompetencyView("rule-resolved", ["skill"], null)),
            null,
            [],
            []);

    private static RulesCoreMechanicView SavingThrow(string mechanicKey, string displayName) =>
        new(
            mechanicKey,
            "saving-throw",
            displayName,
            null,
            true,
            new RulesCoreMechanicApplicabilityView(
                "character-capability",
                true,
                [mechanicKey],
                null),
            "sum",
            true,
            [new RulesCoreMechanicInputView(
                "baseSave",
                "integer",
                "derived",
                true,
                true,
                null,
                null,
                null)],
            [],
            [],
            null,
            null,
            [],
            []);

    private static RulesCoreMechanicView UnevaluatedMechanic(
        string mechanicKey,
        string kind,
        string displayName,
        string evaluationKind = "sum",
        bool canEvaluate = true) =>
        new(
            mechanicKey,
            kind,
            displayName,
            null,
            true,
            new RulesCoreMechanicApplicabilityView(
                "character-capability",
                true,
                [mechanicKey],
                null),
            evaluationKind,
            canEvaluate,
            [],
            [],
            [],
            null,
            null,
            [],
            []);

    private static RulesCoreMechanicView Competency(
        string mechanicKey,
        string conceptKey,
        string displayName,
        string? family = null,
        string? specialty = null,
        IReadOnlyList<RulesCoreMechanicRelationshipView>? relationships = null,
        string competencyKind = "skill",
        bool isFamily = false,
        string governingAbility = "intelligence")
    {
        var definition = new RulesCoreCompetencyDefinitionView(
            competencyKind,
            family,
            specialty,
            governingAbility,
            true,
            true,
            true,
            true,
            true,
            null,
            [],
            IsFamily: isFamily);
        return new RulesCoreMechanicView(
            mechanicKey,
            "competency",
            displayName,
            conceptKey,
            true,
            new RulesCoreMechanicApplicabilityView("always", true, [], null),
            "competency-profile",
            false,
            [],
            relationships ?? [],
            [],
            null,
            definition,
            [],
            [new RulesCoreMechanicSourceAttributionView(
                "fixture",
                "Fixture Rules",
                "Fixture Provider",
                "SRD",
                2,
                "fixture-work",
                "Fixture Work",
                "3.5e",
                "book",
                new DateOnly(2003, 7, 1),
                "fixture-ref",
                "Fixture reference",
                "https://example.test/reference",
                false,
                false)]);
    }

    private static RulesCoreMechanicsCatalogView Catalog(params RulesCoreMechanicView[] mechanics) =>
        new("global", null, 1, Now, mechanics);

    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
}
