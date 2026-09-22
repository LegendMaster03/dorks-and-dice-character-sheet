using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CompetencyProjector
{
    internal static CompetencyCollectionPresentationView ProjectCollection(
        IReadOnlyList<RulesCoreMechanicView> competencyMechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        var conceptByMechanicKey = competencyMechanics.ToDictionary(
            value => value.MechanicKey,
            value => value.ConceptKey!,
            StringComparer.Ordinal);

        var competencies = competencyMechanics
            .Select(value => ProjectCompetency(
                value,
                evaluationByKey.GetValueOrDefault(value.MechanicKey)))
            .ToArray();

        var relationships = competencyMechanics
            .SelectMany(value => value.Relationships)
            .Where(value => value.CanResolve
                && value.MissingMechanicKeys.Count == 0
                && string.Equals(value.EffectiveResolutionKind, "derive-parent", StringComparison.Ordinal))
            .Select(value => ProjectRelationship(value, conceptByMechanicKey))
            .Where(value => value is not null)
            .Cast<CompetencyRelationshipPresentationView>()
            .DistinctBy(
                value => $"{value.ParentKey}|{string.Join("|", value.ComponentKeys)}",
                StringComparer.Ordinal)
            .ToArray();

        return new CompetencyCollectionPresentationView(
            competencies,
            relationships.Length == 0 ? null : relationships);
    }

    private static CompetencyPresentationView ProjectCompetency(
        RulesCoreMechanicView mechanic,
        RulesCoreMechanicEvaluationView? evaluation)
    {
        var competency = mechanic.Competency!;
        return new CompetencyPresentationView(
            mechanic.ConceptKey!,
            mechanic.DisplayName,
            evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
            Kind: competency.CompetencyKind,
            GoverningAbility: competency.GoverningAbilityKey,
            TrainedOnly: competency.TrainedOnly,
            ArmorCheckPenalty: competency.ArmorCheckPenaltyApplies is bool applies
                ? new ArmorCheckPenaltyPresentationView(applies)
                : null,
            Family: competency.FamilyName,
            Specialty: competency.Specialty,
            SupportsRanks: competency.SupportsRanks,
            SupportsClassSkillState: competency.SupportsClassSkillState,
            SupportsTrainingState: competency.SupportsTrainingState,
            SourceAttributions: SourceAttributionMapper.Map(mechanic.SourceAttributions),
            IdentityKey: competency.IdentityKey,
            IdentityName: competency.IdentityName,
            SharedTrainingKey: competency.SharedTrainingKey,
            IsFamily: competency.IsFamily,
            Facets: ProjectFacets(competency.Facets),
            RelatedCompetencies: ProjectRelatedCompetencies(competency.RelatedCompetencies));
    }

    private static IReadOnlyList<CompetencyFacetPresentationView>? ProjectFacets(
        IReadOnlyList<RulesCoreCompetencyFacetView>? facets)
    {
        if (facets is null || facets.Count == 0)
        {
            return null;
        }

        return facets
            .Select(value => new CompetencyFacetPresentationView(
                value.FacetType,
                value.SupportsRanks,
                value.SupportsClassSkillState,
                value.SupportsTrainingState,
                value.MechanicKeys))
            .ToArray();
    }

    private static IReadOnlyList<RelatedCompetencyPresentationView>? ProjectRelatedCompetencies(
        IReadOnlyList<RulesCoreCompetencyRelationshipView>? relatedCompetencies)
    {
        if (relatedCompetencies is null || relatedCompetencies.Count == 0)
        {
            return null;
        }

        return relatedCompetencies
            .Select(value => new RelatedCompetencyPresentationView(
                value.Kind,
                value.TargetType,
                value.TargetName,
                value.Scope,
                value.SharesTrainingState))
            .ToArray();
    }

    private static CompetencyRelationshipPresentationView? ProjectRelationship(
        RulesCoreMechanicRelationshipView relationship,
        IReadOnlyDictionary<string, string> conceptByMechanicKey)
    {
        if (!conceptByMechanicKey.TryGetValue(relationship.ParentMechanicKey, out var parentKey))
        {
            return null;
        }

        var components = new List<string>();
        foreach (var mechanicKey in relationship.ComponentMechanicKeys)
        {
            if (!conceptByMechanicKey.TryGetValue(mechanicKey, out var conceptKey))
            {
                return null;
            }

            components.Add(conceptKey);
        }

        return components.Count == 0
            ? null
            : new CompetencyRelationshipPresentationView(
                parentKey,
                components,
                relationship.Composition,
                relationship.EffectiveResolutionKind);
    }
}
