using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class OriginDraftPersistenceTests
{
    private const string HumanDraft = """
        {"id":"human","intent":"Direct","selectedSpeciesId":"species.human"}
        """;
    private const string ElfDraft = """
        {"id":"elf","intent":"Direct","selectedSpeciesId":"species.elf"}
        """;

    [Fact]
    public async Task DraftPersistsAcrossContextsAndRevisionProtectsAgainstStaleUpdates()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var first = new CharacterSheetDbContext(options))
        {
            await first.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(first).GetOrCreateAsync(characterId);
            var state = await new PostgresCharacterOriginDraftStore(first).SaveAsync(
                characterId, "first-party-001", "old-content", HumanDraft, 0,
                DateTimeOffset.UtcNow);
            Assert.NotNull(state);
            Assert.Equal(1, state.Revision);
            Assert.Equal(HumanDraft, state.ProposalJson);
        }

        await using (var second = new CharacterSheetDbContext(options))
        {
            var store = new PostgresCharacterOriginDraftStore(second);
            var loaded = await store.GetAsync(characterId);
            Assert.Equal(1, loaded!.Revision);
            Assert.Null(await store.SaveAsync(characterId, "first-party-001",
                "new-content", ElfDraft, 0, DateTimeOffset.UtcNow));
            Assert.Equal(1, (await store.GetAsync(characterId))!.Revision);
        }

        await using var writerA = new CharacterSheetDbContext(options);
        await using var writerB = new CharacterSheetDbContext(options);
        var twoWriters = await Task.WhenAll(
            new PostgresCharacterOriginDraftStore(writerA).SaveAsync(characterId,
                "first-party-001", "content-a", HumanDraft, 1,
                DateTimeOffset.UtcNow.AddSeconds(1)),
            new PostgresCharacterOriginDraftStore(writerB).SaveAsync(characterId,
                "first-party-001", "content-b", ElfDraft, 1,
                DateTimeOffset.UtcNow.AddSeconds(2)));
        Assert.Single(twoWriters.Where(x => x is not null));
        await using var verification = new CharacterSheetDbContext(options);
        var final = await new PostgresCharacterOriginDraftStore(verification)
            .GetAsync(characterId);
        Assert.Equal(2, final!.Revision);
        Assert.Contains(final.Fingerprint, new[] { "content-a", "content-b" });
        Assert.Equal(1, await verification.CharacterOriginDrafts.CountAsync());
    }

    [Fact]
    public async Task DraftIsCharacterOwnedAndDeletedWithItsCharacter()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var ownedCharacter = Guid.NewGuid();
        var unrelatedCharacter = Guid.NewGuid();

        await using (var db = new CharacterSheetDbContext(options))
        {
            await db.Database.MigrateAsync();
            var roots = new PostgresCharacterSheetStore(db);
            await roots.GetOrCreateAsync(ownedCharacter);
            await roots.GetOrCreateAsync(unrelatedCharacter);
            var drafts = new PostgresCharacterOriginDraftStore(db);
            await drafts.SaveAsync(ownedCharacter, "first-party-001", null,
                HumanDraft, 0, DateTimeOffset.UtcNow);
            Assert.Null(await drafts.GetAsync(unrelatedCharacter));
            await db.CharacterSheets.Where(x => x.CharacterId == ownedCharacter)
                .ExecuteDeleteAsync();
        }

        await using var verify = new CharacterSheetDbContext(options);
        Assert.Null(await new PostgresCharacterOriginDraftStore(verify)
            .GetAsync(ownedCharacter));
        Assert.Equal(1, await verify.CharacterSheets.CountAsync());
    }

    [Fact]
    public async Task ServiceRejectsUnownedArchivedAndForgedStateWithoutSaving()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(options);
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var store = new PostgresCharacterOriginDraftStore(db);
        var native = new PreviewGateway();
        var proposal = JsonSerializer.Deserialize<JsonElement>(HumanDraft);

        CharacterOriginDraftService BuildService(SiteCharacterAccessStatus status,
            SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active) =>
            new(new Access(status, id, lifecycle),
                new PostgresCharacterBuildStore(db), store, native, TimeProvider.System);

        var denied = await BuildService(SiteCharacterAccessStatus.NotFoundOrNotOwned)
            .SaveAsync(id, "first-party-001", null, proposal, 0);
        Assert.Equal(CharacterOriginDraftStatus.NotFoundOrNotOwned, denied.Status);
        var archived = await BuildService(SiteCharacterAccessStatus.Authorized,
                SiteCharacterLifecycleState.Archived)
            .SaveAsync(id, "first-party-001", null, proposal, 0);
        Assert.Equal(CharacterOriginDraftStatus.ArchivedReadOnly, archived.Status);
        Assert.Equal(0, native.Calls);

        var forged = JsonSerializer.Deserialize<JsonElement>(
            """{"id":"forged","history":[{"id":"completed"}]}""");
        var refused = await BuildService(SiteCharacterAccessStatus.Authorized)
            .SaveAsync(id, "first-party-001", null, forged, 0);
        Assert.Equal(CharacterOriginDraftStatus.InvalidRequest, refused.Status);
        Assert.Null(await store.GetAsync(id));

        var approvedForDraftOnly = await BuildService(SiteCharacterAccessStatus.Authorized)
            .SaveAsync(id, "first-party-001", null, proposal, 0);
        Assert.Equal(CharacterOriginDraftStatus.Ready, approvedForDraftOnly.Status);
        Assert.Equal(1, approvedForDraftOnly.Draft!.Revision);
        Assert.False(approvedForDraftOnly.Draft.Active);
        Assert.NotNull(approvedForDraftOnly.Preview);
    }

    private sealed class Access(SiteCharacterAccessStatus status, Guid characterId,
        SiteCharacterLifecycleState lifecycle) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            if (status != SiteCharacterAccessStatus.Authorized ||
                id != characterId)
                return Task.FromResult(new SiteCharacterAccessResult(status));
            return Task.FromResult(SiteCharacterAccessResult.Authorized(
                new SiteCharacterProjection(id, "Test", lifecycle,
                    lifecycle == SiteCharacterLifecycleState.Archived ?
                        DateTimeOffset.UtcNow : null, [])));
        }
    }

    private sealed class PreviewGateway : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }

        public Task<JsonNode> PreviewNativeOriginAsync(JsonElement request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (request.GetProperty("proposal").TryGetProperty("history", out _))
                throw new RulesCoreGatewayException(
                    "The existing profile must come from trusted state.",
                    statusCode: 400);
            return Task.FromResult(JsonNode.Parse(
                """{"previewOnly":true,"result":{"status":"Incomplete","issues":[]}}""")!);
        }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint,
            string category, string? query, int limit, int offset, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
