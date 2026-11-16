using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Site;
using CharacterSheet.Web;
using Microsoft.AspNetCore.Http;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// The DM identity, campaign and active character association must come from
/// Site's live backend query, never the Tool Host's captured DM role list.
/// </summary>
public sealed class ToolHostFreshOriginDmAccessGatewayTests
{


    private sealed class DelayedContent(bool slowDrip) : HttpContent
    {
        // An HTTP 200 response with complete headers can stall indefinitely
        // after zero or one body byte without exceeding the byte budget.
        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken token)
        {
            if (slowDrip)
                await stream.WriteAsync(new byte[] { (byte)'{' }, token);
            // Twelve seconds is intentionally far beyond the gateway deadline,
            // but keeps the test harness bounded if a regression ignores
            // cancellation (so CI can report a failure instead of hanging).
            await Task.Delay(TimeSpan.FromSeconds(12), token);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class InterruptedContent : HttpContent
    {
        // Simulates a Site response that reports HTTP 200 but disconnects
        // during body buffering, rather than a response-status failure.
        protected override Task SerializeToStreamAsync(
            Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("Backend Site response interrupted"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static readonly Guid Actor =
        Guid.Parse("b8fe820c-8e4d-47d4-b687-8c77e983f9d3");
    private static readonly Guid Campaign =
        Guid.Parse("d693de77-08ee-4f15-b1b0-7f2cb4d22321");
    private static readonly Guid Character =
        Guid.Parse("fe20dff9-e05f-4a38-b5c1-a5d66a7c2841");

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
            Assert.Equal(
                $"/tool-host/registrations/character-sheet/api/campaigns/{Campaign:D}/characters/{Character:D}/fresh-dm-access",
                request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("ddtd_v1_backend-only", request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains(ToolHostAuthenticationHeaders.Ticket));
            Assert.False(request.Headers.Contains(ToolHostAuthenticationHeaders.IntrospectionPath));
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage LiveResponse(object body) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };

    private static object Live(
        Guid? actor = null, Guid? campaign = null, Guid? character = null,
        bool authorized = true) => new
        {
            contractVersion = 1,
            sourceToolKey = "character-sheet",
            actorUserId = actor ?? Actor,
            campaignId = campaign ?? Campaign,
            characterId = character ?? Character,
            hasActiveDmScope = authorized
        };

    private static async Task<DefaultHttpContext> ContextAsync(bool delegation)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[ToolHostAuthenticationHeaders.Ticket] = "source-ticket";
        context.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath] =
            "/tool-host/character-sheet/api/introspect";
        var auth = new ToolHostAuthenticationContext(
            1, "character-sheet", "dorks-and-dice",
            new ToolHostUserContext(Actor.ToString("D"), "DM actor"), [], [], [])
        {
            DelegationCapability = delegation ? "ddtd_v1_backend-only" : null
        };
        await new HostedToolAuthenticationMiddleware(_ => Task.CompletedTask)
            .InvokeAsync(context, new StaticAuth(auth));
        Assert.NotNull(HostedToolAuthenticationMiddleware.GetAuthenticationContext(context));
        return context;
    }

    [Fact]
    public async Task LiveDmAuthorityUsesServerCredentialAndChecksExactScope()
    {
        var handler = new Handler(_ => LiveResponse(Live()));
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });
        var allowed = await gateway.CheckFreshAsync(Campaign, Character);
        Assert.Equal(SiteOriginDmAccessStatus.Authorized, allowed.Status);
        Assert.Equal(Actor, allowed.ActorUserId);
        Assert.Equal(Campaign, allowed.CampaignId);
        Assert.Equal(Character, allowed.CharacterId);
        Assert.Equal(1, handler.Requests);

        var denied = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(false) });
        Assert.Equal(SiteOriginDmAccessStatus.Unauthenticated,
            (await denied.CheckFreshAsync(Campaign, Character)).Status);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task UnlengthenedOversizedLiveDmReplyCannotAuthorize()
    {
        // The extra field preserves a valid authorization reply while
        // exceeding the bounded backend response contract without a length.
        var oversized = LiveResponse(new
        {
            contractVersion = 1,
            sourceToolKey = "character-sheet",
            actorUserId = Actor,
            campaignId = Campaign,
            characterId = Character,
            hasActiveDmScope = true,
            padding = new string('x', 5000)
        });
        oversized.Content.Headers.ContentLength = null;
        var handler = new Handler(_ => oversized);
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });

        var denied = await gateway.CheckFreshAsync(Campaign, Character);
        Assert.Equal(SiteOriginDmAccessStatus.ProjectionUnavailable, denied.Status);
        Assert.Null(denied.ActorUserId);
        Assert.Equal(1, handler.Requests);
    }



    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StalledAndSlowDripDmBodiesFailClosedWithinDeadline(bool slowDrip)
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DelayedContent(slowDrip)
        });
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });

        var result = await gateway.CheckFreshAsync(Campaign, Character)
            .WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(SiteOriginDmAccessStatus.ProjectionUnavailable, result.Status);
        Assert.Null(result.ActorUserId);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationIsNotMisreportedAsDmUnavailability()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new DelayedContent(false)
        });
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.CheckFreshAsync(Campaign, Character, caller.Token)
                .WaitAsync(TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public async Task BackendDmBodyIoFailureIsUnavailableNotCachedAuthorization()
    {
        var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new InterruptedContent()
        });
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });

        var result = await gateway.CheckFreshAsync(Campaign, Character);
        Assert.Equal(SiteOriginDmAccessStatus.ProjectionUnavailable, result.Status);
        Assert.Null(result.ActorUserId);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(401, SiteOriginDmAccessStatus.Unauthenticated)]
    [InlineData(403, SiteOriginDmAccessStatus.Unauthenticated)]
    [InlineData(404, SiteOriginDmAccessStatus.NotAuthorized)]
    [InlineData(503, SiteOriginDmAccessStatus.ProjectionUnavailable)]
    public async Task SiteDmDenialsNeverFallBackToCachedScope(
        int status, SiteOriginDmAccessStatus expected)
    {
        var handler = new Handler(_ => new HttpResponseMessage((HttpStatusCode)status));
        var gateway = new ToolHostFreshOriginDmAccessGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.test") },
            new HttpContextAccessor { HttpContext = await ContextAsync(true) });
        Assert.Equal(expected,
            (await gateway.CheckFreshAsync(Campaign, Character)).Status);
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task ForgedScopeOrActorDoesNotAuthorizeOriginReview()
    {
        foreach (var forged in new[]
        {
            Live(actor: Guid.NewGuid()),
            Live(campaign: Guid.NewGuid()),
            Live(character: Guid.NewGuid()),
            Live(authorized: false)
        })
        {
            var gateway = new ToolHostFreshOriginDmAccessGateway(
                new HttpClient(new Handler(_ => LiveResponse(forged)))
                {
                    BaseAddress = new Uri("https://site.test")
                },
                new HttpContextAccessor { HttpContext = await ContextAsync(true) });
            var denied = await gateway.CheckFreshAsync(Campaign, Character);
            Assert.Equal(SiteOriginDmAccessStatus.ProjectionUnavailable, denied.Status);
            Assert.Null(denied.ActorUserId);
        }
    }
}
