using CharacterSheet.Application.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// Native-only class advancement. A preview is a read-only eligibility decision.
/// Application persistence is owned by Character Sheet; every apply reevaluates
/// native eligibility before mutating the Character advancement occurrence.
/// </summary>
public sealed class CharacterAdvancementService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    ICharacterBuildStore buildStore,
    INativeRulesCoreGateway native,
    TimeProvider timeProvider)
{
    public async Task<CharacterAdvancementPreviewResult> PreviewAsync(
        Guid characterId,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken = default)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (MapBuildAccess(buildResult.Status) is { } denied)
            return new(denied);
        if (buildResult.View is null)
            return new(CharacterAdvancementAccessStatus.SheetNotInitialized);

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (MapStateAccess(stateResult.Status) is { } stateDenied)
            return new(stateDenied);
        try
        {
            var plan = await BuildPlanAsync(
                buildResult.View, stateResult.View, request, cancellationToken);
            return new(CharacterAdvancementAccessStatus.Ready, plan);
        }
        catch (RulesCoreGatewayException exception)
        {
            return new(CharacterAdvancementAccessStatus.ProjectionUnavailable,
                Message: exception.Message);
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException or OverflowException or JsonException
            or FormatException or KeyNotFoundException)
        {
            return new(CharacterAdvancementAccessStatus.InvalidRequest,
                Message: exception.Message);
        }
    }

    public async Task<CharacterAdvancementApplyResult> ApplyAsync(
        Guid characterId,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(characterId, request, cancellationToken);
        if (preview.Status != CharacterAdvancementAccessStatus.Ready ||
            preview.Plan is null)
            return new(preview.Status, preview.Plan, Message: preview.Message);
        if (!preview.Plan.CanApply)
            return new(CharacterAdvancementAccessStatus.InvalidRequest,
                preview.Plan, Message: "Native class eligibility or hit point gain remains unresolved.");
        try
        {
            var root = await buildStore.ApplyProgressionAdvancementAsync(
                characterId,
                request.ClassAdvancementEntryId,
                CharacterAdvancementKind.Class,
                preview.Plan.ClassConceptKey,
                request.HitDieValue,
                null,
                timeProvider.GetUtcNow(),
                cancellationToken);
            if (root is null)
                return new(CharacterAdvancementAccessStatus.SheetNotInitialized, preview.Plan);
            var refreshed = await buildService.GetAsync(characterId, cancellationToken);
            if (refreshed.Status != CharacterBuildAccessStatus.Ready ||
                refreshed.View is null)
                return new(MapBuildAccess(refreshed.Status) ??
                    CharacterAdvancementAccessStatus.ProjectionUnavailable, preview.Plan);
            return new(CharacterAdvancementAccessStatus.Ready,
                preview.Plan with { Status = "applied", CanApply = false },
                refreshed.View);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException or KeyNotFoundException or OverflowException)
        {
            return new(CharacterAdvancementAccessStatus.InvalidRequest,
                preview.Plan, Message: exception.Message);
        }
    }

    private async Task<CharacterAdvancementPlanView> BuildPlanAsync(
        CharacterBuildView build,
        CharacterStateView? state,
        CharacterClassAdvancementRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PrestigeClassAdvancementEntryId is not null ||
            !string.IsNullOrWhiteSpace(request.PrestigeClassConceptKey) ||
            !string.IsNullOrWhiteSpace(request.SubclassConceptKey))
            throw new ArgumentException(
                "Prestige Class and Subclass advancement require a native eligibility policy.");

        var classes = build.ProgressionEntries
            .Where(entry => entry.Kind == CharacterBuildAdvancementKinds.Class)
            .ToArray();
        var selected = request.ClassAdvancementEntryId is Guid occurrence
            ? classes.SingleOrDefault(entry => entry.Id == occurrence)
            : null;
        if (request.ClassAdvancementEntryId is not null && selected is null)
            throw new ArgumentException("Selected Class advancement occurrence was not found.");

        var classId = request.ClassConceptKey?.Trim() ?? selected?.RuleConceptKey;
        if (string.IsNullOrWhiteSpace(classId) ||
            !classId.StartsWith("class.", StringComparison.Ordinal) ||
            classId.Length > 160 ||
            (selected is not null && selected.RuleConceptKey != classId))
            throw new ArgumentException("A matching native Class ID is required.");
        if (selected is null && classes.Length == 0)
            throw new ArgumentException("Choose a starting Class before using the advancement workflow.");
        if (selected is null && classes.Any(entry => entry.RuleConceptKey == classId))
            throw new ArgumentException("The Class is already represented by an advancement occurrence.");
        if (classes.Any(entry => entry.Level is not > 0 or > 1000) ||
            classes.Select(entry => entry.RuleConceptKey).Distinct(StringComparer.Ordinal).Count() != classes.Length ||
            build.ProgressionEntries.Any(entry => entry.Kind != CharacterBuildAdvancementKinds.Class))
            throw new ArgumentException(
                "The saved advancement history includes an unsupported option or ambiguous class occurrence.");
        // Foundational choices, conditions, equipment and ordinary resource
        // state are not class-level advancements. Do not block advancement
        // merely because this routine state exists. Unknown rule choices must
        // still fail closed until they have native option identities.
        var nativeChoices = NativeCharacterOptionSelections.FromState(state);
        if (nativeChoices.HasUnmappedChoices)
            throw new ArgumentException(
                "Saved rule choices must have distinct native option slots and identities before advancement.");
        if (request.HitDieValue is < 1 or > 1000)
            throw new ArgumentException("Hit point gain must be a positive bounded integer.");

        var metadata = await native.GetNativeRulesetAsync(cancellationToken);
        var rulesetId = Text(metadata, "rulesetId");
        var fingerprint = Text(metadata, "fingerprint");
        if (ReadInt(metadata, "contractVersion") != 1 ||
            string.IsNullOrWhiteSpace(rulesetId) || string.IsNullOrWhiteSpace(fingerprint) ||
            fingerprint.Length != 64 ||
            metadata["capabilities"]?["advancementEligibility"]?.GetValue<string>() != "supported-subset")
            throw new RulesCoreGatewayException("Native class eligibility is not available.");

        var catalog = await native.SearchNativeOptionsAsync(
            fingerprint, "class", classId, 100, 0, cancellationToken);
        if (Text(catalog, "rulesetId") != rulesetId ||
            Text(catalog, "fingerprint") != fingerprint ||
            catalog["options"] is not JsonArray items)
            throw new RulesCoreGatewayException("Native class catalog response does not match the active snapshot.");
        var match = items.FirstOrDefault(item => Text(item, "id") == classId &&
            Text(item, "kind") == "class");
        var className = Text(match, "name");
        if (string.IsNullOrWhiteSpace(className))
            throw new ArgumentException("Class has no authoritative native identity.");

        var currentLevel = selected?.Level ?? 0;
        var targetLevel = checked(currentLevel + 1);
        var characterLevel = classes.Sum(entry => entry.Level!.Value);
        var scores = build.BaseAbilityScoreInputs
            .GroupBy(entry => entry.AbilityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Score, StringComparer.Ordinal);
        var nativeInput = new
        {
            rulesetId,
            fingerprint,
            abilityScores = scores,
            advancements = classes.Select(entry => new {
                classId = entry.RuleConceptKey,
                level = entry.Level!.Value,
                occurrenceId = entry.Id.ToString("D")
            }).ToArray(),
            selectedOptions = nativeChoices.Ids
        };
        var rawRequest = JsonSerializer.SerializeToElement(new
        {
            rulesetId, fingerprint, candidateId = classId, character = nativeInput,
            hitDieValue = request.HitDieValue
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var eligibility = await native.EvaluateNativeAdvancementAsync(
            rawRequest, cancellationToken);
        var eligibilityStatus = Text(eligibility, "status");
        if (Text(eligibility, "rulesetId") != rulesetId ||
            Text(eligibility, "fingerprint") != fingerprint ||
            Text(eligibility, "candidateId") != classId ||
            Text(eligibility, "candidateKind") != "class" ||
            ReadInt(eligibility, "contractVersion") != 1 ||
            eligibilityStatus is not ("eligible" or "ineligible" or "unresolved" or "invalid"))
            throw new RulesCoreGatewayException("Native eligibility result is inconsistent.");

        var blockers = new List<RulesCoreCharacterProjectionConflictView>();
        if (eligibility["requirements"] is not JsonArray requirements)
            throw new RulesCoreGatewayException("Native eligibility requirements are missing.");
        foreach (var requirement in requirements)
        {
            if (Text(requirement, "status") == "satisfied") continue;
            blockers.Add(new RulesCoreCharacterProjectionConflictView(
                Text(requirement, "key") ?? "native-requirement", "native-eligibility",
                Text(requirement, "detail") ?? "Native prerequisite unresolved.",
                [], [classId]));
        }
        // A class progression row alone does not settle Patron, Pact,
        // invocation or other mandatory option choices. Project the proposed
        // class state and block apply when Rules Core reports required choices.
        if (eligibilityStatus == "eligible")
        {
            var proposedAdvancements = classes.Select(entry => new {
                classId = entry.RuleConceptKey,
                level = entry.Id == selected?.Id ? targetLevel : entry.Level!.Value,
                occurrenceId = entry.Id.ToString("D")
            }).ToList();
            if (selected is null)
                proposedAdvancements.Add(new {
                    classId, level = targetLevel, occurrenceId = "prospective-class"
                });
            var proposedInput = JsonSerializer.SerializeToElement(new {
                rulesetId, fingerprint, abilityScores = scores,
                advancements = proposedAdvancements,
                selectedOptions = nativeChoices.Ids
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var projected = await native.ProjectNativeCharacterAsync(
                proposedInput, cancellationToken);
            if (Text(projected, "rulesetId") != rulesetId ||
                Text(projected, "fingerprint") != fingerprint ||
                ReadInt(projected, "contractVersion") != 1)
                throw new RulesCoreGatewayException(
                    "Native projected character does not match the selected ruleset.");
            if (Text(projected, "status") == "invalid")
                blockers.Add(new RulesCoreCharacterProjectionConflictView(
                    "native-projection", "native-eligibility",
                    "Rules Core rejected the proposed class progression.", [], [classId]));
            // Projection may be partial for unrelated unresolved defenses,
            // movement, or spells. Only option errors block this advancement.
            // Individual selected option prerequisites are checked below.
            if (projected["issues"] is JsonArray projectedIssues)
                foreach (var issue in projectedIssues)
                {
                    var code = Text(issue, "code");
                    if (code is "exclusive-selection-conflict" or "option-not-native"
                        or "option-occurrence-unresolved" or "option-parent-unavailable"
                        or "option-evaluation-unavailable" or "prerequisite-failed"
                        or "invocation-cost-unresolved" or "invocation-budget-exceeded")
                        blockers.Add(new RulesCoreCharacterProjectionConflictView(
                            code, "native-option",
                            Text(issue, "detail") ?? "A selected native option is not valid.",
                            [], [Text(issue, "path") ?? classId]));
                }
            if (projected["requiredChoices"] is not JsonArray required)
                throw new RulesCoreGatewayException(
                    "Native projection did not describe required advancement choices.");
            foreach (var choice in required)
            {
                var name = choice?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(name))
                    blockers.Add(new RulesCoreCharacterProjectionConflictView(
                        name, "native-choice",
                        "Resolve the mandatory native advancement choice: " + name, [], [classId]));
            }
            // A class-level eligibility result alone does not validate selected
            // option prerequisites. Evaluate each option as a prospective choice
            // against the proposed level and the other already-selected options.
            foreach (var selectedOption in nativeChoices.Ids)
            {
                var otherOptions = nativeChoices.Ids.Where(id => id != selectedOption).ToArray();
                var optionRequest = JsonSerializer.SerializeToElement(new {
                    rulesetId, fingerprint, candidateId = selectedOption,
                    character = new {
                        rulesetId, fingerprint, abilityScores = scores,
                        advancements = proposedAdvancements,
                        selectedOptions = otherOptions
                    }
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var optionEligibility = await native.EvaluateNativeAdvancementAsync(
                    optionRequest, cancellationToken);
                if (Text(optionEligibility, "rulesetId") != rulesetId ||
                    Text(optionEligibility, "fingerprint") != fingerprint ||
                    Text(optionEligibility, "candidateId") != selectedOption ||
                    ReadInt(optionEligibility, "contractVersion") != 1)
                    throw new RulesCoreGatewayException(
                        "Native option eligibility result is inconsistent.");
                if (Text(optionEligibility, "status") != "eligible" ||
                    optionEligibility["eligible"]?.GetValue<bool>() != true)
                    blockers.Add(new RulesCoreCharacterProjectionConflictView(
                        selectedOption, "native-option",
                        "Native option prerequisites are unresolved or not satisfied.",
                        [], [selectedOption]));
            }
        }

        if (request.HitDieValue is null)
            blockers.Add(new RulesCoreCharacterProjectionConflictView(
                "hit-point-gain", "character-input",
                "Select the hit point gain for the next Class level.", [], [classId]));
        if (eligibilityStatus != "eligible" &&
            blockers.Count == 0)
            blockers.Add(new RulesCoreCharacterProjectionConflictView(
                "class-eligibility", "native-eligibility",
                "Native Rules Core did not authorize this advancement.", [], [classId]));

        var canApply = eligibilityStatus == "eligible" &&
            eligibility["eligible"]?.GetValue<bool>() == true &&
            request.HitDieValue is > 0 && blockers.Count == 0;
        return new CharacterAdvancementPlanView(
            Operation: selected is null ? "add-class" : "advance-class",
            ClassAdvancementEntryId: selected?.Id ?? Guid.Empty,
            ClassConceptKey: classId,
            ClassDisplayName: className,
            StartingClass: selected is not null && selected.Ordinal == 0,
            CurrentClassLevel: currentLevel,
            TargetClassLevel: targetLevel,
            CurrentCharacterLevel: characterLevel,
            TargetCharacterLevel: checked(characterLevel + 1),
            HitPointGainRequired: true,
            HitDieValue: request.HitDieValue,
            Status: canApply ? "ready" : eligibilityStatus == "eligible"
                ? "choice-required" : eligibilityStatus,
            CanApply: canApply,
            Prerequisites: [],
            RequiredChoices: [],
            BlockingConflicts: blockers,
            Changes: [new CharacterAdvancementChangeView(
                "class-level", className,
                $"Level {currentLevel} → {targetLevel}")]);
    }

    private static string? Text(JsonNode? value, string key)
    {
        try { return value?[key]?.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }
    private static int? ReadInt(JsonNode? value, string key)
    {
        try { return value?[key]?.GetValue<int>(); }
        catch (InvalidOperationException) { return null; }
    }

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
            _ => CharacterAdvancementAccessStatus.ProjectionUnavailable
        };
}
