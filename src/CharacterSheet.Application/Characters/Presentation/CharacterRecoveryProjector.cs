using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CharacterRecoveryProjector
{
    internal static IReadOnlyList<CharacterRecoveryProcedurePresentationView>? Project(
        RulesCoreCharacterSupportProjectionView support)
    {
        ArgumentNullException.ThrowIfNull(support);

        var procedures = support.RecoveryProcedures
            .Where(value => value.IsAvailableUnderRuleset)
            .OrderBy(value => value.DisplayName, StringComparer.Ordinal)
            .ThenBy(value => value.ProcedureKey, StringComparer.Ordinal)
            .Select(value => new CharacterRecoveryProcedurePresentationView(
                value.ProcedureKey,
                value.DisplayName,
                value.ApplicabilityState,
                value.PresentationRole,
                value.RuntimeRequirements.RequiresCharacterState,
                value.RuntimeRequirements.RequiresPlayerChoices,
                value.RuntimeRequirements.RequiresRolls,
                value.RuntimeRequirements.RequiresResourceExpenditure,
                value.RuntimeRequirements.RequiresOtherRuntimeFacts,
                value.MissingCapabilityKeys.Count == 0
                    ? null
                    : value.MissingCapabilityKeys,
                value.Inputs.Count == 0
                    ? null
                    : value.Inputs.Select(input => new CharacterRecoveryInputPresentationView(
                        input.Key,
                        input.ValueKind,
                        input.Origin,
                        input.Required,
                        input.DefaultInteger)).ToArray(),
                SourceAttributionMapper.Map(value.SourceAttributions)))
            .ToArray();

        return procedures.Length == 0 ? null : procedures;
    }
}
