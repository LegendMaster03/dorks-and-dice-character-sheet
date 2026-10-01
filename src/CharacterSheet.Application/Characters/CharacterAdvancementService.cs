using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

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
    int? HitDieValue = null);

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
    string? SubclassDisplayName = null);

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
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow(),
                1));
        }
        else
        {
            prospectiveEntries = prospectiveEntries
                .Select(value => value.Id == existing.Id
                    ? value with { Level = targetClassLevel, UpdatedAt = timeProvider.GetUtcNow() }
                    : value)
                .ToList();
        }

        var prospectiveBuild = currentBuild with { ProgressionEntries = prospectiveEntries };
        var prospectiveState = AddProspectiveHitPointGain(
            currentState,
            classOccurrenceId,
            targetClassLevel,
            request.HitDieValue);
        var projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
            CharacterRulesProjectionRequestBuilder.Build(prospectiveBuild, prospectiveState),
            cancellationToken);

        var hitPointGainRequired = projection.Resources.Any(value =>
            string.Equals(
                value.ResourceKey,
                $"resource.hit-die.{classConceptKey}",
                StringComparison.Ordinal));

        string? subclassConceptKey = null;
        string? subclassDisplayName = null;
        var subclassChoice = projection.Choices.FirstOrDefault(value =>
            string.Equals(value.Kind, "subclass", StringComparison.OrdinalIgnoreCase)
            && string.Equals(value.SourceConceptKey, classConceptKey, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(value.SelectedValue));
        var currentSubclass = prospectiveBuild.ProgressionEntries.FirstOrDefault(value =>
            value.Kind == CharacterBuildAdvancementKinds.Subclass
            && value.ParentAdvancementEntryId == classOccurrenceId);
        if (currentSubclass is null && subclassChoice?.SelectedValue is string selectedSubclass)
        {
            var subclassRules = await rulesCore.ResolveGlobalRulesAsync([selectedSubclass], cancellationToken);
            if (!subclassRules.TryGetValue(selectedSubclass, out var subclassRule)
                || !string.Equals(subclassRule.EntityType, "subclass", StringComparison.OrdinalIgnoreCase)
                || !(subclassRule.Relationships ?? []).Any(relationship =>
                    string.Equals(relationship.Kind, "parent-class", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(relationship.RelatedEntityType, "class", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(relationship.RelatedConceptKey, classConceptKey, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Rules Core returned a Subclass choice that is not compatible with the selected Class.");
            }

            subclassConceptKey = selectedSubclass;
            subclassDisplayName = subclassRule.DisplayName;
            prospectiveEntries.Add(new CharacterAdvancementEntryView(
                Guid.NewGuid(),
                null,
                CharacterBuildAdvancementKinds.Subclass,
                selectedSubclass,
                classOccurrenceId,
                timeProvider.GetUtcNow(),
                timeProvider.GetUtcNow()));
            prospectiveBuild = prospectiveBuild with { ProgressionEntries = prospectiveEntries };
            projection = await rulesCore.ResolveGlobalCharacterMechanicsAsync(
                CharacterRulesProjectionRequestBuilder.Build(prospectiveBuild, prospectiveState),
                cancellationToken);
        }

        var relevantPrerequisites = projection.Prerequisites
            .Where(value => string.Equals(value.ConceptKey, classConceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.ConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToArray();
        var requiredChoices = projection.Choices
            .Where(value => !IsResolved(value.State))
            .Where(value => string.Equals(value.SourceConceptKey, classConceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.SourceConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToArray();
        var relatedConcepts = new HashSet<string>(StringComparer.Ordinal)
        {
            classConceptKey
        };
        if (subclassConceptKey is not null) relatedConcepts.Add(subclassConceptKey);
        var blockingConflicts = projection.Conflicts
            .Where(value => value.RelatedConceptKeys.Any(relatedConcepts.Contains))
            .ToArray();

        var prerequisitesSatisfied = relevantPrerequisites.All(value => value.Satisfied == true);
        var hitPointsSatisfied = !hitPointGainRequired || request.HitDieValue is not null;
        var canApply = prerequisitesSatisfied
            && hitPointsSatisfied
            && requiredChoices.Length == 0
            && blockingConflicts.Length == 0;
        var status = !prerequisitesSatisfied
            ? "blocked-prerequisite"
            : requiredChoices.Length > 0
                ? "choices-required"
                : blockingConflicts.Length > 0
                    ? "blocked-conflict"
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
        if (subclassConceptKey is not null)
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
            CharacterLevel(currentBuild),
            CharacterLevel(prospectiveBuild),
            hitPointGainRequired,
            request.HitDieValue,
            status,
            canApply,
            relevantPrerequisites,
            requiredChoices,
            blockingConflicts,
            changes,
            subclassConceptKey,
            subclassDisplayName);
    }

    private static CharacterStateView? AddProspectiveHitPointGain(
        CharacterStateView? state,
        Guid occurrenceId,
        int classLevel,
        int? hitDieValue)
    {
        if (state is null || hitDieValue is null)
        {
            return state;
        }

        var now = DateTimeOffset.UtcNow;
        var gains = (state.HitPointGains ?? [])
            .Where(value => value.AdvancementOccurrenceId != occurrenceId || value.ClassLevel != classLevel)
            .Append(new CharacterHitPointGainStateView(
                Guid.NewGuid(),
                occurrenceId,
                classLevel,
                hitDieValue.Value,
                now,
                now))
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
