using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Web;

/// <summary>
/// Queries current Site campaign DM membership and active character association
/// through a registration-scoped backend capability. The original Tool Host
/// DM scope is never accepted as current proof, and no fallback is permitted.
/// </summary>
public sealed class ToolHostFreshOriginDmAccessGateway(
    HttpClient client, IHttpContextAccessor contextAccessor)
    : ISiteFreshOriginDmAccessGateway
{
    private sealed record LiveDmAccess(
        int ContractVersion, string? SourceToolKey, Guid ActorUserId,
        Guid CampaignId, Guid CharacterId, bool HasActiveDmScope);
    private static readonly JsonSerializerOptions Wire =
        new(JsonSerializerDefaults.Web);

    public async Task<SiteOriginDmAccessResult> CheckFreshAsync(
        Guid campaignId, Guid characterId,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty || characterId == Guid.Empty)
            return new(SiteOriginDmAccessStatus.NotAuthorized);

        var context = contextAccessor.HttpContext;
        var auth = context is null ? null :
            HostedToolAuthenticationMiddleware.GetAuthenticationContext(context);
        if (auth is null ||
            auth.ToolSlug != "character-sheet" ||
            auth.SiteMode != "dorks-and-dice" ||
            !Guid.TryParse(auth.User.Id, out var actorId) ||
            actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(auth.DelegationCapability))
            return new(SiteOriginDmAccessStatus.Unauthenticated);
        if (client.BaseAddress is null)
            return new(SiteOriginDmAccessStatus.ProjectionUnavailable);

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/tool-host/registrations/character-sheet/api/campaigns/{campaignId:D}/characters/{characterId:D}/fresh-dm-access");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", auth.DelegationCapability);
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            // ResponseHeadersRead ends HttpClient's timeout at the headers.
            // Cover headers, streamed body buffering and JSON parsing under
            // one independent deadline, regardless of caller token lifetime.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(SiteOriginDmAccessStatus.NotAuthorized);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new(SiteOriginDmAccessStatus.Unauthenticated);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > 4096)
                return new(SiteOriginDmAccessStatus.ProjectionUnavailable);

            // Content-Length is optional. Bound streamed replies as well as
            // replies with a declared size before accepting live DM scope.
            await response.Content.LoadIntoBufferAsync(4096, deadline.Token);
            var live = await response.Content.ReadFromJsonAsync<LiveDmAccess>(
                Wire, deadline.Token);
            if (live is null ||
                live.ContractVersion != 1 ||
                live.SourceToolKey != "character-sheet" ||
                live.ActorUserId != actorId ||
                live.CampaignId != campaignId ||
                live.CharacterId != characterId ||
                !live.HasActiveDmScope)
                return new(SiteOriginDmAccessStatus.ProjectionUnavailable);

            return new(SiteOriginDmAccessStatus.Authorized,
                actorId, campaignId, characterId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SiteOriginDmAccessStatus.ProjectionUnavailable);
        }
        catch (Exception error) when (error is HttpRequestException or JsonException
            or NotSupportedException or InvalidOperationException or IOException)
        {
            return new(SiteOriginDmAccessStatus.ProjectionUnavailable);
        }
    }
}
