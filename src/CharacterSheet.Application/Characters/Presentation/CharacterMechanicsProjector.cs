using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CharacterMechanicsProjector
{
    internal const string Unconfigured = "-";

    internal static CharacterMechanicsPresentationView Project(
        RulesCoreMechanicsCatalogView catalog,
        RulesCoreMechanicsBatchEvaluationView? batchEvaluation)
    {
        var mechanicByKey = catalog.Mechanics.ToDictionary(
            value => value.MechanicKey,
            StringComparer.Ordinal);
        var evaluationByKey = batchEvaluation?.Evaluations
            .Where(value => value.Evaluation is not null && value.Evaluation.RequirementsSatisfied)
            .ToDictionary(value => value.MechanicKey, value => value.Evaluation!, StringComparer.Ordinal)
            ?? new Dictionary<string, RulesCoreMechanicEvaluationView>(StringComparer.Ordinal);

        var competencyMechanics = catalog.Mechanics
            .Where(value => value.IsAvailableUnderRuleset
                && value.Competency is not null
                && !string.IsNullOrWhiteSpace(value.ConceptKey))
            .ToArray();

        var competencies = CompetencyProjector.ProjectCollection(
            catalog.Competencies,
            competencyMechanics,
            evaluationByKey);

        var competencyNameByConcept = CompetencyProjector.BuildDisplayNamesByConcept(
            catalog.Competencies,
            competencyMechanics);
        var checks = CheckProcedureProjector.ProjectChecks(
            catalog.Mechanics,
            competencyNameByConcept,
            evaluationByKey);
        var checkByMechanicKey = checks.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var procedures = CheckProcedureProjector.ProjectProcedures(
            catalog.Mechanics,
            mechanicByKey,
            checkByMechanicKey,
            evaluationByKey);

        var inspiration = MechanicalCollectionProjector.ProjectInspiration(
            catalog.Mechanics,
            evaluationByKey);
        var passiveValues = MechanicalCollectionProjector.ProjectCalculatedValues(
            catalog.Mechanics,
            evaluationByKey,
            "passive-value");
        var training = MechanicalCollectionProjector.ProjectCalculatedValues(
            catalog.Mechanics,
            evaluationByKey,
            "proficiency",
            "training");
        var senses = MechanicalCollectionProjector.ProjectCalculatedValues(
            catalog.Mechanics,
            evaluationByKey,
            "sense");
        var savingThrows = MechanicalCollectionProjector.ProjectSavingThrows(
            catalog.Mechanics,
            evaluationByKey);
        var defenses = MechanicalCollectionProjector.ProjectDefenses(
            catalog.Mechanics,
            evaluationByKey);
        var resistances = MechanicalCollectionProjector.ProjectDefenseTraits(
            catalog.Mechanics,
            evaluationByKey,
            "resistance");
        var immunities = MechanicalCollectionProjector.ProjectDefenseTraits(
            catalog.Mechanics,
            evaluationByKey,
            "immunity");
        var vulnerabilities = MechanicalCollectionProjector.ProjectDefenseTraits(
            catalog.Mechanics,
            evaluationByKey,
            "vulnerability");
        var combat = MechanicalCollectionProjector.ProjectCalculatedValues(
            catalog.Mechanics,
            evaluationByKey,
            "combat-value");
        var movement = MechanicalCollectionProjector.ProjectCalculatedValues(
            catalog.Mechanics,
            evaluationByKey,
            "movement");
        var resources = MechanicalCollectionProjector.ProjectHealthTracks(
            catalog.Mechanics,
            evaluationByKey);

        return new CharacterMechanicsPresentationView(
            Inspiration: inspiration,
            PassiveValues: passiveValues.Length == 0 ? null : passiveValues,
            Training: training.Length == 0 ? null : training,
            Senses: senses.Length == 0 ? null : senses,
            SavingThrows: savingThrows.Length == 0 ? null : savingThrows,
            Defenses: defenses.Length == 0 ? null : new DefenseGroupPresentationView(defenses),
            Resistances: resistances.Length == 0 ? null : resistances,
            Immunities: immunities.Length == 0 ? null : immunities,
            Vulnerabilities: vulnerabilities.Length == 0 ? null : vulnerabilities,
            CombatFundamentals: combat.Length == 0 ? null : combat,
            HealthTracks: resources.Length == 0 ? null : resources,
            Competencies: competencies,
            Checks: checks.Length == 0 ? null : checks,
            Procedures: procedures.Length == 0 ? null : procedures,
            Movement: movement.Length == 0 ? null : movement);
    }
}
