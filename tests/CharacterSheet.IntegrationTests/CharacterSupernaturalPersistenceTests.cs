using System.Text.Json;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// The writer in these tests is a deliberately controlled fixture, not a
/// production acquisition authority. These tests cover PostgreSQL atomicity,
/// migration, dependency revisions and durable ownership only.
/// </summary>
public sealed class CharacterSupernaturalPersistenceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static string Ledger(Guid characterId, long revision,
        params string[] processedEvents) =>
        JsonSerializer.Serialize(new
        {
            characterId = characterId.ToString("D"),
            revision,
            instances = Array.Empty<object>(),
            resources = Array.Empty<object>(),
            processedEventIds = processedEvents
        });

    private static string WolfSheet => """
        [{"id":"wolf","parentId":"@primary","definitionId":"test.wolf",
          "progression":[{"id":"wolf-progression","grants":[]}]}]
        """;

    private static async Task<(PostgresCharacterSupernaturalStateStore Store, string Digest)>
        InitializeAsync(CharacterSheetDbContext db, Guid id)
    {
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        return (store, (await store.GetAuthoritativeInputsFingerprintAsync(id))!);
    }

    private static VerifiedSupernaturalStateWrite Write(
        Guid id, string digest, long expectedRevision = 0,
        long ledgerRevision = 0, long nextLedgerRevision = 0,
        string eventId = "acquire:wolf", bool transition = false,
        string? ledger = null, string? alternate = null) =>
        new(id, "first-party-001", "test-native-catalog", alternate ?? WolfSheet, "[]",
            ledger ?? Ledger(id, nextLedgerRevision, transition ? eventId : "prior"),
            expectedRevision, ledgerRevision, nextLedgerRevision, digest,
            eventId, "fixture:verified-source", transition, Now);

    [Fact]
    public async Task OrdinaryCharacterNeedsNoSyntheticStateAndExistingDataSurvivesMigration()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
        Assert.Equal(64, digest.Length);
        Assert.NotNull(await db.CharacterSheets.AsNoTracking()
            .SingleAsync(x => x.CharacterId == id));
    }

    [Fact]
    public async Task OwnedAlternateSheetAndEventSurviveFreshContextWithoutAnotherCharacter()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var first = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var (store, digest) = await InitializeAsync(first, id);
            var applied = await store.CommitVerifiedAsync(Write(id, digest));
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied, applied.Status);
            Assert.Equal(1, applied.Revision);
        }

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var persisted = new PostgresCharacterSupernaturalStateStore(reload);
        var state = await persisted.GetAsync(id);
        Assert.NotNull(state);
        Assert.Equal(1, state.ContractVersion);
        Assert.Equal(1, state.Revision);
        Assert.Equal(0, state.LedgerRevision);
        Assert.Contains("wolf-progression", state.AlternateSheetsJson);
        Assert.Single(await persisted.GetEventsAsync(id));
        Assert.Equal(1, await reload.CharacterSheets.CountAsync(x => x.CharacterId == id));
    }

    [Fact]
    public async Task TransitionIsAtomicDurableAndDuplicateOrStaleEventsDoNotChangeIt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var first = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var (store, digest) = await InitializeAsync(first, id);
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(Write(id, digest))).Status);
            var transition = Write(id, digest, 1, 0, 1, "enter:wolf",
                transition: true);
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(transition)).Status);
            Assert.Equal(CharacterSupernaturalWriteStatus.DuplicateEvent,
                (await store.CommitVerifiedAsync(Write(id, digest, 2, 1, 2,
                    "enter:wolf", transition: true))).Status);
            Assert.Equal(CharacterSupernaturalWriteStatus.RevisionConflict,
                (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                    "enter:hybrid", transition: true))).Status);
        }

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var storeAgain = new PostgresCharacterSupernaturalStateStore(reload);
        var state = (await storeAgain.GetAsync(id))!;
        Assert.Equal(2, state.Revision);
        Assert.Equal(1, state.LedgerRevision);
        Assert.Contains("enter:wolf", state.LedgerJson);
        Assert.Equal(2, (await storeAgain.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task IndependentPrimaryInputChangeRejectsOlderTransitionPlan()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        await new PostgresCharacterBuildStore(db).SetBaseAbilityScoreInputAsync(
            id, "strength", 12, Now.AddSeconds(1));
        var changed = await store.GetAuthoritativeInputsFingerprintAsync(id);
        Assert.NotEqual(digest, changed);
        Assert.Equal(CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                "enter:wolf", transition: true))).Status);
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task CompetingTransitionsAtSameRevisionCanNotBothCommit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        string digest;
        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var (store, signature) = await InitializeAsync(setup, id);
            digest = signature;
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(Write(id, digest))).Status);
        }

        await using var first = new CharacterSheetDbContext(database.CreateOptions());
        await using var second = new CharacterSheetDbContext(database.CreateOptions());
        var results = await Task.WhenAll(
            new PostgresCharacterSupernaturalStateStore(first)
                .CommitVerifiedAsync(Write(id, digest, 1, 0, 1, "enter:wolf",
                    transition: true)),
            new PostgresCharacterSupernaturalStateStore(second)
                .CommitVerifiedAsync(Write(id, digest, 1, 0, 1, "enter:hybrid",
                    transition: true)));
        Assert.Single(results, value => value.Status == CharacterSupernaturalWriteStatus.Applied);
        Assert.Single(results, value => value.Status == CharacterSupernaturalWriteStatus.RevisionConflict);
        await using var inspect = new CharacterSheetDbContext(database.CreateOptions());
        Assert.Equal(2, (await new PostgresCharacterSupernaturalStateStore(inspect)
            .GetAsync(id))!.Revision);
        Assert.Equal(2, await inspect.SupernaturalEventReceipts.CountAsync(x => x.CharacterId == id));
    }

    [Fact]
    public async Task MalformedAndDuplicateOwnerRecordsDoNotWriteAnyRows()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var duplicateSheets = """
            [{"id":"wolf"},{"id":"wolf"}]
            """;
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, alternate: duplicateSheets)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, ledger: """{"characterId":"wrong"}""")));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
    }
}
