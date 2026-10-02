namespace CharacterSheet.Application.RulesCore;

public sealed record RulesCoreCharacterAdvancementEligibilityRequest(
    string CandidateConceptKey,
    RulesCoreCharacterRulesProjectionRequest Character,
    string? ParentAdvancementOccurrenceKey = null);

public sealed record RulesCoreCharacterAdvancementParentRequirementView(
    string ConceptKey,
    string DisplayName,
    int? RequiredLevel,
    int? CurrentLevel,
    bool? Satisfied,
    string State,
    string? Reason = null);

public sealed record RulesCoreCharacterAdvancementEligibilityView(
    string Scope,
    Guid? CampaignId,
    string CandidateConceptKey,
    string CandidateDisplayName,
    string CandidateKind,
    string State,
    bool? Eligible,
    RulesCoreCharacterAdvancementParentRequirementView? ParentClass,
    RulesCoreCharacterPrerequisiteView? Prerequisites,
    IReadOnlyList<RulesCoreCharacterProjectionConflictView> Conflicts);

public interface IRulesCoreAdvancementGateway : IRulesCoreGateway
{
    Task<RulesCoreCharacterAdvancementEligibilityView> ResolveGlobalCharacterAdvancementEligibilityAsync(
        RulesCoreCharacterAdvancementEligibilityRequest request,
        CancellationToken cancellationToken = default);

    Task<RulesCoreCharacterAdvancementEligibilityView> ResolveCampaignCharacterAdvancementEligibilityAsync(
        Guid campaignId,
        RulesCoreCharacterAdvancementEligibilityRequest request,
        CancellationToken cancellationToken = default);
}
