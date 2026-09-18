using System.Text.Json.Serialization;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

public enum CharacterPresentationAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized
}

public sealed record CharacterPresentationResult(
    CharacterPresentationAccessStatus Status,
    CharacterPresentationView? View = null,
    IReadOnlyList<string>? IntegrationDiagnostics = null);

public sealed record CharacterPresentationView(
    CharacterAdvancementPresentationView Advancement,
    CharacterMechanicsPresentationView? Mechanics);

public sealed record CharacterAdvancementPresentationView(
    IReadOnlyList<AdvancementOccurrencePresentationView> Occurrences);

public sealed record AdvancementOccurrencePresentationView(
    Guid OccurrenceId,
    string ConceptKey,
    string Kind,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? KindLabel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? ParentOccurrenceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record SourceAttributionPresentationView(
    string Key,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OfficialUrl = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LinkLabel = null);

public sealed record CalculatedMechanicalValuePresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Unit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<MechanicalContributionPresentationView>? Breakdown = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RelatedMechanicalValuePresentationView>? RelatedValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record MechanicalContributionPresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null);

public sealed record RelatedMechanicalValuePresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null);

public sealed record SavingThrowPresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GoverningAbility = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Training = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record DefensePresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Role = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record DefenseGroupPresentationView(
    IReadOnlyList<DefensePresentationView> Values,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PrimaryKey = null);

public sealed record HealthTrackPresentationView(
    string Key,
    string Label,
    string Role,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Current = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Maximum = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record ArmorCheckPenaltyPresentationView(bool Applies);

public sealed record CompetencyPresentationView(
    string Key,
    string Label,
    object EffectiveValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FormattedValue = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Kind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    object? Ranks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GoverningAbility = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Training = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? ClassSkill = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? TrainedOnly = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ArmorCheckPenaltyPresentationView? ArmorCheckPenalty = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Specialty = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record CompetencyRelationshipPresentationView(
    string ParentKey,
    IReadOnlyList<string> ComponentKeys);

public sealed record CompetencyCollectionPresentationView(
    IReadOnlyList<CompetencyPresentationView> Entries,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CompetencyRelationshipPresentationView>? Relationships = null);

public sealed record DisplayFieldPresentationView(string Key, string Label, string Value);

public sealed record CharacterCheckPresentationView(
    string Key,
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? Ability = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? CompetencyOrTool = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CalculatedMechanicalValuePresentationView? EffectiveModifierOrResult = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? Target = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record CharacterProcedurePresentationView(
    string Key,
    string Name,
    IReadOnlyList<CharacterCheckPresentationView> Components,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? Result = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? State = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record CharacterMechanicsPresentationView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? AbilityValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SavingThrowPresentationView>? SavingThrows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DefenseGroupPresentationView? Defenses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? CombatFundamentals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<HealthTrackPresentationView>? HealthTracks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CompetencyCollectionPresentationView? Competencies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCheckPresentationView>? Checks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterProcedurePresentationView>? Procedures = null);

public sealed class CharacterPresentationService(
    CharacterBuildService buildService,
    IRulesCoreGateway rulesCoreGateway)
{
    public async Task<CharacterPresentationResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
        {
            return new CharacterPresentationResult(MapStatus(buildResult.Status));
        }

        var build = buildResult.View;
        var diagnostics = new List<string>();
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> resolvedRules =
            new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);

        try
        {
            var references = build.ProgressionEntries
                .Select(value => new RulesCoreRuleReference(value.RuleConceptKey, value.Kind))
                .Distinct()
                .ToArray();
            resolvedRules = await rulesCoreGateway.ResolveGlobalRulesAsync(references, cancellationToken);
        }
        catch (RulesCoreGatewayException exception)
        {
            diagnostics.Add($"advancement:{exception.Message}");
        }

        var advancement = CharacterPresentationProjector.ProjectAdvancement(build, resolvedRules);
        CharacterMechanicsPresentationView? mechanics = null;
        try
        {
            var catalog = await rulesCoreGateway.GetGlobalMechanicsAsync(cancellationToken);
            var batchRequest = CharacterPresentationProjector.BuildSafeAutomaticEvaluations(catalog);
            RulesCoreMechanicsBatchEvaluationView? evaluation = null;
            if (batchRequest.Evaluations.Count > 0)
            {
                evaluation = await rulesCoreGateway.EvaluateGlobalMechanicsAsync(
                    batchRequest,
                    cancellationToken);
            }

            mechanics = CharacterPresentationProjector.ProjectMechanics(catalog, evaluation);
        }
        catch (RulesCoreGatewayException exception)
        {
            diagnostics.Add($"mechanics:{exception.Message}");
        }

        return new CharacterPresentationResult(
            CharacterPresentationAccessStatus.Ready,
            new CharacterPresentationView(advancement, mechanics),
            diagnostics);
    }

    private static CharacterPresentationAccessStatus MapStatus(CharacterBuildAccessStatus status) => status switch
    {
        CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterPresentationAccessStatus.NotFoundOrNotOwned,
        CharacterBuildAccessStatus.ProjectionUnavailable => CharacterPresentationAccessStatus.ProjectionUnavailable,
        CharacterBuildAccessStatus.Unauthenticated => CharacterPresentationAccessStatus.Unauthenticated,
        CharacterBuildAccessStatus.SheetNotInitialized => CharacterPresentationAccessStatus.SheetNotInitialized,
        _ => CharacterPresentationAccessStatus.ProjectionUnavailable
    };
}

public static class CharacterPresentationProjector
{
    private const string Unconfigured = "Not configured";

    public static CharacterAdvancementPresentationView ProjectAdvancement(
        CharacterBuildView build,
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> resolvedRules)
    {
        var occurrences = build.ProgressionEntries
            .Select(entry =>
            {
                resolvedRules.TryGetValue(entry.RuleConceptKey, out var resolved);
                return new AdvancementOccurrencePresentationView(
                    entry.Id,
                    entry.RuleConceptKey,
                    entry.Kind,
                    resolved?.DisplayName ?? "Unavailable rule reference",
                    ParentOccurrenceId: entry.ParentAdvancementEntryId,
                    SourceAttributions: resolved is null
                        ? null
                        : [MapResolvedRuleAttribution(resolved)]);
            })
            .ToArray();
        return new CharacterAdvancementPresentationView(occurrences);
    }

    public static RulesCoreMechanicsBatchEvaluationRequest BuildSafeAutomaticEvaluations(
        RulesCoreMechanicsCatalogView catalog)
    {
        var evaluations = new List<RulesCoreMechanicBatchEvaluationItemRequest>();
        foreach (var mechanic in catalog.Mechanics)
        {
            if (!mechanic.IsAvailableUnderRuleset
                || !mechanic.CanEvaluate
                || mechanic.Competency is not null
                || mechanic.Applicability.RequiredCapabilityKeys.Count > 0
                || mechanic.BooleanRequirements.Count > 0
                || mechanic.ContributorGroups.Count > 0)
            {
                continue;
            }

            var integers = new Dictionary<string, int>(StringComparer.Ordinal);
            var canEvaluate = true;
            foreach (var input in mechanic.Inputs)
            {
                if (input.DefaultInteger is int defaultInteger)
                {
                    integers[input.Key] = defaultInteger;
                    continue;
                }

                if (input.Required)
                {
                    canEvaluate = false;
                    break;
                }
            }

            if (!canEvaluate)
            {
                continue;
            }

            evaluations.Add(new RulesCoreMechanicBatchEvaluationItemRequest(
                mechanic.MechanicKey,
                new RulesCoreMechanicEvaluationRequest(
                    IntegerInputs: integers.Count == 0 ? null : integers)));
        }

        return new RulesCoreMechanicsBatchEvaluationRequest(evaluations);
    }

    public static CharacterMechanicsPresentationView ProjectMechanics(
        RulesCoreMechanicsCatalogView catalog,
        RulesCoreMechanicsBatchEvaluationView? batchEvaluation)
    {
        var mechanicByKey = catalog.Mechanics.ToDictionary(value => value.MechanicKey, StringComparer.Ordinal);
        var evaluationByKey = batchEvaluation?.Evaluations
            .Where(value => value.Evaluation is not null && value.Evaluation.RequirementsSatisfied)
            .ToDictionary(value => value.MechanicKey, value => value.Evaluation!, StringComparer.Ordinal)
            ?? new Dictionary<string, RulesCoreMechanicEvaluationView>(StringComparer.Ordinal);

        var competencyMechanics = catalog.Mechanics
            .Where(value => value.IsAvailableUnderRuleset
                && value.Competency is not null
                && !string.IsNullOrWhiteSpace(value.ConceptKey))
            .ToArray();
        var conceptByMechanicKey = competencyMechanics.ToDictionary(
            value => value.MechanicKey,
            value => value.ConceptKey!,
            StringComparer.Ordinal);
        var competencyNameByConcept = competencyMechanics.ToDictionary(
            value => value.ConceptKey!,
            value => value.DisplayName,
            StringComparer.Ordinal);

        var competencies = competencyMechanics
            .Select(value => ProjectCompetency(value, evaluationByKey.GetValueOrDefault(value.MechanicKey)))
            .ToArray();

        var relationships = competencyMechanics
            .SelectMany(value => value.Relationships)
            .Where(value => value.CanResolve
                && value.MissingMechanicKeys.Count == 0
                && string.Equals(value.EffectiveResolutionKind, "derive-parent", StringComparison.Ordinal))
            .Select(value => ProjectRelationship(value, conceptByMechanicKey))
            .Where(value => value is not null)
            .Cast<CompetencyRelationshipPresentationView>()
            .DistinctBy(value => $"{value.ParentKey}|{string.Join("|", value.ComponentKeys)}", StringComparer.Ordinal)
            .ToArray();

        var checks = catalog.Mechanics
            .Where(value => value.IsAvailableUnderRuleset && value.Check is not null)
            .Select(value => ProjectCheck(
                value,
                competencyNameByConcept,
                evaluationByKey.GetValueOrDefault(value.MechanicKey)))
            .ToArray();
        var checkByMechanicKey = checks.ToDictionary(value => value.Key, StringComparer.Ordinal);

        var procedures = catalog.Mechanics
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

        var evaluated = catalog.Mechanics
            .Where(value => evaluationByKey.ContainsKey(value.MechanicKey))
            .ToArray();

        var savingThrows = evaluated
            .Where(value => string.Equals(value.Kind, "saving-throw", StringComparison.Ordinal))
            .Select(value => new SavingThrowPresentationView(
                value.MechanicKey,
                value.DisplayName,
                evaluationByKey[value.MechanicKey].Value,
                SourceAttributions: MapAttributions(value.SourceAttributions)))
            .ToArray();

        var defenses = evaluated
            .Where(value => string.Equals(value.Kind, "defense", StringComparison.Ordinal))
            .Select(value => new DefensePresentationView(
                value.MechanicKey,
                value.DisplayName,
                evaluationByKey[value.MechanicKey].Value,
                SourceAttributions: MapAttributions(value.SourceAttributions)))
            .ToArray();

        var combat = evaluated
            .Where(value => string.Equals(value.Kind, "combat-value", StringComparison.Ordinal))
            .Select(value => new CalculatedMechanicalValuePresentationView(
                value.MechanicKey,
                value.DisplayName,
                evaluationByKey[value.MechanicKey].Value,
                SourceAttributions: MapAttributions(value.SourceAttributions)))
            .ToArray();

        var resources = evaluated
            .Where(value => string.Equals(value.Kind, "resource", StringComparison.Ordinal))
            .Select(value => new HealthTrackPresentationView(
                value.MechanicKey,
                value.DisplayName,
                string.Equals(value.MechanicKey, "resource.nonlethal-damage", StringComparison.Ordinal)
                    ? "nonlethal-damage"
                    : "resource",
                Current: evaluationByKey[value.MechanicKey].Value,
                SourceAttributions: MapAttributions(value.SourceAttributions)))
            .ToArray();

        return new CharacterMechanicsPresentationView(
            SavingThrows: savingThrows.Length == 0 ? null : savingThrows,
            Defenses: defenses.Length == 0 ? null : new DefenseGroupPresentationView(defenses),
            CombatFundamentals: combat.Length == 0 ? null : combat,
            HealthTracks: resources.Length == 0 ? null : resources,
            Competencies: new CompetencyCollectionPresentationView(
                competencies,
                relationships.Length == 0 ? null : relationships),
            Checks: checks.Length == 0 ? null : checks,
            Procedures: procedures.Length == 0 ? null : procedures);
    }

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

        var result = evaluationByKey.TryGetValue(parent.MechanicKey, out var evaluation)
            ? new DisplayFieldPresentationView(
                $"{parent.MechanicKey}:result",
                "Result",
                evaluation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            : null;

        return new CharacterProcedurePresentationView(
            parent.MechanicKey,
            parent.DisplayName,
            components,
            Result: result,
            SourceAttributions: MapAttributions(parent.SourceAttributions));
    }

    private static CompetencyPresentationView ProjectCompetency(
        RulesCoreMechanicView mechanic,
        RulesCoreMechanicEvaluationView? evaluation)
    {
        var competency = mechanic.Competency!;
        return new CompetencyPresentationView(
            mechanic.ConceptKey!,
            mechanic.DisplayName,
            evaluation?.Value ?? Unconfigured,
            Kind: competency.CompetencyKind,
            GoverningAbility: competency.GoverningAbilityKey,
            TrainedOnly: competency.TrainedOnly,
            ArmorCheckPenalty: competency.ArmorCheckPenaltyApplies is bool applies
                ? new ArmorCheckPenaltyPresentationView(applies)
                : null,
            Specialty: FormatSpecialty(competency),
            SourceAttributions: MapAttributions(mechanic.SourceAttributions));
    }

    private static string? FormatSpecialty(RulesCoreCompetencyDefinitionView competency)
    {
        if (!string.IsNullOrWhiteSpace(competency.FamilyName)
            && !string.IsNullOrWhiteSpace(competency.Specialty))
        {
            return $"{competency.FamilyName} ({competency.Specialty})";
        }

        return competency.Specialty ?? competency.FamilyName;
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
            : new CompetencyRelationshipPresentationView(parentKey, components);
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
            EffectiveModifierOrResult: evaluation is null
                ? null
                : new CalculatedMechanicalValuePresentationView(
                    $"{mechanic.MechanicKey}:result",
                    "Result",
                    evaluation.Value),
            SourceAttributions: MapAttributions(mechanic.SourceAttributions));
    }

    private static string DescribeAbility(RulesCoreCheckAbilityView ability) =>
        ability.ResolutionKind switch
        {
            "fixed" => ability.FixedAbilityKey ?? "Unavailable",
            "caller-selected" => "Caller selected",
            "rule-resolved" => "Rule resolved",
            "character-resolved" => "Character resolved",
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
            "caller-selected" => "Caller selected",
            "rule-resolved" => "Rule resolved",
            "character-resolved" => "Character resolved",
            _ => competency.ResolutionKind
        };
    }

    private static SourceAttributionPresentationView MapResolvedRuleAttribution(
        RulesCoreResolvedRuleSummaryView source)
    {
        var detail = string.Join(
            " · ",
            new[]
            {
                source.EditionDisplayName,
                source.SourceEntityName,
                $"{source.SourceCode} revision {source.SourceRevisionNumber}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new SourceAttributionPresentationView(
            $"source:{source.PackageKey}:{source.SourceCode}:{source.SourceRevisionNumber}",
            source.PackageDisplayName,
            detail);
    }

    private static IReadOnlyList<SourceAttributionPresentationView>? MapAttributions(
        IReadOnlyList<RulesCoreMechanicSourceAttributionView> attributions)
    {
        var mapped = attributions
            .Select(MapAttribution)
            .DistinctBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
        return mapped.Length == 0 ? null : mapped;
    }

    private static SourceAttributionPresentationView MapAttribution(
        RulesCoreMechanicSourceAttributionView source)
    {
        var identity = source.WorkKey
            ?? source.PackageKey
            ?? source.ReferenceKey
            ?? $"{source.Provider}:{source.SourceCode ?? "source"}";
        var detail = string.Join(
            " · ",
            new[]
            {
                source.Provider,
                source.GameEdition,
                source.PublicationDate?.ToString("yyyy-MM-dd"),
                source.SourceCode is null
                    ? null
                    : source.SourceRevisionNumber is int revision
                        ? $"{source.SourceCode} revision {revision}"
                        : source.SourceCode
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return new SourceAttributionPresentationView(
            $"source:{identity}:{source.SourceRevisionNumber?.ToString() ?? "current"}",
            source.WorkDisplayName ?? source.PackageDisplayName ?? source.ReferenceTitle ?? source.Provider,
            detail,
            source.ReferenceUri,
            source.ReferenceLinkRequired ? "Official source" : "Reference");
    }
}
