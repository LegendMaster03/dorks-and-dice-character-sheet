using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// The trusted commit is an internal test fixture. These tests prove only
/// persistence atomicity and revision semantics, not Rules Core authorization
/// or campaign-DM evidence validation.
/// </summary>
public sealed class AuthorizedOriginStatePersistenceTests
{
    private const string HumanProfile = """
        {"id":"human","underlying":{"speciesId":"species.human"}}
        """;
    private const string ElfProfile = """
        {"id":"elf","underlying":{"speciesId":"species.elf"}}
        """;
    private const string Draft = """
        {"id":"creation","selectedSpeciesId":"species.human"}
        """;

    private static VerifiedOriginPersistenceCommand Command(Guid characterId,
        long expectedOrigin = 0, long? expectedDraft = 1,
        string eventId = "origin:creation", string profile = HumanProfile) =>
        new(characterId, "first-party-001", "catalog-1", profile,
            expectedOrigin, expectedDraft, eventId, "rule.creature-origination",
            "biological", "first-party", "native-review:1",
            DateTimeOffset.UtcNow);

    private static async Task InitializeAsync(CharacterSheetDbContext db, Guid characterId)
    {
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", "catalog-1", Draft, 0,
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CommittedOriginAndEventSurviveReloadWithoutActivatingDraftRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var first = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeAsync(first, id);
            var stored = await new PostgresCharacterAuthorizedOriginStore(first)
                .CommitVerifiedAsync(Command(id));
            Assert.Equal(AuthorizedOriginStoreStatus.Applied, stored.Status);
            Assert.Equal(1, stored.Revision);
        }

        await using var next = new CharacterSheetDbContext(database.CreateOptions());
        var store = new PostgresCharacterAuthorizedOriginStore(next);
        var active = await store.GetAsync(id);
        Assert.NotNull(active);
        Assert.Equal(1, active.Revision);
        Assert.Equal("catalog-1", active.Fingerprint);
        Assert.Equal(HumanProfile, active.ProfileJson);
        Assert.Equal(1, active.SourceDraftRevision);
        var receipt = Assert.Single(await store.GetEventsAsync(id));
        Assert.Equal("origin:creation", receipt.EventId);
        Assert.Equal(1, receipt.CommittedRevision);
        Assert.Equal("first-party", receipt.AuthorizationKind);
        Assert.Equal(1, (await new PostgresCharacterOriginDraftStore(next)
            .GetAsync(id))!.Revision);
    }

    [Fact]
    public async Task ChangedDraftCanNotBeCommittedFromAnOlderEvaluation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, id);

        var draftStore = new PostgresCharacterOriginDraftStore(db);
        var replacement = await draftStore.SaveAsync(
            id, "first-party-001", "catalog-2", """{"id":"new-draft"}""",
            1, DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(2, replacement!.Revision);

        var authorized = new PostgresCharacterAuthorizedOriginStore(db);
        var stale = await authorized.CommitVerifiedAsync(Command(id));
        Assert.Equal(AuthorizedOriginStoreStatus.DraftRevisionConflict, stale.Status);
        Assert.Null(await authorized.GetAsync(id));
        Assert.Empty(await authorized.GetEventsAsync(id));

        var actual = await authorized.CommitVerifiedAsync(Command(id,
            expectedDraft: 2));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, actual.Status);
    }

    [Fact]
    public async Task EventIdAndOriginRevisionCanNotBeReusedToDuplicateATransition()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var first = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(first, id);
        var store = new PostgresCharacterAuthorizedOriginStore(first);
        Assert.Equal(AuthorizedOriginStoreStatus.Applied,
            (await store.CommitVerifiedAsync(Command(id))).Status);

        var duplicate = await store.CommitVerifiedAsync(Command(id,
            expectedOrigin: 1));
        Assert.Equal(AuthorizedOriginStoreStatus.DuplicateEvent, duplicate.Status);
        Assert.Equal(1, duplicate.Revision);

        var stale = await store.CommitVerifiedAsync(Command(id, expectedOrigin: 0,
            eventId: "rite:next", profile: ElfProfile));
        Assert.Equal(AuthorizedOriginStoreStatus.OriginRevisionConflict, stale.Status);

        var second = await store.CommitVerifiedAsync(Command(id, expectedOrigin: 1,
            eventId: "rite:next", profile: ElfProfile));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, second.Status);
        Assert.Equal(2, second.Revision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
        Assert.Equal(ElfProfile, (await store.GetAsync(id))!.ProfileJson);
    }

    [Fact]
    public async Task RacingWritersAtOneRevisionPublishOnlyOneCompleteEvent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeAsync(setup, id);
            Assert.Equal(AuthorizedOriginStoreStatus.Applied,
                (await new PostgresCharacterAuthorizedOriginStore(setup)
                    .CommitVerifiedAsync(Command(id))).Status);
        }

        await using var writerA = new CharacterSheetDbContext(database.CreateOptions());
        await using var writerB = new CharacterSheetDbContext(database.CreateOptions());
        var results = await Task.WhenAll(
            new PostgresCharacterAuthorizedOriginStore(writerA)
                .CommitVerifiedAsync(Command(id, 1, 1, "rite:one", ElfProfile)),
            new PostgresCharacterAuthorizedOriginStore(writerB)
                .CommitVerifiedAsync(Command(id, 1, 1, "rite:two", HumanProfile)));
        Assert.Single(results.Where(x => x.Status == AuthorizedOriginStoreStatus.Applied));
        Assert.Single(results.Where(x => x.Status ==
            AuthorizedOriginStoreStatus.OriginRevisionConflict));

        await using var check = new CharacterSheetDbContext(database.CreateOptions());
        var store = new PostgresCharacterAuthorizedOriginStore(check);
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        var receipts = await store.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        Assert.Equal(new long[] { 1, 2 },
            receipts.Select(x => x.CommittedRevision).ToArray());
    }

    [Fact]
    public async Task CharacterDeletionCascadesCurrentOriginAndEventReceipts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeAsync(db, id);
            var result = await new PostgresCharacterAuthorizedOriginStore(db)
                .CommitVerifiedAsync(Command(id));
            Assert.Equal(AuthorizedOriginStoreStatus.Applied, result.Status);
            await db.CharacterSheets.Where(x => x.CharacterId == id)
                .ExecuteDeleteAsync();
        }
        await using var verify = new CharacterSheetDbContext(database.CreateOptions());
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(verify)
            .GetAsync(id));
        Assert.Empty(await verify.OriginEventReceipts.ToArrayAsync());
        Assert.Empty(await verify.CharacterOriginDrafts.ToArrayAsync());
    }

    [Fact]
    public async Task InvalidReceiptDataDoesNotPublishAnAuthorizedProfile()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, id);
        var command = Command(id) with { EventId = "", AuthorizationEvidenceId = "" };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new PostgresCharacterAuthorizedOriginStore(db).CommitVerifiedAsync(command));
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db).GetAsync(id));
        Assert.Empty(await db.OriginEventReceipts.ToArrayAsync());
    }
}
