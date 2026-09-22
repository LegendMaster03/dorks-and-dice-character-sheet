using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Coordinates Character-owned build state with Rules Core presentation projections.
/// Projection details live in focused projectors under Characters/Presentation.
/// </summary>
public sealed class CharacterPresentationService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    IRulesCoreGateway rulesCoreGateway)
{
    public async Task<CharacterPresentationResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
        {
            return new CharacterPresentationResult(MapStatus(buildResult.Status));
        }

        var build = buildResult.View;
        var diagnostics = new List<string>();

        CharacterStateView? state = null;
        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status == CharacterStateAccessStatus.Ready)
        {
            state = stateResult.View;
        }
        else
        {
            diagnostics.Add($"character-state:{stateResult.Status}");
        }

        var advancement = await ProjectAdvancementAsync(build, diagnostics, cancellationToken);
        var mechanics = await ProjectMechanicsAsync(diagnostics, cancellationToken);
        var ruleProjection = await ProjectCharacterRulesAsync(
            build,
            state,
            diagnostics,
            cancellationToken);

        return new CharacterPresentationResult(
            CharacterPresentationAccessStatus.Ready,
            new CharacterPresentationView(advancement, mechanics, ruleProjection),
            diagnostics);
    }

    private async Task<CharacterAdvancementPresentationView> ProjectAdvancementAsync(
        CharacterBuildView build,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> resolvedRules =
            new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);

        try
        {
            var references = build.ProgressionEntries
                .Select(value => value.RuleConceptKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            resolvedRules = await rulesCoreGateway.ResolveGlobalRulesAsync(references, cancellationToken);
        }
        catch (RulesCoreGatewayException exception)
        {
            diagnostics.Add($"advancement:{exception.Message}");
        }

        return CharacterPresentationProjector.ProjectAdvancement(build, resolvedRules);
    }

    private async Task<CharacterMechanicsPresentationView?> ProjectMechanicsAsync(
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var catalog = await rulesCoreGateway.GetGlobalMechanicsAsync(cancellationToken);
            var request = CharacterPresentationProjector.BuildSafeAutomaticEvaluations(catalog);
            RulesCoreMechanicsBatchEvaluationView? evaluation = null;
            if (request.Evaluations.Count > 0)
            {
                evaluation = await rulesCoreGateway.EvaluateGlobalMechanicsAsync(
                    request,
                    cancellationToken);
            }

            return CharacterPresentationProjector.ProjectMechanics(catalog, evaluation);
        }
        catch (RulesCoreGatewayException exception)
        {
            diagnostics.Add($"mechanics:{exception.Message}");
            return null;
        }
    }

    private async Task<RulesCoreCharacterRulesProjectionView?> ProjectCharacterRulesAsync(
        CharacterBuildView build,
        CharacterStateView? state,
        ICollection<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = CharacterRulesProjectionRequestBuilder.Build(build, state);

            // Character-to-Campaign membership is not an active rules-scope selector. Until the
            // host supplies an explicit Campaign context, preserve the existing global projection
            // contract rather than silently choosing one Campaign's effective rules.
            return await rulesCoreGateway.ResolveGlobalCharacterMechanicsAsync(
                request,
                cancellationToken);
        }
        catch (RulesCoreGatewayException exception)
        {
            diagnostics.Add($"rules-projection:{exception.Message}");
            return null;
        }
    }

    private static CharacterPresentationAccessStatus MapStatus(CharacterBuildAccessStatus status) => status switch
    {
        CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterPresentationAccessStatus.NotFoundOrNotOwned,
        CharacterBuildAccessStatus.ProjectionUnavailable => CharacterPresentationAccessStatus.ProjectionUnavailable,
        CharacterBuildAccessStatus.Unauthenticated => CharacterPresentationAccessStatus.Unauthenticated,
        CharacterBuildAccessStatus.SheetNotInitialized => CharacterPresentationAccessStatus.SheetNotInitialized,
        _ => CharacterPresentationAccessStatus.ProjectionUnavailable
    };
}
