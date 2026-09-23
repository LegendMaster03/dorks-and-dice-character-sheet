using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterStateStore(CharacterSheetDbContext dbContext)
    : ICharacterStateStore
{
    public Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        BuildQuery(tracking: false)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    public async Task<CharacterSheetRoot?> SetCurrentHitPointsAsync(
        Guid characterId,
        int? currentHitPoints,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.SetCurrentHitPoints(currentHitPoints, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetDeathSavesAsync(
        Guid characterId,
        int successes,
        int failures,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.SetDeathSaves(successes, failures, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ApplyIntegerStateMutationsAsync(
        Guid characterId,
        IReadOnlyList<CharacterIntegerStateMutation> mutations,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ApplyIntegerStateMutations(mutations, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddInventoryItemOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.AddInventoryItemOccurrence(ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        int quantity,
        bool isCarried,
        bool isEquipped,
        bool isAttuned,
        Guid? containerOccurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.UpdateInventoryItemOccurrence(
            occurrenceId,
            quantity,
            isCarried,
            isEquipped,
            isAttuned,
            containerOccurrenceId,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.RemoveInventoryItemOccurrence(occurrenceId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetRulesInput(kind, key, integerValue, booleanValue, textValue, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveRulesInput(kind, key, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetHitPointGain(advancementOccurrenceId, classLevel, hitDieValue, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveHitPointGain(advancementOccurrenceId, classLevel, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddNoteAsync(
        Guid characterId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.AddNote(content, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateNoteAsync(
        Guid characterId,
        Guid noteId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.UpdateNote(noteId, content, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveNoteAsync(
        Guid characterId,
        Guid noteId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.RemoveNote(noteId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddConditionAsync(
        Guid characterId,
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.AddCondition(
            ruleConceptKey,
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateConditionAsync(
        Guid characterId,
        Guid conditionId,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.UpdateCondition(
            conditionId,
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveConditionAsync(
        Guid characterId,
        Guid conditionId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveCondition(conditionId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    private Task<CharacterSheetRoot?> GetTrackedAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        BuildQuery(tracking: true)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    private IQueryable<CharacterSheetRoot> BuildQuery(bool tracking)
    {
        var query = dbContext.CharacterSheets
            .Include(value => value.InventoryItemOccurrences)
            .Include(value => value.Notes)
            .Include(value => value.Conditions)
            .Include(value => value.RulesInputs)
            .Include(value => value.HitPointGains)
            .Include(value => value.AdvancementEntries)
            .AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }
}
