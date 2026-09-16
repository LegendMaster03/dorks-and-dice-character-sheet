using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Persistence boundary for Character Sheet-owned rich state. Implementations must key
/// records by the canonical Site CharacterId and must not introduce a parallel character identity.
/// </summary>
public interface ICharacterSheetStore
{
    Task<CharacterSheetRoot?> GetAsync(Guid characterId, CancellationToken cancellationToken = default);

    Task SaveAsync(CharacterSheetRoot characterSheet, CancellationToken cancellationToken = default);
}
