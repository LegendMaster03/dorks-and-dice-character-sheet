using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Real PostgreSQL + application-service reads, with a controlled native
/// transport. These tests do NOT certify playable supernatural content.
/// </summary>
public sealed class CharacterEffectiveStateReadTests
{
    private const string Ruleset = "first-party-001";
    private static readonly string Fingerprint = new('a', 64);

    private sealed class SiteAccess(Guid allowedId,
        SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active,
        SiteCharacterAccessStatus status = SiteCharacterAccessStatus.Authorized,
        Func<int, Task>? beforeAuthorizedRead = null)
        : ISiteCharacterAccessGateway, ISiteFreshCharacterAccessGateway
    {
        private int readCount;
        public bool FreshAllowed { get; set; } = true;
        public Func<int, Task>? BeforeFreshRead { get; set; }
        private int freshReadCount;

        public async Task<SiteCharacterAccessResult> GetFreshAuthorizedCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            if (BeforeFreshRead is not null)
                await BeforeFreshRead(Interlocked.Increment(ref freshReadCount));
            return FreshAllowed
                ? await GetAuthorizedCharacterAsync(characterId, cancellationToken)
                : new(SiteCharacterAccessStatus.NotFoundOrNotOwned);
        }

        public async Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            if (beforeAuthorizedRead is not null)
                await beforeAuthorizedRead(Interlocked.Increment(ref readCount));
            return status == SiteCharacterAccessStatus.Authorized && id == allowedId
                ? SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                    id, "Test", lifecycle,
                    lifecycle == SiteCharacterLifecycleState.Archived
                        ? DateTimeOffset.UtcNow : null, []))
                : new SiteCharacterAccessResult(status == SiteCharacterAccessStatus.Authorized
                    ? SiteCharacterAccessStatus.NotFoundOrNotOwned : status);
        }
    }

    private sealed class NativeGateway : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }
        public int VerifiedReadCalls { get; private set; }
        public Func<Task>? BeforeReply { get; set; }
        public Func<Task>? BeforeVerifiedReply { get; set; }
        public bool ReturnInvalid { get; set; }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1, rulesetId = Ruleset, fingerprint = Fingerprint
            }))!);

        public async Task<JsonNode> EvaluatePrimaryStateBaselineAsync(
            JsonElement request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(Ruleset, request.GetProperty("state")
                .GetProperty("primaryMechanics").GetProperty("rulesetId").GetString());
            Assert.Empty(request.GetProperty("state").GetProperty("alternateSheets")
                .EnumerateArray());
            Assert.Empty(request.GetProperty("state").GetProperty("persistentInfluences")
                .EnumerateArray());
            if (BeforeReply is not null) await BeforeReply();
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1, rulesetId = Ruleset,
                fingerprint = Fingerprint, supportedScope = "primary-only",
                effectiveMechanicsReady = false,
                status = ReturnInvalid ? "invalid" : "partial",
                projection = new { status = ReturnInvalid ? "invalid" : "partial" }
            }))!;
        }

        public async Task<JsonNode> EvaluateVerifiedManifestationStateAsync(
            JsonElement request, CancellationToken ct)
        {
            VerifiedReadCalls++;
            if (BeforeVerifiedReply is not null) await BeforeVerifiedReply();
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1, rulesetId = Ruleset,
                fingerprint = Fingerprint, supportedScope = "verified-manifestations",
                effectiveMechanicsReady = false, status = "partial",
                projection = new
                {
                    effectiveCharacter = new
                    {
                        effectiveSheetId = "@primary",
                        manifestationInstanceId = (string?)null
                    }
                }
            }))!;
        }

        public int OriginVerificationCalls { get; private set; }
        public Func<Task>? BeforeOriginVerificationReply { get; set; }
        public bool CorruptOriginVerificationReply { get; set; }

        public async Task<JsonNode> VerifyNativeOriginUnderlyingAsync(
            JsonElement request, CancellationToken ct)
        {
            OriginVerificationCalls++;
            if (BeforeOriginVerificationReply is not null)
                await BeforeOriginVerificationReply();
            var profile = request.GetProperty("profile");
            return JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                rulesetId = Ruleset,
                fingerprint = Fingerprint,
                characterId = request.GetProperty("characterId").GetString(),
                originRevision = request.GetProperty("originRevision").GetInt64(),
                supportedScope = "verified-origin-underlying",
                status = "partial",
                effectiveMechanicsReady = false,
                underlying = CorruptOriginVerificationReply
                    ? JsonSerializer.SerializeToElement(
                        new { speciesId = "species.forged", inherentGrants = Array.Empty<object>() })
                    : profile.GetProperty("underlying"),
                current = profile.GetProperty("current")
            }))!;
        }

        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint, string category,
            string? query, int limit, int offset, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private static CharacterEffectiveStateService Service(CharacterSheetDbContext db,
        Guid id, NativeGateway native,
        SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active,
        SiteCharacterAccessStatus status = SiteCharacterAccessStatus.Authorized,
        Func<int, Task>? beforeAuthorizedRead = null)
    {
        var site = new SiteAccess(id, lifecycle, status, beforeAuthorizedRead);
        var buildStore = new PostgresCharacterBuildStore(db);
        return new CharacterEffectiveStateService(
            new CharacterBuildService(site, buildStore, TimeProvider.System),
            new CharacterStateService(site, new PostgresCharacterStateStore(db),
                TimeProvider.System),
            new CharacterOriginStateService(site, buildStore,
                new PostgresCharacterAuthorizedOriginStore(db)),
            new PostgresCharacterSupernaturalStateStore(db), native);
    }

    [Fact]
    public async Task OrdinaryAndArchivedCharacterReadsArePartialAndNonMutating()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var native = new NativeGateway();
        var before = await new PostgresCharacterSupernaturalStateStore(db)
            .GetAuthoritativeInputsFingerprintAsync(id);

        var normal = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, normal.Status);
        Assert.Equal("partial", normal.View!.Status);
        Assert.False(normal.View.EffectiveMechanicsReady);
        Assert.Equal("@primary", normal.View.EffectiveSheetId);
        Assert.Null(normal.View.ActiveManifestationId);
        Assert.NotNull(normal.View.NativeProjection);
        Assert.Contains("authorized-origin-unavailable", normal.View.Unresolved);
        Assert.False(normal.View.ReadOnly);

        var archived = await Service(db, id, native,
            SiteCharacterLifecycleState.Archived).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, archived.Status);
        Assert.True(archived.View!.ReadOnly);
        Assert.Equal(2, native.Calls);
        Assert.Null(await db.SupernaturalStates.SingleOrDefaultAsync(x => x.CharacterId == id));
        Assert.Equal(before, await new PostgresCharacterSupernaturalStateStore(db)
            .GetAuthoritativeInputsFingerprintAsync(id));
    }

    [Fact]
    public async Task UnauthorizedActorNeverContactsNativeMechanics()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var native = new NativeGateway();
        var result = await Service(db, id, native,
            status: SiteCharacterAccessStatus.NotFoundOrNotOwned).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.NotFoundOrNotOwned, result.Status);
        Assert.Null(result.View);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task StoredSupernaturalRecordRemainsUnresolvedUntilAuthorityIsAvailable()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        var stored = await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
            id, Ruleset, Fingerprint, """[{"id":"wolf"}]""",
            "[]", ledger, 0, 0, 0, digest,
            "fixture:wolf", "fixture:controlled", false, DateTimeOffset.UtcNow));
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied, stored.Status);

        var native = new NativeGateway();
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.False(result.View.EffectiveMechanicsReady);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("supernatural-acquisition-authority-unavailable",
            result.View.Unresolved);
        Assert.Equal(0, native.Calls);
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
    }

    [Fact]
    public async Task ChangeOfIndependentAuthoritativeInputInvalidatesReadProjection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var native = new NativeGateway
        {
            BeforeReply = async () =>
            {
                await new PostgresCharacterBuildStore(db)
                    .SetBaseAbilityScoreInputAsync(id, "strength", 13,
                        DateTimeOffset.UtcNow.AddSeconds(2));
            }
        };
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("authoritative-inputs-changed-during-resolution",
            result.View.Unresolved);
    }

    [Fact]
    public async Task OriginUnderlyingChangedDuringNativeReadInvalidatesProjection()
    {
        // A persisted Origin is separately owned primary state, and the
        // fingerprint must cover the exact Underlying, not only its row revision.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);

        const string beforeProfile = """
            {"id":"origin:fixture","underlying":{"speciesId":"species.human"},"current":{"speciesId":"species.human"}}
            """;
        const string alteredProfile = """
            {"id":"origin:fixture","underlying":{"speciesId":"species.elf"},"current":{"speciesId":"species.elf"}}
            """;
        db.AuthorizedOrigins.Add(new CharacterAuthorizedOriginState(
            id, Ruleset, Fingerprint, beforeProfile, null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var before = await store.GetAuthoritativeInputsFingerprintAsync(id);

        var native = new NativeGateway
        {
            BeforeReply = async () =>
            {
                await db.AuthorizedOrigins.Where(x => x.CharacterId == id)
                    .ExecuteUpdateAsync(update => update.SetProperty(
                        x => x.ProfileJson, alteredProfile));
            }
        };
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("authoritative-inputs-changed-during-resolution",
            result.View.Unresolved);
        Assert.Equal(1, native.Calls);
        Assert.Equal(1, (await db.AuthorizedOrigins.AsNoTracking()
            .SingleAsync(x => x.CharacterId == id)).Revision);
        Assert.NotEqual(before, await store.GetAuthoritativeInputsFingerprintAsync(id));
        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task PrimaryProgressionChangedBetweenAuthorizationAndAssemblyFailsClosed()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        await build.SetBaseAbilityScoreInputAsync(id, "strength", 10,
            DateTimeOffset.UtcNow);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var oldFingerprint = await store.GetAuthoritativeInputsFingerprintAsync(id);

        // Build authorization happens first; the second Site check is the
        // post-fingerprint Build reload. Under the earlier ordering this
        // update happened before the first digest, leaving a stale Build
        // view that looked fresh after a successful native calculation.
        var native = new NativeGateway();
        var result = await Service(db, id, native,
            beforeAuthorizedRead: async read =>
            {
                if (read == 2)
                    await build.SetBaseAbilityScoreInputAsync(id, "strength", 13,
                        DateTimeOffset.UtcNow.AddSeconds(1));
            }).GetAsync(id);

        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("authoritative-inputs-changed-during-resolution",
            result.View.Unresolved);
        Assert.Equal(1, native.Calls);
        Assert.NotEqual(oldFingerprint,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task AcquisitionDuringOrdinaryProjectionNeverReturnsStalePrimaryOnlyState()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var initialLedger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        var native = new NativeGateway
        {
            BeforeReply = async () =>
            {
                var applied = await store.CommitVerifiedAsync(
                    new VerifiedSupernaturalStateWrite(id, Ruleset, Fingerprint,
                        """[{"id":"wolf","parentId":"@primary"}]""",
                        "[]", initialLedger, 0, 0, 0, digest,
                        "fixture:acquired-during-read", "fixture:controlled",
                        false, DateTimeOffset.UtcNow));
                Assert.Equal(CharacterSupernaturalWriteStatus.Applied, applied.Status);
            }
        };
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("supernatural-state-changed-during-resolution",
            result.View.Unresolved);
        Assert.Null(result.View.SupernaturalRevision);
        Assert.Equal(1, native.Calls);
        Assert.Equal(digest, await store.GetAuthoritativeInputsFingerprintAsync(id));
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task ConcurrentAcquisitionInvalidatesOtherwiseVerifiedSupernaturalRead()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var key in new[]
        {
            "strength", "dexterity", "constitution",
            "intelligence", "wisdom", "charisma"
        })
            await build.SetBaseAbilityScoreInputAsync(id, key, 12,
                DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, Ruleset, Fingerprint,
                """[{"id":"wolf","parentId":"@primary"}]""",
                "[]", ledger, 0, 0, 0, digest, "fixture:first-acquisition",
                "fixture:controlled", false, DateTimeOffset.UtcNow))).Status);

        var native = new NativeGateway
        {
            BeforeVerifiedReply = async () =>
            {
                var applied = await store.CommitVerifiedAsync(
                    new VerifiedSupernaturalStateWrite(id, Ruleset, Fingerprint,
                        """
                        [{"id":"wolf","parentId":"@primary"},
                         {"id":"hybrid","parentId":"wolf"}]
                        """,
                        "[]", ledger, 1, 0, 0, digest, "fixture:second-acquisition",
                        "fixture:controlled", false, DateTimeOffset.UtcNow));
                Assert.Equal(CharacterSupernaturalWriteStatus.Applied, applied.Status);
            }
        };
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Null(result.View.SupernaturalRevision);
        Assert.Contains("supernatural-state-changed-during-resolution",
            result.View.Unresolved);
        Assert.Equal(1, native.VerifiedReadCalls);
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
        Assert.Equal(digest, await store.GetAuthoritativeInputsFingerprintAsync(id));
    }

    [Fact]
    public async Task InvalidCoreProjectionIsNeverPresentedAsPartialMechanics()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var native = new NativeGateway { ReturnInvalid = true };
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.Contains("native-state-response-unverified", result.View.Unresolved);
    }

    [Fact]
    public async Task DormantAlternateSheetCanBeInspectedWithoutBeingDeclaredManifested()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, Ruleset, Fingerprint, """[{"id":"wolf","parentId":"werewolf"}]""",
                "[]", ledger, 0, 0, 0, digest, "fixture:acquisition",
                "fixture:authorized", false, DateTimeOffset.UtcNow))).Status);

        var builds = new PostgresCharacterBuildStore(db);
        var reader = new CharacterSupernaturalInspectionService(
            new CharacterBuildService(new SiteAccess(id), builds, TimeProvider.System), store);
        var view = await reader.GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, view.Status);
        Assert.Equal("stored-unverified", view.View!.Status);
        Assert.False(view.View.EffectiveMechanicsVerified);
        Assert.Equal("wolf", view.View.AlternateSheets[0]!["id"]!.GetValue<string>());
        Assert.Single(view.View.CompletedEvents);
        Assert.Equal(0, view.View.ManifestationRevision);

        var unauthorized = new CharacterSupernaturalInspectionService(
            new CharacterBuildService(new SiteAccess(id,
                status: SiteCharacterAccessStatus.NotFoundOrNotOwned),
                builds, TimeProvider.System), store);
        Assert.Equal(CharacterEffectiveStateStatus.NotFoundOrNotOwned,
            (await unauthorized.GetAsync(id)).Status);
    }

    [Fact]
    public async Task OrdinaryCharacterInspectionDoesNotCreateSupernaturalRecords()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var reader = new CharacterSupernaturalInspectionService(
            new CharacterBuildService(new SiteAccess(id,
                SiteCharacterLifecycleState.Archived),
                new PostgresCharacterBuildStore(db), TimeProvider.System),
            new PostgresCharacterSupernaturalStateStore(db));
        var view = await reader.GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, view.Status);
        Assert.Equal("unconfigured", view.View!.Status);
        Assert.True(view.View.ReadOnly);
        Assert.Empty(view.View.AlternateSheets.AsArray());
        Assert.Empty(await db.SupernaturalStates.ToArrayAsync());
    }

    [Fact]
    public async Task ConcurrentPrimaryEffectiveReadsRemainReadOnlyAndIndependent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(id);
        }

        await using var first = new CharacterSheetDbContext(database.CreateOptions());
        await using var second = new CharacterSheetDbContext(database.CreateOptions());
        var firstCore = new NativeGateway();
        var secondCore = new NativeGateway();
        var before = await new PostgresCharacterSupernaturalStateStore(first)
            .GetAuthoritativeInputsFingerprintAsync(id);

        var a = Service(first, id, firstCore).GetAsync(id);
        var b = Service(second, id, secondCore).GetAsync(id);
        await Task.WhenAll(a, b);
        foreach (var read in new[] { await a, await b })
        {
            Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
            Assert.Equal("partial", read.View!.Status);
            Assert.False(read.View.EffectiveMechanicsReady);
            Assert.Equal("@primary", read.View.EffectiveSheetId);
        }
        Assert.Equal(1, firstCore.Calls);
        Assert.Equal(1, secondCore.Calls);
        await using var afterContext = new CharacterSheetDbContext(database.CreateOptions());
        var after = new PostgresCharacterSupernaturalStateStore(afterContext);
        Assert.Equal(before, await after.GetAuthoritativeInputsFingerprintAsync(id));
        Assert.Null(await after.GetAsync(id));
        Assert.Empty(await after.GetEventsAsync(id));
    }

    [Fact]
    public async Task ConcurrentSupernaturalEffectiveReadsDoNotAlterResourceOwnershipOrReceipts()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        string committedLedger;
        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(id);
            var store = new PostgresCharacterSupernaturalStateStore(setup);
            var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
            committedLedger = JsonSerializer.Serialize(new
            {
                characterId = id.ToString("D"), revision = 0,
                instances = Array.Empty<object>(),
                processedEventIds = Array.Empty<string>(),
                resources = new[]
                {
                    new
                    {
                        id = "pool.primary", definitionId = "pool.synthetic",
                        sourceOccurrence = new
                        {
                            source = new { kind = 0, id = "synthetic.resource", version = "test-v1" },
                            occurrencePath = "resources.primary"
                        },
                        evidenceId = "fixture:source-resource",
                        ownerKind = 0, ownerId = "@primary", current = 3, maximum = 7
                    }
                }
            });
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                    id, Ruleset, Fingerprint, "[]", "[]", committedLedger,
                    0, 0, 0, digest, "fixture:acquire-pool",
                    "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);
        }

        await using var first = new CharacterSheetDbContext(database.CreateOptions());
        await using var second = new CharacterSheetDbContext(database.CreateOptions());
        var firstCore = new NativeGateway();
        var secondCore = new NativeGateway();
        var a = Service(first, id, firstCore).GetAsync(id);
        var b = Service(second, id, secondCore).GetAsync(id);
        await Task.WhenAll(a, b);
        foreach (var read in new[] { await a, await b })
        {
            Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
            Assert.Equal("unresolved", read.View!.Status);
            Assert.False(read.View.EffectiveMechanicsReady);
            Assert.Equal(1, read.View.SupernaturalRevision);
            Assert.Null(read.View.ActiveManifestationId);
        }
        // Read-only paths do not invoke the primary-only adapter for stored
        // supernatural ownership because acquisition is not yet reverified.
        Assert.Equal(0, firstCore.Calls);
        Assert.Equal(0, secondCore.Calls);

        await using var afterContext = new CharacterSheetDbContext(database.CreateOptions());
        var saved = new PostgresCharacterSupernaturalStateStore(afterContext);
        var persisted = (await saved.GetAsync(id))!;
        Assert.Equal(1, persisted.Revision);
        Assert.Equal(0, persisted.LedgerRevision);
        // PostgreSQL JSONB canonicalizes property order/spacing on storage.
        // Compare the actual ledger data, not its original wire formatting.
        using var expectedLedger = JsonDocument.Parse(committedLedger);
        using var actualLedger = JsonDocument.Parse(persisted.LedgerJson);
        Assert.True(JsonElement.DeepEquals(expectedLedger.RootElement,
            actualLedger.RootElement));
        using var ledger = JsonDocument.Parse(persisted.LedgerJson);
        var pool = Assert.Single(ledger.RootElement.GetProperty("resources").EnumerateArray());
        Assert.Equal(0, pool.GetProperty("ownerKind").GetInt32());
        Assert.Equal("@primary", pool.GetProperty("ownerId").GetString());
        Assert.Equal(3, pool.GetProperty("current").GetInt32());
        Assert.Equal(7, pool.GetProperty("maximum").GetInt32());
        Assert.Single(await saved.GetEventsAsync(id));
    }

    [Fact]
    public async Task OriginReceiptMethodAndDmScopeChangesInvalidatePrimaryDigest()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var campaign = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        db.AuthorizedOrigins.Add(new CharacterAuthorizedOriginState(
            id, Ruleset, Fingerprint, """
                {"id":"origin:verified","underlying":{"inherentGrants":[]},"current":{"inherentGrants":[]},"history":[],"independentGrants":[],"adjudication":{"adjudication":"supported"}}
                """, null, DateTimeOffset.UtcNow));
        db.OriginEventReceipts.Add(new CharacterOriginEventReceipt(
            id, "construction:verified", 1,
            "rule.creature-origination", "biological", "campaign-dm",
            "evidence:verified", DateTimeOffset.UtcNow, actor, campaign,
            """[{"id":"decision:verified"}]"""));
        await db.SaveChangesAsync();
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var initial = await store.GetAuthoritativeInputsFingerprintAsync(id);
        Assert.NotNull(initial);
        var row = db.OriginEventReceipts.Where(x => x.CharacterId == id);

        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.MethodRuleId, "rule.forged-method"));
        Assert.NotEqual(initial,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.MethodRuleId, "rule.creature-origination"));
        Assert.Equal(initial, await store.GetAuthoritativeInputsFingerprintAsync(id));

        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.MethodId, "forged-origination"));
        Assert.NotEqual(initial,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.MethodId, "biological"));
        Assert.Equal(initial, await store.GetAuthoritativeInputsFingerprintAsync(id));

        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.ApprovalActorUserId, Guid.NewGuid()));
        Assert.NotEqual(initial,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.ApprovalActorUserId, actor));
        Assert.Equal(initial, await store.GetAuthoritativeInputsFingerprintAsync(id));

        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.ApprovalCampaignId, Guid.NewGuid()));
        Assert.NotEqual(initial,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await row.ExecuteUpdateAsync(change => change.SetProperty(
            x => x.ApprovalCampaignId, campaign));
        Assert.Equal(initial, await store.GetAuthoritativeInputsFingerprintAsync(id));

        var origin = db.AuthorizedOrigins.Where(x => x.CharacterId == id);
        await origin.ExecuteUpdateAsync(update => update.SetProperty(
            x => x.SourceDraftRevision, (long?)7));
        Assert.NotEqual(initial,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await origin.ExecuteUpdateAsync(update => update.SetProperty(
            x => x.SourceDraftRevision, (long?)null));
        Assert.Equal(initial, await store.GetAuthoritativeInputsFingerprintAsync(id));
        Assert.Equal(1, (await db.AuthorizedOrigins.AsNoTracking()
            .SingleAsync(x => x.CharacterId == id)).Revision);
    }

    [Fact]
    public async Task OriginUnderlyingVerificationRejectsUntrustedReplyAndInFlightSourceChange()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        const string originalProfile = """
            {"id":"origin:current","underlying":{"speciesId":"species.human","inherentGrants":[]},"current":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        const string rewrittenProfile = """
            {"id":"origin:current","underlying":{"speciesId":"species.elf","inherentGrants":[]},"current":{"speciesId":"species.elf","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        db.AuthorizedOrigins.Add(new CharacterAuthorizedOriginState(
            id, Ruleset, Fingerprint, originalProfile, null, DateTimeOffset.UtcNow));
        db.OriginEventReceipts.Add(new CharacterOriginEventReceipt(
            id, "construction:current", 1, "rule.creature-origination",
            "biological", "first-party", "native-catalog:" + Fingerprint,
            DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var site = new SiteAccess(id);
        var builds = new PostgresCharacterBuildStore(db);
        var origins = new CharacterOriginStateService(
            site, builds, new PostgresCharacterAuthorizedOriginStore(db));
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var native = new NativeGateway();
        var verifier = new CharacterVerifiedOriginUnderlyingService(
            origins, store, native, site);

        var valid = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.VerifiedPartial, valid.Status);
        Assert.Equal(1, valid.OriginRevision);
        Assert.Equal("species.human", valid.Underlying!["speciesId"]!.GetValue<string>());
        Assert.Equal(1, native.OriginVerificationCalls);


        // A saved JSON profile can pass TryGetProperty despite duplicate
        // source/configuration keys. Reject the ambiguous stored candidate
        // before any delegated Core call, with no receipt or revision writes.
        const string duplicateRoot = """
            {"id":"origin:current","underlying":{"speciesId":"species.elf","inherentGrants":[]},"underlying":{"speciesId":"species.human","inherentGrants":[]},"current":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        const string duplicateConfiguration = """
            {"id":"origin:current","underlying":{"speciesId":"species.human","speciesId":"species.elf","inherentGrants":[]},"current":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        const string differentlyCasedUnderlying = """
            {"id":"origin:current","underlying":{"speciesId":"species.human","inherentGrants":[]},"Underlying":{"speciesId":"species.elf","inherentGrants":[]},"current":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        const string duplicatedNestedEvidence = """
            {"id":"origin:current","underlying":{"speciesId":"species.human","inherentGrants":[{"source":{"occurrenceId":"first","occurrenceId":"forged"}}]},"current":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        const string missingCurrent = """
            {"id":"origin:current","underlying":{"speciesId":"species.human","inherentGrants":[]},"independentGrants":[],"history":[],"adjudication":{"adjudication":"supported"}}
            """;
        foreach (var malformed in new[]
                 { duplicateRoot, differentlyCasedUnderlying, duplicateConfiguration,
                   duplicatedNestedEvidence, missingCurrent })
        {
            await db.AuthorizedOrigins.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    x => x.ProfileJson, malformed));
            var denied = await verifier.GetAsync(id);
            Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified,
                denied.Status);
            Assert.Null(denied.Underlying);
            Assert.Equal(1, native.OriginVerificationCalls);
        }
        await db.AuthorizedOrigins.Where(x => x.CharacterId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                x => x.ProfileJson, originalProfile));
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.VerifiedPartial,
            (await verifier.GetAsync(id)).Status);
        Assert.Equal(2, native.OriginVerificationCalls);

        // A matching stored profile is not sufficient without a contiguous
        // separately committed Origin event history.
        await db.OriginEventReceipts.Where(x => x.CharacterId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                x => x.CommittedRevision, 2L));
        var brokenHistory = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified,
            brokenHistory.Status);
        Assert.Equal(2, native.OriginVerificationCalls);
        await db.OriginEventReceipts.Where(x => x.CharacterId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                x => x.CommittedRevision, 1L));

        // A cached Site snapshot can still look authorized after its
        // current authoritative owner check has revoked access.
        site.FreshAllowed = false;
        var revoked = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
            revoked.Status);
        Assert.Equal(2, native.OriginVerificationCalls);
        site.FreshAllowed = true;

        // A live source-state change during the remote call is distinct
        // from PostgreSQL drift. It must not yield a usable baseline.
        var requested = 0;
        site.BeforeFreshRead = _ =>
        {
            if (Interlocked.Increment(ref requested) == 2)
                site.FreshAllowed = false;
            return Task.CompletedTask;
        };
        var changedAccess = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
            changedAccess.Status);
        Assert.Null(changedAccess.Underlying);
        site.BeforeFreshRead = null;
        site.FreshAllowed = true;

        native.CorruptOriginVerificationReply = true;
        var corrupt = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified,
            corrupt.Status);
        Assert.Null(corrupt.Underlying);
        native.CorruptOriginVerificationReply = false;

        native.BeforeOriginVerificationReply = async () =>
        {
            await db.OriginEventReceipts.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    x => x.MethodId, "forged-origination"));
        };
        var changedMethod = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged,
            changedMethod.Status);
        Assert.Null(changedMethod.Underlying);
        await db.OriginEventReceipts.Where(x => x.CharacterId == id)
            .ExecuteUpdateAsync(update => update.SetProperty(
                x => x.MethodId, "biological"));

        native.BeforeOriginVerificationReply = async () =>
        {
            await db.AuthorizedOrigins.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    x => x.ProfileJson, rewrittenProfile));
        };
        var raced = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged,
            raced.Status);
        Assert.Null(raced.Underlying);
        Assert.Equal(1, (await db.AuthorizedOrigins.AsNoTracking()
            .SingleAsync(x => x.CharacterId == id)).Revision);
        Assert.Null(await store.GetAsync(id));
    }

    [Fact]
    public async Task UnauthorizedOriginVerificationDoesNotReadOwnerOrCallRulesCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var site = new SiteAccess(id, status: SiteCharacterAccessStatus.NotFoundOrNotOwned);
        var builds = new PostgresCharacterBuildStore(db);
        var native = new NativeGateway();
        var verifier = new CharacterVerifiedOriginUnderlyingService(
            new CharacterOriginStateService(site, builds,
                new PostgresCharacterAuthorizedOriginStore(db)),
            new PostgresCharacterSupernaturalStateStore(db), native, site);
        var denied = await verifier.GetAsync(id);
        Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
            denied.Status);
        Assert.Equal(0, native.OriginVerificationCalls);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task OriginPinnedToObsoleteNativeCatalogIsExplicitlyUnresolved()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        // Controlled historical Origin row. Its source was authorized in an
        // older native catalog; the latest native fingerprint differs.
        db.AuthorizedOrigins.Add(new CharacterSheet.Domain.Characters.CharacterAuthorizedOriginState(
            id, Ruleset, new string('b', 64), "{}", null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var native = new NativeGateway();
        var read = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
        Assert.Equal("unresolved", read.View!.Status);
        Assert.Null(read.View.NativeProjection);
        Assert.False(read.View.EffectiveMechanicsReady);
        Assert.Equal(1, read.View.OriginRevision);
        Assert.Contains("authorized-origin-native-content-changed", read.View.Unresolved);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task PersistedSupernaturalStatePinnedToObsoleteCatalogIsReportedAsStale()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(), resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        var oldFingerprint = new string('b', 64);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, Ruleset, oldFingerprint, "[]", "[]", ledger,
                0, 0, 0, digest, "fixture:old-acquisition",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);

        var native = new NativeGateway();
        var read = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
        Assert.Equal("unresolved", read.View!.Status);
        Assert.Equal(oldFingerprint, read.View.Fingerprint);
        Assert.Null(read.View.NativeProjection);
        Assert.Contains("supernatural-native-content-changed", read.View.Unresolved);
        Assert.Equal(0, native.Calls);
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task IndependentFeatDoesNotEraseSupportedClassProjection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        var cls = (await build.SetStartingClassAsync(id, "class.barbarian",
            DateTimeOffset.UtcNow))!;
        var occurrence = Assert.Single(cls.AdvancementEntries,
            x => x.Kind == CharacterSheet.Domain.Characters.CharacterAdvancementKind.Class);
        await build.SetAdvancementLevelAsync(id, occurrence.Id, 2,
            DateTimeOffset.UtcNow);
        await build.AddFeatOccurrenceAsync(id, "feat.fixture",
            DateTimeOffset.UtcNow);

        var native = new NativeGateway();
        var result = await Service(db, id, native).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("partial", result.View!.Status);
        Assert.NotNull(result.View.NativeProjection);
        Assert.False(result.View.EffectiveMechanicsReady);
        Assert.Contains("nonclass-advancement-effects-unverified", result.View.Unresolved);
        Assert.Equal(1, native.Calls);
    }

}
