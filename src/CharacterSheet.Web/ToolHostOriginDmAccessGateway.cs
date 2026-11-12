using CharacterSheet.Application.Site;

namespace CharacterSheet.Web;

/// <summary>
/// Checks active DM/character associations from this request's redeemed Site
/// ticket. Campaign membership alone is insufficient. Never use a browser's
/// user ID or a local character record to infer campaign DM authority.
/// </summary>
public sealed class ToolHostOriginDmAccessGateway(IHttpContextAccessor http)
    : ISiteOriginDmAccessGateway
{
    public Task<SiteOriginDmAccessResult> CheckAsync(
        Guid campaignId, Guid characterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = http.HttpContext is { } request
            ? HostedToolAuthenticationMiddleware.GetAuthenticationContext(request)
            : null;
        if (context is null)
            return Task.FromResult(new SiteOriginDmAccessResult(
                SiteOriginDmAccessStatus.Unauthenticated));
        if (!string.Equals(context.SiteMode, "dorks-and-dice", StringComparison.Ordinal) ||
            !Guid.TryParse(context.User?.Id, out var actorId) || actorId == Guid.Empty ||
            context.DmCharacterScopes is null || context.Campaigns is null)
            return Task.FromResult(new SiteOriginDmAccessResult(
                SiteOriginDmAccessStatus.ProjectionUnavailable));
        if (campaignId == Guid.Empty || characterId == Guid.Empty ||
            !context.Campaigns.Any(campaign =>
                campaign.Id == campaignId &&
                string.Equals(campaign.Role, "DM", StringComparison.OrdinalIgnoreCase)) ||
            !context.DmCharacterScopes.Any(scope =>
                scope.CampaignId == campaignId && scope.CharacterId == characterId))
            return Task.FromResult(new SiteOriginDmAccessResult(
                SiteOriginDmAccessStatus.NotAuthorized));
        return Task.FromResult(new SiteOriginDmAccessResult(
            SiteOriginDmAccessStatus.Authorized, actorId, campaignId, characterId));
    }
}
