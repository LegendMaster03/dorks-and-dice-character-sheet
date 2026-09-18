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

public sealed class AbilityScoreInputWorkflowTests
{
    [Fact]
    public async Task ActiveOwnerCanSetReplaceAndClearBaseInputsWithoutEffectiveScoreSemantics()
    {
        using var factory = new AbilityScoreWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Ability Builder", "Active"));

        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        Guid strengthId;
        using (var setStrength = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/STRENGTH",
                   new { score = 15 }))
        {
            Assert.Equal(HttpStatusCode.OK, setStrength.StatusCode);
            var body = await setStrength.Content.ReadAsStringAsync();
            Assert.DoesNotContain("effectiveScore", body, StringComparison.OrdinalIgnoreCase);
            var view = await setStrength.Content.ReadFromJsonAsync<CharacterBuildView>();
            Assert.NotNull(view);
            var strength = Assert.Single(view.BaseAbilityScoreInputs);
            Assert.Equal(CharacterAbilityKey.Strength, strength.AbilityKey);
            Assert.Equal(15, strength.Score);
            strengthId = strength.Id;
        }

        using (var replaceStrength = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/strength",
                   new { score = 18 }))
        {
            Assert.Equal(HttpStatusCode.OK, replaceStrength.StatusCode);
            var view = await replaceStrength.Content.ReadFromJsonAsync<CharacterBuildView>();
            Assert.NotNull(view);
            var strength = Assert.Single(view.BaseAbilityScoreInputs);
            Assert.Equal(strengthId, strength.Id);
            Assert.Equal(18, strength.Score);
        }

        using (var setDexterity = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/dexterity",
                   new { score = 14 }))
        {
            Assert.Equal(HttpStatusCode.OK, setDexterity.StatusCode);
        }

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/build"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var view = await read.Content.ReadFromJsonAsync<CharacterBuildView>();
            Assert.NotNull(view);
            Assert.Equal(2, view.BaseAbilityScoreInputs.Count);
            Assert.Contains(view.BaseAbilityScoreInputs, value =>
                value.AbilityKey == CharacterAbilityKey.Strength && value.Score == 18);
            Assert.Contains(view.BaseAbilityScoreInputs, value =>
                value.AbilityKey == CharacterAbilityKey.Dexterity && value.Score == 14);
        }

        using var clear = await factory.SendHostedAsync(
            HttpMethod.Delete,
            $"/api/characters/{characterId:D}/build/ability-scores/strength");
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        var cleared = await clear.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.NotNull(cleared);
        var remaining = Assert.Single(cleared.BaseAbilityScoreInputs);
        Assert.Equal(CharacterAbilityKey.Dexterity, remaining.AbilityKey);
    }

    [Fact]
    public async Task ArchivedOwnerCanReadPreservedInputsButCanNotMutateThem()
    {
        using var factory = new AbilityScoreWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Archived Ability Builder", "Active"));

        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }
        using (var set = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/wisdom",
                   new { score = 13 }))
        {
            Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        }

        factory.Context = Context(Character(
            characterId,
            "Archived Ability Builder",
            "Archived",
            DateTimeOffset.UtcNow));

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/build"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var view = await read.Content.ReadFromJsonAsync<CharacterBuildView>();
            Assert.NotNull(view);
            Assert.True(view.ReadOnly);
            var wisdom = Assert.Single(view.BaseAbilityScoreInputs);
            Assert.Equal(CharacterAbilityKey.Wisdom, wisdom.AbilityKey);
            Assert.Equal(13, wisdom.Score);
        }

        using (var replace = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/wisdom",
                   new { score = 20 }))
        {
            Assert.Equal(HttpStatusCode.Conflict, replace.StatusCode);
        }
        using (var clear = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/build/ability-scores/wisdom"))
        {
            Assert.Equal(HttpStatusCode.Conflict, clear.StatusCode);
        }

        using var verify = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/build");
        var persisted = await verify.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.NotNull(persisted);
        Assert.Equal(13, Assert.Single(persisted.BaseAbilityScoreInputs).Score);
    }

    [Fact]
    public async Task LocalAbilityRowsCanNotBypassSiteOwnerAuthorization()
    {
        using var factory = new AbilityScoreWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            var root = new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow);
            root.SetBaseAbilityScoreInput("strength", 99, DateTimeOffset.UtcNow);
            db.CharacterSheets.Add(root);
            await db.SaveChangesAsync();
        }

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/build"))
        {
            var body = await read.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.DoesNotContain("\"score\":99", body, StringComparison.Ordinal);
        }

        using var mutate = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/build/ability-scores/strength",
            new { score = 20 });
        Assert.Equal(HttpStatusCode.NotFound, mutate.StatusCode);
    }

    [Fact]
    public async Task UninitializedOwnedCharacterDoesNotGainRichStateThroughAbilityMutation()
    {
        using var factory = new AbilityScoreWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Basic Ability Character", "Active"));

        using (var set = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/ability-scores/charisma",
                   new { score = 16 }))
        {
            Assert.Equal(HttpStatusCode.NotFound, set.StatusCode);
        }

        using (var clear = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/build/ability-scores/charisma"))
        {
            Assert.Equal(HttpStatusCode.NotFound, clear.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(0, await db.CharacterSheets.CountAsync());
        Assert.Equal(0, await db.BaseAbilityScoreInputs.CountAsync());
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

    private sealed class AbilityScoreWorkflowFactory : WebApplicationFactory<Program>
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

        private sealed class MutableAuthenticationClient(AbilityScoreWorkflowFactory factory)
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
