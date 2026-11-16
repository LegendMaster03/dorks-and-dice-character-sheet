using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Exercises the real Character Sheet PostgreSQL source chain and CAS using
/// a test-only native HTTP response. Actual native course calculation is
/// independently verified in Rules Core host tests; this fake is never used
/// as a production evaluator.
/// </summary>
public sealed class NativeDirectInnateSpellLedgerBootstrapTests
{
    private const string Fingerprint = "catalog-1";
    private const string Draft = """
        {"id":"initial","intent":0,
         "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
         "contributors":[],"candidates":[],"manifestations":[],
         "selectedSpeciesId":"species.human"}
        """;
    private const string Profile = """
        {"id":"origin:initial","proposalId":"initial",
         "underlying":{"speciesId":"species.human"},
         "current":{"speciesId":"species.human"}}
        """;
    private const string CourseHash =
        "7E2F9A1E2BF14579B23C95EE88A7A4E3E1E9FD63F3AA2DA37F96F41351BDB82A";
    private static readonly Guid User =
        Guid.Parse("0a0749af-56a3-4df6-bcc9-8a6fb7ced14c");

    private sealed class Site(Guid character) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid requested, CancellationToken ct = default) =>
            Task.FromResult(requested == character
                ? SiteCharacterAccessResult.Authorized(new(character,
                    "Verified owner", SiteCharacterLifecycleState.Active, null, []))
                : new SiteCharacterAccessResult(
                    SiteCharacterAccessStatus.NotFoundOrNotOwned));
    }
    private sealed class Actor : ICharacterInnateSpellActorSource
    {
        public Guid? GetActorUserId() => User;
    }

    private sealed class Native : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }
        public JsonElement? LastRequest { get; private set; }
        public string SourceFingerprint { get; set; } = Fingerprint;
        public bool ValidResult { get; set; } = true;
        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            Task.FromResult(JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                rulesetId = "first-party-001",
                fingerprint = SourceFingerprint,
                capabilities = new { innateSpellLedgerBootstrap = "source-matched-preview" }
            }))!);
        public Task<JsonNode> PreviewDirectInnateSpellLedgerBootstrapAsync(
            JsonElement request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request.Clone();
            return Task.FromResult(JsonNode.Parse(JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                rulesetId = "first-party-001",
                fingerprint = Fingerprint,
                previewOnly = true,
                committedOriginMatched = true,
                result = new
                {
                    status = "resolved", previewOnly = true,
                    state = new
                    {
                        rulesetId = "first-party-001",
                        catalogFingerprint = Fingerprint,
                        originProfileId = ValidResult ? "origin:initial" : "origin:forged",
                        proposalId = "initial",
                        characterLevel = 1,
                        projectionFingerprint = CourseHash,
                        revision = 0,
                        remainingFreeCasts = new Dictionary<string, int>
                        {
                            ["native:grant"] = 1
                        }
                    }
                }
            }))!);
        }
        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint,
            string category, string? query, int limit, int offset,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private static async Task PrepareAsync(CharacterSheetDbContext db, Guid id,
        bool withScores = true)
    {
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var drafts = new PostgresCharacterOriginDraftStore(db);
        Assert.NotNull(await drafts.SaveAsync(id, "first-party-001",
            Fingerprint, Draft, 0, DateTimeOffset.UtcNow));
        var origin = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new(id, "first-party-001", Fingerprint,
                Profile, 0, 1, "origin:initial", "rule.creature-origination",
                "biological", "first-party", "catalog:1", DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, origin.Status);

        var builds = new PostgresCharacterBuildStore(db);
        if (withScores)
        {
            foreach (var ability in new[]
                { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" })
                Assert.NotNull(await builds.SetBaseAbilityScoreInputAsync(
                    id, ability, 12, DateTimeOffset.UtcNow));
        }
        Assert.NotNull(await builds.ApplyClassAdvancementAsync(
            id, null, "class.warlock", 5, null, DateTimeOffset.UtcNow));
    }

    private static CharacterInnateSpellLedgerService Service(
        CharacterSheetDbContext db, Guid id, Native gateway)
    {
        var site = new Site(id);
        var origin = new PostgresCharacterAuthorizedOriginStore(db);
        var ledger = new PostgresCharacterInnateSpellLedgerStore(db);
        var evaluator = new NativeDirectInnateSpellLedgerEvaluator(
            new PostgresCharacterOriginDraftStore(db),
            new CharacterBuildService(site, new PostgresCharacterBuildStore(db),
                TimeProvider.System),
            new CharacterStateService(site, new PostgresCharacterStateStore(db),
                TimeProvider.System),
            gateway);
        return new CharacterInnateSpellLedgerService(site, new Actor(),
            origin, ledger, evaluator, TimeProvider.System);
    }

    [Fact]
    public async Task TrustedDirectBootstrapFromSavedSourceCommitsOnceWithNativeEnvelope()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);
        var gateway = new Native();
        var service = Service(db, id, gateway);

        var initialized = await service.ApplyAsync(id,
            CharacterInnateSpellLedgerAction.Initialize, null, null);
        Assert.Equal(CharacterInnateSpellLedgerStatus.Applied, initialized.Status);
        Assert.Equal(0, initialized.Revision);
        Assert.Equal(1, gateway.Calls);
        var sent = gateway.LastRequest!.Value;
        Assert.Equal("initial", sent.GetProperty("proposal").GetProperty("id").GetString());
        Assert.Equal("origin:initial",
            sent.GetProperty("committedProfile").GetProperty("id").GetString());
        Assert.Equal(6, sent.GetProperty("character").GetProperty("abilityScores")
            .EnumerateObject().Count());
        Assert.Equal("class.warlock", sent.GetProperty("character")
            .GetProperty("advancements")[0].GetProperty("classId").GetString());
        Assert.Equal(Fingerprint, sent.GetProperty("fingerprint").GetString());

        var stored = await new PostgresCharacterInnateSpellLedgerStore(db).GetAsync(id);
        Assert.Equal(0, stored!.LedgerRevision);
        Assert.Equal(1, JsonDocument.Parse(stored.LedgerJson).RootElement
            .GetProperty("remainingFreeCasts").GetProperty("native:grant").GetInt32());
        Assert.Single(await new PostgresCharacterInnateSpellLedgerStore(db)
            .GetEventsAsync(id));
        var duplicate = await service.ApplyAsync(id,
            CharacterInnateSpellLedgerAction.Initialize, null, null);
        Assert.Equal(CharacterInnateSpellLedgerStatus.LedgerAlreadyInitialized,
            duplicate.Status);
        Assert.Equal(1, gateway.Calls);
        var noSpending = await service.ApplyAsync(id,
            CharacterInnateSpellLedgerAction.SpendFreeCast, "native:grant", 0);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            noSpending.Status);
        Assert.Equal(1, gateway.Calls);
        Assert.Single(await new PostgresCharacterInnateSpellLedgerStore(db)
            .GetEventsAsync(id));
    }

    [Fact]
    public async Task ChangedSourceDraftDisablesBootstrapRatherThanTrustingNewSelection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);
        Assert.NotNull(await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            id, "first-party-001", Fingerprint,
            Draft.Replace("species.human", "species.elf", StringComparison.Ordinal),
            1, DateTimeOffset.UtcNow));
        var gateway = new Native();
        var result = await Service(db, id, gateway).ApplyAsync(id,
            CharacterInnateSpellLedgerAction.Initialize, null, null);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved, result.Status);
        Assert.Equal(0, gateway.Calls);
        Assert.Null(await new PostgresCharacterInnateSpellLedgerStore(db).GetAsync(id));
    }

    [Fact]
    public async Task MissingAbilitiesOrCurrentCatalogMismatchCannotCreatePool()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id, withScores: false);
        var gateway = new Native();
        var service = Service(db, id, gateway);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.ApplyAsync(id, CharacterInnateSpellLedgerAction.Initialize,
                null, null)).Status);
        Assert.Equal(0, gateway.Calls);

        var builds = new PostgresCharacterBuildStore(db);
        foreach (var key in new[]
            { "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma" })
            Assert.NotNull(await builds.SetBaseAbilityScoreInputAsync(
                id, key, 12, DateTimeOffset.UtcNow));
        gateway.SourceFingerprint = "changed-catalog";
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.ApplyAsync(id, CharacterInnateSpellLedgerAction.Initialize,
                null, null)).Status);
        Assert.Equal(0, gateway.Calls);
        gateway.SourceFingerprint = Fingerprint;
        gateway.ValidResult = false;
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.ApplyAsync(id, CharacterInnateSpellLedgerAction.Initialize,
                null, null)).Status);
        Assert.Equal(1, gateway.Calls);
        Assert.Null(await new PostgresCharacterInnateSpellLedgerStore(db).GetAsync(id));
    }
}
