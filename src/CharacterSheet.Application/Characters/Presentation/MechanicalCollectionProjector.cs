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
                    GoverningAbility: ResolveSavingThrowAbility(value.MechanicKey),
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions));
            })
            .ToArray();

    private static string? ResolveSavingThrowAbility(string mechanicKey) =>
        mechanicKey.Trim().ToLowerInvariant() switch
        {
            "save.strength" or "saving-throw.strength" => "strength",
            "save.dexterity" or "saving-throw.dexterity" => "dexterity",
            "save.constitution" or "saving-throw.constitution" => "constitution",
            "save.intelligence" or "saving-throw.intelligence" => "intelligence",
            "save.wisdom" or "saving-throw.wisdom" => "wisdom",
            "save.charisma" or "saving-throw.charisma" => "charisma",
            "save.fortitude" or "saving-throw.fortitude" => "constitution",
            "save.reflex" or "saving-throw.reflex" => "dexterity",
            "save.will" or "saving-throw.will" => "wisdom",
            _ => null
        };

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
        params string[] kinds) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && kinds.Contains(value.Kind, StringComparer.Ordinal))
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

    internal static CalculatedMechanicalValuePresentationView? ProjectInspiration(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        var mechanic = mechanics.FirstOrDefault(value =>
            value.IsAvailableUnderRuleset
            && string.Equals(value.Kind, "resource", StringComparison.Ordinal)
            && IsInspirationResource(value));
        if (mechanic is null) return null;

        evaluationByKey.TryGetValue(mechanic.MechanicKey, out var evaluation);
        return new CalculatedMechanicalValuePresentationView(
            mechanic.MechanicKey,
            mechanic.DisplayName,
            evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
            SourceAttributions: SourceAttributionMapper.Map(mechanic.SourceAttributions));
    }

    internal static HealthTrackPresentationView[] ProjectHealthTracks(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .Where(value =>
                value.IsAvailableUnderRuleset
                && string.Equals(value.Kind, "resource", StringComparison.Ordinal)
                && !IsInspirationResource(value))
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
    private static bool IsInspirationResource(RulesCoreMechanicView value)
    {
        var key = NormalizeIdentity(value.MechanicKey);
        var label = NormalizeIdentity(value.DisplayName);
        return key is "inspiration" or "resourceinspiration" or "heroicinspiration" or "resourceheroicinspiration"
            || label is "inspiration" or "heroicinspiration";
    }

    private static string NormalizeIdentity(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

}
