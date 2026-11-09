using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Builds Rules Core projection inputs from character-owned selections and mutable state. It sends
/// decisions and recorded facts rather than performing rule calculations.
/// </summary>
public static class CharacterRulesProjectionRequestBuilder
{
    private const string DeathSaveSuccessesResourceKey = "resource.death-save.successes";
    private const string DeathSaveFailuresResourceKey = "resource.death-save.failures";
    private const string StartingClassFactKey = "advancement.starting-class";
    public const string RulesCoreRuntimeRollInputPrefix = "rules-core-roll:";

    public static RulesCoreCharacterRulesProjectionRequest Build(
        CharacterBuildView build,
        CharacterStateView? state)
    {
        ArgumentNullException.ThrowIfNull(build);

        var baseAbilityScores = build.BaseAbilityScoreInputs
            .GroupBy(value => value.AbilityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(value => value.UpdatedAt).First().Score, StringComparer.Ordinal);

        // Keep the original occurrence IDs so Rules Core can distinguish multiple
        // instances of the same concept without treating the concept key as identity.
        var selectedConcepts = build.FoundationalSelections
            .Select(value => new RulesCoreCharacterSelectedConceptInput(value.RuleConceptKey, value.Id.ToString("D")))
            .Concat(build.ProgressionEntries.Select(value => new RulesCoreCharacterSelectedConceptInput(value.RuleConceptKey, value.Id.ToString("D"))))
            .ToArray();

        var advancementById = build.ProgressionEntries.ToDictionary(value => value.Id);
        // Subclass facts inherit their parent Class level. Entries without an
        // established positive level are not sent as invented advancement facts.
        var advancements = build.ProgressionEntries
            .Select(value =>
            {
                int? level = value.Kind switch
                {
                    CharacterBuildAdvancementKinds.Class or CharacterBuildAdvancementKinds.PrestigeClass => value.Level,
                    CharacterBuildAdvancementKinds.Subclass when value.ParentAdvancementEntryId is Guid parentId
                        && advancementById.TryGetValue(parentId, out var parent)
                        && parent.Kind == CharacterBuildAdvancementKinds.Class => parent.Level,
                    _ => null
                };
                if (level is not > 0) return null;
                string? parentConceptKey = null;
                string? parentOccurrenceKey = null;
                if (value.ParentAdvancementEntryId is Guid linkedParentId && advancementById.TryGetValue(linkedParentId, out var linkedParent))
                {
                    parentConceptKey = linkedParent.RuleConceptKey;
                    parentOccurrenceKey = linkedParent.Id.ToString("D");
                }
                return new RulesCoreCharacterAdvancementFactInput(value.RuleConceptKey, level.Value, value.Id.ToString("D"), parentConceptKey, parentOccurrenceKey);
            })
            .Where(value => value is not null)
            .Cast<RulesCoreCharacterAdvancementFactInput>()
            .ToArray();

        var ruleInputs = state?.RulesInputs ?? [];
        var choices = ruleInputs
            .Where(value =>
                value.Kind == CharacterRulesInputKinds.Choice
                && value.TextValue is not null)
            .Select(value => new RulesCoreCharacterRuntimeChoiceInput(value.Key, value.TextValue!))
            .ToArray();
        // Roll values use a dedicated storage-key prefix so they do not leak
        // into unrelated integer facts. The newest value wins for duplicate keys.
        var rolls = ruleInputs
            .Where(value =>
                value.Kind == CharacterRulesInputKinds.IntegerFact
                && value.IntegerValue is not null
                && value.Key.StartsWith(RulesCoreRuntimeRollInputPrefix, StringComparison.Ordinal)
                && value.Key.Length > RulesCoreRuntimeRollInputPrefix.Length)
            .Select(value => new
            {
                RollKey = value.Key[RulesCoreRuntimeRollInputPrefix.Length..],
                Value = value.IntegerValue!.Value,
                value.UpdatedAt
            })
            .GroupBy(value => value.RollKey, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(value => value.UpdatedAt).First())
            .Select(value => new RulesCoreCharacterRuntimeRollInput(value.RollKey, value.Value))
            .ToArray();
        var competencyRanks = ToIntegerDictionary(ruleInputs, CharacterRulesInputKinds.CompetencyRank);
        var trainingKeys = ToFlagKeys(ruleInputs, CharacterRulesInputKinds.Training);
        var classSkillKeys = ToFlagKeys(ruleInputs, CharacterRulesInputKinds.ClassSkill);
        var knownSpellConceptKeys = ToFlagKeys(ruleInputs, CharacterRulesInputKinds.KnownSpell);
        var integerFacts = ruleInputs
            .Where(value =>
                value.Kind == CharacterRulesInputKinds.IntegerFact
                && value.IntegerValue is not null
                && !value.Key.StartsWith(RulesCoreRuntimeRollInputPrefix, StringComparison.Ordinal))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().IntegerValue!.Value, StringComparer.Ordinal);
        var booleanFacts = ruleInputs
            .Where(value => value.Kind == CharacterRulesInputKinds.BooleanFact && value.BooleanValue is not null)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().BooleanValue!.Value, StringComparer.Ordinal);
        var stringFacts = ruleInputs
            .Where(value => value.Kind == CharacterRulesInputKinds.StringFact && value.TextValue is not null)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().TextValue!, StringComparer.Ordinal);

        // Starting Class is an explicit runtime fact: it is not safely inferred
        // from an arbitrary ordering of the selected Classes.
        var startingClass = build.ProgressionEntries.SingleOrDefault(value =>
            value.Kind == CharacterBuildAdvancementKinds.Class && value.Ordinal == 0 && value.ParentAdvancementEntryId is null);
        if (startingClass is not null) stringFacts[StartingClassFactKey] = startingClass.RuleConceptKey;

        var currentResources = ToIntegerDictionary(ruleInputs, CharacterRulesInputKinds.Resource);
        IReadOnlyList<string>? conditionKeys = null;
        IReadOnlyList<string>? itemConceptKeys = null;
        IReadOnlyList<string>? equippedItemConceptKeys = null;
        if (state is not null)
        {
            currentResources[DeathSaveSuccessesResourceKey] = state.DeathSaves.Successes;
            currentResources[DeathSaveFailuresResourceKey] = state.DeathSaves.Failures;
            conditionKeys = state.Conditions.Where(value => !string.IsNullOrWhiteSpace(value.RuleConceptKey)).Select(value => value.RuleConceptKey!).Distinct(StringComparer.Ordinal).ToArray();
            itemConceptKeys = state.InventoryItemOccurrences.Where(value => !string.IsNullOrWhiteSpace(value.RuleConceptKey)).Select(value => value.RuleConceptKey!).Distinct(StringComparer.Ordinal).ToArray();
            var equipped = state.InventoryItemOccurrences.Where(value => value.IsEquipped && !string.IsNullOrWhiteSpace(value.RuleConceptKey)).Select(value => value.RuleConceptKey!).Distinct(StringComparer.Ordinal).ToArray();
            equippedItemConceptKeys = equipped.Length == 0 ? null : equipped;
        }

        // Ignore orphaned hit-point gain records when no matching advancement
        // occurrence exists; Rules Core only receives attributable gains.
        var hitPointGains = (state?.HitPointGains ?? [])
            .Select(value =>
            {
                if (!advancementById.TryGetValue(value.AdvancementOccurrenceId, out var advancement)) return null;
                return new RulesCoreCharacterHitPointGainInput(advancement.RuleConceptKey, value.ClassLevel, value.HitDieValue, value.AdvancementOccurrenceId.ToString("D"));
            })
            .Where(value => value is not null)
            .Cast<RulesCoreCharacterHitPointGainInput>()
            .ToArray();

        return new RulesCoreCharacterRulesProjectionRequest(
            BaseAbilityScores: baseAbilityScores.Count == 0 ? null : baseAbilityScores,
            SelectedConcepts: selectedConcepts.Length == 0 ? null : selectedConcepts,
            Advancements: advancements.Length == 0 ? null : advancements,
            CompetencyRanks: NullIfEmpty(competencyRanks),
            TrainingKeys: trainingKeys.Length == 0 ? null : trainingKeys,
            ClassSkillKeys: classSkillKeys.Length == 0 ? null : classSkillKeys,
            KnownSpellConceptKeys: knownSpellConceptKeys.Length == 0 ? null : knownSpellConceptKeys,
            Choices: choices.Length == 0 ? null : choices,
            Rolls: rolls.Length == 0 ? null : rolls,
            IntegerFacts: NullIfEmpty(integerFacts),
            BooleanFacts: NullIfEmpty(booleanFacts),
            StringFacts: NullIfEmpty(stringFacts),
            CurrentResources: NullIfEmpty(currentResources),
            ConditionKeys: conditionKeys,
            EquippedItemConceptKeys: equippedItemConceptKeys,
            ItemConceptKeys: itemConceptKeys,
            HitPointGains: hitPointGains.Length == 0 ? null : hitPointGains);
    }

    private static Dictionary<string, int> ToIntegerDictionary(IReadOnlyList<CharacterRulesInputStateView> inputs, string kind) =>
        inputs.Where(value => value.Kind == kind && value.IntegerValue is not null)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().IntegerValue!.Value, StringComparer.Ordinal);

    private static string[] ToFlagKeys(IReadOnlyList<CharacterRulesInputStateView> inputs, string kind) =>
        inputs.Where(value => value.Kind == kind).Select(value => value.Key).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();

    private static Dictionary<string, TValue>? NullIfEmpty<TValue>(Dictionary<string, TValue> values) => values.Count == 0 ? null : values;
}
