namespace CharacterSheet.Application.Site;

public enum SiteOriginDmAccessStatus
{
    Authorized,
    Unauthenticated,
    NotAuthorized,
    ProjectionUnavailable
}

public sealed record SiteOriginDmAccessResult(
    SiteOriginDmAccessStatus Status,
    Guid? ActorUserId = null,
    Guid? CampaignId = null,
    Guid? CharacterId = null);

/// <summary>
/// Authorizes an exact DM/active-campaign/active-character association from
/// a Site-issued backend authentication ticket. This is NOT owner access,
/// a browser claim, or durable authorization of an individual table decision.
/// </summary>
public interface ISiteOriginDmAccessGateway
{
    Task<SiteOriginDmAccessResult> CheckAsync(
        Guid campaignId, Guid characterId,
        CancellationToken cancellationToken = default);
}
