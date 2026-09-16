using System.Net;
using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterSheet.IntegrationTests;

public sealed class LifecycleEndpointAuthenticationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public LifecycleEndpointAuthenticationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MissingLifecycleAuthenticationIsRejected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.PostAsync("/api/lifecycle/events", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidLifecycleTicketIsRejected()
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IToolLifecycleIntrospectionClient>();
            services.AddSingleton<IToolLifecycleIntrospectionClient>(new NullLifecycleClient());
        }));
        using var client = host.CreateClient();
        using var request = LifecycleRequest("invalid-ticket");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LifecycleIntrospectionPathMustBeFixedCharacterSheetRoute()
    {
        using var host = _factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IToolLifecycleIntrospectionClient>();
            services.AddSingleton<IToolLifecycleIntrospectionClient>(new ThrowingLifecycleClient());
        }));
        using var client = host.CreateClient();
        using var request = LifecycleRequest("ticket", "/tool-host/rules-core/api/lifecycle/introspect");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage LifecycleRequest(
        string ticket,
        string introspectionPath = DorksAndDiceToolLifecycleIntrospectionClient.ExpectedIntrospectionPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/lifecycle/events");
        request.Headers.Add(ToolLifecycleHeaders.Ticket, ticket);
        request.Headers.Add(ToolLifecycleHeaders.IntrospectionPath, introspectionPath);
        return request;
    }

    private sealed class NullLifecycleClient : IToolLifecycleIntrospectionClient
    {
        public Task<ToolLifecycleContext?> RedeemAsync(
            string ticket,
            string introspectionPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolLifecycleContext?>(null);
    }

    private sealed class ThrowingLifecycleClient : IToolLifecycleIntrospectionClient
    {
        public Task<ToolLifecycleContext?> RedeemAsync(
            string ticket,
            string introspectionPath,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This client must not be called for a wrong route.");
    }
}
