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
            .AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }
}
