namespace CharacterSheet.Application.Site;

public enum SiteCharacterLifecycleState
{
    Active,
    Archived
}

public enum SiteCharacterAccessStatus
{
    Authorized,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated
}

/// <summary>
/// Transient Site-authoritative projection. This is not Character Sheet persistence.
/// </summary>
public sealed record SiteCampaignDisplayProjection(Guid CampaignId, string Name);

public sealed record SiteCharacterProjection(
    Guid CharacterId,
    string Name,
    SiteCharacterLifecycleState LifecycleState,
    DateTimeOffset? ArchivedAt,
    IReadOnlyCollection<Guid> CampaignIds,
    string? PlayerName = null,
    IReadOnlyCollection<SiteCampaignDisplayProjection>? Campaigns = null)
{
    public bool AllowsOrdinaryEditingByLifecycle => LifecycleState == SiteCharacterLifecycleState.Active;
}

public sealed record SiteCharacterAccessResult(
    SiteCharacterAccessStatus Status,
    SiteCharacterProjection? Character = null)
{
    public static SiteCharacterAccessResult Authorized(SiteCharacterProjection character) =>
        new(SiteCharacterAccessStatus.Authorized, character);
}

/// <summary>
/// Resolves Site-authoritative owner access for the current request. Implementations must use the
/// freshly redeemed Tool Host authentication context and must not infer ownership from local state.
/// </summary>
public interface ISiteCharacterAccessGateway
{
    Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);
}
