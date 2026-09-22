using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterStateAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized,
    ArchivedReadOnly,
    EntryNotFound
}

public sealed record CharacterInventoryItemOccurrenceView(
    Guid Id,
    string RuleConceptKey,
    DateTimeOffset CreatedAt,
    int Quantity = 1,
    bool IsCarried = true,
    bool IsEquipped = false,
    bool IsAttuned = false,
    Guid? ContainerOccurrenceId = null,
    DateTimeOffset? UpdatedAt = null);

public sealed record CharacterNoteView(
    Guid Id,
    string Content,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterDeathSavesView(
    int Successes,
    int Failures);

public static class CharacterRulesInputKinds
{
    public const string Choice = "choice";
    public const string CompetencyRank = "competencyRank";
    public const string Training = "training";
    public const string ClassSkill = "classSkill";
    public const string KnownSpell = "knownSpell";
    public const string Resource = "resource";
    public const string IntegerFact = "integerFact";
    public const string BooleanFact = "booleanFact";
    public const string StringFact = "stringFact";
}

public sealed record CharacterRulesInputStateView(
    Guid Id,
    string Kind,
    string Key,
    int? IntegerValue,
    bool? BooleanValue,
    string? TextValue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterHitPointGainStateView(
    Guid Id,
    Guid AdvancementOccurrenceId,
    int ClassLevel,
    int HitDieValue,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterConditionOccurrenceView(
    Guid Id,
    string? RuleConceptKey,
    string? CustomName,
    int? Level,
    int? CounterCurrent,
    int? CounterMaximum,
    string? Duration,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterStateView(
    Guid CharacterId,
    bool ReadOnly,
    int? CurrentHitPoints,
    CharacterDeathSavesView DeathSaves,
    IReadOnlyList<CharacterInventoryItemOccurrenceView> InventoryItemOccurrences,
    IReadOnlyList<CharacterNoteView> Notes,
    IReadOnlyList<CharacterConditionOccurrenceView> Conditions,
    IReadOnlyList<CharacterRulesInputStateView>? RulesInputs = null,
    IReadOnlyList<CharacterHitPointGainStateView>? HitPointGains = null);

public sealed record CharacterStateResult(
    CharacterStateAccessStatus Status,
    CharacterStateView? View = null);

/// <summary>
/// Application boundary for persisted Character-owned routine state. This service deliberately does
/// not resolve Rules Core mechanics or calculate effective Character values.
/// </summary>
public sealed class CharacterStateService(
    ISiteCharacterAccessGateway siteCharacterAccess,
    ICharacterStateStore stateStore,
    TimeProvider timeProvider)
{
    public async Task<CharacterStateResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var root = await stateStore.GetAsync(characterId, cancellationToken);
        if (root is null)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.SheetNotInitialized);
        }

        return Ready(root, access.Character!);
    }

    public Task<CharacterStateResult> SetCurrentHitPointsAsync(
        Guid characterId,
        int? currentHitPoints,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.SetCurrentHitPointsAsync(
                characterId,
                currentHitPoints,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> SetDeathSavesAsync(
        Guid characterId,
        int successes,
        int failures,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.SetDeathSavesAsync(
                characterId,
                successes,
                failures,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> AddInventoryItemOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.AddInventoryItemOccurrenceAsync(
                characterId,
                ruleConceptKey,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> UpdateInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        int quantity,
        bool isCarried,
        bool isEquipped,
        bool isAttuned,
        Guid? containerOccurrenceId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.UpdateInventoryItemOccurrenceAsync(
                characterId,
                occurrenceId,
                quantity,
                isCarried,
                isEquipped,
                isAttuned,
                containerOccurrenceId,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> RemoveInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.RemoveInventoryItemOccurrenceAsync(
                characterId,
                occurrenceId,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> SetRulesInputAsync(
        Guid characterId,
        string kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.SetRulesInputAsync(
                characterId,
                ParseRulesInputKind(kind),
                key,
                integerValue,
                booleanValue,
                textValue,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> RemoveRulesInputAsync(
        Guid characterId,
        string kind,
        string key,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.RemoveRulesInputAsync(
                characterId,
                ParseRulesInputKind(kind),
                key,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> SetHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.SetHitPointGainAsync(
                characterId,
                advancementOccurrenceId,
                classLevel,
                hitDieValue,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> RemoveHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.RemoveHitPointGainAsync(
                characterId,
                advancementOccurrenceId,
                classLevel,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> AddNoteAsync(
        Guid characterId,
        string content,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.AddNoteAsync(
                characterId,
                content,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> UpdateNoteAsync(
        Guid characterId,
        Guid noteId,
        string content,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.UpdateNoteAsync(
                characterId,
                noteId,
                content,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> RemoveNoteAsync(
        Guid characterId,
        Guid noteId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.RemoveNoteAsync(
                characterId,
                noteId,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> AddConditionAsync(
        Guid characterId,
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.AddConditionAsync(
                characterId,
                ruleConceptKey,
                customName,
                level,
                counterCurrent,
                counterMaximum,
                duration,
                notes,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> UpdateConditionAsync(
        Guid characterId,
        Guid conditionId,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.UpdateConditionAsync(
                characterId,
                conditionId,
                customName,
                level,
                counterCurrent,
                counterMaximum,
                duration,
                notes,
                changedAt,
                token),
            cancellationToken);

    public Task<CharacterStateResult> RemoveConditionAsync(
        Guid characterId,
        Guid conditionId,
        CancellationToken cancellationToken = default) =>
        MutateAsync(
            characterId,
            (changedAt, token) => stateStore.RemoveConditionAsync(
                characterId,
                conditionId,
                changedAt,
                token),
            cancellationToken);

    private async Task<CharacterStateResult> MutateAsync(
        Guid characterId,
        Func<DateTimeOffset, CancellationToken, Task<CharacterSheetRoot?>> mutation,
        CancellationToken cancellationToken)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var character = access.Character!;
        if (!character.AllowsOrdinaryEditingByLifecycle)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.ArchivedReadOnly);
        }

        CharacterSheetRoot? root;
        try
        {
            root = await mutation(timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.EntryNotFound);
        }

        if (root is null)
        {
            return new CharacterStateResult(CharacterStateAccessStatus.SheetNotInitialized);
        }

        return Ready(root, character);
    }

    private static CharacterStateResult Ready(
        CharacterSheetRoot root,
        SiteCharacterProjection character) =>
        new(
            CharacterStateAccessStatus.Ready,
            ToView(root, !character.AllowsOrdinaryEditingByLifecycle));

    private static CharacterStateResult? MapDeniedAccess(SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Authorized => null,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            new(CharacterStateAccessStatus.NotFoundOrNotOwned),
        SiteCharacterAccessStatus.ProjectionUnavailable =>
            new(CharacterStateAccessStatus.ProjectionUnavailable),
        SiteCharacterAccessStatus.Unauthenticated =>
            new(CharacterStateAccessStatus.Unauthenticated),
        _ => new(CharacterStateAccessStatus.ProjectionUnavailable)
    };

    private static CharacterStateView ToView(CharacterSheetRoot root, bool readOnly) =>
        new(
            root.CharacterId,
            readOnly,
            root.CurrentHitPoints,
            new CharacterDeathSavesView(root.DeathSaveSuccesses, root.DeathSaveFailures),
            root.InventoryItemOccurrences
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => new CharacterInventoryItemOccurrenceView(
                    value.Id,
                    value.RuleConceptKey,
                    value.CreatedAt,
                    value.Quantity,
                    value.IsCarried,
                    value.IsEquipped,
                    value.IsAttuned,
                    value.ContainerOccurrenceId,
                    value.UpdatedAt))
                .ToArray(),
            root.Notes
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => new CharacterNoteView(
                    value.Id,
                    value.Content,
                    value.CreatedAt,
                    value.UpdatedAt))
                .ToArray(),
            root.Conditions
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .Select(value => new CharacterConditionOccurrenceView(
                    value.Id,
                    value.RuleConceptKey,
                    value.CustomName,
                    value.Level,
                    value.CounterCurrent,
                    value.CounterMaximum,
                    value.Duration,
                    value.Notes,
                    value.CreatedAt,
                    value.UpdatedAt))
                .ToArray(),
            root.RulesInputs
                .OrderBy(value => value.Kind)
                .ThenBy(value => value.Key, StringComparer.Ordinal)
                .Select(value => new CharacterRulesInputStateView(
                    value.Id,
                    MapRulesInputKind(value.Kind),
                    value.Key,
                    value.IntegerValue,
                    value.BooleanValue,
                    value.TextValue,
                    value.CreatedAt,
                    value.UpdatedAt))
                .ToArray(),
            root.HitPointGains
                .OrderBy(value => value.AdvancementOccurrenceId)
                .ThenBy(value => value.ClassLevel)
                .Select(value => new CharacterHitPointGainStateView(
                    value.Id,
                    value.AdvancementOccurrenceId,
                    value.ClassLevel,
                    value.HitDieValue,
                    value.CreatedAt,
                    value.UpdatedAt))
                .ToArray());
    private static CharacterRulesInputKind ParseRulesInputKind(string value) =>
        value?.Trim() switch
        {
            CharacterRulesInputKinds.Choice => CharacterRulesInputKind.Choice,
            CharacterRulesInputKinds.CompetencyRank => CharacterRulesInputKind.CompetencyRank,
            CharacterRulesInputKinds.Training => CharacterRulesInputKind.Training,
            CharacterRulesInputKinds.ClassSkill => CharacterRulesInputKind.ClassSkill,
            CharacterRulesInputKinds.KnownSpell => CharacterRulesInputKind.KnownSpell,
            CharacterRulesInputKinds.Resource => CharacterRulesInputKind.Resource,
            CharacterRulesInputKinds.IntegerFact => CharacterRulesInputKind.IntegerFact,
            CharacterRulesInputKinds.BooleanFact => CharacterRulesInputKind.BooleanFact,
            CharacterRulesInputKinds.StringFact => CharacterRulesInputKind.StringFact,
            _ => throw new ArgumentException($"Unsupported rules input kind '{value}'.", nameof(value))
        };

    private static string MapRulesInputKind(CharacterRulesInputKind kind) => kind switch
    {
        CharacterRulesInputKind.Choice => CharacterRulesInputKinds.Choice,
        CharacterRulesInputKind.CompetencyRank => CharacterRulesInputKinds.CompetencyRank,
        CharacterRulesInputKind.Training => CharacterRulesInputKinds.Training,
        CharacterRulesInputKind.ClassSkill => CharacterRulesInputKinds.ClassSkill,
        CharacterRulesInputKind.KnownSpell => CharacterRulesInputKinds.KnownSpell,
        CharacterRulesInputKind.Resource => CharacterRulesInputKinds.Resource,
        CharacterRulesInputKind.IntegerFact => CharacterRulesInputKinds.IntegerFact,
        CharacterRulesInputKind.BooleanFact => CharacterRulesInputKinds.BooleanFact,
        CharacterRulesInputKind.StringFact => CharacterRulesInputKinds.StringFact,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported rules input kind.")
    };
}
