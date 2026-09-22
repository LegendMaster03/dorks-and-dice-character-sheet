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

        var advancementById = build.ProgressionEntries.ToDictionary(value => value.Id);
        var advancements = build.ProgressionEntries
            .Select(value =>
            {
                int? level = value.Kind switch
                {
                    CharacterBuildAdvancementKinds.Class
                        or CharacterBuildAdvancementKinds.PrestigeClass => value.Level,
                    CharacterBuildAdvancementKinds.Subclass
                        when value.ParentAdvancementEntryId is Guid parentId
                            && advancementById.TryGetValue(parentId, out var parent)
                            && parent.Kind == CharacterBuildAdvancementKinds.Class => parent.Level,
                    _ => null
                };

                if (level is not > 0)
                {
                    return null;
                }

                string? parentConceptKey = null;
                if (value.ParentAdvancementEntryId is Guid linkedParentId
                    && advancementById.TryGetValue(linkedParentId, out var linkedParent))
                {
                    parentConceptKey = linkedParent.RuleConceptKey;
                }

                return new RulesCoreCharacterAdvancementFactInput(
                    value.RuleConceptKey,
                    level.Value,
                    value.Id.ToString("D"),
                    parentConceptKey);
            })
            .Where(value => value is not null)
            .Cast<RulesCoreCharacterAdvancementFactInput>()
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
            Advancements: advancements.Length == 0 ? null : advancements,
            CurrentResources: currentResources,
            ConditionKeys: conditionKeys,
            EquippedItemConceptKeys: state?.InventoryItemOccurrences
                .Where(value => value.IsEquipped)
                .Select(value => value.RuleConceptKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ItemConceptKeys: itemConceptKeys);
    }
}
