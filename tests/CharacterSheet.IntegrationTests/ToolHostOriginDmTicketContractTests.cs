using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Web;
using Microsoft.AspNetCore.Http;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Redeems the published Character Sheet contract-v1 Site introspection
/// response through the actual HTTP authentication client and middleware.
/// This verifies the consumer boundary; it is NOT a running Site instance,
/// a reusable DM approval, or proof of an authorized character owner.
/// </summary>
public sealed class ToolHostOriginDmTicketContractTests
{
    private static readonly Guid Actor =
        Guid.Parse("960ef100-8e6b-451c-88ef-dee0b921ae6e");
    private static readonly Guid Campaign =
        Guid.Parse("d50681b4-8339-4df2-8b27-2c13ce1b937d");
    private static readonly Guid Character =
        Guid.Parse("3540e56f-a90f-4d32-bb78-a5843f568281");

    [Theory]
    [InlineData("current", SiteOriginDmAccessStatus.Authorized)]
    [InlineData("missing-scopes", SiteOriginDmAccessStatus.ProjectionUnavailable)]
    [InlineData("empty-scopes", SiteOriginDmAccessStatus.NotAuthorized)]
    [InlineData("unrelated-character", SiteOriginDmAccessStatus.NotAuthorized)]
    [InlineData("other-campaign", SiteOriginDmAccessStatus.NotAuthorized)]
    [InlineData("demoted-dm", SiteOriginDmAccessStatus.NotAuthorized)]
    [InlineData("wrong-site-mode", SiteOriginDmAccessStatus.ProjectionUnavailable)]
    public async Task RedeemedSiteTicketRequiresBothCurrentDmRoleAndExactCharacterAssociation(
        string state, SiteOriginDmAccessStatus expected)
    {
        var json = SiteWireResponse();
        switch (state)
        {
            case "missing-scopes":
                json.AsObject().Remove("dmCharacterScopes");
                break;
            case "empty-scopes":
                json["dmCharacterScopes"] = new JsonArray();
                break;
            case "unrelated-character":
                json["dmCharacterScopes"]![0]!["characterId"] = Guid.NewGuid();
                break;
            case "other-campaign":
                json["campaigns"]![0]!["id"] = Guid.NewGuid();
                break;
            case "demoted-dm":
                // A stale source ticket must not turn a current Player role
                // into an Origin-approving DM even when a target pair exists.
                json["campaigns"]![0]!["role"] = "Player";
                break;
            case "wrong-site-mode":
                json["siteMode"] = "professional";
                break;
        }

        var result = await RedeemAndCheckAsync(json);
        Assert.Equal(expected, result.Status);
        if (expected == SiteOriginDmAccessStatus.Authorized)
        {
            Assert.Equal(Actor, result.ActorUserId);
            Assert.Equal(Campaign, result.CampaignId);
            Assert.Equal(Character, result.CharacterId);
        }
        else
        {
            Assert.Null(result.ActorUserId);
            Assert.Null(result.CampaignId);
            Assert.Null(result.CharacterId);
        }
    }

    [Fact]
    public async Task FreshReintrospectionDoesNotReusePreviouslyAuthorizedDmScope()
    {
        var authorized = await RedeemAndCheckAsync(SiteWireResponse());
        Assert.Equal(SiteOriginDmAccessStatus.Authorized, authorized.Status);

        var revoked = SiteWireResponse();
        revoked["campaigns"]![0]!["role"] = "Player";
        revoked["dmCharacterScopes"] = new JsonArray();
        var denied = await RedeemAndCheckAsync(revoked);
        Assert.Equal(SiteOriginDmAccessStatus.NotAuthorized, denied.Status);
        Assert.Null(denied.ActorUserId);
    }

    private static JsonNode SiteWireResponse() =>
        JsonNode.Parse(JsonSerializer.Serialize(new
        {
            contractVersion = 1,
            toolSlug = "character-sheet",
            siteMode = "dorks-and-dice",
            user = new { id = Actor.ToString("D"), displayName = "Site DM" },
            globalRoles = Array.Empty<string>(),
            campaigns = new[]
            {
                new { id = Campaign, name = "Current Campaign", role = "DM" }
            },
            characters = Array.Empty<object>(),
            dmCharacterScopes = new[]
            {
                new { campaignId = Campaign, characterId = Character }
            }
        }))!;

    private static async Task<SiteOriginDmAccessResult> RedeemAndCheckAsync(JsonNode json)
    {
        using var http = new HttpClient(new SiteIntrospectionHandler(json.ToJsonString()))
        {
            BaseAddress = new Uri("https://site.test")
        };
        var authenticator = new DorksAndDiceToolHostAuthenticationClient(http);
        var request = new DefaultHttpContext();
        request.Request.Headers[ToolHostAuthenticationHeaders.Ticket] = "test-site-ticket";
        request.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath] =
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath;
        SiteOriginDmAccessResult? checkedScope = null;
        var middleware = new HostedToolAuthenticationMiddleware(async context =>
        {
            checkedScope = await new ToolHostOriginDmAccessGateway(
                new HttpContextAccessor { HttpContext = context })
                .CheckAsync(Campaign, Character);
        });
        await middleware.InvokeAsync(request, authenticator);
        Assert.NotNull(checkedScope);
        return checkedScope;
    }

    private sealed class SiteIntrospectionHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath,
                request.RequestUri?.AbsolutePath);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "test-site-ticket"),
                request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
