using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class MechanicalCollectionProjector
{
    internal static SavingThrowPresentationView[] ProjectSavingThrows(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && string.Equals(value.Kind, "saving-throw", StringComparison.Ordinal))
            .Select(value =>
            {
                evaluationByKey.TryGetValue(value.MechanicKey, out var evaluation);
                return new SavingThrowPresentationView(
                    value.MechanicKey,
                    value.DisplayName,
                    evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions));
            })
            .ToArray();

    internal static DefensePresentationView[] ProjectDefenses(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && string.Equals(value.Kind, "defense", StringComparison.Ordinal))
            .Select(value =>
            {
                evaluationByKey.TryGetValue(value.MechanicKey, out var evaluation);
                return new DefensePresentationView(
                    value.MechanicKey,
                    value.DisplayName,
                    evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions));
            })
            .ToArray();

    internal static CalculatedMechanicalValuePresentationView[] ProjectCalculatedValues(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey,
        string kind) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && string.Equals(value.Kind, kind, StringComparison.Ordinal))
            .Select(value =>
            {
                evaluationByKey.TryGetValue(value.MechanicKey, out var evaluation);
                return new CalculatedMechanicalValuePresentationView(
                    value.MechanicKey,
                    value.DisplayName,
                    evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions));
            })
            .ToArray();

    internal static HealthTrackPresentationView[] ProjectHealthTracks(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && string.Equals(value.Kind, "resource", StringComparison.Ordinal))
            .Select(value =>
            {
                evaluationByKey.TryGetValue(value.MechanicKey, out var evaluation);
                return new HealthTrackPresentationView(
                    value.MechanicKey,
                    value.DisplayName,
                    string.Equals(value.MechanicKey, "resource.nonlethal-damage", StringComparison.Ordinal)
                        ? "nonlethal-damage"
                        : "resource",
                    Current: evaluation is null
                        ? CharacterMechanicsProjector.Unconfigured
                        : (object)evaluation.Value,
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions));
            })
            .ToArray();
}
