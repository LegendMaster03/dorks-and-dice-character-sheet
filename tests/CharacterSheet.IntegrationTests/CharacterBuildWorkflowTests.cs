using System.Net;
using System.Net.Http.Json;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterBuildWorkflowTests
{
    [Fact]
    public async Task OwnerCanReadAndMutateActiveCharacterBuild()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Builder", "Active"));
        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        using (var initial = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/build"))
        {
            var view = await initial.Content.ReadFromJsonAsync<CharacterBuildView>();
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            Assert.NotNull(view);
            Assert.False(view.ReadOnly);
            Assert.Empty(view.FoundationalSelections);
            Assert.Empty(view.ProgressionEntries);
        }

        using (var race = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/race-species",
                   new { conceptKey = "race:elf" }))
        {
            Assert.Equal(HttpStatusCode.OK, race.StatusCode);
        }
        using (var startingClass = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/starting-class",
                   new { conceptKey = "class:fighter" }))
        {
            Assert.Equal(HttpStatusCode.OK, startingClass.StatusCode);
        }

        using var response = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/build");
        var build = await response.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.NotNull(build);
        Assert.Equal("race:elf", Assert.Single(build.FoundationalSelections).RuleConceptKey);
        var progression = Assert.Single(build.ProgressionEntries);
        Assert.Equal("class", progression.Kind);
        Assert.Equal(0, progression.Ordinal);
        Assert.Equal("class:fighter", progression.RuleConceptKey);
    }

    [Fact]
    public async Task DifferentlyCasedConceptInputPersistsAsOneCanonicalKey()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Canonical Builder", "Active"));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        foreach (var key in new[] { "  Race:ELF  ", "RACE:Elf" })
        {
            using var response = await factory.SendHostedAsync(
                HttpMethod.Put,
                $"/api/characters/{characterId:D}/build/race-species",
                new { conceptKey = key });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var key in new[] { "  CLASS:WIZARD  ", "Class:Wizard" })
        {
            using var response = await factory.SendHostedAsync(
                HttpMethod.Put,
                $"/api/characters/{characterId:D}/build/starting-class",
                new { conceptKey = key });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var read = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/build");
        var build = await read.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(build);
        Assert.Equal("race:elf", Assert.Single(build.FoundationalSelections).RuleConceptKey);
        Assert.Equal("class:wizard", Assert.Single(build.ProgressionEntries).RuleConceptKey);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(1, await db.FoundationalRuleSelections.CountAsync());
        Assert.Equal(1, await db.CharacterAdvancementEntries.CountAsync());
    }

    [Fact]
    public async Task ReplacingAndClearingStartingChoicesDoNotCreateDuplicates()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Builder", "Active"));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        foreach (var key in new[] { "race:elf", "race:dwarf" })
        {
            using var response = await factory.SendHostedAsync(
                HttpMethod.Put,
                $"/api/characters/{characterId:D}/build/race-species",
                new { conceptKey = key });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var key in new[] { "class:fighter", "class:wizard" })
        {
            using var response = await factory.SendHostedAsync(
                HttpMethod.Put,
                $"/api/characters/{characterId:D}/build/starting-class",
                new { conceptKey = key });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            Assert.Equal(1, await db.FoundationalRuleSelections.CountAsync());
            Assert.Equal("race:dwarf", (await db.FoundationalRuleSelections.SingleAsync()).RuleConceptKey);
            Assert.Equal(1, await db.CharacterAdvancementEntries.CountAsync());
            Assert.Equal("class:wizard", (await db.CharacterAdvancementEntries.SingleAsync()).RuleConceptKey);
        }

        using (var raceClear = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/build/race-species"))
        {
            Assert.Equal(HttpStatusCode.OK, raceClear.StatusCode);
        }
        using (var classClear = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/build/starting-class"))
        {
            Assert.Equal(HttpStatusCode.OK, classClear.StatusCode);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            Assert.Equal(0, await db.FoundationalRuleSelections.CountAsync());
            Assert.Equal(0, await db.CharacterAdvancementEntries.CountAsync());
        }
    }

    [Fact]
    public async Task ArchivedCharacterCanReadBuildButCanNotMutateIt()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Archived Builder", "Active"));
        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }
        using (var select = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/race-species",
                   new { conceptKey = "race:elf" }))
        {
            Assert.Equal(HttpStatusCode.OK, select.StatusCode);
        }

        factory.Context = Context(Character(
            characterId,
            "Archived Builder",
            "Archived",
            DateTimeOffset.UtcNow));

        using var read = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/build");
        var build = await read.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.NotNull(build);
        Assert.True(build.ReadOnly);
        Assert.Equal("race:elf", Assert.Single(build.FoundationalSelections).RuleConceptKey);

        using var mutate = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/build/starting-class",
            new { conceptKey = "class:wizard" });
        Assert.Equal(HttpStatusCode.Conflict, mutate.StatusCode);
    }

    [Fact]
    public async Task LocalBuilderStateCanNotBypassSiteOwnerAuthorization()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            var root = new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow);
            root.SetFoundationalSelection(
                CharacterFoundationalSelectionCategory.RaceSpecies,
                "race:secret",
                DateTimeOffset.UtcNow);
            db.CharacterSheets.Add(root);
            await db.SaveChangesAsync();
        }

        using var read = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/build");
        var body = await read.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.DoesNotContain("race:secret", body, StringComparison.Ordinal);

        using var mutate = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/build/starting-class",
            new { conceptKey = "class:secret" });
        Assert.Equal(HttpStatusCode.NotFound, mutate.StatusCode);
    }

    [Fact]
    public async Task UninitializedOwnedCharacterDoesNotGainBuilderRowsThroughBuildMutation()
    {
        using var factory = new CharacterBuildWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Basic", "Active"));

        using var response = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/build/starting-class",
            new { conceptKey = "class:fighter" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(0, await db.CharacterAdvancementEntries.CountAsync());
    }

    private static ToolHostAuthenticationContext Context(params ToolHostCharacterContext[] characters) =>
        new(
            1,
            "character-sheet",
            "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Owner"),
            [],
            [],
            characters);

    private static ToolHostCharacterContext Character(
        Guid id,
        string name,
        string status,
        DateTimeOffset? archivedAt = null) =>
        new(id, name, status, archivedAt, []);

    private sealed class CharacterBuildWorkflowFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();

        public ToolHostAuthenticationContext Context { get; set; } = Context();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CharacterSheet", _database.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToolHostAuthenticationClient>();
                services.AddSingleton<IToolHostAuthenticationClient>(new MutableAuthenticationClient(this));
            });
        }

        public async Task<HttpResponseMessage> SendHostedAsync(
            HttpMethod method,
            string path,
            object? body = null)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ToolHostAuthenticationHeaders.Ticket, "test-ticket");
            request.Headers.Add(
                ToolHostAuthenticationHeaders.IntrospectionPath,
                DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }
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

        private sealed class MutableAuthenticationClient(CharacterBuildWorkflowFactory factory)
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
