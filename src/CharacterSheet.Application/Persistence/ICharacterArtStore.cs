using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Persistence contract for character art metadata and portrait designation; image bytes are stored
/// through a separate storage boundary.
/// </summary>
public interface ICharacterArtStore
{
    Task<IReadOnlyList<CharacterArtAsset>> ListAsync(Guid characterId, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> GetAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset> AddAsync(CharacterArtAsset asset, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> SetPortraitAsync(Guid characterId, Guid assetId, DateTimeOffset changedAt, CancellationToken cancellationToken = default);
    Task ClearPortraitAsync(Guid characterId, DateTimeOffset changedAt, CancellationToken cancellationToken = default);
    Task<CharacterArtAsset?> DeleteAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default);
}
