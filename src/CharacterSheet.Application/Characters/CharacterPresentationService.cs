using System.Text.Json;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Projects Character-owned development state through the current native Rules Core.
/// The retired concept-resolution and character-mechanics API is not a fallback.
/// </summary>
public sealed class CharacterPresentationService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    INativeRulesCoreGateway nativeRulesCoreGateway)
{
    public async Task<CharacterPresentationResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
            return new CharacterPresentationResult(MapStatus(buildResult.Status));

        var build = buildResult.View;
        var diagnostics = new List<string>();
        CharacterStateView? state = null;
        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status == CharacterStateAccessStatus.Ready)
            state = stateResult.View;
        else
            diagnostics.Add($"character-state:{stateResult.Status}");

        NativeCharacterPresentationBridge.Outcome projection;
        try
        {
            projection = await NativeCharacterPresentationBridge.ProjectAsync(
                nativeRulesCoreGateway, build, state, cancellationToken);
        }
        catch (Exception exception) when (exception is RulesCoreGatewayException
            or JsonException or InvalidOperationException or FormatException)
        {
            diagnostics.Add($"native-rules-projection:{exception.Message}");
            projection = new NativeCharacterPresentationBridge.Outcome(
                "unavailable", null,
                "Native Rules Core is unavailable or returned an invalid response.");
        }

        var advancement = ProjectAdvancement(build, projection.NativeClassNames);
        return new CharacterPresentationResult(
            CharacterPresentationAccessStatus.Ready,
            new CharacterPresentationView(
                advancement,
                projection.Mechanics,
                MechanicsStatus: projection.Status,
                MechanicsMessage: projection.Message),
            diagnostics);
    }

    private static CharacterAdvancementPresentationView ProjectAdvancement(
        CharacterBuildView build, IReadOnlyDictionary<string, string>? classNames)
    {
        var byId = build.ProgressionEntries.ToDictionary(entry => entry.Id);
        var occurrences = build.ProgressionEntries.Select(entry =>
        {
            int? level = entry.Kind switch
            {
                CharacterBuildAdvancementKinds.Class or CharacterBuildAdvancementKinds.PrestigeClass => entry.Level,
                CharacterBuildAdvancementKinds.Subclass
                    when entry.ParentAdvancementEntryId is Guid parentId
                        && byId.TryGetValue(parentId, out var parent) => parent.Level,
                _ => null
            };
            var label = classNames is not null &&
                classNames.TryGetValue(entry.RuleConceptKey, out var nativeName)
                ? nativeName : entry.RuleConceptKey;
            return new AdvancementOccurrencePresentationView(
                entry.Id,
                entry.RuleConceptKey,
                entry.Kind,
                label,
                Progression: level is > 0
                    ? new AdvancementProgressionPresentationView(
                        entry.Kind == CharacterBuildAdvancementKinds.Subclass ? "Parent Level" : "Level",
                        level.Value,
                        entry.Kind == CharacterBuildAdvancementKinds.Subclass
                            ? $"Parent Level {level.Value}" : $"Level {level.Value}")
                    : null,
                ParentOccurrenceId: entry.ParentAdvancementEntryId);
        }).ToArray();
        return new CharacterAdvancementPresentationView(occurrences);
    }

    private static CharacterPresentationAccessStatus MapStatus(CharacterBuildAccessStatus status) =>
        status switch
        {
            CharacterBuildAccessStatus.NotFoundOrNotOwned =>
                CharacterPresentationAccessStatus.NotFoundOrNotOwned,
            CharacterBuildAccessStatus.ProjectionUnavailable =>
                CharacterPresentationAccessStatus.ProjectionUnavailable,
            CharacterBuildAccessStatus.Unauthenticated =>
                CharacterPresentationAccessStatus.Unauthenticated,
            CharacterBuildAccessStatus.SheetNotInitialized =>
                CharacterPresentationAccessStatus.SheetNotInitialized,
            _ => CharacterPresentationAccessStatus.ProjectionUnavailable
        };
}
