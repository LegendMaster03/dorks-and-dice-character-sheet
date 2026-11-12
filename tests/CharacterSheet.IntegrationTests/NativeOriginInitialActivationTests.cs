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
/// Tests application authorization and atomic storage using a controlled Core
/// gateway response. These are not live cross-repository Core mechanics tests.
/// </summary>
public sealed class NativeOriginInitialActivationTests
{
    private const string Draft = """
        {
          "id":"creation-1", "intent":0,
          "method":{"ruleId":"rule.native-origination","methodId":"biological"},
          "contributors":[], "candidates":[], "manifestations":[],
          "selectedSpeciesId":"species.human"
        }
        """;

    // Representative shape of an authorized first-party native Core result.
    // The actual service verifies the delegated result for every activation.
    private const string AllowedResponse = """
        {
          "contractVersion":1, "rulesetId":"first-party-001",
          "fingerprint":"latest-native-content", "previewOnly":true,
          "result": {
            "status":4, "issues":[],
            "profile":{
              "id":"origin:creation-1", "proposalId":"creation-1",
              "underlying":{
                "speciesId":"species.human", "ancestryId":null,
                "inherentGrants":[], "profileId":"origin:creation-1"
              },
              "current":{
                "speciesId":"species.human", "ancestryId":null,
                "inherentGrants":[], "profileId":"origin:creation-1"
              },
              "independentGrants":[],
              "history":[{
                "id":"construction:creation-1",
                "method":{"ruleId":"rule.native-origination","methodId":"biological"},
                "stage":2,"contributorIds":[],
                "replacesConfiguration":false,"resultingProfileId":"origin:creation-1"
              }],
              "adjudication":{
                "adjudication":0,
                "nativeRuleReference":"rule.native-origination"
              }
            }
          }
        }
        """;

    private static async Task PrepareAsync(CharacterSheetDbContext db, Guid characterId)
    {
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        var saved = await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", "old-native-content", Draft,
            0, DateTimeOffset.UtcNow);
        Assert.Equal(1, saved!.Revision);
    }

    private static CharacterOriginInitialActivationService Service(
        CharacterSheetDbContext db, Guid id, FakeNativeGateway native,
        SiteCharacterAccessStatus access = SiteCharacterAccessStatus.Authorized,
        SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active,
        Guid? projectedId = null) =>
        new(new Access(access, projectedId ?? id, lifecycle),
            new PostgresCharacterBuildStore(db),
            new PostgresCharacterOriginDraftStore(db),
            new PostgresCharacterAuthorizedOriginStore(db),
            native, TimeProvider.System);

    [Fact]
    public async Task NativeAuthorizedDirectOriginCommitsOnceAndBlocksLegacySpeciesChanges()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await PrepareAsync(db, id);
            var native = new FakeNativeGateway();
            var applied = await Service(db, id, native).ApplyAsync(id, 1);
            Assert.Equal(CharacterOriginActivationStatus.Applied, applied.Status);
            Assert.Equal(1, applied.Revision);
            Assert.Equal(1, native.Calls);

            var retry = await Service(db, id, native).ApplyAsync(id, 1);
            Assert.Equal(CharacterOriginActivationStatus.OriginAlreadyCommitted, retry.Status);
            Assert.Equal(1, native.Calls);

            await Assert.ThrowsAsync<CharacterOriginManagedException>(() =>
                new PostgresCharacterBuildStore(db).SetFoundationalSelectionAsync(
                    id, CharacterFoundationalSelectionCategory.Species,
                    "species.elf", DateTimeOffset.UtcNow));
            await Assert.ThrowsAsync<CharacterOriginManagedException>(() =>
                new PostgresCharacterBuildStore(db).ClearFoundationalSelectionAsync(
                    id, CharacterFoundationalSelectionCategory.Subspecies,
                    DateTimeOffset.UtcNow));

            var legacyBuilder = new CharacterBuildService(
                new Access(SiteCharacterAccessStatus.Authorized, id,
                    SiteCharacterLifecycleState.Active),
                new PostgresCharacterBuildStore(db), TimeProvider.System);
            var denied = await legacyBuilder.SetSpeciesAsync(id, "species.elf");
            Assert.Equal(CharacterBuildAccessStatus.OriginManaged, denied.Status);
            // A first-party Origin never revokes independently acquired
            // Background choices or makes them depend on legacy Species rows.
            var independent = await legacyBuilder.SetBackgroundAsync(id,
                "background.acolyte");
            Assert.Equal(CharacterBuildAccessStatus.Ready, independent.Status);
            Assert.Contains(independent.View!.FoundationalSelections,
                x => x.Category == CharacterBuildSelectionCategories.Background);
        }

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var saved = await new PostgresCharacterAuthorizedOriginStore(reload).GetAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("latest-native-content", saved.Fingerprint);
        Assert.Equal(1, saved.Revision);
        using var profile = JsonDocument.Parse(saved.ProfileJson);
        Assert.Equal("species.human", profile.RootElement.GetProperty("underlying")
            .GetProperty("speciesId").GetString());
        var receipt = Assert.Single(await new PostgresCharacterAuthorizedOriginStore(reload)
            .GetEventsAsync(id));
        Assert.Equal("construction:creation-1", receipt.EventId);
        Assert.Equal("first-party", receipt.AuthorizationKind);
        Assert.Equal("native-catalog:latest-native-content",
            receipt.AuthorizationEvidenceId);
        var selections = await reload.Set<CharacterFoundationalRuleSelection>()
            .ToArrayAsync();
        Assert.Single(selections, x =>
            x.Category == CharacterFoundationalSelectionCategory.Background);
        Assert.DoesNotContain(selections, x =>
            x.Category is CharacterFoundationalSelectionCategory.Species
                or CharacterFoundationalSelectionCategory.Subspecies
                or CharacterFoundationalSelectionCategory.RaceSpecies);
    }

    [Theory]
    [InlineData(SiteCharacterAccessStatus.NotFoundOrNotOwned, SiteCharacterLifecycleState.Active,
        CharacterOriginActivationStatus.NotFoundOrNotOwned)]
    [InlineData(SiteCharacterAccessStatus.Unauthenticated, SiteCharacterLifecycleState.Active,
        CharacterOriginActivationStatus.Unauthenticated)]
    [InlineData(SiteCharacterAccessStatus.Authorized, SiteCharacterLifecycleState.Archived,
        CharacterOriginActivationStatus.ArchivedReadOnly)]
    public async Task UnauthorizedOrArchivedCannotActivate(
        SiteCharacterAccessStatus access, SiteCharacterLifecycleState lifecycle,
        CharacterOriginActivationStatus expected)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);
        var native = new FakeNativeGateway();
        var refused = await Service(db, id, native, access, lifecycle)
            .ApplyAsync(id, 1);
        Assert.Equal(expected, refused.Status);
        Assert.Equal(0, native.Calls);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db).GetAsync(id));
    }

    [Fact]
    public async Task SiteProjectionMismatchAndRevisionMismatchNeverCallCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);
        var native = new FakeNativeGateway();

        var projection = await Service(db, id, native, projectedId: Guid.NewGuid())
            .ApplyAsync(id, 1);
        Assert.Equal(CharacterOriginActivationStatus.ProjectionUnavailable, projection.Status);
        var stale = await Service(db, id, native).ApplyAsync(id, 2);
        Assert.Equal(CharacterOriginActivationStatus.DraftRevisionConflict, stale.Status);
        Assert.Equal(0, native.Calls);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db).GetAsync(id));
    }

    [Fact]
    public async Task OlderNativeReviewCannotCommitAfterDraftChangesDuringPreview()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var setup = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(setup, id);
        var native = new FakeNativeGateway(async () =>
        {
            await using var updated = new CharacterSheetDbContext(database.CreateOptions());
            var draft = await new PostgresCharacterOriginDraftStore(updated).SaveAsync(
                id, "first-party-001", "changed-content", Draft, 1,
                DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Equal(2, draft!.Revision);
        });
        var result = await Service(setup, id, native).ApplyAsync(id, 1);
        Assert.Equal(CharacterOriginActivationStatus.DraftRevisionConflict, result.Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(setup).GetAsync(id));
        Assert.Empty(await setup.OriginEventReceipts.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingLegacySpeciesRequiresReconciliationAndCannotBeOverwritten()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);
        await new PostgresCharacterBuildStore(db).SetFoundationalSelectionAsync(
            id, CharacterFoundationalSelectionCategory.Species,
            "species.elf", DateTimeOffset.UtcNow);
        var result = await Service(db, id, new FakeNativeGateway()).ApplyAsync(id, 1);
        Assert.Equal(CharacterOriginActivationStatus.LegacySelectionConflict, result.Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db).GetAsync(id));
        Assert.Single((await new PostgresCharacterBuildStore(db).GetAsync(id))!
            .FoundationalSelections);
    }

    [Fact]
    public async Task SelfAssertedDmAndIndependentGrantsNeverActivate()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, id);

        var tests = new Action<JsonNode>[]
        {
            json => json["result"]!["profile"]!["adjudication"]!["adjudication"] =
                "DmAuthorized",
            json => json["result"]!["profile"]!["independentGrants"] =
                JsonNode.Parse("""[{"id":"forged"}]"""),
            json => json["result"]!["profile"]!["history"]![0]!["stage"] =
                "Pending",
            json => json["result"]!["profile"]!["underlying"]!["speciesId"] =
                "species.elf",
            json => json["result"]!["status"] = "Unresolved",
            json => json["result"]!["issues"] =
                JsonNode.Parse("""[{"code":"requires-review"}]"""),
            json => json["result"]!["profile"]!["activeTemporaryTransformation"] =
                JsonNode.Parse("""{"id":"forged-form"}"""),
            json => json["previewOnly"] = false
        };
        foreach (var change in tests)
        {
            var native = new FakeNativeGateway(mutator: change);
            var result = await Service(db, id, native).ApplyAsync(id, 1);
            Assert.Equal(CharacterOriginActivationStatus.NotAuthorizedByNativeRules, result.Status);
            Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db).GetAsync(id));
            Assert.Empty(await db.OriginEventReceipts.ToArrayAsync());
        }
    }

    private sealed class Access(SiteCharacterAccessStatus status, Guid projectedId,
        SiteCharacterLifecycleState lifecycle) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(Guid characterId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(status == SiteCharacterAccessStatus.Authorized
                ? SiteCharacterAccessResult.Authorized(
                    new SiteCharacterProjection(projectedId, "Tester", lifecycle,
                        lifecycle == SiteCharacterLifecycleState.Archived
                            ? DateTimeOffset.UtcNow : null, []))
                : new SiteCharacterAccessResult(status));
    }

    private sealed class FakeNativeGateway(
        Func<Task>? beforeReply = null,
        Action<JsonNode>? mutator = null) : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }

        public async Task<JsonNode> PreviewNativeOriginAsync(JsonElement request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("first-party-001", request.GetProperty("rulesetId").GetString());
            Assert.Equal("creation-1", request.GetProperty("proposal").GetProperty("id").GetString());
            if (beforeReply is not null) await beforeReply();
            var response = JsonNode.Parse(AllowedResponse)!;
            mutator?.Invoke(response);
            return response;
        }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint,
            string category, string? query, int limit, int offset, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
    }
}
