using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CharacterAdvancementProjector
{
    internal static CharacterAdvancementPresentationView Project(
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
                        : [SourceAttributionMapper.MapResolvedRule(resolved)]);
            })
            .ToArray();

        return new CharacterAdvancementPresentationView(occurrences);
    }
}
