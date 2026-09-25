namespace CharacterSheet.Application.RulesCore;

public sealed record RulesCoreCraftingManualCompetencyInput(
    string DisplayName,
    int Contribution,
    bool IsQualified);

public sealed record RulesCoreCraftingCompetencyInput(
    string? CompetencyKey = null,
    RulesCoreCraftingManualCompetencyInput? Manual = null);

public sealed record RulesCoreManufacturingResolutionRequest(
    RulesCoreCharacterRulesProjectionRequest Character,
    RulesCoreCraftingCompetencyInput Competency,
    bool HasQualifiedGuidance = false,
    int? D20Roll = null,
    int OtherModifier = 0,
    int? TargetDc = null);

public sealed record RulesCoreEnchantingResolutionRequest(
    RulesCoreCharacterRulesProjectionRequest Character,
    string? CreatureType = null,
    RulesCoreCraftingCompetencyInput? Competency = null,
    string? SpellcastingKey = null,
    int? D20Roll = null,
    int OtherModifier = 0,
    int? TargetDc = null);

public sealed record RulesCoreCraftingCheckResolutionView(
    string ProcedureKey,
    string DisplayName,
    string CompetencyKey,
    string CompetencyDisplayName,
    bool ManualCompetency,
    bool IsQualified,
    string RollMode,
    int CompetencyContribution,
    int AbilityContribution,
    int OtherModifier,
    int? D20Roll,
    int? Total,
    int? TargetDc,
    bool? MeetsTarget,
    string Outcome = "pending",
    int? Margin = null,
    int? FlawCount = null,
    bool InputsConsumed = false,
    bool ProducesFunctionalOutput = false);
