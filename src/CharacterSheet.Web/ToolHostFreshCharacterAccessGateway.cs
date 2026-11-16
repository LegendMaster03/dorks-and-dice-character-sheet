using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Web;

/// <summary>
/// Independently queries the Site's live Character database using a
/// server-issued, source-scoped delegation capability. Never falls back to
/// cached introspection Characters or a browser-supplied bearer token.
/// </summary>
public sealed class ToolHostFreshCharacterAccessGateway(
    HttpClient client, IHttpContextAccessor contextAccessor)
    : ISiteFreshCharacterAccessGateway
{
    private sealed record LiveAccess(
        int ContractVersion, string? SourceToolKey, Guid ActorUserId,
        Guid CharacterId, string? CharacterName, string? Status,
        DateTimeOffset? ArchivedAt, IReadOnlyList<Guid>? CampaignIds,
        bool AllowsOrdinaryEditing);
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<SiteCharacterAccessResult> GetFreshAuthorizedCharacterAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty)
            return new(SiteCharacterAccessStatus.NotFoundOrNotOwned);
        var context = contextAccessor.HttpContext;
        var auth = context is null ? null :
            HostedToolAuthenticationMiddleware.GetAuthenticationContext(context);
        if (auth is null ||
            !string.Equals(auth.ToolSlug, "character-sheet", StringComparison.Ordinal) ||
            !string.Equals(auth.SiteMode, "dorks-and-dice", StringComparison.Ordinal) ||
            !Guid.TryParse(auth.User.Id, out var actorId) ||
            actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(auth.DelegationCapability))
            return new(SiteCharacterAccessStatus.Unauthenticated);
        if (client.BaseAddress is null)
            return new(SiteCharacterAccessStatus.ProjectionUnavailable);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/tool-host/registrations/character-sheet/api/characters/{characterId:D}/fresh-access");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", auth.DelegationCapability);
        request.Headers.Accept.ParseAdd("application/json");
        try
        {
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(SiteCharacterAccessStatus.NotFoundOrNotOwned);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(SiteCharacterAccessStatus.Unauthenticated);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > 8192)
                return new(SiteCharacterAccessStatus.ProjectionUnavailable);

            var live = await response.Content.ReadFromJsonAsync<LiveAccess>(Wire, cancellationToken);
            if (live is null ||
                live.ContractVersion != 1 ||
                live.SourceToolKey != "character-sheet" ||
                live.ActorUserId != actorId ||
                live.CharacterId != characterId ||
                string.IsNullOrWhiteSpace(live.CharacterName) ||
                live.CampaignIds is null ||
                live.CampaignIds.Any(id => id == Guid.Empty) ||
                live.CampaignIds.Distinct().Count() != live.CampaignIds.Count)
                return new(SiteCharacterAccessStatus.ProjectionUnavailable);

            var lifecycle = live.Status switch
            {
                "Active" when live.ArchivedAt is null && live.AllowsOrdinaryEditing =>
                    SiteCharacterLifecycleState.Active,
                "Archived" when live.ArchivedAt is not null && !live.AllowsOrdinaryEditing =>
                    SiteCharacterLifecycleState.Archived,
                _ => (SiteCharacterLifecycleState?)null
            };
            if (lifecycle is null)
                return new(SiteCharacterAccessStatus.ProjectionUnavailable);
            return SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                characterId, live.CharacterName.Trim(), lifecycle.Value,
                live.ArchivedAt, live.CampaignIds));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SiteCharacterAccessStatus.ProjectionUnavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            or NotSupportedException or InvalidOperationException)
        {
            return new(SiteCharacterAccessStatus.ProjectionUnavailable);
        }
    }
}
