namespace CharacterSheet.Application.Site;

/// <summary>
/// Live Site-owned DM membership and active campaign-character association
/// lookup. Unlike the request ticket's captured DM scopes, each call must
/// independently query current Site state. It authorizes no individual
/// table decision and provides no cross-database commit lease.
/// </summary>
public interface ISiteFreshOriginDmAccessGateway
{
    Task<SiteOriginDmAccessResult> CheckFreshAsync(
        Guid campaignId, Guid characterId,
        CancellationToken cancellationToken = default);
}
