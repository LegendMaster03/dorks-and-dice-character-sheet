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
        string? ledger = null, string? alternate = null)
    {
        var candidate = ledger ?? (transition
            ? DefaultTransitionLedger(id, nextLedgerRevision, eventId)
            : Ledger(id, nextLedgerRevision));
        return new(id, "first-party-001", "test-native-catalog",
            alternate ?? WolfSheet, "[]", candidate,
            expectedRevision, ledgerRevision, nextLedgerRevision, digest,
            eventId, "fixture:verified-source", transition, Now,
            transition ? SyntheticTransitionAudit(id, ledgerRevision, eventId, candidate) : null);
    }

    private static string DefaultTransitionLedger(
        Guid id, long revision, string eventId) =>
        JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision,
            instances = new[]
            {
                new
                {
                    characterId = id.ToString("D"),
                    id = "fixture.instance." + eventId,
                    sheetId = "wolf", definitionId = "form.test.wolf",
                    activationEventId = eventId, lastEventId = eventId,
                    activationEvidenceId = "fixture:verified-source",
                    lifecycle = 0, contribution = 0,
                    activationCaptures = new { amount = 9 }
                }
            },
            resources = Array.Empty<object>(),
            processedEventIds = new[] { eventId }
        });

    private static string SyntheticTransitionAudit(
        Guid id, long previousRevision, string eventId, string? candidateLedger = null)
    {
        // Fixtures explicitly describe any newly minted resource in the
        // event audit. The persisted store must never infer real authority
        // from the proposed ledger, even though this isolated test builder
        // conveniently derives the expected controlled payload.
        using var ledger = candidateLedger is null ? null : JsonDocument.Parse(candidateLedger);
        var root = ledger?.RootElement;
        JsonElement? newInstance = null;
        if (root is { } current &&
            current.TryGetProperty("instances", out var instances))
            foreach (var instance in instances.EnumerateArray())
                if (instance.ValueKind == JsonValueKind.Object &&
                    instance.TryGetProperty("activationEventId", out var activated) &&
                    activated.ValueKind == JsonValueKind.String &&
                    activated.GetString() == eventId)
                {
                    newInstance = instance;
                    break;
                }

        var target = newInstance is JsonElement activation
            ? activation.GetProperty("id").GetString()!
            : root is { } currentWithInstances &&
                currentWithInstances.TryGetProperty("instances", out var existing) &&
                existing.GetArrayLength() > 0
                ? existing[0].GetProperty("id").GetString()!
                : "fixture.instance";
        var action = newInstance is not null ? 0 :
            eventId.StartsWith("revert:", StringComparison.Ordinal) ? 1 :
            eventId.StartsWith("suppress:", StringComparison.Ordinal) ? 2 :
            eventId.StartsWith("restore:", StringComparison.Ordinal) ? 3 : 0;
        var newResources = new List<JsonElement>();
        if (newInstance is not null && root is { } state &&
            state.TryGetProperty("resources", out var resources))
            foreach (var resource in resources.EnumerateArray())
                if (resource.ValueKind == JsonValueKind.Object &&
                    resource.TryGetProperty("ownerId", out var ownerId) &&
                    ownerId.ValueKind == JsonValueKind.String &&
                    ownerId.GetString() == target)
                    newResources.Add(resource);

        return JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), expectedRevision = previousRevision,
            eventId,
            steps = new[]
            {
                new
                {
                    action, targetInstanceId = target,
                    newInstance, endingConditionId = action == 1
                        ? "rule.synthetic.revert" : null,
                    ruleOccurrence = new
                    {
                        source = new
                        {
                            kind = 0, id = "rule.synthetic.fixture", version = "test-v1"
                        },
                        occurrencePath = "actions.fixture"
                    },
                    basis = new { adjudication = 3, nativeRuleReference = "rule.synthetic.fixture" },
                    authorizationEvidenceId = "fixture:verified-source",
                    newResources = action == 0 ? newResources : null
                }
            }
        });
    }


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

        // Seed the historical root with SQL: the current EF model expects
        // MutationRevision, which this deliberately pre-Pillar-2 schema lacks.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_sheet_roots (
                "CharacterId", "SchemaVersion", "BuilderStatus",
                "CreatedAt", "UpdatedAt")
            VALUES ({id}, {CharacterSheetRoot.CurrentSchemaVersion},
                {"BuildInProgress"}, {Now}, {Now})
            """);

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

        // A newer additive migration can roll back before the older audit
        // guard refuses its own downgrade. Restore the latest schema before
        // querying with the current EF model; the original audit remains.
        await migrator.MigrateAsync();

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
    public async Task DowngradingAfterExpressionChangeCannotDiscardItsImmutableAudit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var acquired = Write(id, digest) with
        {
            PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage1")
        };
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(acquired)).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 1, "2"))).Status);

        var migrator = db.Database.GetService<IMigrator>();
        var downgrade = await Record.ExceptionAsync(() =>
            migrator.MigrateAsync("20261115020000_AddSupernaturalTransitionRequestAudit"));
        Assert.NotNull(downgrade);
        Assert.Contains("Cannot discard durable influence expression-change history",
            downgrade.ToString(), StringComparison.Ordinal);
        Assert.Contains("20261117090000_AddInfluenceExpressionChangeAudit",
            await db.Database.GetAppliedMigrationsAsync());
        var persisted = await store.GetEventsAsync(id);
        Assert.Equal("expression-change", persisted[1].Kind);
        Assert.NotNull(persisted[1].ExpressionChangeJson);
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
    public async Task CorruptHistoricalReceiptsCannotAuthorizeAnotherSupernaturalCommit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                "enter:wolf", transition: true))).Status);
        var before = (await store.GetAsync(id))!;
        var next = Write(id, digest, 2, 1, 1, "acquire:later",
            ledger: before.LedgerJson);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE character_supernatural_event_receipts
            SET "Kind" = {"acquisition"}
            WHERE "CharacterId" = {id} AND "EventId" = {"enter:wolf"}
            """);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CommitVerifiedAsync(next));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE character_supernatural_event_receipts
            SET "Kind" = {"transition"}, "LedgerRevision" = {0L}
            WHERE "CharacterId" = {id} AND "EventId" = {"enter:wolf"}
            """);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CommitVerifiedAsync(next));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE character_supernatural_event_receipts
            SET "LedgerRevision" = {1L}, "CommittedRevision" = {4L}
            WHERE "CharacterId" = {id} AND "EventId" = {"enter:wolf"}
            """);
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CommitVerifiedAsync(next));

        // Failed writes never changed the authoritative state or journal.
        Assert.Equal(before.Revision, (await store.GetAsync(id))!.Revision);
        Assert.Equal(before.LedgerJson, (await store.GetAsync(id))!.LedgerJson);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE character_supernatural_event_receipts
            SET "CommittedRevision" = {2L}
            WHERE "CharacterId" = {id} AND "EventId" = {"enter:wolf"}
            """);
        var applied = await store.CommitVerifiedAsync(next);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied, applied.Status);
        Assert.Equal(3, (await store.GetAsync(id))!.Revision);
        Assert.Equal(1, (await store.GetAsync(id))!.LedgerRevision);
        Assert.Equal(new[] { "acquire:wolf", "enter:wolf", "acquire:later" },
            (await store.GetEventsAsync(id)).Select(x => x.EventId));
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
    public async Task OrderedStepAuditMustMatchEachCommittedInstanceLifecycleDelta()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        var activated = ActivationLedger(id, 1, ["enter:wolf"],
            [Instance(id, "enter:wolf")]);
        var validActivation = Write(id, digest, 1, 0, 1, "enter:wolf",
            transition: true, ledger: activated);
        using (var actual = JsonDocument.Parse(validActivation.TransitionRequestJson!))
        {
            Assert.Equal("manifest.wolf",
                actual.RootElement.GetProperty("steps")[0]
                    .GetProperty("newInstance").GetProperty("id").GetString());
        }

        var alteredInstance = JsonNode.Parse(validActivation.TransitionRequestJson!)!.AsObject();
        alteredInstance["steps"]![0]!["newInstance"]!["activationCaptures"]!["amount"] = 100;
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validActivation with { TransitionRequestJson = alteredInstance.ToJsonString() }));

        var conflictingAction = JsonNode.Parse(validActivation.TransitionRequestJson!)!.AsObject();
        conflictingAction["steps"]![0]!["action"] = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validActivation with { TransitionRequestJson = conflictingAction.ToJsonString() }));

        var duplicateStep = JsonNode.Parse(validActivation.TransitionRequestJson!)!.AsObject();
        duplicateStep["steps"]!.AsArray().Add(duplicateStep["steps"]![0]!.DeepClone());
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validActivation with { TransitionRequestJson = duplicateStep.ToJsonString() }));

        var addedInstance = JsonNode.Parse(activated)!.AsObject();
        var invented = addedInstance["instances"]![0]!.DeepClone();
        invented["id"] = "manifest.extra";
        addedInstance["instances"]!.AsArray().Add(invented);
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validActivation with { LedgerJson = addedInstance.ToJsonString() }));

        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(validActivation)).Status);

        var ended = ActivationLedger(id, 2, ["enter:wolf", "revert:wolf"],
            [Instance(id, "revert:wolf", 1)]);
        var validEnd = Write(id, digest, 2, 1, 2, "revert:wolf",
            transition: true, ledger: ended);
        var falseSuppression = JsonNode.Parse(validEnd.TransitionRequestJson!)!.AsObject();
        falseSuppression["steps"]![0]!["action"] = 2;
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validEnd with { TransitionRequestJson = falseSuppression.ToJsonString() }));

        var wrongTarget = JsonNode.Parse(validEnd.TransitionRequestJson!)!.AsObject();
        wrongTarget["steps"]![0]!["targetInstanceId"] = "manifest.missing";
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validEnd with { TransitionRequestJson = wrongTarget.ToJsonString() }));

        var unrecordedEnd = JsonNode.Parse(ended)!.AsObject();
        unrecordedEnd["instances"]![0]!["lastEventId"] = "enter:wolf";
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            validEnd with { LedgerJson = unrecordedEnd.ToJsonString() }));

        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(validEnd)).Status);

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var durable = new PostgresCharacterSupernaturalStateStore(reload);
        Assert.Equal(3, (await durable.GetAsync(id))!.Revision);
        var receipts = await durable.GetEventsAsync(id);
        Assert.Equal(new[] { "acquire:wolf", "enter:wolf", "revert:wolf" },
            receipts.Select(x => x.EventId));
        using var endAudit = JsonDocument.Parse(receipts[2].TransitionRequestJson!);
        Assert.Equal(1, endAudit.RootElement.GetProperty("steps")[0]
            .GetProperty("action").GetInt32());
    }

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
        var firstLedger = JsonNode.Parse(ActivationLedger(id, 1,
            ["enter:wolf"], [Instance(id, "enter:wolf")]))!.AsObject();
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


    private static string SyntheticInfluence(string severity, string evidence) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "curse.influence", definitionId = "test.curse",
                acquisitionEvidenceId = "fixture:acquired",
                expressionEvidenceId = evidence,
                expression = new Dictionary<string, string> { ["severity"] = severity },
                effects = Array.Empty<object>(),
                progression = new[] { new { id = "curse.earned", grants = Array.Empty<object>() } },
                grantApplicability = Array.Empty<object>()
            }
        });

    private static VerifiedSupernaturalStateWrite ExpressionChange(
        Guid id, string digest, long expectedRevision, string severity,
        string previousSeverity = "1", string previousEvidence = "fixture:stage1",
        string nextEvidence = "fixture:stage2")
    {
        var eventId = "expression:curse:" + expectedRevision;
        var audit = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), eventId, influenceId = "curse.influence",
            previousExpressionEvidenceId = previousEvidence,
            nextExpressionEvidenceId = nextEvidence,
            previousExpression = new { severity = previousSeverity },
            nextExpression = new { severity }
        });
        return Write(id, digest, expectedRevision, 0, 0, eventId) with
        {
            PersistentInfluencesJson = SyntheticInfluence(severity, nextEvidence),
            IsExpressionChange = true, ExpressionChangeJson = audit
        };
    }

    [Fact]
    public async Task ExpressionChangePreservesEarnedProgressionAndSurvivesReload()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var acquired = Write(id, digest) with
        {
            PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage1")
        };
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(acquired)).Status);

        var increase = ExpressionChange(id, digest, 1, "3");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(increase)).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.DuplicateEvent,
            (await store.CommitVerifiedAsync(increase)).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.RevisionConflict,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 1, "4") with
            {
                EventId = "expression:stale",
                ExpressionChangeJson = ExpressionChange(id, digest, 1, "4")
                    .ExpressionChangeJson!.Replace("expression:curse:1",
                        "expression:stale", StringComparison.Ordinal)
            })).Status);

        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(reloaded);
        var updated = (await fresh.GetAsync(id))!;
        Assert.Equal(2, updated.Revision);
        Assert.Equal(0, updated.LedgerRevision);
        Assert.Contains("curse.earned", updated.PersistentInfluencesJson);
        using var influence = JsonDocument.Parse(updated.PersistentInfluencesJson);
        Assert.Equal("3", influence.RootElement[0]
            .GetProperty("expression").GetProperty("severity").GetString());
        var receipts = await fresh.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        Assert.Equal("expression-change", receipts[1].Kind);
        Assert.Null(receipts[1].TransitionRequestJson);
        using var audit = JsonDocument.Parse(receipts[1].ExpressionChangeJson!);
        Assert.Equal("curse.influence",
            audit.RootElement.GetProperty("influenceId").GetString());
        Assert.Equal("3",
            audit.RootElement.GetProperty("nextExpression").GetProperty("severity").GetString());

        var decrease = ExpressionChange(id, digest, 2, "0", "3",
            "fixture:stage2", "fixture:stage3");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await fresh.CommitVerifiedAsync(decrease)).Status);
        Assert.Contains("curse.earned", (await fresh.GetAsync(id))!.PersistentInfluencesJson);
        Assert.Equal(3, (await fresh.GetEventsAsync(id)).Count);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("wrong-character")]
    [InlineData("wrong-event")]
    [InlineData("wrong-influence")]
    [InlineData("wrong-evidence")]
    [InlineData("invalid-dimension")]
    [InlineData("broken-expression-chain")]
    [InlineData("broken-evidence-chain")]
    [InlineData("latest-audit-current-mismatch")]
    [InlineData("ambiguous-audit-field")]
    [InlineData("missing-expression-field")]
    public async Task CorruptHistoricalExpressionAuditBlocksNextPostgreSqlCommit(string mutation)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var acquired = Write(id, digest) with
        {
            PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage1")
        };
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(acquired)).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 1, "3"))).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 2, "0", "3",
                "fixture:stage2", "fixture:stage3"))).Status);

        var tamperLatest = mutation is "latest-audit-current-mismatch"
            or "broken-evidence-chain" or "ambiguous-audit-field"
            or "missing-expression-field";
        var targetEvent = tamperLatest ? "expression:curse:2" : "expression:curse:1";
        var original = Assert.Single((await store.GetEventsAsync(id)),
            receipt => receipt.EventId == targetEvent);
        var audit = JsonNode.Parse(original.ExpressionChangeJson!)!.AsObject();
        switch (mutation)
        {
            case "empty":
                audit = new JsonObject();
                break;
            case "wrong-character":
                audit["characterId"] = Guid.NewGuid().ToString("D");
                break;
            case "wrong-event":
                audit["eventId"] = "forged:event";
                break;
            case "wrong-influence":
                audit["influenceId"] = "forged:influence";
                break;
            case "wrong-evidence":
                audit["nextExpressionEvidenceId"] = "forged:evidence";
                break;
            case "invalid-dimension":
                audit["nextExpression"]!["severity"] = 42;
                break;
            case "broken-expression-chain":
                audit["nextExpression"]!["severity"] = "9";
                break;
            case "broken-evidence-chain":
                audit["previousExpressionEvidenceId"] = "forged:previous";
                break;
            case "latest-audit-current-mismatch":
                audit["nextExpression"]!["severity"] = "9";
                break;
            case "ambiguous-audit-field":
                audit["EventId"] = "forged:alias";
                break;
            case "missing-expression-field":
                audit.Remove("previousExpression");
                break;
            default:
                throw new InvalidOperationException("Unknown adversarial mutation.");
        }

        var corrupt = audit.ToJsonString();
        Assert.Equal(1, await db.SupernaturalEventReceipts
            .Where(x => x.CharacterId == id && x.EventId == targetEvent)
            .ExecuteUpdateAsync(update => update
                .SetProperty(x => x.ExpressionChangeJson, corrupt)));

        // Reload the corrupted receipt with a genuinely fresh EF context.
        await using var freshDb = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(freshDb);
        var snapshot = (await fresh.GetAsync(id))!;
        var attempted = ExpressionChange(id, digest, 3, "5", "0",
            "fixture:stage3", "fixture:stage4");
        await Assert.ThrowsAsync<ArgumentException>(
            () => fresh.CommitVerifiedAsync(attempted));
        var unchanged = (await fresh.GetAsync(id))!;
        Assert.Equal(3, unchanged.Revision);
        Assert.Equal(snapshot.PersistentInfluencesJson, unchanged.PersistentInfluencesJson);
        Assert.Equal(3, (await fresh.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task ValidHistoricalExpressionEvidenceChainSurvivesFreshContextReload()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest) with
            {
                PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage1")
            })).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 1, "3"))).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(ExpressionChange(id, digest, 2, "0", "3",
                "fixture:stage2", "fixture:stage3"))).Status);
        await using var freshDb = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(freshDb);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await fresh.CommitVerifiedAsync(ExpressionChange(id, digest, 3, "4", "0",
                "fixture:stage3", "fixture:stage4"))).Status);
        var state = (await fresh.GetAsync(id))!;
        Assert.Equal(4, state.Revision);
        Assert.Equal(0, state.LedgerRevision);
        Assert.Contains("curse.earned", state.PersistentInfluencesJson);
        Assert.Equal(4, (await fresh.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task InterleavedExpressionHistoriesValidateIndependentlyAfterDatabaseReload()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);

        var influences = JsonNode.Parse(SyntheticInfluence("1", "fixture:stage1"))!.AsArray();
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest) with
            {
                PersistentInfluencesJson = influences.ToJsonString()
            })).Status);

        // The second influence is acquired independently in a later event.
        var other = influences[0]!.DeepClone().AsObject();
        other["id"] = "curse.second";
        other["acquisitionEvidenceId"] = "fixture:acquired-second";
        other["expressionEvidenceId"] = "fixture:other-stage1";
        other["progression"]![0]!["id"] = "curse.other-earned";
        influences.Add(other);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 0,
                "acquire:second") with
            {
                PersistentInfluencesJson = influences.ToJsonString()
            })).Status);

        string current = influences.ToJsonString();
        VerifiedSupernaturalStateWrite Change(
            long revision, string target, string eventId, string severity, string evidence)
        {
            var items = JsonNode.Parse(current)!.AsArray();
            var owner = Assert.Single(items.OfType<JsonObject>(),
                value => value["id"]!.GetValue<string>() == target);
            var oldExpression = owner["expression"]!.DeepClone();
            var oldEvidence = owner["expressionEvidenceId"]!.GetValue<string>();
            var newExpression = new JsonObject { ["severity"] = severity };
            owner["expression"] = newExpression.DeepClone();
            owner["expressionEvidenceId"] = evidence;
            var audit = new JsonObject
            {
                ["characterId"] = id.ToString("D"),
                ["eventId"] = eventId,
                ["influenceId"] = target,
                ["previousExpressionEvidenceId"] = oldEvidence,
                ["nextExpressionEvidenceId"] = evidence,
                ["previousExpression"] = oldExpression,
                ["nextExpression"] = newExpression
            };
            current = items.ToJsonString();
            return Write(id, digest, revision, 0, 0, eventId) with
            {
                PersistentInfluencesJson = current,
                IsExpressionChange = true,
                ExpressionChangeJson = audit.ToJsonString()
            };
        }

        var first = Change(2, "curse.influence", "change:first:1",
            "3", "fixture:stage2");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(first)).Status);
        var second = Change(3, "curse.second", "change:second:1",
            "2", "fixture:other-stage2");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(second)).Status);

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(reload);
        var third = Change(4, "curse.influence", "change:first:2",
            "0", "fixture:stage3");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await fresh.CommitVerifiedAsync(third)).Status);
        var fourth = Change(5, "curse.second", "change:second:2",
            "4", "fixture:other-stage3");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await fresh.CommitVerifiedAsync(fourth)).Status);

        var saved = (await fresh.GetAsync(id))!;
        Assert.Equal(6, saved.Revision);
        Assert.Equal(0, saved.LedgerRevision);
        using var document = JsonDocument.Parse(saved.PersistentInfluencesJson);
        Assert.Equal("0", document.RootElement[0].GetProperty("expression")
            .GetProperty("severity").GetString());
        Assert.Equal("4", document.RootElement[1].GetProperty("expression")
            .GetProperty("severity").GetString());
        Assert.Equal(6, (await fresh.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task ExpressionChangeRejectsForgedScopeAndUnauditedMutations()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var acquired = Write(id, digest) with
        {
            PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage1")
        };
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(acquired)).Status);

        var changed = ExpressionChange(id, digest, 1, "2");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with { ExpressionChangeJson = null }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with { IsExpressionChange = false }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with { IsTransition = true }));
        var forgedAudit = JsonNode.Parse(changed.ExpressionChangeJson!)!.AsObject();
        forgedAudit["previousExpression"]!["severity"] = "9";
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with
            {
                ExpressionChangeJson = forgedAudit.ToJsonString()
            }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with
            {
                PersistentInfluencesJson = changed.PersistentInfluencesJson.Replace(
                    "curse.earned", "forged.earned", StringComparison.Ordinal)
            }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with
            {
                AlternateSheetsJson = "[]"
            }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with
            {
                LedgerJson = Ledger(id, 0, "forged:transition")
            }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(changed with
            {
                PersistentInfluencesJson = SyntheticInfluence("1", "fixture:stage2")
            }));
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
    }


    [Fact]
    public async Task InfluenceExpressionChangesDuringActiveFormWithoutMutatingResourcesOrReversion()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        var expressionAtAcquisition = SyntheticInfluence("1", "fixture:stage1");
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest) with
            {
                PersistentInfluencesJson = expressionAtAcquisition
            })).Status);

        var active = ActivationLedger(id, 1,
            ["enter:wolf"], [Instance(id, "enter:wolf")], [Resource()]);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 1, 0, 1,
                "enter:wolf", transition: true, ledger: active) with
            {
                PersistentInfluencesJson = expressionAtAcquisition
            })).Status);

        // A form transition retains the influence's acquisition and expression
        // while the separate expression event retains the activation and pool.
        var increased = ExpressionChange(id, digest, 2, "4") with
        {
            ExpectedLedgerRevision = 1, NextLedgerRevision = 1,
            LedgerJson = active
        };
        var changedLedger = JsonNode.Parse(active)!.AsObject();
        changedLedger["resources"]![0]!["current"] = 8;
        await Assert.ThrowsAsync<ArgumentException>(() => store.CommitVerifiedAsync(
            increased with { LedgerJson = changedLedger.ToJsonString() }));
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(increased)).Status);

        var changed = (await store.GetAsync(id))!;
        Assert.Equal(3, changed.Revision);
        Assert.Equal(1, changed.LedgerRevision);
        using (var ledger = JsonDocument.Parse(changed.LedgerJson))
        {
            Assert.Equal("manifest.wolf", ledger.RootElement
                .GetProperty("instances")[0].GetProperty("id").GetString());
            Assert.Equal(4, ledger.RootElement
                .GetProperty("resources")[0].GetProperty("current").GetInt32());
            Assert.Equal(1, ledger.RootElement
                .GetProperty("processedEventIds").GetArrayLength());
        }

        var ended = ActivationLedger(id, 2,
            ["enter:wolf", "revert:wolf"],
            [Instance(id, "revert:wolf", lifecycle: 1)], [Resource()]);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest, 3, 1, 2,
                "revert:wolf", transition: true, ledger: ended) with
            {
                PersistentInfluencesJson = SyntheticInfluence("4", "fixture:stage2")
            })).Status);
        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var durable = new PostgresCharacterSupernaturalStateStore(reloaded);
        var snapshot = (await durable.GetAsync(id))!;
        Assert.Equal(4, snapshot.Revision);
        Assert.Equal(2, snapshot.LedgerRevision);
        Assert.Contains("curse.earned", snapshot.PersistentInfluencesJson);
        Assert.Contains("fixture:stage2", snapshot.PersistentInfluencesJson);
        var receipts = await durable.GetEventsAsync(id);
        Assert.Equal(new[] { "acquisition", "transition", "expression-change", "transition" },
            receipts.Select(x => x.Kind));
        Assert.Null(receipts[2].TransitionRequestJson);
        Assert.NotNull(receipts[2].ExpressionChangeJson);
        Assert.Equal("revert:wolf", receipts[3].EventId);
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
    public async Task ActivationResourceMustExactlyMatchRecordedOrderedStepAudit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(Write(id, digest))).Status);

        var ledger = ActivationLedger(id, 1, ["enter:wolf"],
            [Instance(id, "enter:wolf")], [Resource()]);
        var valid = Write(id, digest, 1, 0, 1, "enter:wolf",
            transition: true, ledger: ledger);
        using (var audit = JsonDocument.Parse(valid.TransitionRequestJson!))
        {
            var listed = Assert.Single(audit.RootElement.GetProperty("steps")[0]
                .GetProperty("newResources").EnumerateArray());
            Assert.Equal("resource.wolf", listed.GetProperty("id").GetString());
        }

        var missing = JsonNode.Parse(valid.TransitionRequestJson!)!.AsObject();
        missing["steps"]![0]!["newResources"] = new JsonArray();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(valid with
            {
                TransitionRequestJson = missing.ToJsonString()
            }));

        var changed = JsonNode.Parse(valid.TransitionRequestJson!)!.AsObject();
        changed["steps"]![0]!["newResources"]![0]!["current"] = 9;
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(valid with
            {
                TransitionRequestJson = changed.ToJsonString()
            }));

        var foreign = JsonNode.Parse(valid.TransitionRequestJson!)!.AsObject();
        foreign["steps"]![0]!["newResources"]![0]!["ownerId"] = "manifest.other";
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(valid with
            {
                TransitionRequestJson = foreign.ToJsonString()
            }));

        var extra = JsonNode.Parse(valid.TransitionRequestJson!)!.AsObject();
        extra["steps"]![0]!["newResources"]!.AsArray().Add(
            extra["steps"]![0]!["newResources"]![0]!.DeepClone());
        // The event audit may not repeat or invent an additional pool.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CommitVerifiedAsync(valid with
            {
                TransitionRequestJson = extra.ToJsonString()
            }));

        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Equal(0, (await store.GetAsync(id))!.LedgerRevision);
        Assert.Single(await store.GetEventsAsync(id));
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(valid)).Status);
        using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var stored = new PostgresCharacterSupernaturalStateStore(reload);
        var eventReceipt = (await stored.GetEventsAsync(id))[1];
        using var persisted = JsonDocument.Parse(eventReceipt.TransitionRequestJson!);
        Assert.Equal("resource.wolf", persisted.RootElement.GetProperty("steps")[0]
            .GetProperty("newResources")[0].GetProperty("id").GetString());
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

    [Fact]
    public async Task ExplicitProgressionReceiptPreservesEarnedHistoryAcrossRestart()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        var (store, digest) = await InitializeAsync(db, id);

        var initial = Write(id, digest);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(initial)).Status);

        var augmented = JsonNode.Parse(WolfSheet)!.AsArray();
        augmented[0]!["progression"]!.AsArray().Add(JsonNode.Parse("""
            {"id":"wolf-earned-next","definitionId":"fixture.progression",
             "acquisitionEvidenceId":"fixture:verified-progression","grants":[]}
            """));
        var advancement = Write(id, digest, 1, 0, 0, "progress:wolf",
            alternate: augmented.ToJsonString()) with { IsProgression = true };

        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(advancement)).Status);
        Assert.Equal(CharacterSupernaturalWriteStatus.DuplicateEvent,
            (await store.CommitVerifiedAsync(advancement)).Status);

        await using var reloadedDb = new CharacterSheetDbContext(database.CreateOptions());
        var reloaded = new PostgresCharacterSupernaturalStateStore(reloadedDb);
        var saved = await reloaded.GetAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(0, saved.LedgerRevision);
        Assert.Contains("wolf-progression", saved.AlternateSheetsJson);
        Assert.Contains("wolf-earned-next", saved.AlternateSheetsJson);
        Assert.Equal(new[] { "acquisition", "progression" },
            (await reloaded.GetEventsAsync(id)).Select(x => x.Kind));

        // Advancement is never a first event or a disguised form transition.
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reloaded.CommitVerifiedAsync(Write(id, digest) with
            {
                EventId = "progress:first",
                IsProgression = true
            }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            reloaded.CommitVerifiedAsync(advancement with { IsTransition = true }));
    }
}
