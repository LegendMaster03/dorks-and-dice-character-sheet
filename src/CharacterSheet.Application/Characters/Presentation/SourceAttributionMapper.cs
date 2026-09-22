using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class SourceAttributionMapper
{
    internal static SourceAttributionPresentationView MapResolvedRule(
        RulesCoreResolvedRuleSummaryView source)
    {
        var detail = string.Join(
            " · ",
            new[]
            {
                source.EditionDisplayName,
                source.SourceEntityName,
                $"{source.SourceCode} revision {source.SourceRevisionNumber}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new SourceAttributionPresentationView(
            $"source:{source.WorkKey ?? source.PackageKey}:{source.SourceCode}:{source.SourceRevisionNumber}",
            source.WorkDisplayName ?? source.PackageDisplayName,
            detail);
    }

    internal static IReadOnlyList<SourceAttributionPresentationView>? Map(
        IReadOnlyList<RulesCoreMechanicSourceAttributionView> attributions)
    {
        var mapped = attributions
            .Select(MapOne)
            .DistinctBy(value => value.Key, StringComparer.Ordinal)
            .ToArray();
        return mapped.Length == 0 ? null : mapped;
    }

    internal static IReadOnlyList<SourceAttributionPresentationView>? Map(
        RulesCoreCharacterMechanicProvenanceView? provenance)
    {
        if (provenance is null)
        {
            return null;
        }

        return Map(
            provenance.EffectiveRule
                .Concat(provenance.MechanicalProfile)
                .Concat(provenance.CanonicalConcept)
                .ToArray());
    }

    private static SourceAttributionPresentationView MapOne(
        RulesCoreMechanicSourceAttributionView source)
    {
        var identity = source.WorkKey
            ?? source.PackageKey
            ?? source.ReferenceKey
            ?? $"{source.Provider}:{source.SourceCode ?? "source"}";
        var detail = string.Join(
            " · ",
            new[]
            {
                string.IsNullOrWhiteSpace(source.Provider)
                    ? null
                    : source.PresentationRequired
                        ? $"Rules by {source.Provider}"
                        : source.Provider,
                source.GameEdition,
                source.PublicationDate?.ToString("yyyy-MM-dd"),
                source.SourceCode is null
                    ? null
                    : source.SourceRevisionNumber is int revision
                        ? $"{source.SourceCode} revision {revision}"
                        : source.SourceCode
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new SourceAttributionPresentationView(
            $"source:{identity}:{source.SourceRevisionNumber?.ToString() ?? "current"}",
            source.WorkDisplayName
                ?? source.PackageDisplayName
                ?? source.ReferenceTitle
                ?? source.Provider,
            detail,
            source.ReferenceUri,
            source.ReferenceLinkRequired ? "Official rules" : "Reference",
            source.PresentationRequired);
    }
}
