using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

public interface ICharacterArtStore
{
    Task<IReadOnlyList<CharacterArtAsset>> ListAsync(Guid characterId, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> GetAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset> AddAsync(CharacterArtAsset asset, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> SetPortraitAsync(Guid characterId, Guid assetId, DateTimeOffset changedAt, CancellationToken cancellationToken = default);
    Task ClearPortraitAsync(Guid characterId, DateTimeOffset changedAt, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> DeleteAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default);
}
