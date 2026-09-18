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

public sealed class CharacterStateWorkflowTests
{
    [Fact]
    public async Task ActiveOwnerCanReadAndMutateRoutineState()
    {
        using var factory = new CharacterStateWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Stateful", "Active"));

        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        using (var initial = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/state"))
        {
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
            var view = await initial.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.NotNull(view);
            Assert.False(view.ReadOnly);
            Assert.Empty(view.InventoryItemOccurrences);
            Assert.Empty(view.Notes);
        }

        Guid firstItemId;
        using (var firstItem = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/inventory",
                   new { conceptKey = "  ITEM:TORCH  " }))
        {
            Assert.Equal(HttpStatusCode.OK, firstItem.StatusCode);
            var view = await firstItem.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.NotNull(view);
            firstItemId = Assert.Single(view.InventoryItemOccurrences).Id;
            Assert.Equal("item:torch", Assert.Single(view.InventoryItemOccurrences).RuleConceptKey);
        }

        using (var duplicate = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/inventory",
                   new { conceptKey = "item:torch" }))
        {
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            var view = await duplicate.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.NotNull(view);
            Assert.Equal(2, view.InventoryItemOccurrences.Count);
            Assert.Equal(2, view.InventoryItemOccurrences.Select(value => value.Id).Distinct().Count());
        }

        Guid noteId;
        using (var addNote = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/notes",
                   new { content = "Original" }))
        {
            Assert.Equal(HttpStatusCode.OK, addNote.StatusCode);
            var view = await addNote.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.NotNull(view);
            noteId = Assert.Single(view.Notes).Id;
        }

        using (var updateNote = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/state/notes/{noteId:D}",
                   new { content = "Updated" }))
        {
            Assert.Equal(HttpStatusCode.OK, updateNote.StatusCode);
        }

        using (var removeItem = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/state/inventory/{firstItemId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, removeItem.StatusCode);
        }

        using var verify = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/state");
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var persisted = await verify.Content.ReadFromJsonAsync<CharacterStateView>();
        Assert.NotNull(persisted);
        Assert.Single(persisted.InventoryItemOccurrences);
        Assert.Equal("Updated", Assert.Single(persisted.Notes).Content);
    }

    [Fact]
    public async Task ArchivedOwnerCanReadButCanNotMutateRoutineState()
    {
        using var factory = new CharacterStateWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Archived State", "Active"));

        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        Guid itemId;
        using (var addItem = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/inventory",
                   new { conceptKey = "item:rope" }))
        {
            var view = await addItem.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.Equal(HttpStatusCode.OK, addItem.StatusCode);
            Assert.NotNull(view);
            itemId = Assert.Single(view.InventoryItemOccurrences).Id;
        }

        Guid noteId;
        using (var addNote = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/notes",
                   new { content = "Preserved" }))
        {
            var view = await addNote.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.Equal(HttpStatusCode.OK, addNote.StatusCode);
            Assert.NotNull(view);
            noteId = Assert.Single(view.Notes).Id;
        }

        factory.Context = Context(Character(
            characterId,
            "Archived State",
            "Archived",
            DateTimeOffset.UtcNow));

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/state"))
        {
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var view = await read.Content.ReadFromJsonAsync<CharacterStateView>();
            Assert.NotNull(view);
            Assert.True(view.ReadOnly);
            Assert.Equal(itemId, Assert.Single(view.InventoryItemOccurrences).Id);
            Assert.Equal(noteId, Assert.Single(view.Notes).Id);
        }

        using (var add = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/inventory",
                   new { conceptKey = "item:torch" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, add.StatusCode);
        }

        using (var update = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/state/notes/{noteId:D}",
                   new { content = "Changed" }))
        {
            Assert.Equal(HttpStatusCode.Conflict, update.StatusCode);
        }

        using (var removeItem = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/state/inventory/{itemId:D}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, removeItem.StatusCode);
        }

        using (var removeNote = await factory.SendHostedAsync(
                   HttpMethod.Delete,
                   $"/api/characters/{characterId:D}/state/notes/{noteId:D}"))
        {
            Assert.Equal(HttpStatusCode.Conflict, removeNote.StatusCode);
        }
    }

    [Fact]
    public async Task LocalRoutineStateCanNotBypassOwnerAuthorization()
    {
        using var factory = new CharacterStateWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
            var root = new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow);
            root.AddInventoryItemOccurrence("item:secret", DateTimeOffset.UtcNow);
            root.AddNote("secret note", DateTimeOffset.UtcNow);
            db.CharacterSheets.Add(root);
            await db.SaveChangesAsync();
        }

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/state"))
        {
            var body = await read.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.DoesNotContain("item:secret", body, StringComparison.Ordinal);
            Assert.DoesNotContain("secret note", body, StringComparison.Ordinal);
        }

        using var mutate = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/state/notes",
            new { content = "other" });
        Assert.Equal(HttpStatusCode.NotFound, mutate.StatusCode);
    }

    [Fact]
    public async Task UninitializedOwnedCharacterIsNotImplicitlyInitializedByRoutineStateMutations()
    {
        using var factory = new CharacterStateWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(Character(characterId, "Basic", "Active"));

        using (var inventory = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/inventory",
                   new { conceptKey = "item:torch" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, inventory.StatusCode);
        }

        using (var note = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/state/notes",
                   new { content = "Should not persist" }))
        {
            Assert.Equal(HttpStatusCode.NotFound, note.StatusCode);
        }

        using (var read = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/state"))
        {
            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
        Assert.Equal(0, await db.CharacterSheets.CountAsync());
        Assert.Equal(0, await db.InventoryItemOccurrences.CountAsync());
        Assert.Equal(0, await db.CharacterNotes.CountAsync());
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

    private sealed class CharacterStateWorkflowFactory : WebApplicationFactory<Program>
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

        private sealed class MutableAuthenticationClient(CharacterStateWorkflowFactory factory)
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
