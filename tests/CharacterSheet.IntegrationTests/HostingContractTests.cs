using System.Net;
using System.Net.Http.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Infrastructure.Hosting;

namespace CharacterSheet.IntegrationTests;

public sealed class HostingContractTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HostingContractTests(PostgresWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthAndReadinessAreAvailable()
    {
        var health = await _client.GetAsync("/health");
        var ready = await _client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var payload = await ready.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("ready", payload!["status"]);
        Assert.Equal("postgresql-ready", payload["persistence"]);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/new")]
    [InlineData("/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7")]
    public async Task StandaloneRoutesServeTheDevelopmentShell(string path)
    {
        var response = await _client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"tool-root\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/app.js\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuiltEmbeddedModuleAssetIsServed()
    {
        var response = await _client.GetAsync("/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "javascript",
            response.Content.Headers.ContentType?.MediaType ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartialHostedAuthenticationHeadersAreRejectedWithoutCreatingASession()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api");
        request.Headers.Add(ToolHostAuthenticationHeaders.Ticket, "ticket-only");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthenticationContractIsPinnedToCharacterSheet()
    {
        Assert.Equal("character-sheet", DorksAndDiceToolHostAuthenticationClient.ExpectedToolSlug);
        Assert.Equal(
            "/tool-host/character-sheet/api/introspect",
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);
    }
}
