using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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
            ledger ?? (transition ? Ledger(id, nextLedgerRevision, eventId)
                : Ledger(id, nextLedgerRevision)),
            expectedRevision, ledgerRevision, nextLedgerRevision, digest,
            eventId, "fixture:verified-source", transition, Now,
            transition ? SyntheticTransitionAudit(id, ledgerRevision, eventId) : null);

    private static string SyntheticTransitionAudit(Guid id, long previousRevision, string eventId) =>
        JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), expectedRevision = previousRevision,
            eventId,
            steps = new[]
            {
                new
                {
                    action = 0, targetInstanceId = "fixture.instance",
                    ruleOccurrence = new
                    {
                        source = new
                        {
                            kind = 0, id = "rule.synthetic.fixture", version = "test-v1"
                        },
                        occurrencePath = "actions.fixture"
                    },
                    basis = new { adjudication = 3, nativeRuleReference = "rule.synthetic.fixture" },
                    authorizationEvidenceId = "fixture:verified-source"
                }
            }
        });


    [Fact]
    public async Task AdditiveAuditMigrationPreservesUnauditedHistoryAndSupportsRollback()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var migrator = db.Database.GetService<IMigrator>();
        var beforeAudit = "20261115010000_AddCharacterSupernaturalState";
        var auditMigration = "20261115020000_AddSupernaturalTransitionRequestAudit";
        await migrator.MigrateAsync(beforeAudit);
        var id = Guid.NewGuid();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);

        // Seed the original schema using SQL. The current EF receipt model
        // necessarily includes the added column and must not write into the
        // pre-migration table. These are real old-format persisted receipts.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_supernatural_states (
                "CharacterId", "ContractVersion", "RulesetId", "Fingerprint",
                "AlternateSheetsJson", "PersistentInfluencesJson", "LedgerJson",
                "LedgerRevision", "DependencyFingerprint", "Revision",
                "CreatedAt", "UpdatedAt")
            VALUES (
                {id}, {1}, {"first-party-001"}, {"test-native-catalog"},
                CAST({"[]"} AS jsonb), CAST({"[]"} AS jsonb),
                CAST({Ledger(id, 1, "enter:legacy")} AS jsonb),
                {1L}, {new string('a', 64)}, {2L}, {Now}, {Now})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_supernatural_event_receipts (
                "CharacterId", "EventId", "CommittedRevision",
                "LedgerRevision", "Kind", "AuthorizationEvidenceId",
                "CreatedAt")
            VALUES ({id}, {"acquire:legacy"}, {1L}, {0L},
                {"acquisition"}, {"fixture:legacy-source"}, {Now}),
                ({id}, {"enter:legacy"}, {2L}, {1L},
                {"transition"}, {"fixture:legacy-source"}, {Now})
            """);
        Assert.DoesNotContain(auditMigration,
            await db.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync();
        Assert.Contains(auditMigration, await db.Database.GetAppliedMigrationsAsync());
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var receipts = await store.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, x => Assert.Null(x.TransitionRequestJson));
        Assert.Equal("transition", receipts[1].Kind);
        Assert.Equal("enter:legacy", receipts[1].EventId);
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);

        // Rollback removes only the additive column; no prior character
        // identity, ledger or receipt may be removed or rewritten.
        await migrator.MigrateAsync(beforeAudit);
        Assert.DoesNotContain(auditMigration,
            await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync();
        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var after = new PostgresCharacterSupernaturalStateStore(reload);
        Assert.Equal(2, (await after.GetAsync(id))!.Revision);
        var restored = await after.GetEventsAsync(id);
        Assert.Equal(new[] { "acquire:legacy", "enter:legacy" },
            restored.Select(x => x.EventId));
        Assert.All(restored, x => Assert.Null(x.TransitionRequestJson));
    }

    [Fact]
    public async Task DowngradingAfterAuditedTransitionCannotSilentlyDiscardStepHistory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);
        var command = Write(id, digest, 1, 0, 1, "enter:wolf", transition: true);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(command)).Status);

        var migrator = db.Database.GetService<IMigrator>();
        var beforeAudit = "20261115010000_AddCharacterSupernaturalState";
        var auditMigration = "20261115020000_AddSupernaturalTransitionRequestAudit";
        var downgrade = await Record.ExceptionAsync(
            () => migrator.MigrateAsync(beforeAudit));
        Assert.NotNull(downgrade);
        Assert.Contains("Cannot downgrade supernatural transition audit",
            downgrade.ToString(), StringComparison.Ordinal);
        Assert.Contains(auditMigration, await db.Database.GetAppliedMigrationsAsync());

        // A rejected schema downgrade must not damage the originally
        // committed ledger, event identities or the JSONB transition audit.
        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var persisted = new PostgresCharacterSupernaturalStateStore(reload);
        var state = (await persisted.GetAsync(id))!;
        Assert.Equal(2, state.Revision);
        Assert.Equal(1, state.LedgerRevision);
        var receipts = await persisted.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        Assert.Null(receipts[0].TransitionRequestJson);
        using var actual = JsonDocument.Parse(receipts[1].TransitionRequestJson!);
        using var expected = JsonDocument.Parse(command.TransitionRequestJson!);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

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
    public async Task TransitionStepAuditSurvivesReloadAndRejectsMissingOrMismatchedEvents()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        var valid = Write(id, digest, 1, 0, 1, "enter:wolf", transition: true);
        Assert.Throws<ArgumentException>(() => store.CommitVerifiedAsync(
            valid with { TransitionRequestJson = null }).GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => store.CommitVerifiedAsync(
            valid with { TransitionRequestJson = SyntheticTransitionAudit(
                id, 0, "wrong:event") }).GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => store.CommitVerifiedAsync(
            valid with { TransitionRequestJson = SyntheticTransitionAudit(
                id, 5, "enter:wolf") }).GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => store.CommitVerifiedAsync(
            valid with
            {
                TransitionRequestJson = valid.TransitionRequestJson!.Replace(
                    "\"eventId\":\"enter:wolf\"",
                    "\"eventId\":\"enter:wolf\",\"eventId\":\"enter:wolf\"",
                    StringComparison.Ordinal)
            }).GetAwaiter().GetResult());
        Assert.Throws<ArgumentException>(() => store.CommitVerifiedAsync(
            valid with
            {
                TransitionRequestJson = valid.TransitionRequestJson!.Replace(
                    "\"occurrencePath\":\"actions.fixture\"",
                    "\"occurrencePath\":\"\"",
                    StringComparison.Ordinal)
            }).GetAwaiter().GetResult());

        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(valid)).Status);

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var verified = new PostgresCharacterSupernaturalStateStore(reload);
        var receipts = await verified.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        Assert.Null(receipts[0].TransitionRequestJson);
        Assert.NotNull(receipts[1].TransitionRequestJson);
        using var expected = JsonDocument.Parse(valid.TransitionRequestJson!);
        using var actual = JsonDocument.Parse(receipts[1].TransitionRequestJson!);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal("enter:wolf", actual.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(0, actual.RootElement.GetProperty("expectedRevision").GetInt64());
        Assert.Single(actual.RootElement.GetProperty("steps").EnumerateArray());
        Assert.Equal(2, (await verified.GetAsync(id))!.Revision);
    }

    [Fact]
    public async Task AcquisitionRejectsForgedTransitionStepsWithoutCreatingNewReceipt()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var forged = Write(id, digest) with
        {
            TransitionRequestJson = SyntheticTransitionAudit(id, 0, "acquire:wolf")
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(forged));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
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

    private static string ActivationLedger(Guid id, long revision,
        string[] events, object[] instances, object[]? resources = null) =>
        JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision, instances,
            resources = resources ?? Array.Empty<object>(),
            processedEventIds = events
        });

    private static object Instance(Guid id, string lastEvent, int lifecycle = 0,
        int capture = 9) => new
    {
        characterId = id.ToString("D"),
        id = "manifest.wolf", sheetId = "wolf",
        activationEventId = "enter:wolf", lastEventId = lastEvent,
        activationEvidenceId = "receipt:activation",
        lifecycle, contribution = 0,
        activationCaptures = new { amount = capture },
        definitionId = "form.test.wolf"
    };

    private static object Resource(int current = 4, int ownerKind = 3) => new
    {
        id = "resource.wolf", ownerId = "manifest.wolf", ownerKind,
        current, maximum = 10, evidenceId = "receipt:resource"
    };

    [Fact]
    public async Task ActivationAndReversionPreserveImmutableOwnersAndPriorEventsAcrossReload()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        string digest;
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var (store, inputDigest) = await InitializeAsync(db, id);
            digest = inputDigest;
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(Write(id, digest))).Status);

            var active = ActivationLedger(id, 1, ["enter:wolf"], [Instance(id, "enter:wolf")],
                [Resource()]);
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                    "enter:wolf", transition: true, ledger: active))).Status);

            var ended = ActivationLedger(id, 2,
                ["enter:wolf", "revert:wolf"], [Instance(id, "revert:wolf", 1)], [Resource()]);
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(Write(id, digest, 2, 1, 2,
                    "revert:wolf", transition: true, ledger: ended))).Status);
        }

        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var persisted = new PostgresCharacterSupernaturalStateStore(reloaded);
        var state = (await persisted.GetAsync(id))!;
        Assert.Equal(3, state.Revision);
        Assert.Equal(2, state.LedgerRevision);
        Assert.Contains("wolf-progression", state.AlternateSheetsJson);
        using var journal = JsonDocument.Parse(state.LedgerJson);
        Assert.Equal(new[] { "enter:wolf", "revert:wolf" },
            journal.RootElement.GetProperty("processedEventIds")
                .EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(1, journal.RootElement.GetProperty("instances")[0]
            .GetProperty("lifecycle").GetInt32());
        Assert.Equal(3, (await persisted.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task TransitionCanNotRewriteSheetOrLoseOrReorderPastManifestationEvents()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                "enter:wolf", transition: true,
                ledger: ActivationLedger(id, 1, ["enter:wolf"],
                    [Instance(id, "enter:wolf")])))).Status);

        var droppedEvents = ActivationLedger(id, 2, ["revert:wolf"],
            [Instance(id, "revert:wolf", 1)]);
        var reordersEvents = ActivationLedger(id, 2,
            ["revert:wolf", "enter:wolf"], [Instance(id, "revert:wolf", 1)]);
        var forgedCapture = ActivationLedger(id, 2,
            ["enter:wolf", "revert:wolf"], [Instance(id, "revert:wolf", 1, capture: 100)]);
        foreach (var journal in new[] { droppedEvents, reordersEvents, forgedCapture })
            await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
                Write(id, digest, 2, 1, 2, "revert:wolf", transition: true, ledger: journal)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 2, 1, 2, "revert:wolf", transition: true,
                ledger: ActivationLedger(id, 2,
                    ["enter:wolf", "revert:wolf"], [Instance(id, "revert:wolf", 1)]),
                alternate: WolfSheet.Replace("wolf-progression", "fabricated-grant",
                    StringComparison.Ordinal))));

        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task TransitionCanNotChangeInteractionsWithoutAuditedReplacement()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        const string interaction = """
            [{"id":"fixture.interaction","kind":1,
              "instanceIds":["manifest.wolf"],"selectedInstanceId":"manifest.wolf",
              "definitionOccurrence":{
                "source":{"kind":0,"id":"rule.synthetic.fixture","version":"test-v1"},
                "occurrencePath":"interaction.fixture"},
              "basis":{"adjudication":3,"nativeRuleReference":"rule.synthetic.fixture"},
              "authorizationEvidenceId":"fixture:rule-decision"}]
            """;
        var firstLedger = JsonNode.Parse(Ledger(id, 1, "enter:wolf"))!.AsObject();
        firstLedger["interactions"] = JsonNode.Parse(interaction);
        var unapproved = Write(id, digest, 1, 0, 1, "enter:wolf",
            transition: true, ledger: firstLedger.ToJsonString());

        // A valid event and per-step audit do not grant permission to rewrite
        // the separately authorized simultaneous-form interaction decisions.
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CommitVerifiedAsync(unapproved));
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));

        var authorizedRequest = JsonNode.Parse(unapproved.TransitionRequestJson!)!.AsObject();
        authorizedRequest["replacementInteractions"] = JsonNode.Parse(interaction);
        var approvedFixture = unapproved with
        {
            TransitionRequestJson = authorizedRequest.ToJsonString()
        };
        // The internal store trusts the already-verified command: only a
        // real Core planner plus independently validating DM/native authority
        // may create the audited replacement on the production path.
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(approvedFixture)).Status);
        using var recorded = JsonDocument.Parse(
            (await store.GetEventsAsync(id))[1].TransitionRequestJson!);
        Assert.True(recorded.RootElement.TryGetProperty("replacementInteractions", out _));

        var nextLedger = JsonNode.Parse(Ledger(id, 2, "enter:wolf", "revert:wolf"))!.AsObject();
        // Omitting the previously active interaction without a replacement is
        // equally unauthorized, even if an End step happens in the event.
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CommitVerifiedAsync(Write(id, digest, 2, 1, 2,
                "revert:wolf", transition: true, ledger: nextLedger.ToJsonString())));
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task AcquisitionPreservesPreviouslyEarnedProgressionAndExistingLedger()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:second",
                alternate: WolfSheet.Replace("wolf-progression", "replacement",
                    StringComparison.Ordinal))));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:second", alternate: "[]")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:second",
                ledger: Ledger(id, 0, "invented-event"))));

        // Adding a distinct owned sheet can be an acquisition without
        // activating either form or reassigning the original advancement.
        var prior = WolfSheet.TrimEnd();
        var additional = prior[..^1] +
            """,{"id":"hybrid","parentId":"wolf","definitionId":"test.hybrid"}]""";
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 0,
                "acquire:hybrid", alternate: additional))).Status);
        var updated = (await store.GetAsync(id))!;
        Assert.Contains("wolf-progression", updated.AlternateSheetsJson);
        Assert.Contains("hybrid", updated.AlternateSheetsJson);
        Assert.Equal(0, updated.LedgerRevision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task FormSwitchCanNotModifyExistingResourceQuantityOrMintPrimaryPool()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        var active = ActivationLedger(id, 1, ["enter:wolf"],
            [Instance(id, "enter:wolf")], [Resource()]);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                "enter:wolf", transition: true, ledger: active))).Status);

        var altered = ActivationLedger(id, 2,
            ["enter:wolf", "revert:wolf"], [Instance(id, "revert:wolf", 1)],
            [Resource(current: 5)]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 2, 1, 2, "revert:wolf",
                transition: true, ledger: altered)));
        var inventedPrimary = ActivationLedger(id, 2,
            ["enter:wolf", "revert:wolf"], [Instance(id, "revert:wolf", 1)],
            [Resource(), new
            {
                id = "resource.cheat", ownerKind = 0, ownerId = "@primary",
                current = 9, maximum = 9
            }]);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 2, 1, 2, "revert:wolf",
                transition: true, ledger: inventedPrimary)));
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
    }

    [Fact]
    public async Task FirstAcquisitionCannotHideAnExistingManifestationOrForgedPastEvent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 0, 0, 1, "enter:wolf", transition: true)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, ledger: Ledger(id, 0, "claimed:old-event"))));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
    }

    private static string InitialOwnerResourceLedger(Guid id, int ownerKind, string ownerId) =>
        JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), processedEventIds = Array.Empty<string>(),
            resources = new[]
            {
                new
                {
                    id = "resource.initial",
                    definitionId = "resource.definition.initial",
                    sourceOccurrence = new
                    {
                        source = new { kind = 0, id = "test.lycanthropy", version = "v1" },
                        occurrencePath = "resources.initial"
                    },
                    evidenceId = "fixture:resource-acquired",
                    ownerKind, ownerId, current = 2, maximum = 5
                }
            }
        });

    [Theory]
    [InlineData(0, "@primary", false)]
    [InlineData(1, "wolf", false)]
    [InlineData(2, "curse", true)]
    public async Task FirstAcquisitionCanPersistSourceQualifiedPrimarySheetOrInfluenceResource(
        int ownerKind, string ownerId, bool influenceOwner)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var command = Write(id, digest,
            ledger: InitialOwnerResourceLedger(id, ownerKind, ownerId));
        if (influenceOwner)
            command = command with { PersistentInfluencesJson = """[{"id":"curse"}]""" };
        var result = await store.CommitVerifiedAsync(command);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied, result.Status);

        var saved = (await store.GetAsync(id))!;
        using var journal = JsonDocument.Parse(saved.LedgerJson);
        var pool = Assert.Single(journal.RootElement.GetProperty("resources").EnumerateArray());
        Assert.Equal(ownerKind, pool.GetProperty("ownerKind").GetInt32());
        Assert.Equal(ownerId, pool.GetProperty("ownerId").GetString());
        Assert.Equal(2, pool.GetProperty("current").GetInt32());
        Assert.Equal(5, pool.GetProperty("maximum").GetInt32());
        Assert.Equal(0, saved.LedgerRevision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Theory]
    [InlineData(3, "manifest.wolf")]
    [InlineData(1, "foreign-sheet")]
    [InlineData(2, "foreign-influence")]
    public async Task FirstAcquisitionRejectsUnownedOrActivationOnlyResource(
        int ownerKind, string ownerId)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, ledger: InitialOwnerResourceLedger(id, ownerKind, ownerId))));
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task SeparateAcquisitionCanNotRewriteExistingSheetOverridesOrApplicability()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);

        var original = JsonNode.Parse(WolfSheet)!.AsArray();
        original[0]!["overrides"] = JsonNode.Parse("""
            {"abilityScores":{"replace":false,"entries":{"strength":17}}}
            """);
        original[0]!["grantApplicability"] = JsonNode.Parse("""
            [{"grantId":"grant.test","state":1}]
            """);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest,
                alternate: original.ToJsonString()))).Status);

        foreach (var change in new Action<JsonArray>[]
        {
            sheet => sheet[0]!["overrides"]!["abilityScores"]!["entries"]!["strength"] = 99,
            sheet => sheet[0]!["grantApplicability"]![0]!["state"] = 0,
            sheet => sheet[0]!["newCapability"] = "forged",
            sheet => sheet[0]!.AsObject().Remove("overrides")
        })
        {
            var changed = JsonNode.Parse(original.ToJsonString())!.AsArray();
            change(changed);
            await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
                Write(id, digest, 1, 0, 0, "acquire:extra",
                    alternate: changed.ToJsonString())));
        }
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task SeparateAcquisitionCanAppendProgressionButNotRewriteInfluenceExpression()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        const string initial = """
            [{"id":"curse","definitionId":"test.curse",
              "definitionSource":{"kind":0,"id":"source.curse","version":"v1"},
              "acquisitionEvidenceId":"receipt.acquire.curse",
              "expressionEvidenceId":"receipt.expression.zero",
              "expression":{"intensity":"0"},"effects":[]}]
            """;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest) with
            {
                PersistentInfluencesJson = initial
            })).Status);

        var changed = JsonNode.Parse(initial)!.AsArray();
        changed[0]!["expression"]!["intensity"] = "3";
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:extra") with
            {
                PersistentInfluencesJson = changed.ToJsonString()
            }));

        changed = JsonNode.Parse(initial)!.AsArray();
        changed[0]!["effects"] = JsonNode.Parse("""
            [{"id":"effect.forged","definitionId":"test.effect"}]
            """);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:extra") with
            {
                PersistentInfluencesJson = changed.ToJsonString()
            }));

        // An existing sheet can earn a new independent progression occurrence
        // without replacing its prior progression/definition.
        var augmentedSheets = JsonNode.Parse(WolfSheet)!.AsArray();
        augmentedSheets[0]!["progression"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"wolf-progress-extra","definitionId":"test.extra",
             "acquisitionEvidenceId":"receipt.progress.extra","grants":[]}
            """));
        var result = await store.CommitVerifiedAsync(
            Write(id, digest, 1, 0, 0, "acquire:extra",
                alternate: augmentedSheets.ToJsonString()) with
            {
                PersistentInfluencesJson = initial
            });
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied, result.Status);
        Assert.Contains("wolf-progress-extra", (await store.GetAsync(id))!.AlternateSheetsJson);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
    }
}
