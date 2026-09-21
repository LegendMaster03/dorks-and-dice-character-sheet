using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class AutomaticMechanicEvaluationPlanner
{
    internal static RulesCoreMechanicsBatchEvaluationRequest Build(
        RulesCoreMechanicsCatalogView catalog)
    {
        var evaluations = catalog.Mechanics
            .Where(mechanic =>
                mechanic.IsAvailableUnderRuleset
                && mechanic.CanEvaluate
                && mechanic.Competency is null
                && !mechanic.Applicability.RequiresCharacterState
                && mechanic.Applicability.RequiredCapabilityKeys.Count == 0
                && mechanic.BooleanRequirements.Count == 0
                && mechanic.ContributorGroups.Count == 0
                && mechanic.Inputs.Count == 0)
            .Select(mechanic => new RulesCoreMechanicBatchEvaluationItemRequest(
                mechanic.MechanicKey,
                new RulesCoreMechanicEvaluationRequest()))
            .ToArray();

        return new RulesCoreMechanicsBatchEvaluationRequest(evaluations);
    }
}
