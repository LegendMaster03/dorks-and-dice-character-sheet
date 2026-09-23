using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterRecoveryAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized,
    ArchivedReadOnly,
    UnsupportedConsequence
}

public sealed record CharacterRecoveryRequest(
    Dictionary<string, int>? IntegerInputs = null,
    Dictionary<string, bool>? BooleanInputs = null,
    Dictionary<string, string>? StringInputs = null,
    Dictionary<string, string>? Choices = null,
    Dictionary<string, int>? Rolls = null);

public sealed record CharacterRecoveryResult(
    CharacterRecoveryAccessStatus Status,
    RulesCoreCharacterRecoveryResolutionView? Resolution = null,
    CharacterStateView? State = null,
    string? Message = null);

/// <summary>
/// Resolves recovery semantics in Rules Core, then persists only explicitly supported
/// Character-owned consequences. Unknown targets and operations fail before any state is saved.
/// </summary>
public sealed class CharacterRecoveryService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    IRulesCoreGateway rulesCoreGateway)
{
    public async Task<CharacterRecoveryResult> ResolveAsync(
        Guid characterId,
        string procedureKey,
        CharacterRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(procedureKey))
        {
            throw new ArgumentException("Recovery procedure key can not be blank.", nameof(procedureKey));
        }
        ArgumentNullException.ThrowIfNull(request);

        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
        {
            return new CharacterRecoveryResult(MapBuildStatus(buildResult.Status));
        }

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status != CharacterStateAccessStatus.Ready || stateResult.View is null)
        {
            return new CharacterRecoveryResult(MapStateStatus(stateResult.Status));
        }
        if (stateResult.View.ReadOnly || buildResult.View.ReadOnly)
        {
            return new CharacterRecoveryResult(CharacterRecoveryAccessStatus.ArchivedReadOnly);
        }

        RulesCoreCharacterRecoveryResolutionView resolution;
        try
        {
            var projectionRequest = CharacterRulesProjectionRequestBuilder.Build(
                buildResult.View,
                stateResult.View);
            var projection = await rulesCoreGateway.ResolveGlobalCharacterMechanicsAsync(
                projectionRequest,
                cancellationToken);
            var capabilities = projection.Capabilities
                .Select(value => value.CapabilityKey)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            resolution = await rulesCoreGateway.ResolveGlobalCharacterRecoveryAsync(
                procedureKey.Trim(),
                new RulesCoreCharacterRecoveryResolutionRequest(
                    request.IntegerInputs,
                    request.BooleanInputs,
                    request.StringInputs,
                    capabilities,
                    request.Choices,
                    request.Rolls),
                cancellationToken);
        }
        catch (RulesCoreGatewayException exception)
        {
            return new CharacterRecoveryResult(
                CharacterRecoveryAccessStatus.ProjectionUnavailable,
                Message: exception.Message);
        }

        if (!string.Equals(resolution.Status, "resolved", StringComparison.Ordinal))
        {
            return new CharacterRecoveryResult(
                CharacterRecoveryAccessStatus.Ready,
                resolution,
                stateResult.View);
        }

        IReadOnlyList<CharacterIntegerStateMutation> mutations;
        try
        {
            mutations = TranslateConsequences(resolution.Consequences);
        }
        catch (InvalidOperationException exception)
        {
            return new CharacterRecoveryResult(
                CharacterRecoveryAccessStatus.UnsupportedConsequence,
                resolution,
                stateResult.View,
                exception.Message);
        }

        CharacterStateResult applied;
        try
        {
            applied = await stateService.ApplyIntegerStateMutationsAsync(
                characterId,
                mutations,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            return new CharacterRecoveryResult(
                CharacterRecoveryAccessStatus.UnsupportedConsequence,
                resolution,
                stateResult.View,
                exception.Message);
        }

        return applied.Status == CharacterStateAccessStatus.Ready && applied.View is not null
            ? new CharacterRecoveryResult(
                CharacterRecoveryAccessStatus.Ready,
                resolution,
                applied.View)
            : new CharacterRecoveryResult(MapStateStatus(applied.Status), resolution);
    }

    private static IReadOnlyList<CharacterIntegerStateMutation> TranslateConsequences(
        IReadOnlyList<RulesCoreCharacterRecoveryEffectView> consequences)
    {
        var mutations = new List<CharacterIntegerStateMutation>(consequences.Count);
        foreach (var effect in consequences)
        {
            if (!string.Equals(effect.TargetKind, "resource", StringComparison.Ordinal))
            {
                throw Unsupported(effect, $"target kind '{effect.TargetKind}'");
            }
            if (effect.ReferenceKey is not null)
            {
                throw Unsupported(
                    effect,
                    $"reference '{effect.ReferenceKey}' requires additional rule-defined interpretation");
            }
            if (effect.Value is not null || effect.Amount is not int amount)
            {
                throw Unsupported(effect, "a non-integer consequence value");
            }

            var operation = effect.Operation switch
            {
                "adjust" => CharacterIntegerStateMutationOperation.Adjust,
                "expend" when amount >= 0 => CharacterIntegerStateMutationOperation.Expend,
                "set" => CharacterIntegerStateMutationOperation.Set,
                _ => throw Unsupported(effect, $"operation '{effect.Operation}'")
            };

            var target = effect.TargetKey switch
            {
                "hit-points" => new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.CurrentHitPoints,
                    operation,
                    amount),
                "resource.death-save.successes" => new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.DeathSaveSuccesses,
                    operation,
                    amount),
                "resource.death-save.failures" => new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.DeathSaveFailures,
                    operation,
                    amount),
                _ => new CharacterIntegerStateMutation(
                    CharacterIntegerStateMutationTarget.Resource,
                    operation,
                    amount,
                    effect.TargetKey)
            };
            mutations.Add(target);
        }
        return mutations;
    }

    private static InvalidOperationException Unsupported(
        RulesCoreCharacterRecoveryEffectView effect,
        string reason) =>
        new(
            $"Recovery effect '{effect.EffectKey}' can not be persisted because it uses {reason}. " +
            "No Character state was changed.");

    private static CharacterRecoveryAccessStatus MapBuildStatus(CharacterBuildAccessStatus status) => status switch
    {
        CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterRecoveryAccessStatus.NotFoundOrNotOwned,
        CharacterBuildAccessStatus.ProjectionUnavailable => CharacterRecoveryAccessStatus.ProjectionUnavailable,
        CharacterBuildAccessStatus.Unauthenticated => CharacterRecoveryAccessStatus.Unauthenticated,
        CharacterBuildAccessStatus.SheetNotInitialized => CharacterRecoveryAccessStatus.SheetNotInitialized,
        CharacterBuildAccessStatus.ArchivedReadOnly => CharacterRecoveryAccessStatus.ArchivedReadOnly,
        _ => CharacterRecoveryAccessStatus.ProjectionUnavailable
    };

    private static CharacterRecoveryAccessStatus MapStateStatus(CharacterStateAccessStatus status) => status switch
    {
        CharacterStateAccessStatus.NotFoundOrNotOwned => CharacterRecoveryAccessStatus.NotFoundOrNotOwned,
        CharacterStateAccessStatus.ProjectionUnavailable => CharacterRecoveryAccessStatus.ProjectionUnavailable,
        CharacterStateAccessStatus.Unauthenticated => CharacterRecoveryAccessStatus.Unauthenticated,
        CharacterStateAccessStatus.SheetNotInitialized => CharacterRecoveryAccessStatus.SheetNotInitialized,
        CharacterStateAccessStatus.ArchivedReadOnly => CharacterRecoveryAccessStatus.ArchivedReadOnly,
        _ => CharacterRecoveryAccessStatus.ProjectionUnavailable
    };
}
