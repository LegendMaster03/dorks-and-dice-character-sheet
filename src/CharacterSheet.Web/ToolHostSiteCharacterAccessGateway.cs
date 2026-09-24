using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Web;

public sealed class ToolHostSiteCharacterAccessGateway(IHttpContextAccessor httpContextAccessor)
    : ISiteCharacterAccessGateway
{
    public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.Unauthenticated));
        }

        var authenticationContext = HostedToolAuthenticationMiddleware.GetAuthenticationContext(httpContext);
        if (authenticationContext is null)
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.Unauthenticated));
        }

        if (authenticationContext.Characters is null)
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.ProjectionUnavailable));
        }

        var matches = authenticationContext.Characters
            .Where(entry => entry.Id == characterId)
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.NotFoundOrNotOwned));
        }

        if (matches.Length != 1)
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.ProjectionUnavailable));
        }

        var character = matches[0];

        SiteCharacterLifecycleState lifecycle;
        if (string.Equals(character.Status, "Active", StringComparison.Ordinal))
        {
            lifecycle = SiteCharacterLifecycleState.Active;
        }
        else if (string.Equals(character.Status, "Archived", StringComparison.Ordinal))
        {
            lifecycle = SiteCharacterLifecycleState.Archived;
        }
        else
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.ProjectionUnavailable));
        }

        if (character.Id == Guid.Empty
            || string.IsNullOrWhiteSpace(character.Name)
            || character.CampaignIds is null
            || (lifecycle == SiteCharacterLifecycleState.Active && character.ArchivedAt is not null)
            || (lifecycle == SiteCharacterLifecycleState.Archived && character.ArchivedAt is null))
        {
            return Task.FromResult(new SiteCharacterAccessResult(SiteCharacterAccessStatus.ProjectionUnavailable));
        }

        var campaignIds = character.CampaignIds.Distinct().ToArray();
        var campaigns = authenticationContext.Campaigns
            .Where(entry => campaignIds.Contains(entry.Id))
            .GroupBy(entry => entry.Id)
            .Select(group => new SiteCampaignDisplayProjection(
                group.Key,
                group.Select(value => value.Name)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    ?.Trim() ?? string.Empty))
            .Where(value => value.Name.Length > 0)
            .OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(value => value.CampaignId)
            .ToArray();

        return Task.FromResult(SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
            character.Id,
            character.Name,
            lifecycle,
            character.ArchivedAt,
            campaignIds,
            string.IsNullOrWhiteSpace(authenticationContext.User.DisplayName)
                ? null
                : authenticationContext.User.DisplayName.Trim(),
            campaigns)));
    }
}
