namespace CharacterSheet.Application.Site;

public enum SiteCharacterLifecycleState
{
    Active,
    Archived
}

/// <summary>
/// Transient Site-authoritative projection. This is not Character Sheet persistence.
/// </summary>
public sealed record SiteCharacterProjection(
    Guid CharacterId,
    string Name,
    SiteCharacterLifecycleState LifecycleState,
    DateTimeOffset? ArchivedAt,
    IReadOnlyCollection<Guid> CampaignIds)
{
    public bool AllowsOrdinaryEditingByLifecycle => LifecycleState == SiteCharacterLifecycleState.Active;
}

/// <summary>
/// Future integration boundary for authoritative Site character access/lifecycle data.
/// No production implementation exists until the Site extends the Tool Host contract with
/// per-character authorization.
/// </summary>
public interface ISiteCharacterAccessGateway
{
    Task<SiteCharacterProjection?> GetAuthorizedCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default);
}
