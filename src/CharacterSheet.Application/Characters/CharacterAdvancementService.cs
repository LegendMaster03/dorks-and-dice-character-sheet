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
    string? SubclassConceptKey = null,
    Guid? PrestigeClassAdvancementEntryId = null,
    string? PrestigeClassConceptKey = null);

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
    RulesCoreCharacterAdvancementEligibilityView? SubclassEligibility = null,
    string AdvancementKind = CharacterBuildAdvancementKinds.Class,
    RulesCoreCharacterAdvancementEligibilityView? AdvancementEligibility = null);

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
/// Coordinates one player-facing independently leveled progression step. Rules Core owns
/// prerequisites, choices, conflicts, features, candidate eligibility, hit-die semantics, and
/// resulting mechanics; Character Sheet owns the Character occurrence mutation and persists the
/// accepted Class or Prestige Class step atomically.
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

        var kind = ToDomainKind(preview.Plan.AdvancementKind);
        var occurrenceId = kind == CharacterAdvancementKind.PrestigeClass
            ? request.PrestigeClassAdvancementEntryId
            : request.ClassAdvancementEntryId;
        var root = await buildStore.ApplyProgressionAdvancementAsync(
            characterId,
            occurrenceId,
            kind,
            preview.Plan.ClassConceptKey,
            preview.Plan.HitPointGainRequired ? request.HitDieValue : null,
            kind == CharacterAdvancementKind.Class ? preview.Plan.SubclassConceptKey : null,
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
        var prestigeRequested = request.PrestigeClassAdvancementEntryId is not null
            || !string.IsNullOrWhiteSpace(request.PrestigeClassConceptKey);
        var classRequested = request.ClassAdvancementEntryId is not null
            || !string.IsNullOrWhiteSpace(request.ClassConceptKey)
            || !string.IsNullOrWhiteSpace(request.SubclassConceptKey);
        if (prestigeRequested && classRequested)
        {
            throw new ArgumentException(
                "An advancement request can target either a Class or a Prestige Class, not both.",
                nameof(request));
        }

        var advancementKind = prestigeRequested
            ? CharacterBuildAdvancementKinds.PrestigeClass
            : CharacterBuildAdvancementKinds.Class;
        var expectedEntityType = prestigeRequested ? "prestigeClass" : "class";
        var displayKind = prestigeRequested ? "Prestige Class" : "Class";
        var requestedOccurrenceId = prestigeRequested
            ? request.PrestigeClassAdvancementEntryId
            : request.ClassAdvancementEntryId;
        var requestedConceptKey = prestigeRequested
            ? request.PrestigeClassConceptKey
            : request.ClassConceptKey;

        if (prestigeRequested && !string.IsNullOrWhiteSpace(request.SubclassConceptKey))
        {
            throw new ArgumentException(
                "A Prestige Class advancement can not select a Subclass.",
                nameof(request));
        }

        var existing = requestedOccurrenceId is Guid occurrenceId
            ? currentBuild.ProgressionEntries.SingleOrDefault(value => value.Id == occurrenceId)
            : null;
        if (requestedOccurrenceId is not null && existing is null)
        {
            throw new ArgumentException(
                $"The selected {displayKind} occurrence was not found.",
                nameof(request));
        }
        if (existing is not null && existing.Kind != advancementKind)
        {
            throw new ArgumentException(
                $"The selected occurrence is not a {displayKind} progression.",
                nameof(request));
        }

        var conceptKey = existing?.RuleConceptKey
            ?? NormalizeConceptKey(requestedConceptKey, displayKind);
        if (existing is null && currentBuild.ProgressionEntries.Any(value =>
                value.Kind == advancementKind
                && string.Equals(value.RuleConceptKey, conceptKey, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"This Character already has that {displayKind}. Advance its existing occurrence instead of adding a duplicate.");
        }

        var resolved = await rulesCore.ResolveGlobalRulesAsync([conceptKey], cancellationToken);
        if (!resolved.TryGetValue(conceptKey, out var progressionRule)
            || !string.Equals(progressionRule.EntityType, expectedEntityType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The selected rule is not an available {displayKind}.",
                nameof(request));
        }

        var progressionOccurrenceId = existing?.Id ?? Guid.NewGuid();
        var currentProgressionLevel = existing?.Level ?? 0;
        if (existing is not null && currentProgressionLevel <= 0)
        {
            throw new InvalidOperationException(
                $"The selected {displayKind} occurrence does not have a valid current level.");
        }
        var targetProgressionLevel = checked(currentProgressionLevel + 1);
        var currentCharacterLevel = CharacterLevel(currentBuild);
        var targetCharacterLevel = checked(currentCharacterLevel + 1);
        CharacterNormalProgressionPolicy.EnsureCharacterLevelChangeAllowed(
            currentCharacterLevel,
            targetCharacterLevel);

        var advancementGateway = rulesCore as IRulesCoreAdvancementGateway;
        RulesCoreCharacterAdvancementEligibilityView? advancementEligibility = null;
        if (prestigeRequested && existing is null)
        {
            advancementGateway ??= throw new RulesCoreGatewayException(
                "The configured Rules Core gateway does not support advancement eligibility.");
            advancementEligibility = await advancementGateway.ResolveGlobalCharacterAdvancementEligibilityAsync(
                new RulesCoreCharacterAdvancementEligibilityRequest(
                    conceptKey,
                    CharacterRulesProjectionRequestBuilder.Build(currentBuild, currentState)),
                cancellationToken);
        }

        var changedAt = timeProvider.GetUtcNow();
        var prospectiveEntries = currentBuild.ProgressionEntries.ToList();
        if (existing is null)
        {
            var nextOrdinal = prospectiveEntries
                .Where(value => value.Kind == advancementKind)
                .Select(value => value.Ordinal ?? -1)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            if (!prestigeRequested && nextOrdinal == 0)
            {
                nextOrdinal = 1;
            }
            prospectiveEntries.Add(new CharacterAdvancementEntryView(
                progressionOccurrenceId,
                nextOrdinal,
                advancementKind,
                conceptKey,
                null,
                changedAt,
                changedAt,
                1));
        }
        else
        {
            prospectiveEntries = prospectiveEntries
                .Select(value => value.Id == existing.Id
                    ? value with { Level = targetProgressionLevel, UpdatedAt = changedAt }
                    : value)
                .ToList();
        }

        var prospectiveBuild = currentBuild with { ProgressionEntries = prospectiveEntries };
        var prospectiveState = AddProspectiveHitPointGain(
            currentState,
            progressionOccurrenceId,
            targetProgressionLevel,
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
                $"resource.hit-die.{conceptKey}",
                StringComparison.Ordinal));

        CharacterAdvancementEntryView? currentSubclass = null;
        string? subclassConceptKey = null;
        string? subclassDisplayName = null;
        var newlyAcquiredSubclass = false;
        RulesCoreCharacterAdvancementEligibilityView? subclassEligibility = null;
        RulesCoreCharacterChoiceView? subclassChoice = null;
        var requestedSubclass = NormalizeOptionalConceptKey(request.SubclassConceptKey);

        if (!prestigeRequested)
        {
            currentSubclass = prospectiveBuild.ProgressionEntries.FirstOrDefault(value =>
                value.Kind == CharacterBuildAdvancementKinds.Subclass
                && value.ParentAdvancementEntryId == progressionOccurrenceId);
            subclassConceptKey = currentSubclass?.RuleConceptKey;
            subclassChoice = FindSubclassChoice(
                projection,
                conceptKey,
                progressionOccurrenceId);

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

                advancementGateway ??= throw new RulesCoreGatewayException(
                    "The configured Rules Core gateway does not support advancement eligibility.");
                subclassEligibility = await advancementGateway.ResolveGlobalCharacterAdvancementEligibilityAsync(
                    new RulesCoreCharacterAdvancementEligibilityRequest(
                        requestedSubclass,
                        projectionRequest,
                        progressionOccurrenceId.ToString("D")),
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
                        progressionOccurrenceId,
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
                        conceptKey,
                        progressionOccurrenceId);
                }
            }
        }

        var relevantPrerequisites = projection.Prerequisites
            .Where(value => string.Equals(value.ConceptKey, conceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.ConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToList();
        AddEligibilityPrerequisite(relevantPrerequisites, advancementEligibility);
        AddEligibilityPrerequisite(relevantPrerequisites, subclassEligibility);

        var requiredChoices = projection.Choices
            .Where(value => !IsResolved(value.State))
            .Where(value => string.Equals(value.SourceConceptKey, conceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.SourceConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .ToList();
        if (!prestigeRequested
            && currentSubclass is null
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
            conceptKey
        };
        if (subclassConceptKey is not null) relatedConcepts.Add(subclassConceptKey);
        var blockingConflicts = projection.Conflicts
            .Where(value => value.RelatedConceptKeys.Any(relatedConcepts.Contains))
            .ToList();
        AddEligibilityConflicts(blockingConflicts, advancementEligibility);
        AddEligibilityConflicts(blockingConflicts, subclassEligibility);

        var prerequisitesSatisfied = relevantPrerequisites.All(value => value.Satisfied == true);
        var hitPointsSatisfied = !hitPointGainRequired || request.HitDieValue is not null;
        var advancementEligibilitySatisfied = advancementEligibility is null
            || advancementEligibility.Eligible == true;
        var subclassEligibilitySatisfied = subclassEligibility is null
            || subclassEligibility.Eligible == true;
        var canApply = prerequisitesSatisfied
            && hitPointsSatisfied
            && advancementEligibilitySatisfied
            && subclassEligibilitySatisfied
            && requiredChoices.Count == 0
            && blockingConflicts.Count == 0;
        var status = !prerequisitesSatisfied
            ? "blocked-prerequisite"
            : !advancementEligibilitySatisfied || !subclassEligibilitySatisfied
                ? "blocked-eligibility"
                : requiredChoices.Count > 0
                    ? "choices-required"
                    : blockingConflicts.Count > 0
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

        var changeKindPrefix = prestigeRequested ? "prestige-class" : "class";
        var changes = new List<CharacterAdvancementChangeView>
        {
            new(
                existing is null ? $"{changeKindPrefix}-acquired" : $"{changeKindPrefix}-level",
                existing is null
                    ? $"Acquire {progressionRule.DisplayName} level 1"
                    : $"Advance {progressionRule.DisplayName} to level {targetProgressionLevel}")
        };
        if (newlyAcquiredSubclass && subclassConceptKey is not null)
        {
            changes.Add(new CharacterAdvancementChangeView(
                "subclass-acquired",
                $"Choose {subclassDisplayName ?? subclassConceptKey}"));
        }
        changes.AddRange(projection.Features
            .Where(value => !existingFeatureKeys.Contains(value.FeatureKey))
            .Where(value => string.Equals(value.SourceConceptKey, conceptKey, StringComparison.Ordinal)
                || (subclassConceptKey is not null
                    && string.Equals(value.SourceConceptKey, subclassConceptKey, StringComparison.Ordinal)))
            .Select(value => new CharacterAdvancementChangeView(
                "feature",
                value.DisplayName,
                value.AcquisitionLevel is int acquisitionLevel
                    ? $"Level {acquisitionLevel}"
                    : null)));

        return new CharacterAdvancementPlanView(
            prestigeRequested
                ? existing is null ? "prestige-class-acquire" : "prestige-class-level-up"
                : existing is null ? "multiclass" : "level-up",
            progressionOccurrenceId,
            conceptKey,
            progressionRule.DisplayName,
            !prestigeRequested && existing?.Ordinal == 0,
            currentProgressionLevel,
            targetProgressionLevel,
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
            subclassEligibility,
            advancementKind,
            advancementEligibility);
    }

    private static void AddEligibilityPrerequisite(
        List<RulesCoreCharacterPrerequisiteView> target,
        RulesCoreCharacterAdvancementEligibilityView? eligibility)
    {
        if (eligibility?.Prerequisites is not { } prerequisite) return;
        if (target.Any(value => string.Equals(
                value.ConceptKey,
                prerequisite.ConceptKey,
                StringComparison.Ordinal)))
        {
            return;
        }
        target.Add(prerequisite);
    }

    private static void AddEligibilityConflicts(
        List<RulesCoreCharacterProjectionConflictView> target,
        RulesCoreCharacterAdvancementEligibilityView? eligibility)
    {
        if (eligibility is null) return;
        foreach (var conflict in eligibility.Conflicts)
        {
            if (target.Any(value => string.Equals(
                    value.ConflictKey,
                    conflict.ConflictKey,
                    StringComparison.Ordinal)))
            {
                continue;
            }
            target.Add(conflict);
        }
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
        int progressionLevel,
        int? hitDieValue,
        DateTimeOffset changedAt)
    {
        if (state is null || hitDieValue is null)
        {
            return state;
        }

        var gains = (state.HitPointGains ?? [])
            .Where(value => value.AdvancementOccurrenceId != occurrenceId || value.ClassLevel != progressionLevel)
            .Append(new CharacterHitPointGainStateView(
                Guid.NewGuid(),
                occurrenceId,
                progressionLevel,
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

    private static string NormalizeConceptKey(string? value, string displayKind)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(
                $"A {displayKind} must be selected before previewing advancement.");
        }
        return normalized;
    }

    private static string? NormalizeOptionalConceptKey(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static CharacterAdvancementKind ToDomainKind(string kind) =>
        kind switch
        {
            CharacterBuildAdvancementKinds.Class => CharacterAdvancementKind.Class,
            CharacterBuildAdvancementKinds.PrestigeClass => CharacterAdvancementKind.PrestigeClass,
            _ => throw new InvalidOperationException(
                $"Unsupported independently leveled advancement kind '{kind}'.")
        };

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
