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
    CharacterOriginStateService origins,
    CharacterOriginParentNativeReviewService nativeOriginReviews,
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
        // Routine state may be unavailable while owned advancement history is
        // still readable; keep that distinction in the presentation result.
        if (stateResult.Status == CharacterStateAccessStatus.Ready)
            state = stateResult.View;
        else
            diagnostics.Add($"character-state:{stateResult.Status}");

        // The native character kernel does not yet accept or execute a
        // verified Origin profile. Read it through Site-authorized state,
        // rather than trusting legacy Species selections or a browser draft.
        // If Origin state is unavailable, fail closed instead of claiming
        // a mechanically complete character.
        var origin = await origins.GetAsync(characterId, cancellationToken);
        if (origin.Status != CharacterOriginReadStatus.Ready)
        {
            diagnostics.Add($"character-origin:{origin.Status}");
            return new CharacterPresentationResult(
                CharacterPresentationAccessStatus.Ready,
                new CharacterPresentationView(
                    ProjectAdvancement(build, null), null,
                    MechanicsStatus: "unavailable",
                    MechanicsMessage: "Authorized Origin state is unavailable; effective character mechanics cannot be verified."),
                diagnostics);
        }

        // A committed Origin can produce a few current native intrinsics,
        // but only after the server has re-evaluated the *immutable* draft,
        // matched the whole native profile, and verified its event revision.
        // No client-submitted profile or preview is used as an input here.
        VerifiedOriginIntrinsics? verifiedIntrinsics = null;
        if (origin.SavedState is { } saved)
        {
            var reviewed = await nativeOriginReviews.ReviewAsync(
                characterId, characterId, cancellationToken);
            if (reviewed.Status == CharacterOriginParentNativeReviewStatus.Ready &&
                reviewed.Review?.StoredRevision == saved.Revision &&
                reviewed.Review.RulesetId == saved.RulesetId)
                verifiedIntrinsics = VerifiedOriginIntrinsicReader.Read(
                    reviewed.EvaluatedIntrinsics, saved.ProfileJson);
            if (verifiedIntrinsics is null)
                diagnostics.Add($"origin-native-intrinsics:{reviewed.Status}");
        }

        NativeCharacterPresentationBridge.Outcome projection;
        try
        {
            projection = await NativeCharacterPresentationBridge.ProjectAsync(
                nativeRulesCoreGateway, build, state, cancellationToken,
                hasCommittedOrigin: origin.SavedState is not null,
                originIntrinsics: verifiedIntrinsics);
        }
        catch (Exception exception) when (exception is RulesCoreGatewayException
            or JsonException or InvalidOperationException or FormatException)
        {
            diagnostics.Add($"native-rules-projection:{exception.Message}");
            projection = new NativeCharacterPresentationBridge.Outcome(
                "unavailable", null,
                "Native Rules Core is unavailable or returned an invalid response.");
        }

        // Advancement occurrences remain present even when native mechanics
        // are unavailable; only their optional resolved class names are lost.
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
