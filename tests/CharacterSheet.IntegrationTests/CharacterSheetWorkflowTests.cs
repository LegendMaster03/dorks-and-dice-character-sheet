using System.Net;
using System.Net.Http.Json;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterSheetWorkflowTests
{
    [Fact]
    public async Task OwnedActiveBasicCharacterGetDoesNotCreateRichSheetAndPreservesCampaignIds()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();
        var firstCampaignId = Guid.NewGuid();
        var secondCampaignId = Guid.NewGuid();
        factory.Context = CreateContext(
            characters:
            [
                Character(characterId, "Basic Hero", "Active", null, firstCampaignId, secondCampaignId)
            ]);

        using var response = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{characterId:D}/sheet");
        var payload = await response.Content.ReadFromJsonAsync<CharacterSheetBootstrapView>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal(characterId, payload.CharacterId);
        Assert.Equal("Basic Hero", payload.Name);
        Assert.Equal("Active", payload.Lifecycle);
        Assert.Equal(new[] { firstCampaignId, secondCampaignId }, payload.CampaignIds);
        Assert.False(payload.HasRichSheet);
        Assert.Null(payload.Sheet);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(0, await db.CharacterSheets.CountAsync());
    }

    [Fact]
    public async Task ExplicitCreationInitializesExistingCharacterIdIdempotently()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = CreateContext(characters: [Character(characterId, "Rich Hero", "Active")]);

        using var firstResponse = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet");
        using var secondResponse = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet");
        var first = await firstResponse.Content.ReadFromJsonAsync<CharacterSheetBootstrapView>();
        var second = await secondResponse.Content.ReadFromJsonAsync<CharacterSheetBootstrapView>();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first.HasRichSheet);
        Assert.True(second.HasRichSheet);
        Assert.Equal(characterId, first.CharacterId);
        Assert.Equal(characterId, second.CharacterId);
        Assert.Equal(first.Sheet!.CreatedAt, second.Sheet!.CreatedAt);
        Assert.Equal("BuildInProgress", first.Sheet.BuilderStatus);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        var stored = Assert.Single(await db.CharacterSheets.AsNoTracking().ToListAsync());
        Assert.Equal(characterId, stored.CharacterId);
    }

    [Fact]
    public async Task ArchivedCharacterCanBeReadButCanNotInitializeAndExistingRichStateIsPreserved()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = CreateContext(characters: [Character(characterId, "Archived Hero", "Active")]);

        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        var archivedAt = DateTimeOffset.UtcNow;
        factory.Context = CreateContext(characters: [Character(characterId, "Archived Hero", "Archived", archivedAt)]);

        using var getResponse = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{characterId:D}/sheet");
        var get = await getResponse.Content.ReadFromJsonAsync<CharacterSheetBootstrapView>();
        using var postResponse = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.NotNull(get);
        Assert.Equal("Archived", get.Lifecycle);
        Assert.Equal(archivedAt, get.ArchivedAt);
        Assert.True(get.HasRichSheet);
        Assert.Equal(HttpStatusCode.Conflict, postResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(1, await db.CharacterSheets.CountAsync());
    }

    [Fact]
    public async Task ArchivedBasicCharacterCanNotInitializeRichSheet()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = CreateContext(characters:
        [
            Character(characterId, "Archived Basic Hero", "Archived", DateTimeOffset.UtcNow)
        ]);

        using var response = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(0, await db.CharacterSheets.CountAsync());
    }

    [Fact]
    public async Task LocalOrphanAndDmCampaignRoleDoNotBypassOwnerProjection()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var orphanCharacterId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        factory.Context = CreateContext(
            campaigns: [new ToolHostCampaignContext(campaignId, "DM Campaign", "DM")],
            characters: []);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            db.CharacterSheets.Add(new CharacterSheet.Domain.Characters.CharacterSheetRoot(
                orphanCharacterId,
                DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        using var response = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{orphanCharacterId:D}/sheet?userId={Guid.NewGuid():D}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Character unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildInProgress", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingProjectionFailsSafelyWhileEmptyProjectionMeansNoOwnedCharacter()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();

        factory.Context = CreateContext(characters: null);
        using var missing = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, missing.StatusCode);

        factory.Context = CreateContext(characters: []);
        using var empty = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode);
    }

    [Fact]
    public async Task DirectBackendCharacterRequestWithoutToolHostAuthenticationIsRejected()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/characters/{Guid.NewGuid():D}/sheet");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownLifecycleFailsClosedInsteadOfMappingLoosely()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = CreateContext(characters: [Character(characterId, "Unknown Hero", "active")]);

        using var response = await factory.SendHostedAsync(HttpMethod.Get, $"/api/characters/{characterId:D}/sheet");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessReportsUsablePostgreSqlPersistenceAndImplementedOwnerAuthorization()
    {
        using var factory = new CharacterSheetWorkflowFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/ready");
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Equal("ready", payload["status"]);
        Assert.Equal("postgresql-ready", payload["persistence"]);
        Assert.Equal("tool-host-owner-projection", payload["characterAuthorization"]);
    }

    private static ToolHostAuthenticationContext CreateContext(
        IReadOnlyList<ToolHostCampaignContext>? campaigns = null,
        IReadOnlyList<ToolHostCharacterContext>? characters = null) =>
        new(
            1,
            "character-sheet",
            "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Owner"),
            [],
            campaigns ?? [],
            characters);

    private static ToolHostCharacterContext Character(
        Guid id,
        string name,
        string status,
        DateTimeOffset? archivedAt = null,
        params Guid[] campaignIds) =>
        new(id, name, status, archivedAt, campaignIds);

    private sealed class CharacterSheetWorkflowFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();

        public ToolHostAuthenticationContext Context { get; set; } = CreateContext(characters: []);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CharacterSheet", _database.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToolHostAuthenticationClient>();
                services.AddSingleton<IToolHostAuthenticationClient>(new MutableAuthenticationClient(this));
            });
        }

        public async Task<HttpResponseMessage> SendHostedAsync(HttpMethod method, string path)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ToolHostAuthenticationHeaders.Ticket, "test-ticket");
            request.Headers.Add(
                ToolHostAuthenticationHeaders.IntrospectionPath,
                DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);
            return await client.SendAsync(request);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _database.Dispose();
            }
        }

        private sealed class MutableAuthenticationClient(CharacterSheetWorkflowFactory factory)
            : IToolHostAuthenticationClient
        {
            public Task<ToolHostAuthenticationContext?> RedeemAsync(
                string ticket,
                string introspectionPath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<ToolHostAuthenticationContext?>(factory.Context);
        }
    }
}
