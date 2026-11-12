using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Stores only untrusted Origin drafts in the existing character database.
/// ExpectedRevision=0 means create; successful updates advance one revision.
/// A null write result indicates a stale or conflicting request.
/// </summary>
public interface ICharacterOriginDraftStore
{
    Task<CharacterOriginDraftState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default);

    Task<CharacterOriginDraftState?> SaveAsync(Guid characterId,
        string rulesetId, string? fingerprint, string proposalJson,
        long expectedRevision, DateTimeOffset changedAt,
        CancellationToken cancellationToken = default);
}
