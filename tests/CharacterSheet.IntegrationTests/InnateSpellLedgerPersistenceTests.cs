using System.Text.Json;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// PostgreSQL storage and concurrency checks using internally prepared
/// commands. They do not claim to authenticate a Site user or calculate
/// the native Rules Core spell progression.
/// </summary>
public sealed class InnateSpellLedgerPersistenceTests
{
    private static readonly Guid Actor = Guid.Parse("934f3d99-36bc-4ceb-aa3e-10f03e470d11");
    private const string Profile = """
        {"id":"human","underlying":{"speciesId":"species.human"}}
        """;
    private const string Draft = """
        {"id":"test","selectedSpeciesId":"species.human"}
        """;
    private const string ProjectionHash =
        "7E2F9A1E2BF14579B23C95EE88A7A4E3E1E9FD63F3AA2DA37F96F41351BDB82A";

    private static string Ledger(long revision, int remaining, int level = 3,
        string originProfileId = "human", string fingerprint = "catalog-1") =>
        JsonSerializer.Serialize(new
        {
            RulesetId = "first-party-001",
            CatalogFingerprint = fingerprint,
            OriginProfileId = originProfileId,
            ProposalId = "proposal-human",
            CharacterLevel = level,
            ProjectionFingerprint = ProjectionHash,
            Revision = revision,
            RemainingFreeCasts = new Dictionary<string, int>
            {
                ["grant:spell"] = remaining
            }
        });

    private static VerifiedInnateSpellLedgerPersistenceCommand Command(
        Guid id, long? expected = null, string eventId = "ledger:init",
        int remaining = 1, long originRevision = 1,
        string originProfileId = "human", string? ledgerJson = null,
        string? decisionIdsJson = null, long expectedCharacterRevision = 0) =>
        new(id, "first-party-001", "catalog-1", originProfileId,
            "proposal-human", originRevision, expected,
            ledgerJson ?? Ledger(expected is null ? 0 : expected.Value + 1,
                remaining, originProfileId: originProfileId),
            eventId, Actor, "trusted-native-projection:test", expected is null
                ? "initialize" : "spend-free-cast", DateTimeOffset.UtcNow,
            decisionIdsJson, expectedCharacterRevision);

    private static async Task InitializeOriginAsync(
        CharacterSheetDbContext db, Guid id)
    {
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            id, "first-party-001", "catalog-1", Draft, 0,
            DateTimeOffset.UtcNow);
        var origin = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new(id, "first-party-001", "catalog-1",
                Profile, 0, 1, "origin:created",
                "rule.creature-origination", "biological", "first-party",
                "origin-test-verified", DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, origin.Status);
    }

    [Fact]
    public async Task SnapshotAndReceiptSurviveReloadAndRetainAuthoritativeOwner()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeOriginAsync(db, id);
            var store = new PostgresCharacterInnateSpellLedgerStore(db);
            var saved = await store.CommitVerifiedAsync(Command(id));
            Assert.Equal(InnateSpellLedgerStoreStatus.Applied, saved.Status);
            Assert.Equal(0, saved.Revision);
        }
        await using var nextDb = new CharacterSheetDbContext(database.CreateOptions());
        var next = new PostgresCharacterInnateSpellLedgerStore(nextDb);
        var ledger = await next.GetAsync(id);
        Assert.NotNull(ledger);
        Assert.Equal(id, ledger.CharacterId);
        Assert.Equal(1, ledger.OriginRevision);
        Assert.Equal(0, ledger.LedgerRevision);
        Assert.Equal(1, JsonDocument.Parse(ledger.LedgerJson).RootElement
            .GetProperty("RemainingFreeCasts").GetProperty("grant:spell").GetInt32());
        var receipt = Assert.Single(await next.GetEventsAsync(id));
        Assert.Equal("ledger:init", receipt.EventId);
        Assert.Equal(0, receipt.CommittedRevision);
        Assert.Equal(Actor, receipt.ActorUserId);
        Assert.Equal("initialize", receipt.Operation);
    }

    [Fact]
    public async Task StoreEnforcesCreateOnceCasAndEventReplayProtection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeOriginAsync(db, id);
        var store = new PostgresCharacterInnateSpellLedgerStore(db);
        Assert.Equal(InnateSpellLedgerStoreStatus.Applied,
            (await store.CommitVerifiedAsync(Command(id))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.LedgerAlreadyInitialized,
            (await store.CommitVerifiedAsync(Command(id, eventId: "ledger:init-again"))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.DuplicateEvent,
            (await store.CommitVerifiedAsync(Command(id))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.LedgerRevisionConflict,
            (await store.CommitVerifiedAsync(Command(id, 5, "ledger:stale"))).Status);

        var spent = await store.CommitVerifiedAsync(Command(id, 0, "ledger:spend", 0));
        Assert.Equal(InnateSpellLedgerStoreStatus.Applied, spent.Status);
        Assert.Equal(1, spent.Revision);
        Assert.Equal(InnateSpellLedgerStoreStatus.DuplicateEvent,
            (await store.CommitVerifiedAsync(Command(id, 1, "ledger:spend", 1))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.LedgerRevisionConflict,
            (await store.CommitVerifiedAsync(Command(id, 0, "ledger:old", 1))).Status);
        var rest = await store.CommitVerifiedAsync(Command(id, 1,
            "ledger:long-rest", 1, decisionIdsJson: """["dm:capacity:1"]"""));
        Assert.Equal(InnateSpellLedgerStoreStatus.Applied, rest.Status);
        var receipts = await store.GetEventsAsync(id);
        Assert.Equal(new long[] { 0, 1, 2 },
            receipts.Select(x => x.CommittedRevision).ToArray());
        Assert.Equal("""["dm:capacity:1"]""",
            receipts.Last().AppliedDecisionIdsJson);
        Assert.Equal(1, JsonDocument.Parse((await store.GetAsync(id))!.LedgerJson)
            .RootElement.GetProperty("RemainingFreeCasts")
            .GetProperty("grant:spell").GetInt32());
    }

    [Fact]
    public async Task OriginRevisionAndNativeProfileMismatchNeverPublishLedger()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var missing = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        var store = new PostgresCharacterInnateSpellLedgerStore(db);
        Assert.Equal(InnateSpellLedgerStoreStatus.SheetNotInitialized,
            (await store.CommitVerifiedAsync(Command(missing))).Status);
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        Assert.Equal(InnateSpellLedgerStoreStatus.AuthorizedOriginMissing,
            (await store.CommitVerifiedAsync(Command(id))).Status);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            id, "first-party-001", "catalog-1", Draft, 0,
            DateTimeOffset.UtcNow);
        Assert.Equal(AuthorizedOriginStoreStatus.Applied,
            (await new PostgresCharacterAuthorizedOriginStore(db)
                .CommitVerifiedAsync(new(id, "first-party-001", "catalog-1",
                    Profile, 0, 1, "origin:created",
                    "rule.creature-origination", "biological", "first-party",
                    "origin-test-verified", DateTimeOffset.UtcNow))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.OriginRevisionConflict,
            (await store.CommitVerifiedAsync(Command(id,
                originRevision: 2))).Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.OriginIdentityConflict,
            (await store.CommitVerifiedAsync(Command(id,
                originProfileId: "elf"))).Status);
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task RacingWritersCommitOneLedgerRevisionAndOneEvent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeOriginAsync(setup, id);
            Assert.Equal(InnateSpellLedgerStoreStatus.Applied,
                (await new PostgresCharacterInnateSpellLedgerStore(setup)
                    .CommitVerifiedAsync(Command(id))).Status);
        }
        await using var first = new CharacterSheetDbContext(database.CreateOptions());
        await using var second = new CharacterSheetDbContext(database.CreateOptions());
        var results = await Task.WhenAll(
            new PostgresCharacterInnateSpellLedgerStore(first)
                .CommitVerifiedAsync(Command(id, 0, "spend:A", 0)),
            new PostgresCharacterInnateSpellLedgerStore(second)
                .CommitVerifiedAsync(Command(id, 0, "spend:B", 0)));
        Assert.Single(results, x => x.Status == InnateSpellLedgerStoreStatus.Applied);
        Assert.Single(results, x => x.Status ==
            InnateSpellLedgerStoreStatus.LedgerRevisionConflict);

        await using var verify = new CharacterSheetDbContext(database.CreateOptions());
        var store = new PostgresCharacterInnateSpellLedgerStore(verify);
        Assert.Equal(1, (await store.GetAsync(id))!.LedgerRevision);
        Assert.Equal(new long[] { 0, 1 },
            (await store.GetEventsAsync(id))
                .Select(x => x.CommittedRevision).ToArray());
    }

    [Fact]
    public async Task ChangedCommittedOriginInvalidatesPreviouslyPreparedLedgerWrite()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeOriginAsync(db, id);
        var ledger = new PostgresCharacterInnateSpellLedgerStore(db);
        Assert.Equal(InnateSpellLedgerStoreStatus.Applied,
            (await ledger.CommitVerifiedAsync(Command(id))).Status);
        var replacement = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new(id, "first-party-001", "catalog-1",
                """{"id":"different","underlying":{}}""", 1, 1,
                "origin:replaced", "rule.creature-origination", "biological",
                "first-party", "origin-test-verified", DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, replacement.Status);
        Assert.Equal(InnateSpellLedgerStoreStatus.OriginRevisionConflict,
            (await ledger.CommitVerifiedAsync(Command(id, 0, "ledger:stale"))).Status);
        Assert.Equal(0, (await ledger.GetAsync(id))!.LedgerRevision);
        Assert.Single(await ledger.GetEventsAsync(id));
    }

    [Fact]
    public async Task InvalidLedgerAndReceiptInputsDoNotPublishAnything()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeOriginAsync(db, id);
        var store = new PostgresCharacterInnateSpellLedgerStore(db);
        foreach (var bad in new[]
        {
            Command(id) with { LedgerJson = """{"Revision":0}""" },
            Command(id) with { LedgerJson = Ledger(5, 1) },
            Command(id) with { ActorUserId = Guid.Empty },
            Command(id) with { EventId = "" },
            Command(id) with { AppliedDecisionIdsJson = "{}" },
            Command(id) with { LedgerJson = Ledger(0, -1) },
            Command(id) with { ExpectedCharacterMutationRevision = null },
            Command(id) with { ExpectedCharacterMutationRevision = -1 }
        })
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.CommitVerifiedAsync(bad));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
    }

    [Theory]
    [InlineData("strength")]
    [InlineData("dexterity")]
    public async Task ScoreMutationAfterNativeEvaluationRejectsStaleLedgerCandidate(
        string score)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeOriginAsync(db, id);
        var nativeToken = (await new PostgresCharacterMutationRevisionReader(db)
            .GetRevisionAsync(id))!.Value;

        // Simulate a separate Character Sheet writer committing after the
        // native request captured the token. A deliberately identical/older
        // timestamp must not conceal the aggregate mutation.
        await using (var writer = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var root = await writer.CharacterSheets.AsNoTracking()
                .SingleAsync(x => x.CharacterId == id);
            Assert.Equal(0, root.MutationRevision);
            var saved = await new PostgresCharacterBuildStore(writer)
                .SetBaseAbilityScoreInputAsync(id, score, 14, DateTimeOffset.UnixEpoch);
            Assert.NotNull(saved);
            Assert.Equal(root.UpdatedAt, saved!.UpdatedAt);
            Assert.Equal(nativeToken + 1, saved.MutationRevision);
        }

        var attempted = await new PostgresCharacterInnateSpellLedgerStore(db)
            .CommitVerifiedAsync(Command(id, expectedCharacterRevision: nativeToken));
        Assert.Equal(InnateSpellLedgerStoreStatus.CharacterMutationConflict,
            attempted.Status);
        Assert.Null(await new PostgresCharacterInnateSpellLedgerStore(db).GetAsync(id));
        Assert.Empty(await new PostgresCharacterInnateSpellLedgerStore(db)
            .GetEventsAsync(id));
        var latest = (await new PostgresCharacterMutationRevisionReader(db)
            .GetRevisionAsync(id))!.Value;
        Assert.Equal(nativeToken + 1, latest);

        // Only a newly calculated native state pinned to the new aggregate
        // revision can initialize. The storage layer does not manufacture a
        // fresh candidate automatically from a previous snapshot.
        var afterReevaluation = await new PostgresCharacterInnateSpellLedgerStore(db)
            .CommitVerifiedAsync(Command(id, eventId: "ledger:recalculated",
                expectedCharacterRevision: latest));
        Assert.Equal(InnateSpellLedgerStoreStatus.Applied,
            afterReevaluation.Status);
        Assert.Equal(0, afterReevaluation.Revision);
        Assert.Single(await new PostgresCharacterInnateSpellLedgerStore(db)
            .GetEventsAsync(id));
    }

    [Fact]
    public async Task ClassProgressionMutationInvalidatesPreviouslyCalculatedFreeCastCourse()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeOriginAsync(db, id);
        var before = (await new PostgresCharacterMutationRevisionReader(db)
            .GetRevisionAsync(id))!.Value;
        await using (var writer = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var changed = await new PostgresCharacterBuildStore(writer)
                .ApplyClassAdvancementAsync(id, null, "class.warlock", 6,
                    null, DateTimeOffset.UnixEpoch);
            Assert.NotNull(changed);
            Assert.True(changed!.MutationRevision > before);
        }
        var stale = await new PostgresCharacterInnateSpellLedgerStore(db)
            .CommitVerifiedAsync(Command(id, expectedCharacterRevision: before));
        Assert.Equal(InnateSpellLedgerStoreStatus.CharacterMutationConflict,
            stale.Status);
        Assert.Null(await new PostgresCharacterInnateSpellLedgerStore(db).GetAsync(id));
    }

    [Fact]
    public async Task DeletingCharacterCascadesLedgerAndReceipts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await InitializeOriginAsync(db, id);
            Assert.Equal(InnateSpellLedgerStoreStatus.Applied,
                (await new PostgresCharacterInnateSpellLedgerStore(db)
                    .CommitVerifiedAsync(Command(id))).Status);
            await db.CharacterSheets.Where(x => x.CharacterId == id).ExecuteDeleteAsync();
        }
        await using var check = new CharacterSheetDbContext(database.CreateOptions());
        Assert.Empty(await check.InnateSpellLedgers.ToArrayAsync());
        Assert.Empty(await check.InnateSpellLedgerReceipts.ToArrayAsync());
    }
}
