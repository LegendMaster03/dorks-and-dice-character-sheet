using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using CharacterSheet.Application.Persistence;
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
        SiteCharacterAccessStatus status = SiteCharacterAccessStatus.Authorized)
        : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(status == SiteCharacterAccessStatus.Authorized && id == allowedId
                ? SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                    id, "Test", lifecycle,
                    lifecycle == SiteCharacterLifecycleState.Archived
                        ? DateTimeOffset.UtcNow : null, []))
                : new SiteCharacterAccessResult(status == SiteCharacterAccessStatus.Authorized
                    ? SiteCharacterAccessStatus.NotFoundOrNotOwned : status));
    }

    private sealed class NativeGateway : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }
        public Func<Task>? BeforeReply { get; set; }
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
        SiteCharacterAccessStatus status = SiteCharacterAccessStatus.Authorized)
    {
        var site = new SiteAccess(id, lifecycle, status);
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
}
