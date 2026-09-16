using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Infrastructure.Hosting;

namespace CharacterSheet.IntegrationTests;

public sealed class ToolLifecycleIntrospectionClientTests
{
    [Fact]
    public async Task RedeemsLifecycleTicketAgainstFixedCharacterSheetRoute()
    {
        var context = LifecycleContext(ToolLifecycleEventTypes.CharacterDeleted);
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(context)
        });
        var client = new DorksAndDiceToolLifecycleIntrospectionClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://site.example")
        });

        var result = await client.RedeemAsync(
            "lifecycle-ticket",
            DorksAndDiceToolLifecycleIntrospectionClient.ExpectedIntrospectionPath);

        Assert.Equal(context, result);
        Assert.Equal(
            new Uri("https://site.example/tool-host/character-sheet/api/lifecycle/introspect"),
            handler.RequestUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "lifecycle-ticket"), handler.Authorization);
    }

    [Fact]
    public async Task RejectsLifecycleIntrospectionPathForAnotherTool()
    {
        var client = new DorksAndDiceToolLifecycleIntrospectionClient(new HttpClient(new RecordingHandler(
            new HttpResponseMessage(HttpStatusCode.OK)))
        {
            BaseAddress = new Uri("https://site.example")
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RedeemAsync("ticket", "/tool-host/rules-core/api/lifecycle/introspect"));
    }

    [Fact]
    public async Task RejectsUnsupportedLifecycleEventContext()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(LifecycleContext("future.event"))
        });
        var client = new DorksAndDiceToolLifecycleIntrospectionClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://site.example")
        });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.RedeemAsync(
                "ticket",
                DorksAndDiceToolLifecycleIntrospectionClient.ExpectedIntrospectionPath));
    }

    [Fact]
    public async Task UnauthorizedRedemptionReturnsNoLifecycleContext()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = new DorksAndDiceToolLifecycleIntrospectionClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://site.example")
        });

        var result = await client.RedeemAsync(
            "invalid-ticket",
            DorksAndDiceToolLifecycleIntrospectionClient.ExpectedIntrospectionPath);

        Assert.Null(result);
    }

    private static ToolLifecycleContext LifecycleContext(string eventType) => new(
        1,
        "character-sheet",
        Guid.NewGuid(),
        eventType,
        Guid.NewGuid(),
        DateTimeOffset.UtcNow);

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
