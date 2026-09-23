using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class CharacterAdvancementProjector
{
    internal static CharacterAdvancementPresentationView Project(
        CharacterBuildView build,
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> resolvedRules)
    {
        var byId = build.ProgressionEntries.ToDictionary(value => value.Id);
        var occurrences = build.ProgressionEntries
            .Select(entry =>
            {
                resolvedRules.TryGetValue(entry.RuleConceptKey, out var resolved);
                var effectiveLevel = entry.Kind switch
                {
                    CharacterBuildAdvancementKinds.Class
                        or CharacterBuildAdvancementKinds.PrestigeClass => entry.Level,
                    CharacterBuildAdvancementKinds.Subclass
                        when entry.ParentAdvancementEntryId is Guid parentId
                            && byId.TryGetValue(parentId, out var parent)
                            && parent.Kind == CharacterBuildAdvancementKinds.Class => parent.Level,
                    _ => null
                };
                var progression = effectiveLevel is > 0
                    ? new AdvancementProgressionPresentationView(
                        entry.Kind == CharacterBuildAdvancementKinds.Subclass
                            ? "Parent Level"
                            : "Level",
                        effectiveLevel.Value,
                        entry.Kind == CharacterBuildAdvancementKinds.Subclass
                            ? $"Parent Level {effectiveLevel.Value}"
                            : $"Level {effectiveLevel.Value}")
                    : null;

                return new AdvancementOccurrencePresentationView(
                    entry.Id,
                    entry.RuleConceptKey,
                    entry.Kind,
                    resolved?.DisplayName ?? "Unavailable rule reference",
                    Progression: progression,
                    ParentOccurrenceId: entry.ParentAdvancementEntryId,
                    SourceAttributions: resolved is null
                        ? null
                        : [SourceAttributionMapper.MapResolvedRule(resolved)]);
            })
            .ToArray();

        return new CharacterAdvancementPresentationView(occurrences);
    }
}
