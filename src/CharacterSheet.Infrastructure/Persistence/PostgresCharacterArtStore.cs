using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterArtStore(CharacterSheetDbContext dbContext) : ICharacterArtStore
{
    public async Task<IReadOnlyList<CharacterArtAsset>> ListAsync(Guid characterId, CancellationToken cancellationToken = default) =>
        await dbContext.CharacterArtAssets.AsNoTracking()
            .Where(value => value.CharacterId == characterId)
            .OrderByDescending(value => value.IsPortrait)
            .ThenBy(value => value.CreatedAt)
            .ThenBy(value => value.Id)
            .ToArrayAsync(cancellationToken);

    public Task<CharacterArtAsset?> GetAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default) =>
        dbContext.CharacterArtAssets.AsNoTracking()
            .SingleOrDefaultAsync(value => value.CharacterId == characterId && value.Id == assetId, cancellationToken);

    public async Task<CharacterArtAsset> AddAsync(CharacterArtAsset asset, CancellationToken cancellationToken = default)
    {
        dbContext.CharacterArtAssets.Add(asset);
        await dbContext.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public async Task<CharacterArtAsset?> SetPortraitAsync(Guid characterId, Guid assetId, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        var assets = await dbContext.CharacterArtAssets
            .Where(value => value.CharacterId == characterId)
            .ToArrayAsync(cancellationToken);
        var selected = assets.SingleOrDefault(value => value.Id == assetId);
        if (selected is null) return null;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var previousPortraits = assets
            .Where(value => value.IsPortrait && value.Id != assetId)
            .ToArray();

        if (previousPortraits.Length > 0)
        {
            foreach (var asset in previousPortraits)
            {
                asset.SetPortrait(false, changedAt);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        selected.SetPortrait(true, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return selected;
    }

    public async Task ClearPortraitAsync(Guid characterId, DateTimeOffset changedAt, CancellationToken cancellationToken = default)
    {
        var portraits = await dbContext.CharacterArtAssets
            .Where(value => value.CharacterId == characterId && value.IsPortrait)
            .ToArrayAsync(cancellationToken);
        foreach (var asset in portraits) asset.SetPortrait(false, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CharacterArtAsset?> DeleteAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var asset = await dbContext.CharacterArtAssets
            .SingleOrDefaultAsync(value => value.CharacterId == characterId && value.Id == assetId, cancellationToken);
        if (asset is null) return null;
        dbContext.CharacterArtAssets.Remove(asset);
        await dbContext.SaveChangesAsync(cancellationToken);
        return asset;
    }
}
