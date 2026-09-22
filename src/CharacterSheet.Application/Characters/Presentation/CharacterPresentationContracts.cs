using System.Text.Json.Serialization;

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
    string? LinkLabel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool PresentationRequired = false);

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
    IReadOnlyList<MechanicalContributionPresentationView>? Breakdown = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RelatedMechanicalValuePresentationView>? RelatedValues = null,
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
    IReadOnlyList<MechanicalContributionPresentationView>? Breakdown = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<RelatedMechanicalValuePresentationView>? RelatedValues = null,
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
    string? Family = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Specialty = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? SupportsRanks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? SupportsClassSkillState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    bool? SupportsTrainingState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null);

public sealed record CompetencyRelationshipPresentationView(
    string ParentKey,
    IReadOnlyList<string> ComponentKeys,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Composition = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ResolutionKind = null);

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
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Supplemental = false);

public sealed record CharacterProcedurePresentationView(
    string Key,
    string Name,
    IReadOnlyList<CharacterCheckPresentationView> Components,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? Result = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DisplayFieldPresentationView? State = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceAttributionPresentationView>? SourceAttributions = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Supplemental = false);

public sealed record CharacterMechanicsPresentationView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? AbilityValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CalculatedMechanicalValuePresentationView? Inspiration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? PassiveValues = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Training = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Senses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SavingThrowPresentationView>? SavingThrows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DefenseGroupPresentationView? Defenses = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Resistances = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Immunities = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Vulnerabilities = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? CombatFundamentals = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<HealthTrackPresentationView>? HealthTracks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CompetencyCollectionPresentationView? Competencies = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterCheckPresentationView>? Checks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CharacterProcedurePresentationView>? Procedures = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<CalculatedMechanicalValuePresentationView>? Movement = null);
