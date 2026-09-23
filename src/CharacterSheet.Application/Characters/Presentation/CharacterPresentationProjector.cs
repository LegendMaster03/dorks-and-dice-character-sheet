using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Stable public projection facade retained for callers and tests. Detailed projection
/// responsibilities are delegated to focused modules in this directory.
/// </summary>
public static class CharacterPresentationProjector
{
    public static CharacterAdvancementPresentationView ProjectAdvancement(
        CharacterBuildView build,
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> resolvedRules) =>
        CharacterAdvancementProjector.Project(build, resolvedRules);

    public static RulesCoreMechanicsBatchEvaluationRequest BuildSafeAutomaticEvaluations(
        RulesCoreMechanicsCatalogView catalog) =>
        AutomaticMechanicEvaluationPlanner.Build(catalog);

    public static CharacterMechanicsPresentationView ProjectMechanics(
        RulesCoreMechanicsCatalogView catalog,
        RulesCoreMechanicsBatchEvaluationView? batchEvaluation) =>
        CharacterMechanicsProjector.Project(catalog, batchEvaluation);

    public static CharacterMechanicsPresentationView ProjectCharacterRules(
        CharacterMechanicsPresentationView? fallback,
        RulesCoreCharacterRulesProjectionView projection,
        CharacterStateView? state = null) =>
        RulesCoreCharacterProjectionProjector.Apply(fallback, projection, state);
}
