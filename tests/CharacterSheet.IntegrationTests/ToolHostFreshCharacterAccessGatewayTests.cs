using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;
using CharacterSheet.Web;
using Microsoft.AspNetCore.Http;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Only a backend-issued Site capability may initiate a live ownership query.
/// The response must agree with its authenticated actor, target and lifecycle.
/// </summary>
public sealed class ToolHostFreshCharacterAccessGatewayTests
{
    private sealed class StaticAuth(ToolHostAuthenticationContext context)
        : IToolHostAuthenticationClient
    {
        public Task<ToolHostAuthenticationContext?> RedeemAsync(
            string ticket, string introspectionPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolHostAuthenticationContext?>(context);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.StartsWith(
                "/tool-host/registrations/character-sheet/api/characters/",
                request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("ddtd_v1_server-only", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains(ToolHostAuthenticationHeaders.Ticket));
            Assert.False(request.Headers.Contains(ToolHostAuthenticationHeaders.IntrospectionPath));
            return Task.FromResult(answer(request));
        }
    }

    private static readonly Guid Actor =
        Guid.Parse("aad2a27f-e596-4f67-b2fc-7b40b68043b9");
    private static readonly Guid Character =
        Guid.Parse("aacfc832-e39d-4089-aa83-e96ea360e18c");

    private static HttpResponseMessage LiveResponse(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static object Live(
        Guid? actor = null, Guid? character = null, string status = "Active",
        DateTimeOffset? archivedAt = null, bool edit = true,
        Guid[]? campaigns = null) => new
    {
        contractVersion = 1,
        sourceToolKey = "character-sheet",
        actorUserId = actor ?? Actor,
        characterId = character ?? Character,
        characterName = "Owner character",
        status, archivedAt,
        campaignIds = campaigns ?? Array.Empty<Guid>(),
        allowsOrdinaryEditing = edit
    };

    private static async Task<DefaultHttpContext> ContextAsync(bool delegated)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ToolHostAuthenticationHeaders.Ticket] = "source-ticket";
        context.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath] =
            "/tool-host/character-sheet/api/introspect";
        var auth = new ToolHostAuthenticationContext(
            1, "character-sheet", "dorks-and-dice",
            new ToolHostUserContext(Actor.ToString("D"), "Owner"), [], [], [])
        {
            DelegationCapability = delegated ? "ddtd_v1_server-only" : null
        };
        await new HostedToolAuthenticationMiddleware(_ => Task.CompletedTask)
            .InvokeAsync(context, new StaticAuth(auth));
        Assert.NotNull(HostedToolAuthenticationMiddleware.GetAuthenticationContext(context));
        return context;
    }

    [Fact]
    public async Task CurrentSiteAccessUsesOnlyServerDelegationAndStrictIdentity()
    {
        var campaign = Guid.NewGuid();
        var handler = new Handler(_ => LiveResponse(Live(campaigns: [campaign])));
        var context = await ContextAsync(true);
        var gateway = new ToolHostFreshCharacterAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = context });
        var result = await gateway.GetFreshAuthorizedCharacterAsync(Character);
        Assert.Equal(SiteCharacterAccessStatus.Authorized, result.Status);
        Assert.Equal(Character, result.Character!.CharacterId);
        Assert.True(result.Character.AllowsOrdinaryEditingByLifecycle);
        Assert.Equal(campaign, Assert.Single(result.Character.CampaignIds));
        Assert.Equal(1, handler.Requests);

        var missing = await ContextAsync(false);
        var denied = new ToolHostFreshCharacterAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = missing });
        Assert.Equal(SiteCharacterAccessStatus.Unauthenticated,
            (await denied.GetFreshAuthorizedCharacterAsync(Character)).Status);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task UnlengthenedOversizedLiveOwnerReplyCannotAuthorize()
    {
        // A valid owner JSON object with an unknown extra field would be
        // accepted by the normal deserializer unless the actual body is bounded.
        var oversized = LiveResponse(new
        {
            contractVersion = 1,
            sourceToolKey = "character-sheet",
            actorUserId = Actor,
            characterId = Character,
            characterName = "Owner character",
            status = "Active",
            archivedAt = (DateTimeOffset?)null,
            campaignIds = Array.Empty<Guid>(),
            allowsOrdinaryEditing = true,
            padding = new string('x', 9000)
        });
        oversized.Content.Headers.ContentLength = null;
        var handler = new Handler(_ => oversized);
        var gateway = new ToolHostFreshCharacterAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });

        var denied = await gateway.GetFreshAuthorizedCharacterAsync(Character);
        Assert.Equal(SiteCharacterAccessStatus.ProjectionUnavailable, denied.Status);
        Assert.Null(denied.Character);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(401, SiteCharacterAccessStatus.Unauthenticated)]
    [InlineData(403, SiteCharacterAccessStatus.Unauthenticated)]
    [InlineData(404, SiteCharacterAccessStatus.NotFoundOrNotOwned)]
    [InlineData(503, SiteCharacterAccessStatus.ProjectionUnavailable)]
    public async Task SiteRejectionsDoNotFallbackToCachedIntrospection(
        int status, SiteCharacterAccessStatus expected)
    {
        var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)status));
        var gateway = new ToolHostFreshCharacterAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });
        Assert.Equal(expected,
            (await gateway.GetFreshAuthorizedCharacterAsync(Character)).Status);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task HostReplyCannotSubstituteAnotherActorOrCharacter()
    {
        var bad = new[]
        {
            Live(actor: Guid.NewGuid()),
            Live(character: Guid.NewGuid()),
            Live(status: "Archived"),
            Live(status: "Archived", archivedAt: DateTimeOffset.UtcNow, edit: true),
            Live(campaigns: [Guid.NewGuid(), Guid.Empty]),
            Live(campaigns: [Actor, Actor])
        };
        foreach (var body in bad)
        {
            var gateway = new ToolHostFreshCharacterAccessGateway(
                new HttpClient(new Handler(_ => LiveResponse(body)))
                {
                    BaseAddress = new Uri("https://site.test")
                },
                new HttpContextAccessor { HttpContext = await ContextAsync(true) });
            var result = await gateway.GetFreshAuthorizedCharacterAsync(Character);
            Assert.Equal(SiteCharacterAccessStatus.ProjectionUnavailable, result.Status);
            Assert.Null(result.Character);
        }
    }
}
