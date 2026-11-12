using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginReadStatus
{
    Ready, Unauthenticated, NotFoundOrNotOwned, ProjectionUnavailable,
    SheetNotInitialized
}

public sealed record CharacterOriginReadResult(
    CharacterOriginReadStatus Status,
    CharacterAuthorizedOriginState? SavedState = null,
    IReadOnlyList<CharacterOriginEventReceipt>? CompletedEvents = null,
    bool ReadOnly = false);

/// <summary>
/// Loads the character-owned, last committed Origin after the Site has verified
/// account ownership. This is trusted *stored* input for future reevaluation,
/// not proof that dependent Class, Background, inventory or supernatural grants
/// are still earned or currently applicable under the selected native rules.
/// </summary>
public sealed class CharacterOriginStateService(
    ISiteCharacterAccessGateway siteAccess,
    ICharacterBuildStore builds,
    ICharacterAuthorizedOriginStore authorizedOrigins)
{
    public async Task<CharacterOriginReadResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        var access = await siteAccess.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        switch (access.Status)
        {
            case SiteCharacterAccessStatus.Unauthenticated:
                return new(CharacterOriginReadStatus.Unauthenticated);
            case SiteCharacterAccessStatus.NotFoundOrNotOwned:
                return new(CharacterOriginReadStatus.NotFoundOrNotOwned);
            case SiteCharacterAccessStatus.Authorized:
                break;
            default:
                return new(CharacterOriginReadStatus.ProjectionUnavailable);
        }

        // Defend against inconsistent or stale Site projection data.
        if (access.Character?.CharacterId != characterId)
            return new(CharacterOriginReadStatus.ProjectionUnavailable);
        if (await builds.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginReadStatus.SheetNotInitialized);

        var saved = await authorizedOrigins.GetAsync(characterId, cancellationToken);
        var receipts = saved is null
            ? []
            : await authorizedOrigins.GetEventsAsync(characterId, cancellationToken);
        return new(CharacterOriginReadStatus.Ready, saved, receipts,
            !access.Character.AllowsOrdinaryEditingByLifecycle);
    }
}
