using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CheckProcedureProjector
{
    internal static CharacterCheckPresentationView[] ProjectChecks(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, string> competencyNameByConcept,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .Where(value => value.IsAvailableUnderRuleset && value.Check is not null)
            .Select(value => ProjectCheck(
                value,
                competencyNameByConcept,
                evaluationByKey.GetValueOrDefault(value.MechanicKey)))
            .ToArray();

    internal static CharacterProcedurePresentationView[] ProjectProcedures(
        IReadOnlyList<RulesCoreMechanicView> mechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicView> mechanicByKey,
        IReadOnlyDictionary<string, CharacterCheckPresentationView> checkByMechanicKey,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey) =>
        mechanics
            .SelectMany(value => value.Relationships)
            .Where(value => string.Equals(value.Kind, "composite-check", StringComparison.Ordinal)
                && value.CanResolve
                && value.MissingMechanicKeys.Count == 0)
            .DistinctBy(value => value.RelationshipKey, StringComparer.Ordinal)
            .Select(value => ProjectProcedure(
                value,
                mechanicByKey,
                checkByMechanicKey,
                evaluationByKey))
            .Where(value => value is not null)
            .Cast<CharacterProcedurePresentationView>()
            .ToArray();

    private static CharacterProcedurePresentationView? ProjectProcedure(
        RulesCoreMechanicRelationshipView relationship,
        IReadOnlyDictionary<string, RulesCoreMechanicView> mechanicByKey,
        IReadOnlyDictionary<string, CharacterCheckPresentationView> checkByMechanicKey,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        if (!mechanicByKey.TryGetValue(relationship.ParentMechanicKey, out var parent)
            || !parent.IsAvailableUnderRuleset)
        {
            return null;
        }

        var components = new List<CharacterCheckPresentationView>();
        foreach (var componentKey in relationship.ComponentMechanicKeys)
        {
            if (!checkByMechanicKey.TryGetValue(componentKey, out var component))
            {
                return null;
            }

            components.Add(component);
        }

        if (components.Count == 0)
        {
            return null;
        }

        var result = new DisplayFieldPresentationView(
            $"{parent.MechanicKey}:result",
            "Result",
            evaluationByKey.TryGetValue(parent.MechanicKey, out var evaluation)
                ? evaluation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : CharacterMechanicsProjector.Unconfigured);

        return new CharacterProcedurePresentationView(
            parent.MechanicKey,
            parent.DisplayName,
            components,
            Result: result,
            SourceAttributions: SourceAttributionMapper.Map(parent.SourceAttributions),
            Supplemental: IsSupplementalMechanic(parent));
    }

    private static CharacterCheckPresentationView ProjectCheck(
        RulesCoreMechanicView mechanic,
        IReadOnlyDictionary<string, string> competencyNameByConcept,
        RulesCoreMechanicEvaluationView? evaluation)
    {
        var check = mechanic.Check!;
        return new CharacterCheckPresentationView(
            mechanic.MechanicKey,
            mechanic.DisplayName,
            Ability: new DisplayFieldPresentationView(
                $"{mechanic.MechanicKey}:ability",
                "Ability",
                DescribeAbility(check.Ability)),
            CompetencyOrTool: new DisplayFieldPresentationView(
                $"{mechanic.MechanicKey}:competency",
                "Competency",
                DescribeCompetency(check.Competency, competencyNameByConcept)),
            EffectiveModifierOrResult: new CalculatedMechanicalValuePresentationView(
                $"{mechanic.MechanicKey}:result",
                "Result",
                evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value),
            SourceAttributions: SourceAttributionMapper.Map(mechanic.SourceAttributions),
            Supplemental: IsSupplementalMechanic(mechanic));
    }

    private static bool IsSupplementalMechanic(RulesCoreMechanicView mechanic) =>
        string.Equals(
            mechanic.Applicability.Kind,
            "external-public-rules",
            StringComparison.Ordinal);

    private static string DescribeAbility(RulesCoreCheckAbilityView ability) =>
        ability.ResolutionKind switch
        {
            "fixed" => ability.FixedAbilityKey ?? "Unavailable",
            "caller-selected" => "Choose when used",
            "rule-resolved" => "Set by the rule",
            "character-resolved" => "From character",
            _ => ability.ResolutionKind
        };

    private static string DescribeCompetency(
        RulesCoreCheckCompetencyView competency,
        IReadOnlyDictionary<string, string> competencyNameByConcept)
    {
        if (string.Equals(competency.ResolutionKind, "fixed", StringComparison.Ordinal)
            && competency.FixedConceptKey is { } conceptKey)
        {
            return competencyNameByConcept.GetValueOrDefault(conceptKey) ?? conceptKey;
        }

        return competency.ResolutionKind switch
        {
            "caller-selected" => "Choose when used",
            "rule-resolved" => "Set by the rule",
            "character-resolved" => "From character",
            _ => competency.ResolutionKind
        };
    }
}
