using System.Text.Json.Serialization;

namespace CharacterSheet.Application.RulesCore;

public sealed class RulesCoreGatewayException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
}

public sealed record RulesCoreResolvedRuleSummaryView(
    string ConceptKey,
    string EntityType,
    string DisplayName,
    int SourceRevisionNumber,
    string SourceEntityName,
    string SourceCode,
    string PackageKey,
    string PackageDisplayName,
    string EditionKey,
    string EditionDisplayName,
    string? WorkKey = null,
    string? WorkDisplayName = null);

public sealed record RulesCoreResolvedRulesCatalogView(
    int TotalCount,
    IReadOnlyList<RulesCoreResolvedRuleSummaryView> Rules);

public sealed record RulesCoreMechanicsCatalogView(
    string Scope,
    Guid? CampaignId,
    int? RevisionNumber,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<RulesCoreMechanicView> Mechanics);

public sealed record RulesCoreMechanicView(
    string MechanicKey,
    string Kind,
    string DisplayName,
    string? ConceptKey,
    bool IsAvailableUnderRuleset,
    RulesCoreMechanicApplicabilityView Applicability,
    string EvaluationKind,
    bool CanEvaluate,
    IReadOnlyList<RulesCoreMechanicInputView> Inputs,
    IReadOnlyList<RulesCoreMechanicRelationshipView> Relationships,
    IReadOnlyList<RulesCoreMechanicBooleanRequirementView> BooleanRequirements,
    RulesCoreMechanicCheckView? Check,
    RulesCoreCompetencyDefinitionView? Competency,
    IReadOnlyList<RulesCoreMechanicContributorGroupView> ContributorGroups,
    IReadOnlyList<RulesCoreMechanicSourceAttributionView> SourceAttributions);

public sealed record RulesCoreMechanicApplicabilityView(
    string Kind,
    bool RequiresCharacterState,
    IReadOnlyList<string> RequiredCapabilityKeys,
    string? SourcePackageKey);

public sealed record RulesCoreMechanicInputView(
    string Key,
    string ValueKind,
    string Origin,
    bool Required,
    bool ParticipatesInValue,
    int? DefaultInteger,
    string? IncludeWhenBooleanInputKey,
    bool? IncludeWhenBooleanValue,
    string? ContributionRole = null);

public sealed record RulesCoreMechanicBooleanRequirementView(string InputKey, bool ExpectedValue);

public sealed record RulesCoreMechanicContributorGroupView(string Key);

public sealed record RulesCoreMechanicRelationshipView(
    string RelationshipKey,
    string Kind,
    string ParentMechanicKey,
    IReadOnlyList<string> ComponentMechanicKeys,
    string Composition,
    string Direction,
    string? EffectiveResolutionKind,
    bool CanResolve,
    IReadOnlyList<string> MissingMechanicKeys);

public sealed record RulesCoreCheckAbilityView(
    string ResolutionKind,
    string? FixedAbilityKey,
    IReadOnlyList<string> AllowedAbilityKeys);

public sealed record RulesCoreCheckCompetencyView(
    string ResolutionKind,
    IReadOnlyList<string> AllowedCompetencyKinds,
    string? FixedConceptKey);

public sealed record RulesCoreMechanicCheckView(
    RulesCoreCheckAbilityView Ability,
    RulesCoreCheckCompetencyView Competency);

public sealed record RulesCoreCompetencyRelationshipView(
    string Kind,
    string TargetType,
    string TargetName,
    string? Scope,
    bool SharesTrainingState);

public sealed record RulesCoreCompetencyFacetView(
    string FacetType,
    IReadOnlyList<Guid> ProfileSourceEntityRevisionIds,
    bool SupportsRanks,
    bool SupportsClassSkillState,
    bool SupportsTrainingState,
    IReadOnlyList<string>? MechanicKeys = null);

public sealed record RulesCoreCompetencyProfileView(
    Guid SourceEntityRevisionId,
    string ProfileKey,
    IReadOnlyList<string> RequiredCapabilityKeys,
    string CompetencyKind,
    string? FamilyName,
    string? Specialty,
    string? GoverningAbilityKey,
    bool SupportsRanks,
    bool SupportsClassSkillState,
    bool SupportsTrainingState,
    bool? TrainedOnly,
    bool? ArmorCheckPenaltyApplies,
    string EvaluationProfileKey,
    string EvaluationKind,
    bool CanEvaluate,
    IReadOnlyList<RulesCoreMechanicInputView> Inputs,
    IReadOnlyList<RulesCoreMechanicBooleanRequirementView> BooleanRequirements,
    string? GameEdition,
    IReadOnlyList<RulesCoreMechanicSourceAttributionView>? SourceAttributions = null,
    string? FacetType = null,
    bool IsFamily = false,
    string? IdentityKey = null,
    string? IdentityName = null,
    string? SharedTrainingKey = null,
    IReadOnlyList<RulesCoreCompetencyRelationshipView>? RelatedCompetencies = null);

public sealed record RulesCoreCompetencyDefinitionView(
    string CompetencyKind,
    string? FamilyName,
    string? Specialty,
    string? GoverningAbilityKey,
    bool SupportsRanks,
    bool SupportsClassSkillState,
    bool SupportsTrainingState,
    bool? TrainedOnly,
    bool? ArmorCheckPenaltyApplies,
    Guid? DefaultProfileSourceEntityRevisionId,
    IReadOnlyList<RulesCoreCompetencyProfileView> Profiles,
    string? IdentityKey = null,
    string? IdentityName = null,
    string? SharedTrainingKey = null,
    bool IsFamily = false,
    IReadOnlyList<RulesCoreCompetencyFacetView>? Facets = null,
    IReadOnlyList<RulesCoreCompetencyRelationshipView>? RelatedCompetencies = null);

public sealed record RulesCoreMechanicSourceAttributionView(
    string? PackageKey,
    string? PackageDisplayName,
    string Provider,
    string? SourceCode,
    int? SourceRevisionNumber,
    string? WorkKey,
    string? WorkDisplayName,
    string? GameEdition,
    string? ReleaseKind,
    DateOnly? PublicationDate,
    string? ReferenceKey,
    string? ReferenceTitle,
    string? ReferenceUri,
    bool PresentationRequired,
    bool ReferenceLinkRequired);

public sealed record RulesCoreMechanicEvaluationRequest(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, int>? IntegerInputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, bool>? BooleanInputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Dictionary<string, string>? StringInputs = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? CapabilityKeys = null);

public sealed record RulesCoreMechanicBatchEvaluationItemRequest(
    string MechanicKey,
    RulesCoreMechanicEvaluationRequest Evaluation);

public sealed record RulesCoreMechanicsBatchEvaluationRequest(
    IReadOnlyList<RulesCoreMechanicBatchEvaluationItemRequest> Evaluations);

public sealed record RulesCoreCompetencyEvaluationBreakdownView(
    int AbilityContribution,
    int CompetencyContribution);

public sealed record RulesCoreMechanicEvaluationView(
    string MechanicKey,
    string EvaluationKind,
    int Value,
    int? Target,
    bool? MeetsTarget,
    bool RequirementsSatisfied,
    IReadOnlyList<string> UnsatisfiedRequirementKeys,
    RulesCoreCompetencyEvaluationBreakdownView? CompetencyBreakdown = null,
    Guid? CompetencyProfileSourceEntityRevisionId = null);

public sealed record RulesCoreMechanicBatchEvaluationItemView(
    string MechanicKey,
    RulesCoreMechanicEvaluationView? Evaluation);

public sealed record RulesCoreMechanicsBatchEvaluationView(
    string Scope,
    Guid? CampaignId,
    int? RevisionNumber,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<RulesCoreMechanicBatchEvaluationItemView> Evaluations);

public interface IRulesCoreGateway
{
    Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
        IReadOnlyCollection<string> conceptKeys,
        CancellationToken cancellationToken = default);

    Task<RulesCoreMechanicsCatalogView> GetGlobalMechanicsAsync(
        CancellationToken cancellationToken = default);

    Task<RulesCoreMechanicsBatchEvaluationView> EvaluateGlobalMechanicsAsync(
        RulesCoreMechanicsBatchEvaluationRequest request,
        CancellationToken cancellationToken = default);

    Task<RulesCoreCharacterRulesProjectionView> ResolveGlobalCharacterMechanicsAsync(
        RulesCoreCharacterRulesProjectionRequest request,
        CancellationToken cancellationToken = default);

    Task<RulesCoreCharacterRulesProjectionView> ResolveCampaignCharacterMechanicsAsync(
        Guid campaignId,
        RulesCoreCharacterRulesProjectionRequest request,
        CancellationToken cancellationToken = default);
}
