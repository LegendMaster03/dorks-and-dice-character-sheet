using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

public static class CharacterRulesProjectionRequestBuilder
{
    private const string DeathSaveSuccessesResourceKey = "resource.death-save.successes";
    private const string DeathSaveFailuresResourceKey = "resource.death-save.failures";

    public static RulesCoreCharacterRulesProjectionRequest Build(
        CharacterBuildView build,
        CharacterStateView? state)
    {
        ArgumentNullException.ThrowIfNull(build);

        var baseAbilityScores = build.BaseAbilityScoreInputs
            .GroupBy(value => value.AbilityKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(value => value.UpdatedAt).First().Score,
                StringComparer.Ordinal);

        var selectedConcepts = build.FoundationalSelections
            .Select(value => new RulesCoreCharacterSelectedConceptInput(
                value.RuleConceptKey,
                value.Id.ToString("D")))
            .Concat(build.ProgressionEntries.Select(value =>
                new RulesCoreCharacterSelectedConceptInput(
                    value.RuleConceptKey,
                    value.Id.ToString("D"))))
            .ToArray();

        Dictionary<string, int>? currentResources = null;
        IReadOnlyList<string>? conditionKeys = null;
        IReadOnlyList<string>? itemConceptKeys = null;
        if (state is not null)
        {
            currentResources = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [DeathSaveSuccessesResourceKey] = state.DeathSaves.Successes,
                [DeathSaveFailuresResourceKey] = state.DeathSaves.Failures
            };

            conditionKeys = state.Conditions
                .Where(value => !string.IsNullOrWhiteSpace(value.RuleConceptKey))
                .Select(value => value.RuleConceptKey!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            itemConceptKeys = state.InventoryItemOccurrences
                .Select(value => value.RuleConceptKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        return new RulesCoreCharacterRulesProjectionRequest(
            BaseAbilityScores: baseAbilityScores.Count == 0 ? null : baseAbilityScores,
            SelectedConcepts: selectedConcepts.Length == 0 ? null : selectedConcepts,
            CurrentResources: currentResources,
            ConditionKeys: conditionKeys,
            ItemConceptKeys: itemConceptKeys);
    }
}
