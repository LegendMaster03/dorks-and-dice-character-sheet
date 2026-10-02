using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterAdvancementAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized,
    ArchivedReadOnly,
    EntryNotFound,
    InvalidRequest
}

public sealed record CharacterClassAdvancementRequest(
    Guid? ClassAdvancementEntryId = null,
    string? ClassConceptKey = null,
    int? HitDieValue = null,
    string? SubclassConceptKey = null);

public sealed record CharacterAdvancementChangeView(
    string Kind,
    string Label,
    string? Detail = null);

public sealed record CharacterAdvancementPlanView(
    string Operation,
    Guid ClassAdvancementEntryId,
    string ClassConceptKey,
    string ClassDisplayName,
    bool StartingClass,
    int CurrentClassLevel,
    int TargetClassLevel,
    int CurrentCharacterLevel,
    int TargetCharacterLevel,
    bool HitPointGainRequired,
    int? HitDieValue,
    string Status,
    bool CanApply,
    IReadOnlyList<RulesCoreCharacterPrerequisiteView> Prerequisites,
    IReadOnlyList<RulesCoreCharacterChoiceView> RequiredChoices,
    IReadOnlyList<RulesCoreCharacterProjectionConflictView> BlockingConflicts,
    IReadOnlyList<CharacterAdvancementChangeView> Changes,
    string? SubclassConceptKey = null,
    string? SubclassDisplayName = null,
    RulesCoreCharacterAdvancementEligibilityView? SubclassEligibility = null);

public sealed record CharacterAdvancementPreviewResult(
    CharacterAdvancementAccessStatus Status,
    CharacterAdvancementPlanView? Plan = null,
    string? Message = null);

public sealed record CharacterAdvancementApplyResult(
    CharacterAdvancementAccessStatus Status,
    CharacterAdvancementPlanView? Plan = null,
    CharacterBuildView? Build = null,
    string? Message = null);

/// <summary>
/// Coordinates one player-facing Class advancement step. Rules Core owns prerequisites, choices,
/// conflicts, features, hit-die semantics, and resulting mechanics; Character Sheet owns the
/// Character occurrence mutation and persists the accepted step atomically.
/// </summary>
public sealed class CharacterAdvancementService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    ICharacterBuildStore buildStore,
    IRulesCoreGateway rulesCore,
    TimeProvider timeProvider)
{
    public async Task<CharacterAdvancementPreviewResult> PreviewAsync(
        Guid characterId,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        var denied = MapBuildAccess(buildResult.Status);
        if (denied is not null)
        {
            return new CharacterAdvancementPreviewResult(denied.Value);
        }
        if (buildResult.View is null)
        {
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.SheetNotInitialized);
        }

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        var stateDenied = MapStateAccess(stateResult.Status);
        if (stateDenied is not null)
        {
            return new CharacterAdvancementPreviewResult(stateDenied.Value);
        }

        try
        {
            var plan = await BuildPlanAsync(
                buildResult.View,
                stateResult.View,
                request,
                cancellationToken);
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.Ready,
                plan);
        }
        catch (RulesCoreGatewayException exception)
        {
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.ProjectionUnavailable,
                Message: exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.InvalidRequest,
                Message: exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.InvalidRequest,
                Message: exception.Message);
        }
        catch (OverflowException exception)
        {
            return new CharacterAdvancementPreviewResult(
                CharacterAdvancementAccessStatus.InvalidRequest,
                Message: exception.Message);
        }
    }

    public async Task<CharacterAdvancementApplyResult> ApplyAsync(
        Guid characterId,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(characterId, request, cancellationToken);
        if (preview.Status != CharacterAdvancementAccessStatus.Ready || preview.Plan is null)
        {
            return new CharacterAdvancementApplyResult(
                preview.Status,
                preview.Plan,
                Message: preview.Message);
        }
        if (!preview.Plan.CanApply)
        {
            return new CharacterAdvancementApplyResult(
                CharacterAdvancementAccessStatus.InvalidRequest,
                preview.Plan,
                Message: "Advancement still has unresolved requirements or blocking conflicts.");
        }

        var root = await buildStore.ApplyClassAdvancementAsync(
            characterId,
            request.ClassAdvancementEntryId,
            preview.Plan.ClassConceptKey,
            preview.Plan.HitPointGainRequired ? request.HitDieValue : null,
            preview.Plan.SubclassConceptKey,
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (root is null)
        {
            return new CharacterAdvancementApplyResult(
                CharacterAdvancementAccessStatus.SheetNotInitialized,
                preview.Plan);
        }

        var refreshed = await buildService.GetAsync(characterId, cancellationToken);
        if (refreshed.Status != CharacterBuildAccessStatus.Ready || refreshed.View is null)
        {
            return new CharacterAdvancementApplyResult(
                MapBuildAccess(refreshed.Status) ?? CharacterAdvancementAccessStatus.ProjectionUnavailable,
                preview.Plan);
        }

        var appliedPlan = preview.Plan with
        {
            Status = "applied",
            CanApply = false
        };
        return new CharacterAdvancementApplyResult(
            CharacterAdvancementAccessStatus.Ready,
            appliedPlan,
            refreshed.View);
    }

    private async Task<CharacterAdvancementPlanView> BuildPlanAsync(
        CharacterBuildView currentBuild,
        CharacterStateView? currentState,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken)
    {
        var existing = request.ClassAdvancementEntryId is Guid occurrenceId
            ? currentBuild.ProgressionEntries.SingleOrDefault(value => value.Id == occurrenceId)
            : null;
        if (request.ClassAdvancementEntryId is not null && existing is null)
        {
            throw new ArgumentException("The selected Class occurrence was not found.", nameof(request));
        }
        if (existing is not null && existing.Kind != CharacterBuildAdvancementKinds.Class)
        {
            throw new ArgumentException("Only a base Class occurrence can be advanced by this workflow.", nameof(request));
        }

        var classConceptKey = existing?.RuleConceptKey
            ?? NormalizeConceptKey(request.ClassConceptKey);
        if (existing is null && currentBuild.ProgressionEntries.Any(value =>
                value.Kind == CharacterBuildAdvancementKinds.Class
                && string.Equals(value.RuleConceptKey, classConceptKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "This Character already has that Class. Advance its existing Class occurrence instead of adding a duplicate.");
        }

        var resolved = await rulesCore.ResolveGlobalRulesAsync([classConceptKey], cancellationToken);
        if (!resolved.TryGetValue(classConceptKey, out var classRule)
            || !string.Equals(classRule.EntityType, "class", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The selected rule is not an available Class.", nameof(request));
        }

        var classOccurrenceId = existing?.Id ?? Guid.NewGuid();
        var currentClassLevel = existing?.Level ?? 0;
        if (existing is not null && currentClassLevel <= 0)
        {
            throw new InvalidOperationException("The selected Class occurrence does not have a valid current level.");
        }
        var targetClassLevel = checked(currentClassLevel + 1);
        var currentCharacterLevel = CharacterLevel(currentBuild);
        var targetCharacterLevel = checked(currentCharacterLevel + 1);
        CharacterNormalProgressionPolicy.EnsureCharacterLevelChangeAllowed(
            currentCharacterLevel,
            targetCharacterLevel);

        var changedAt = timeProvider.GetUtcNow();
        var prospectiveEntries = currentBuild.ProgressionEntries.ToList();
        if (existing is null)
        {
            var nextOrdinal = prospectiveEntries
                .Where(value => value.Kind == CharacterBuildAdvancementKinds.Class)
                .Select(value => value.Ordinal ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;
            prospectiveEntries.Add(new CharacterAdvancementEntryView(
                classOccurrenceId,
                nextOrdinal,
                CharacterBuildAdvancementKinds.Class,
                classConceptKey,
                null,
                changedAt,
                changedAt,
                1));
        }
        else
        {
            prospectiveEntries = prospectiveEntries
                .Select(value => value.Id == existing.Id
                    ? value with { Level = targetClassLevel, UpdatedAt = changedAt }
                    : value)
                .ToList();
        }

        var prospectiveBuild = currentBuild with { ProgressionEntries = prospectiveEntries };
        var prospectiveState = AddProspectiveHitPointGain(
            currentState,
            classOccurrenceId,
            targetClassLevel,
            request.HitDieValue,
            changedAt);
        var projectionRequest = CharacterRulesProjectionRequestBuilder.Build(
            prospectiveBuild,
            prospectiveState);
        var projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
            projectionRequest,
            cancellationToken);

        var hitPointGainRequired = projection.Resources.Any(value =>
            string.Equals(
                value.ResourceKey,
                $"resource.hit-die.{classConceptKey}",
                StringComparison.Ordinal));

        var currentSubclass = prospectiveBuild.ProgressionEntries.FirstOrDefault(value =>
            value.Kind == CharacterBuildAdvancementKinds.Subclass
            && value.ParentAdvancementEntryId == classOccurrenceId);
        string? subclassConceptKey = currentSubclass?.RuleConceptKey;
        string? subclassDisplayName = null;
        var newlyAcquiredSubclass = false;
        RulesCoreCharacterAdvancementEligibilityView? subclassEligibility = null;
        var subclassChoice = FindSubclassChoice(
            projection,
            classConceptKey,
            classOccurrenceId);
        var requestedSubclass = NormalizeOptionalConceptKey(request.SubclassConceptKey);

        if (currentSubclass is not null && requestedSubclass is not null)
        {
            throw new InvalidOperationException(
                "This Class occurrence already has a Subclass; a new Subclass can not be selected during level advancement.");
        }

        if (currentSubclass is null && requestedSubclass is not null)
        {
            if (subclassChoice is null)
            {
                throw new InvalidOperationException(
                    "A Subclass can not be selected at the proposed Class level because Rules Core did not expose a Subclass choice.");
            }

            projectionRequest = WithChoice(
                projectionRequest,
                subclassChoice.ChoiceKey,
                requestedSubclass);
            projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
                projectionRequest,
                cancellationToken);
            subclassChoice = projection.Choices.FirstOrDefault(value =>
                string.Equals(value.ChoiceKey, subclassChoice.ChoiceKey, StringComparison.Ordinal));

            var advancementRulesCore = rulesCore as IRulesCoreAdvancementGateway
                ?? throw new RulesCoreGatewayException(
                    "The configured Rules Core gateway does not support advancement eligibility.");
            subclassEligibility = await advancementRulesCore.ResolveGlobalCharacterAdvancementEligibilityAsync(
                new RulesCoreCharacterAdvancementEligibilityRequest(
                    requestedSubclass,
                    projectionRequest,
                    classOccurrenceId.ToString("D")),
                cancellationToken);
            subclassConceptKey = requestedSubclass;
            subclassDisplayName = subclassEligibility.CandidateDisplayName;

            var choiceResolved = subclassChoice is not null
                && IsResolved(subclassChoice.State)
                && string.Equals(
                    subclassChoice.SelectedValue,
                    requestedSubclass,
                    StringComparison.OrdinalIgnoreCase);
            if (choiceResolved && subclassEligibility.Eligible == true)
            {
                newlyAcquiredSubclass = true;
                prospectiveEntries.Add(new CharacterAdvancementEntryView(
                    Guid.NewGuid(),
                    null,
                    CharacterBuildAdvancementKinds.Subclass,
                    requestedSubclass,
                    classOccurrenceId,
                    changedAt,
                    changedAt));
                prospectiveBuild = prospectiveBuild with { ProgressionEntries = prospectiveEntries };
                projectionRequest = CharacterRulesProjectionRequestBuilder.Build(
                    prospectiveBuild,
                    prospectiveState);
                projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
                    projectionRequest,
                    cancellationToken);
                subclassChoice = FindSubclassChoice(
                    projection,
                    classConceptKey,
                    classOccurrenceId);
            }
        }

        var relevantPrerequisites = projection.Prerequisites
            .Where(value => string.Equals(value.ConceptKey, classConceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.ConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToList();
        if (subclassEligibility?.Prerequisites is { } eligibilityPrerequisite
            && !relevantPrerequisites.Any(value => string.Equals(
                value.ConceptKey,
                eligibilityPrerequisite.ConceptKey,
                StringComparison.Ordinal)))
        {
            relevantPrerequisites.Add(eligibilityPrerequisite);
        }

        var requiredChoices = projection.Choices
            .Where(value => !IsResolved(value.State))
            .Where(value => string.Equals(value.SourceConceptKey, classConceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.SourceConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToList();
        if (currentSubclass is null
            && requestedSubclass is null
            && subclassChoice is not null
            && !requiredChoices.Any(value => string.Equals(
                value.ChoiceKey,
                subclassChoice.ChoiceKey,
                StringComparison.Ordinal)))
        {
            requiredChoices.Add(subclassChoice with
            {
                State = "choice-required",
                SelectedValue = null
            });
        }

        var relatedConcepts = new HashSet<string>(StringComparer.Ordinal)
        {
            classConceptKey
        };
        if (subclassConceptKey is not null) relatedConcepts.Add(subclassConceptKey);
        var blockingConflicts = projection.Conflicts
            .Where(value => value.RelatedConceptKeys.Any(relatedConcepts.Contains))
            .ToList();
        if (subclassEligibility is not null)
        {
            foreach (var conflict in subclassEligibility.Conflicts)
            {
                if (!blockingConflicts.Any(value => string.Equals(
                        value.ConflictKey,
                        conflict.ConflictKey,
                        StringComparison.Ordinal)))
                {
                    blockingConflicts.Add(conflict);
                }
            }
        }

        var prerequisitesSatisfied = relevantPrerequisites.All(value => value.Satisfied == true);
        var hitPointsSatisfied = !hitPointGainRequired || request.HitDieValue is not null;
        var eligibilitySatisfied = subclassEligibility is null || subclassEligibility.Eligible == true;
        var canApply = prerequisitesSatisfied
            && hitPointsSatisfied
            && eligibilitySatisfied
            && requiredChoices.Count == 0
            && blockingConflicts.Count == 0;
        var status = !prerequisitesSatisfied
            ? "blocked-prerequisite"
            : requiredChoices.Count > 0
                ? "choices-required"
                : blockingConflicts.Count > 0
                    ? "blocked-conflict"
                    : !eligibilitySatisfied
                        ? "blocked-eligibility"
                        : !hitPointsSatisfied
                            ? "hit-points-required"
                            : "ready";

        var existingFeatureKeys = new HashSet<string>(StringComparer.Ordinal);
        if (currentBuild.ProgressionEntries.Count > 0)
        {
            var currentProjection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
                CharacterRulesProjectionRequestBuilder.Build(currentBuild, currentState),
                cancellationToken);
            existingFeatureKeys.UnionWith(currentProjection.Features.Select(value => value.FeatureKey));
        }
        var changes = new List<CharacterAdvancementChangeView>
        {
            new(
                existing is null ? "class-acquired" : "class-level",
                existing is null
                    ? $"Acquire {classRule.DisplayName} level 1"
                    : $"Advance {classRule.DisplayName} to level {targetClassLevel}")
        };
        if (newlyAcquiredSubclass && subclassConceptKey is not null)
        {
            changes.Add(new CharacterAdvancementChangeView(
                "subclass-acquired",
                $"Choose {subclassDisplayName ?? subclassConceptKey}"));
        }
        changes.AddRange(projection.Features
            .Where(value => !existingFeatureKeys.Contains(value.FeatureKey))
            .Where(value => string.Equals(value.SourceConceptKey, classConceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.SourceConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .Select(value => new CharacterAdvancementChangeView(
                "feature",
                value.DisplayName,
                value.AcquisitionLevel is int acquisitionLevel
                    ? $"Level {acquisitionLevel}"
                    : null)));

        return new CharacterAdvancementPlanView(
            existing is null ? "multiclass" : "level-up",
            classOccurrenceId,
            classConceptKey,
            classRule.DisplayName,
            existing?.Ordinal == 0,
            currentClassLevel,
            targetClassLevel,
            currentCharacterLevel,
            targetCharacterLevel,
            hitPointGainRequired,
            request.HitDieValue,
            status,
            canApply,
            relevantPrerequisites,
            requiredChoices,
            blockingConflicts,
            changes,
            subclassConceptKey,
            subclassDisplayName,
            subclassEligibility);
    }

    private static RulesCoreCharacterChoiceView? FindSubclassChoice(
        RulesCoreCharacterRulesProjectionView projection,
        string classConceptKey,
        Guid classOccurrenceId)
    {
        var occurrence = classOccurrenceId.ToString("D");
        return projection.Choices.FirstOrDefault(value =>
            string.Equals(value.Kind, "subclass", StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.SourceConceptKey, classConceptKey, StringComparison.Ordinal)
            && value.ChoiceKey.Contains(occurrence, StringComparison.OrdinalIgnoreCase));
    }

    private static RulesCoreCharacterRulesProjectionRequest WithChoice(
        RulesCoreCharacterRulesProjectionRequest request,
        string choiceKey,
        string value)
    {
        var choices = (request.Choices ?? [])
            .Where(choice => !string.Equals(
                choice.ChoiceKey,
                choiceKey,
                StringComparison.Ordinal))
            .Append(new RulesCoreCharacterRuntimeChoiceInput(choiceKey, value))
            .ToArray();
        return request with { Choices = choices };
    }

    private static CharacterStateView? AddProspectiveHitPointGain(
        CharacterStateView? state,
        Guid occurrenceId,
        int classLevel,
        int? hitDieValue,
        DateTimeOffset changedAt)
    {
        if (state is null || hitDieValue is null)
        {
            return state;
        }

        var gains = (state.HitPointGains ?? [])
            .Where(value => value.AdvancementOccurrenceId != occurrenceId || value.ClassLevel != classLevel)
            .Append(new CharacterHitPointGainStateView(
                Guid.NewGuid(),
                occurrenceId,
                classLevel,
                hitDieValue.Value,
                changedAt,
                changedAt))
            .ToArray();
        return state with { HitPointGains = gains };
    }

    private static int CharacterLevel(CharacterBuildView build) =>
        build.ProgressionEntries
            .Where(value => value.Kind is CharacterBuildAdvancementKinds.Class
                or CharacterBuildAdvancementKinds.PrestigeClass)
            .Sum(value => Math.Max(value.Level ?? 0, 0));

    private static string NormalizeConceptKey(string? value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("A Class must be selected before previewing multiclass advancement.");
        }
        return normalized;
    }

    private static string? NormalizeOptionalConceptKey(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool IsResolved(string state) =>
        string.Equals(state, "resolved", StringComparison.OrdinalIgnoreCase);

    private static CharacterAdvancementAccessStatus? MapBuildAccess(CharacterBuildAccessStatus status) =>
        status switch
        {
            CharacterBuildAccessStatus.Ready => null,
            CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterAdvancementAccessStatus.NotFoundOrNotOwned,
            CharacterBuildAccessStatus.ProjectionUnavailable => CharacterAdvancementAccessStatus.ProjectionUnavailable,
            CharacterBuildAccessStatus.Unauthenticated => CharacterAdvancementAccessStatus.Unauthenticated,
            CharacterBuildAccessStatus.SheetNotInitialized => CharacterAdvancementAccessStatus.SheetNotInitialized,
            CharacterBuildAccessStatus.ArchivedReadOnly => CharacterAdvancementAccessStatus.ArchivedReadOnly,
            _ => CharacterAdvancementAccessStatus.ProjectionUnavailable
        };

    private static CharacterAdvancementAccessStatus? MapStateAccess(CharacterStateAccessStatus status) =>
        status switch
        {
            CharacterStateAccessStatus.Ready => null,
            CharacterStateAccessStatus.NotFoundOrNotOwned => CharacterAdvancementAccessStatus.NotFoundOrNotOwned,
            CharacterStateAccessStatus.ProjectionUnavailable => CharacterAdvancementAccessStatus.ProjectionUnavailable,
            CharacterStateAccessStatus.Unauthenticated => CharacterAdvancementAccessStatus.Unauthenticated,
            CharacterStateAccessStatus.SheetNotInitialized => CharacterAdvancementAccessStatus.SheetNotInitialized,
            CharacterStateAccessStatus.ArchivedReadOnly => CharacterAdvancementAccessStatus.ArchivedReadOnly,
            CharacterStateAccessStatus.EntryNotFound => CharacterAdvancementAccessStatus.EntryNotFound,
            _ => CharacterAdvancementAccessStatus.ProjectionUnavailable
        };
}
