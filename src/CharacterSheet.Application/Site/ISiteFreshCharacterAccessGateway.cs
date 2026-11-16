namespace CharacterSheet.Application.Site;

/// <summary>
/// Unlike the ordinary per-request Site introspection snapshot, this gateway
/// must retrieve current owner, lifecycle and active campaign links from the
/// Site-owned database on each invocation. A missing/expired delegation
/// capability, network failure or malformed response fails closed.
/// </summary>
public interface ISiteFreshCharacterAccessGateway
{
    Task<SiteCharacterAccessResult> GetFreshAuthorizedCharacterAsync(
        Guid characterId, CancellationToken cancellationToken = default);
}
