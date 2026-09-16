using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterBuildStore(CharacterSheetDbContext dbContext)
    : ICharacterBuildStore
{
    public Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        BuildQuery(tracking: false)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    public async Task<CharacterSheetRoot?> SetFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.SetFoundationalSelection(category, ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.ClearFoundationalSelection(category, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetStartingClassAsync(
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

        root.SetStartingClass(ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearStartingClassAsync(
        Guid characterId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.ClearStartingClass(changedAt);
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
            .Include(value => value.FoundationalSelections)
            .Include(value => value.AdvancementEntries)
            .AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }
}
