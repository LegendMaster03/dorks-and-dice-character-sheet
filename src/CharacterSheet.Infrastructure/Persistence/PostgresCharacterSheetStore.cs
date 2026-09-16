using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterSheetStore(CharacterSheetDbContext dbContext)
    : ICharacterSheetStore
{
    public Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        dbContext.CharacterSheets
            .AsNoTracking()
            .SingleOrDefaultAsync(character => character.CharacterId == characterId, cancellationToken);

    public async Task<CharacterSheetRoot> GetOrCreateAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.CharacterSheets
            .AsNoTracking()
            .SingleOrDefaultAsync(character => character.CharacterId == characterId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var root = new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow);
        dbContext.CharacterSheets.Add(root);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return root;
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            existing = await dbContext.CharacterSheets
                .AsNoTracking()
                .SingleOrDefaultAsync(character => character.CharacterId == characterId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }
}
