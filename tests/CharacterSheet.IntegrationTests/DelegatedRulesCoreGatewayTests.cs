using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CharacterSheet.IntegrationTests;

public sealed class DelegatedRulesCoreGatewayTests
{
    [Fact]
    public async Task UsesStableConceptResolutionAndServerIssuedDelegationWithoutForwardingToolHeaders()
    {
        var handler = new RecordingHandler(request =>
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            if (path.EndsWith("/api/rules/feat:alert", StringComparison.Ordinal))
            {
                return Ok(Rule("feat:alert", "feat", "Alert"));
            }

            if (path.EndsWith("/api/rules/position.acquisitions-documancer", StringComparison.Ordinal))
            {
                return Ok(Rule(
                    "position.acquisitions-documancer",
                    "charoption",
                    "Documancer"));
            }

            if (path.EndsWith("/api/rules/missing:rule", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.EndsWith("/api/rules/mechanics", StringComparison.Ordinal)
                && request.Method == HttpMethod.Get)
            {
                return Ok(new RulesCoreMechanicsCatalogView(
                    "global",
                    null,
                    1,
                    DateTimeOffset.UtcNow,
                    []));
            }

            if (path.EndsWith("/api/rules/mechanics/evaluate", StringComparison.Ordinal)
                && request.Method == HttpMethod.Post)
            {
                return Ok(new RulesCoreMechanicsBatchEvaluationView(
                    "global",
                    null,
                    1,
                    DateTimeOffset.UtcNow,
                    []));
            }

            throw new InvalidOperationException($"Unexpected delegated request: {request.Method} {path}");
        });
        var httpContext = await AuthenticatedContextAsync(withDelegation: true);
        var gateway = new DelegatedRulesCoreGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.example") },
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<DelegatedRulesCoreGateway>.Instance);

        var resolved = await gateway.ResolveGlobalRulesAsync(
            ["feat:alert", "position.acquisitions-documancer", "missing:rule"]);
        await gateway.GetGlobalMechanicsAsync();
        await gateway.EvaluateGlobalMechanicsAsync(new RulesCoreMechanicsBatchEvaluationRequest([]));

        Assert.Equal(2, resolved.Count);
        Assert.Equal("feat", resolved["feat:alert"].EntityType);
        Assert.Equal("charoption", resolved["position.acquisitions-documancer"].EntityType);
        Assert.False(resolved.ContainsKey("missing:rule"));
        Assert.Equal(5, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(
                new AuthenticationHeaderValue("Bearer", "ddtd_v1_test-capability"),
                request.Authorization);
            Assert.False(request.HasToolTicket);
            Assert.False(request.HasToolIntrospectionPath);
            Assert.False(request.HasDelegationHeader);
            Assert.True(
                request.Uri.AbsolutePath.StartsWith(
                    "/tool-host/character-sheet/api/delegate/rules-core/upstream/api/",
                    StringComparison.Ordinal));
        });
        Assert.DoesNotContain(handler.Requests, request =>
            request.Uri.Query.Contains("entityType=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDelegationCapabilityFailsBeforeAnyRulesCoreRequest()
    {
        var handler = new RecordingHandler(_ =>
            throw new InvalidOperationException("The gateway must fail before transport."));
        var httpContext = await AuthenticatedContextAsync(withDelegation: false);
        var gateway = new DelegatedRulesCoreGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://site.example") },
            new HttpContextAccessor { HttpContext = httpContext },
            NullLogger<DelegatedRulesCoreGateway>.Instance);

        await Assert.ThrowsAsync<RulesCoreGatewayException>(() => gateway.GetGlobalMechanicsAsync());

        Assert.Empty(handler.Requests);
    }

    private static async Task<DefaultHttpContext> AuthenticatedContextAsync(bool withDelegation)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[ToolHostAuthenticationHeaders.Ticket] = "source-tool-ticket";
        httpContext.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath] =
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath;
        var authenticationContext = new ToolHostAuthenticationContext(
            1,
            "character-sheet",
            "dorks-and-dice",
            new ToolHostUserContext("site-user-1", "Owner"),
            [],
            [],
            []);
        if (withDelegation)
        {
            authenticationContext = authenticationContext with
            {
                DelegationCapability = "ddtd_v1_test-capability",
                DelegationPath = "/tool-host/character-sheet/api/delegate/{targetSlug}/upstream"
            };
        }

        var middleware = new HostedToolAuthenticationMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(
            httpContext,
            new StaticAuthenticationClient(authenticationContext));
        Assert.NotNull(HostedToolAuthenticationMiddleware.GetAuthenticationContext(httpContext));
        return httpContext;
    }

    private static RulesCoreResolvedRuleSummaryView Rule(
        string conceptKey,
        string entityType,
        string displayName) =>
        new(
            conceptKey,
            entityType,
            displayName,
            1,
            displayName,
            "SRD",
            "fixture",
            "Fixture",
            "5e",
            "5e");

    private static HttpResponseMessage Ok<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class StaticAuthenticationClient(ToolHostAuthenticationContext context)
        : IToolHostAuthenticationClient
    {
        public Task<ToolHostAuthenticationContext?> RedeemAsync(
            string ticket,
            string introspectionPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolHostAuthenticationContext?>(context);
    }

    private sealed record RecordedRequest(
        Uri Uri,
        AuthenticationHeaderValue? Authorization,
        bool HasToolTicket,
        bool HasToolIntrospectionPath,
        bool HasDelegationHeader);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Headers.Authorization,
                request.Headers.Contains(ToolHostAuthenticationHeaders.Ticket),
                request.Headers.Contains(ToolHostAuthenticationHeaders.IntrospectionPath),
                request.Headers.Contains(ToolHostAuthenticationHeaders.DelegationCapability)));
            return Task.FromResult(responder(request));
        }
    }
}
