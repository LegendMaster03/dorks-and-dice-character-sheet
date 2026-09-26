using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.UnitTests;

public sealed class RulesCoreContextualHelpContractTests
{
    [Fact]
    public void ProjectionContractCarriesHelpAndAttackResolutionWithoutReinterpretingThem()
    {
        var help = new RulesCoreCharacterContextualHelpView(
            "defense.ac.touch",
            "Touch Armor Class",
            "Touch Armor Class is used when a rule explicitly targets it.",
            "Do not infer Touch Armor Class from a magical or spell attack alone.",
            "prominent");
        var provenance = new RulesCoreCharacterMechanicProvenanceView([], [], []);
        var mechanic = new RulesCoreCharacterResolvedMechanicView(
            "defense.ac.touch",
            "defense",
            "Touch Armor Class",
            "resolved",
            13,
            null,
            null,
            [],
            [],
            [],
            [],
            [],
            provenance,
            help);
        var resolution = new RulesCoreCharacterAttackResolutionView(
            "defense.ac.touch",
            "normal",
            ["state.touch-attack"]);
        var action = new RulesCoreCharacterActionView(
            "action.ray",
            "Ray",
            "attack",
            "resolved",
            "attack.ray",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            provenance,
            AttackResolution: resolution);
        var projection = new RulesCoreCharacterRulesProjectionView(
            "global",
            null,
            1,
            null,
            [mechanic],
            [],
            [],
            [],
            [],
            [],
            [action],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            HelpTopics: [help]);

        Assert.Same(help, Assert.Single(projection.Mechanics).Help);
        Assert.Same(help, Assert.Single(projection.HelpTopics!));
        var projectedResolution = Assert.Single(projection.Actions).AttackResolution;
        Assert.NotNull(projectedResolution);
        Assert.Equal("defense.ac.touch", projectedResolution!.TargetDefenseKey);
        Assert.Equal("normal", projectedResolution.RollMode);
        Assert.Equal(["state.touch-attack"], projectedResolution.TargetStateKeys);
    }
}
