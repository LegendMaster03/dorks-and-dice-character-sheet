namespace CharacterSheet.Application.RulesCore;

public sealed record RulesCoreCharacterSelectedConceptInput(
    string ConceptKey,
    string? OccurrenceKey = null);

public sealed record RulesCoreCharacterAdvancementFactInput(
    string ConceptKey,
    int Level,
    string? OccurrenceKey = null,
    string? ParentConceptKey = null);

public sealed record RulesCoreCharacterRuntimeChoiceInput(
    string ChoiceKey,
    string Value);

public sealed record RulesCoreCharacterRuntimeRollInput(
    string RollKey,
    int Value);

public sealed record RulesCoreCharacterHitPointGainInput(
    string ConceptKey,
    int ClassLevel,
    int HitDieValue,
    string? OccurrenceKey = null);

public sealed record RulesCoreCharacterChoiceOptionView(
    string Value,
    string DisplayName,
    string? ConceptKey);

public sealed record RulesCoreCharacterChoiceView(
    string ChoiceKey,
    string GroupKey,
    string DisplayName,
    string Kind,
    string State,
    IReadOnlyList<RulesCoreCharacterChoiceOptionView> Options,
    string? SelectedValue,
    string? SourceConceptKey,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterRulesProjectionRequest(
    Dictionary<string, int>? BaseAbilityScores = null,
    IReadOnlyList<RulesCoreCharacterSelectedConceptInput>? SelectedConcepts = null,
    IReadOnlyList<RulesCoreCharacterAdvancementFactInput>? Advancements = null,
    IReadOnlyList<string>? CapabilityKeys = null,
    Dictionary<string, int>? CompetencyRanks = null,
    IReadOnlyList<string>? TrainingKeys = null,
    IReadOnlyList<string>? ClassSkillKeys = null,
    IReadOnlyList<string>? EquippedItemConceptKeys = null,
    IReadOnlyList<string>? KnownSpellConceptKeys = null,
    IReadOnlyList<string>? PreparedSpellConceptKeys = null,
    Dictionary<string, int>? CurrentResources = null,
    IReadOnlyList<string>? ConditionKeys = null,
    Dictionary<string, int>? IntegerFacts = null,
    Dictionary<string, bool>? BooleanFacts = null,
    Dictionary<string, string>? StringFacts = null,
    IReadOnlyList<RulesCoreCharacterRuntimeChoiceInput>? Choices = null,
    IReadOnlyList<RulesCoreCharacterRuntimeRollInput>? Rolls = null,
    IReadOnlyList<string>? RequestedMechanicKeys = null,
    IReadOnlyList<RulesCoreCharacterHitPointGainInput>? HitPointGains = null,
    IReadOnlyList<string>? ItemConceptKeys = null);

public sealed record RulesCoreCharacterMechanicProvenanceView(
    IReadOnlyList<RulesCoreMechanicSourceAttributionView> CanonicalConcept,
    IReadOnlyList<RulesCoreMechanicSourceAttributionView> MechanicalProfile,
    IReadOnlyList<RulesCoreMechanicSourceAttributionView> EffectiveRule);

public sealed record RulesCoreCharacterMechanicContributionView(
    string ContributionKey,
    string Label,
    string Operation,
    int? NumericValue,
    string? TextValue,
    string? SourceConceptKey,
    RulesCoreCharacterMechanicProvenanceView? Provenance = null,
    string? StateKind = null,
    string? ConditionKey = null);

public sealed record RulesCoreCharacterResolvedMechanicView(
    string MechanicKey,
    string Kind,
    string DisplayName,
    string State,
    int? NumericValue,
    string? TextValue,
    string? Unit,
    IReadOnlyList<string> MissingCharacterInputs,
    IReadOnlyList<string> MissingCapabilities,
    IReadOnlyList<string> RequiredChoices,
    IReadOnlyList<string> RequiredRolls,
    IReadOnlyList<RulesCoreCharacterMechanicContributionView> Contributions,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterRuleEffectView(
    string EffectKey,
    string Kind,
    string Operation,
    string TargetKey,
    int? NumericValue,
    string? TextValue,
    string? ConditionKey,
    string? SourceConceptKey,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterCapabilityView(
    string CapabilityKey,
    string DisplayName,
    IReadOnlyList<string> GrantedByConceptKeys,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterGrantView(
    string GrantKey,
    string Kind,
    string TargetKey,
    string DisplayName,
    string? SourceConceptKey,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterMovementModeView(
    string MovementKey,
    string DisplayName,
    string State,
    int? Value,
    string? Unit,
    IReadOnlyList<RulesCoreCharacterMechanicContributionView> Contributions,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterQualificationView(
    string QualificationKey,
    string Category,
    string DisplayName,
    bool? IsQualified,
    string State,
    IReadOnlyList<string> GrantedByConceptKeys,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterActionView(
    string ActionKey,
    string DisplayName,
    string? ActionType,
    string State,
    string? AttackMechanicKey,
    string? DamageExpression,
    string? DamageType,
    string? Range,
    string? Reach,
    string? Target,
    string? ResourceKey,
    int? ResourceCost,
    IReadOnlyList<string> RequiredCapabilityKeys,
    RulesCoreCharacterMechanicProvenanceView Provenance,
    string? SourceConceptKey = null,
    int? SpellLevel = null,
    string? SpellSchool = null,
    string? CastingTime = null,
    IReadOnlyList<string>? SpellComponents = null,
    string? MaterialComponent = null,
    string? Duration = null,
    bool? Ritual = null,
    bool? Concentration = null);

public sealed record RulesCoreCharacterFeatureView(
    string FeatureKey,
    string DisplayName,
    string Kind,
    string State,
    string? SourceConceptKey,
    IReadOnlyList<RulesCoreCharacterRuleEffectView> Effects,
    RulesCoreCharacterMechanicProvenanceView Provenance,
    string? OccurrenceKey = null,
    string? GrantingSourceKind = null,
    int? AcquisitionLevel = null,
    string? FeatureConceptKey = null,
    string? FeatureEntityType = null,
    RulesCoreCharacterMechanicProvenanceView? FeatureProvenance = null);

public sealed record RulesCoreCharacterEquipmentDefinitionView(
    string ItemKey,
    string ConceptKey,
    string DisplayName,
    string State,
    string? ItemType,
    string? EquipmentCategory,
    string? ArmorRole,
    decimal? Weight,
    string? WeightUnit,
    string? AmmunitionType,
    string? Capacity,
    bool? RequiresAttunement,
    string? AttunementRequirement,
    IReadOnlyList<string> PropertyKeys,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterResourceView(
    string ResourceKey,
    string DisplayName,
    string State,
    int? CurrentValue,
    int? MaximumValue,
    string? RecoveryProcedureKey,
    IReadOnlyList<RulesCoreCharacterMechanicContributionView> MaximumContributions,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterSpellcastingView(
    string SpellcastingKey,
    string DisplayName,
    string State,
    string? CastingAbilityKey,
    string? ResourceSystemKey,
    string? SaveDcMechanicKey,
    string? SpellAttackMechanicKey,
    IReadOnlyList<string> SpellListConceptKeys,
    IReadOnlyList<string> RequiredChoices,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterProcedureView(
    string ProcedureKey,
    string DisplayName,
    string State,
    string? PresentationRole,
    IReadOnlyList<string> RequiredCapabilityKeys,
    IReadOnlyList<string> RequiredCharacterInputs,
    IReadOnlyList<string> RequiredChoices,
    IReadOnlyList<string> RequiredRolls,
    IReadOnlyList<RulesCoreCharacterRuleEffectView> Effects,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterPrerequisiteRequirementView(
    string RequirementKey,
    string Kind,
    string? TargetKey,
    string? Operator,
    int? NumericValue,
    string? TextValue,
    bool? Satisfied,
    string State,
    string? Reason,
    string? GroupKey = null,
    int GroupMatchCount = 1);

public sealed record RulesCoreCharacterPrerequisiteView(
    string ConceptKey,
    string State,
    bool? Satisfied,
    IReadOnlyList<RulesCoreCharacterPrerequisiteRequirementView> Requirements,
    RulesCoreCharacterMechanicProvenanceView Provenance);

public sealed record RulesCoreCharacterProjectionConflictView(
    string ConflictKey,
    string Kind,
    string Message,
    IReadOnlyList<string> RelatedMechanicKeys,
    IReadOnlyList<string> RelatedConceptKeys);

public sealed record RulesCoreCharacterRuleResolutionView(
    string ConceptKey,
    string State,
    bool RequiresAdjudication,
    Guid SourceEntityRevisionId,
    int SourceRevisionNumber);

public sealed record RulesCoreCharacterRulesProjectionView(
    string Scope,
    Guid? CampaignId,
    int? RevisionNumber,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<RulesCoreCharacterResolvedMechanicView> Mechanics,
    IReadOnlyList<RulesCoreCharacterCapabilityView> Capabilities,
    IReadOnlyList<RulesCoreCharacterGrantView> Grants,
    IReadOnlyList<RulesCoreCharacterRuleEffectView> Effects,
    IReadOnlyList<RulesCoreCharacterMovementModeView> Movement,
    IReadOnlyList<RulesCoreCharacterQualificationView> Qualifications,
    IReadOnlyList<RulesCoreCharacterActionView> Actions,
    IReadOnlyList<RulesCoreCharacterFeatureView> Features,
    IReadOnlyList<RulesCoreCharacterResourceView> Resources,
    IReadOnlyList<RulesCoreCharacterSpellcastingView> Spellcasting,
    IReadOnlyList<RulesCoreCharacterProcedureView> Procedures,
    IReadOnlyList<RulesCoreCharacterChoiceView> Choices,
    IReadOnlyList<RulesCoreCharacterPrerequisiteView> Prerequisites,
    IReadOnlyList<RulesCoreCharacterProjectionConflictView> Conflicts,
    IReadOnlyList<RulesCoreCharacterEquipmentDefinitionView> Equipment,
    IReadOnlyList<RulesCoreUniversalCompetencyView>? Competencies = null,
    IReadOnlyList<RulesCoreCharacterRuleResolutionView>? RuleResolutions = null);
