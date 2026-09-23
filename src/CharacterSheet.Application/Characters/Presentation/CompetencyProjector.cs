using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CompetencyProjector
{
    internal static CompetencyCollectionPresentationView ProjectCollection(
        IReadOnlyList<RulesCoreUniversalCompetencyView>? universalCompetencies,
        IReadOnlyList<RulesCoreMechanicView> competencyMechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        if (universalCompetencies is not null)
        {
            return ProjectUniversalCollection(
                universalCompetencies,
                competencyMechanics,
                evaluationByKey);
        }

        return ProjectLegacyCollection(competencyMechanics, evaluationByKey);
    }

    internal static IReadOnlyDictionary<string, string> BuildDisplayNamesByConcept(
        IReadOnlyList<RulesCoreUniversalCompetencyView>? universalCompetencies,
        IReadOnlyList<RulesCoreMechanicView> competencyMechanics)
    {
        var universalNameByMechanic = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var universal in universalCompetencies ?? [])
        {
            foreach (var mechanicKey in universal.MechanicKeys)
            {
                universalNameByMechanic[mechanicKey] = universal.DisplayName;
            }
        }

        return competencyMechanics
            .Where(value => !string.IsNullOrWhiteSpace(value.ConceptKey))
            .GroupBy(value => value.ConceptKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var mechanic = group.First();
                    return universalNameByMechanic.GetValueOrDefault(mechanic.MechanicKey)
                        ?? mechanic.DisplayName;
                },
                StringComparer.Ordinal);
    }

    private static CompetencyCollectionPresentationView ProjectUniversalCollection(
        IReadOnlyList<RulesCoreUniversalCompetencyView> universalCompetencies,
        IReadOnlyList<RulesCoreMechanicView> competencyMechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        var mechanicByKey = competencyMechanics.ToDictionary(
            value => value.MechanicKey,
            StringComparer.Ordinal);
        var semanticByMechanicKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var universal in universalCompetencies)
        {
            foreach (var mechanicKey in universal.MechanicKeys
                         .Concat(universal.CompatibilityMechanicKeys)
                         .Distinct(StringComparer.Ordinal))
            {
                semanticByMechanicKey[mechanicKey] = universal.SemanticKey;
            }
        }

        var familyGoverningAbilityByName = universalCompetencies
            .Where(value => value.IsFamily)
            .Select(value => new
            {
                value.DisplayName,
                GoverningAbility = ResolveGoverningAbility(
                    value,
                    FindImplementations(value, mechanicByKey))
            })
            .Where(value => !string.IsNullOrWhiteSpace(value.GoverningAbility))
            .GroupBy(value => value.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().GoverningAbility!,
                StringComparer.OrdinalIgnoreCase);

        var entries = universalCompetencies
            .Select(value => ProjectUniversalCompetency(
                value,
                mechanicByKey,
                evaluationByKey,
                value.FamilyName is null
                    ? null
                    : familyGoverningAbilityByName.GetValueOrDefault(value.FamilyName)))
            .ToArray();

        var relationships = competencyMechanics
            .SelectMany(value => value.Relationships)
            .Where(value => value.CanResolve
                && value.MissingMechanicKeys.Count == 0
                && string.Equals(value.EffectiveResolutionKind, "derive-parent", StringComparison.Ordinal))
            .Select(value => ProjectUniversalRelationship(value, semanticByMechanicKey))
            .Where(value => value is not null)
            .Cast<CompetencyRelationshipPresentationView>()
            .DistinctBy(
                value => $"{value.ParentKey}|{string.Join("|", value.ComponentKeys)}",
                StringComparer.Ordinal)
            .ToArray();

        return new CompetencyCollectionPresentationView(
            entries,
            relationships.Length == 0 ? null : relationships);
    }

    private static CompetencyPresentationView ProjectUniversalCompetency(
        RulesCoreUniversalCompetencyView universal,
        IReadOnlyDictionary<string, RulesCoreMechanicView> mechanicByKey,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey,
        string? familyGoverningAbility)
    {
        var implementations = FindImplementations(universal, mechanicByKey);
        var evaluations = implementations
            .Select(value => evaluationByKey.GetValueOrDefault(value.MechanicKey))
            .Where(value => value is not null)
            .Cast<RulesCoreMechanicEvaluationView>()
            .ToArray();
        var evaluation = evaluations.Length == 1 ? evaluations[0] : null;

        var profiles = universal.Profiles;
        var governingAbility = ResolveUniversalGoverningAbility(universal.Mechanics?.GoverningAbility)
            ?? ResolveGoverningAbility(universal, implementations)
            ?? familyGoverningAbility;
        var competencyKinds = profiles
            .Select(value => value.CompetencyKind)
            .Concat(implementations
                .Where(value => value.Competency is not null)
                .Select(value => value.Competency!.CompetencyKind))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var kind = universal.IsFamily
            ? "family"
            : !string.IsNullOrWhiteSpace(universal.FamilyName)
                ? "specialized-skill"
                : competencyKinds.Any(value =>
                    string.Equals(value, "skill", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "specialized-skill", StringComparison.OrdinalIgnoreCase))
                    ? "skill"
                    : competencyKinds.Length == 1
                        ? competencyKinds[0]
                        : "competency";

        var trainedOnly = universal.Mechanics?.TrainedOnly
            ?? SingleDistinctBoolean(profiles.Select(value => value.TrainedOnly));
        var armorCheckPenalty = universal.Mechanics?.ArmorCheckPenaltyApplies
            ?? SingleDistinctBoolean(profiles.Select(value => value.ArmorCheckPenaltyApplies));
        var supportsRanks = !universal.IsFamily
            && (universal.Mechanics?.SupportsRanks
                ?? (profiles.Any(value => value.SupportsRanks)
                    || universal.Facets.Any(value => value.SupportsRanks)));
        var supportsClassSkillState = !universal.IsFamily
            && (universal.Mechanics?.SupportsClassSkillState
                ?? (profiles.Any(value => value.SupportsClassSkillState)
                    || universal.Facets.Any(value => value.SupportsClassSkillState)));
        var supportsTrainingState = !universal.IsFamily
            && (universal.Mechanics?.SupportsTrainingState
                ?? (profiles.Any(value => value.SupportsTrainingState)
                    || universal.Facets.Any(value => value.SupportsTrainingState)
                    || !string.IsNullOrWhiteSpace(universal.TrainingStateKey)));

        return new CompetencyPresentationView(
            universal.SemanticKey,
            universal.DisplayName,
            evaluation is null ? CharacterMechanicsProjector.Unconfigured : (object)evaluation.Value,
            Kind: kind,
            GoverningAbility: governingAbility,
            TrainedOnly: trainedOnly,
            ArmorCheckPenalty: armorCheckPenalty is bool applies
                ? new ArmorCheckPenaltyPresentationView(applies)
                : null,
            Family: universal.FamilyName,
            Specialty: universal.IsFamily || universal.FamilyName is null
                ? null
                : universal.DisplayName,
            SupportsRanks: supportsRanks,
            SupportsClassSkillState: supportsClassSkillState,
            SupportsTrainingState: supportsTrainingState,
            SourceAttributions: SourceAttributionMapper.Map(universal.SourceAttributions),
            IdentityKey: universal.IdentityKey,
            IdentityName: universal.DisplayName,
            SharedTrainingKey: universal.TrainingStateKey,
            IsFamily: universal.IsFamily,
            Facets: ProjectFacets(universal.Facets),
            RelatedCompetencies: ProjectRelatedCompetencies(universal.RelatedCompetencies),
            ChildCompetencyKeys: universal.ChildCompetencyKeys.Count == 0
                ? null
                : universal.ChildCompetencyKeys,
            MechanicKeys: universal.MechanicKeys.Count == 0
                ? null
                : universal.MechanicKeys,
            CompatibilityMechanicKeys: universal.CompatibilityMechanicKeys.Count == 0
                ? null
                : universal.CompatibilityMechanicKeys,
            RankInputKey: ResolveRankInputKey(universal, mechanicByKey));
    }

    private static string? ResolveUniversalGoverningAbility(
        RulesCoreUniversalGoverningAbilityView? governingAbility)
    {
        if (governingAbility is null
            || string.Equals(
                governingAbility.ResolutionKind,
                "none",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(
                governingAbility.ResolutionKind,
                "fixed",
                StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(governingAbility.FixedAbilityKey))
        {
            return NormalizeAbilityKey(governingAbility.FixedAbilityKey);
        }

        var abilities = governingAbility.AbilityKeys
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeAbilityKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AbilityOrder)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return abilities.Length == 0
            ? null
            : string.Join(" / ", abilities);
    }

    private static RulesCoreMechanicView[] FindImplementations(
        RulesCoreUniversalCompetencyView universal,
        IReadOnlyDictionary<string, RulesCoreMechanicView> mechanicByKey) =>
        universal.MechanicKeys
            .Select(key => mechanicByKey.GetValueOrDefault(key))
            .Where(value => value is not null)
            .Cast<RulesCoreMechanicView>()
            .ToArray();

    private static string? ResolveGoverningAbility(
        RulesCoreUniversalCompetencyView universal,
        IReadOnlyList<RulesCoreMechanicView> implementations)
    {
        var abilities = universal.Profiles
            .Select(value => value.GoverningAbilityKey)
            .Concat(implementations.Select(value => value.Competency?.GoverningAbilityKey))
            .Concat(implementations
                .Where(value => value.Competency is not null)
                .SelectMany(value => value.Competency!.Profiles)
                .Select(value => value.GoverningAbilityKey))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Select(NormalizeAbilityKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(AbilityOrder)
            .ThenBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return abilities.Length == 0
            ? null
            : string.Join(" / ", abilities);
    }

    private static string NormalizeAbilityKey(string ability)
    {
        var normalized = ability.Trim().ToLowerInvariant();
        return normalized switch
        {
            "str" or "strength" => "strength",
            "dex" or "dexterity" => "dexterity",
            "con" or "constitution" => "constitution",
            "int" or "intelligence" => "intelligence",
            "wis" or "wisdom" => "wisdom",
            "cha" or "charisma" => "charisma",
            _ => normalized
        };
    }

    private static int AbilityOrder(string ability) => ability switch
    {
        "strength" => 0,
        "dexterity" => 1,
        "constitution" => 2,
        "intelligence" => 3,
        "wisdom" => 4,
        "charisma" => 5,
        _ => int.MaxValue
    };

    private static string? ResolveRankInputKey(
        RulesCoreUniversalCompetencyView universal,
        IReadOnlyDictionary<string, RulesCoreMechanicView> mechanicByKey)
    {
        var rankMechanicKeys = universal.Facets
            .Where(value => value.SupportsRanks)
            .SelectMany(value => value.MechanicKeys ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (rankMechanicKeys.Length == 0)
        {
            rankMechanicKeys = universal.MechanicKeys
                .Where(key => mechanicByKey.TryGetValue(key, out var mechanic)
                    && mechanic.Competency?.SupportsRanks == true)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var conceptKeys = rankMechanicKeys
            .Select(key => mechanicByKey.GetValueOrDefault(key)?.ConceptKey)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return conceptKeys.Length == 1 ? conceptKeys[0] : null;
    }

    private static CompetencyRelationshipPresentationView? ProjectUniversalRelationship(
        RulesCoreMechanicRelationshipView relationship,
        IReadOnlyDictionary<string, string> semanticByMechanicKey)
    {
        if (!semanticByMechanicKey.TryGetValue(relationship.ParentMechanicKey, out var parentKey))
        {
            return null;
        }

        var components = new List<string>();
        foreach (var mechanicKey in relationship.ComponentMechanicKeys)
        {
            if (!semanticByMechanicKey.TryGetValue(mechanicKey, out var semanticKey))
            {
                return null;
            }

            if (!string.Equals(semanticKey, parentKey, StringComparison.Ordinal)
                && !components.Contains(semanticKey, StringComparer.Ordinal))
            {
                components.Add(semanticKey);
            }
        }

        return components.Count == 0
            ? null
            : new CompetencyRelationshipPresentationView(
                parentKey,
                components,
                relationship.Composition,
                relationship.EffectiveResolutionKind);
    }

    private static CompetencyCollectionPresentationView ProjectLegacyCollection(
        IReadOnlyList<RulesCoreMechanicView> competencyMechanics,
        IReadOnlyDictionary<string, RulesCoreMechanicEvaluationView> evaluationByKey)
    {
        var conceptByMechanicKey = competencyMechanics.ToDictionary(
            value => value.MechanicKey,
            value => value.ConceptKey!,
            StringComparer.Ordinal);

        var competencies = competencyMechanics
            .Select(value => ProjectLegacyCompetency(
                value,
                evaluationByKey.GetValueOrDefault(value.MechanicKey)))
            .ToArray();

        var relationships = competencyMechanics
            .SelectMany(value => value.Relationships)
            .Where(value => value.CanResolve
                && value.MissingMechanicKeys.Count == 0
                && string.Equals(value.EffectiveResolutionKind, "derive-parent", StringComparison.Ordinal))
            .Select(value => ProjectLegacyRelationship(value, conceptByMechanicKey))
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

    private static CompetencyPresentationView ProjectLegacyCompetency(
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
            RelatedCompetencies: ProjectRelatedCompetencies(competency.RelatedCompetencies),
            MechanicKeys: [mechanic.MechanicKey],
            RankInputKey: competency.SupportsRanks ? mechanic.ConceptKey : null);
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

    private static CompetencyRelationshipPresentationView? ProjectLegacyRelationship(
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

    private static bool? SingleDistinctBoolean(IEnumerable<bool?> values)
    {
        var distinct = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }
}
