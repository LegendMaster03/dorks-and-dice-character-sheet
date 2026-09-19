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
        var knowledge = Competency(
            "competency.skill.knowledge-planes",
            "skill.knowledge-planes",
            "Knowledge (the planes)",
            family: "Knowledge",
            specialty: "the planes");
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
        var catalog = Catalog(hide, move, stealth, knowledge, independent);

        var mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, null);

        Assert.NotNull(mechanics.Competencies);
        var entries = mechanics.Competencies.Entries;
        var specialized = Assert.Single(entries, value => value.Key == "skill.knowledge-planes");
        Assert.Equal("Not configured", specialized.EffectiveValue);
        Assert.Equal("Knowledge", specialized.Family);
        Assert.Equal("the planes", specialized.Specialty);
        Assert.Equal("intelligence", specialized.GoverningAbility);
        Assert.True(specialized.SupportsRanks);
        Assert.True(specialized.SupportsClassSkillState);
        Assert.True(specialized.SupportsTrainingState);
        Assert.True(specialized.TrainedOnly);
        Assert.True(specialized.ArmorCheckPenalty?.Applies);
        var relationship = Assert.Single(mechanics.Competencies.Relationships!);
        Assert.Equal("skill.stealth", relationship.ParentKey);
        Assert.Equal(["skill.hide", "skill.move-silently"], relationship.ComponentKeys);
        Assert.DoesNotContain(
            mechanics.Competencies.Relationships!,
            value => value.ParentKey == "skill.perception");
        Assert.Null(mechanics.AbilityValues);
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
        Assert.Null(procedure.Result);
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

    private static RulesCoreMechanicView Competency(
        string mechanicKey,
        string conceptKey,
        string displayName,
        string? family = null,
        string? specialty = null,
        IReadOnlyList<RulesCoreMechanicRelationshipView>? relationships = null)
    {
        var definition = new RulesCoreCompetencyDefinitionView(
            "skill",
            family,
            specialty,
            "intelligence",
            true,
            true,
            true,
            true,
            true,
            null,
            []);
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
