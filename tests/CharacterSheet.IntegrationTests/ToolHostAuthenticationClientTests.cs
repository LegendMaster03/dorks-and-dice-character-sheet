using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Infrastructure.Hosting;

namespace CharacterSheet.IntegrationTests;

public sealed class ToolHostAuthenticationClientTests
{
    [Fact]
    public async Task RedeemsTicketAgainstFixedCharacterSheetIntrospectionPath()
    {
        var context = new ToolHostAuthenticationContext(
            1,
            "character-sheet",
            "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Test User"),
            [],
            []);
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(context)
        });
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://site.example")
        };
        var client = new DorksAndDiceToolHostAuthenticationClient(httpClient);

        var result = await client.RedeemAsync(
            "ticket-123",
            DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);

        Assert.NotNull(result);
        Assert.Equal("character-sheet", result.ToolSlug);
        Assert.Equal(
            new Uri("https://site.example/tool-host/character-sheet/api/introspect"),
            handler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "ticket-123"), handler.Authorization);
    }

    [Fact]
    public async Task RejectsIntrospectionPathForAnotherTool()
    {
        var client = new DorksAndDiceToolHostAuthenticationClient(new HttpClient(new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://site.example")
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RedeemAsync("ticket-123", "/tool-host/rules-core/api/introspect"));
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(response);
        }
    }
}
