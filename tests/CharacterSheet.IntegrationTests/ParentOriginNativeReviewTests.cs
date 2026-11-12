using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// The real database proves source-draft and receipt continuity. The native
/// gateway is controlled: this suite does not substitute for the separate
/// cross-repository Rules Core catalog probe.
/// </summary>
public sealed class ParentOriginNativeReviewTests
{
    private const string Proposal = """
        {
          "id":"parent-creation-1","intent":0,
          "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
          "contributors":[],"candidates":[],"manifestations":[],
          "selectedSpeciesId":"species.orc","selectedAncestryId":null
        }
        """;

    private const string StoredProfile = """
        {
          "id":"origin:parent-creation-1","proposalId":"parent-creation-1",
          "underlying":{
            "speciesId":"species.orc","ancestryId":null,"inherentGrants":[],
            "profileId":"origin:parent-creation-1"
          },
          "current":{
            "speciesId":"species.orc","ancestryId":null,"inherentGrants":[],
            "profileId":"origin:parent-creation-1"
          },
          "independentGrants":[],
          "history":[{
            "id":"construction:parent-creation-1",
            "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
            "stage":2,"contributorIds":[],"replacesConfiguration":false,
            "resultingProfileId":"origin:parent-creation-1"
          }],
          "adjudication":{"adjudication":0,"nativeRuleReference":"rule.creature-origination"}
        }
        """;

    private static JsonNode Response()
    {
        var profile = JsonNode.Parse(StoredProfile)!;
        return new JsonObject
        {
            ["contractVersion"] = 1,
            ["rulesetId"] = "first-party-001",
            ["fingerprint"] = "native-current",
            ["previewOnly"] = true,
            // This is deliberately not a valid committed trait grant.
            // The server preserves native interpretation privately and the
            // Sheet reader must reject its missing saved occurrence.
            ["vision"] = JsonNode.Parse("""
                {
                  "status":"resolved","previewOnly":true,"unresolvedReasons":[],
                  "lowLightVision":{
                    "grantId":"grant:unverified-vision",
                    "sourceId":"species.orc","occurrencePath":"/traits/0",
                    "ruleId":"rule.low-light-vision",
                    "actualLightLevel":"dim","perceivedLightLevel":"bright"}
                }
                """),
            ["result"] = new JsonObject
            {
                ["status"] = 4,
                ["issues"] = new JsonArray(),
                ["profile"] = profile
            }
        };
    }

    private static async Task InitializeAsync(CharacterSheetDbContext db,
        Guid childId, Guid parentId)
    {
        await db.Database.MigrateAsync();
        var roots = new PostgresCharacterSheetStore(db);
        await roots.GetOrCreateAsync(childId);
        await roots.GetOrCreateAsync(parentId);
        var draft = await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            parentId, "first-party-001", "native-old", Proposal, 0,
            DateTimeOffset.UtcNow);
        Assert.Equal(1, draft!.Revision);
        var committed = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new VerifiedOriginPersistenceCommand(
                parentId, "first-party-001", "native-old", StoredProfile,
                0, 1, "construction:parent-creation-1",
                "rule.creature-origination", "biological",
                "first-party", "native-catalog:native-old",
                DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, committed.Status);
    }

    private static CharacterOriginParentNativeReviewService Service(
        CharacterSheetDbContext db, Guid childId, Guid parentId,
        NativeGateway native, bool childOwned = true, bool parentOwned = true,
        bool archivedParent = false)
    {
        var site = new Access(childId, parentId, childOwned, parentOwned, archivedParent);
        var build = new PostgresCharacterBuildStore(db);
        var origins = new PostgresCharacterAuthorizedOriginStore(db);
        var snapshot = new CharacterOriginParentSnapshotService(site, build, origins);
        return new(snapshot, origins,
            new PostgresCharacterOriginDraftStore(db), native);
    }

    [Fact]
    public async Task StoredDirectParentMatchesLatestCatalogWithoutPublishingGrants()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway();
        var result = await Service(db, childId, parentId, native,
                archivedParent: true).ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.Ready, result.Status);
        Assert.Equal(parentId, result.Review!.ParentCharacterId);
        Assert.Equal("first-party-001", result.Review.RulesetId);
        Assert.Equal("native-current", result.Review.LatestNativeFingerprint);
        Assert.Equal(1, result.Review.StoredRevision);
        Assert.Equal("parent-creation-1", result.Review.VerifiedProposalId);
        Assert.Equal(1, native.Calls);
        Assert.NotNull(result.EvaluatedVision);
        // Verified native initial parent material is only available to
        // trusted in-process consumers; it never authorizes inheritance.
        Assert.NotNull(result.VerifiedInitialProfile);
        Assert.Equal("species.orc", result.VerifiedInitialProfile!.Value
            .GetProperty("underlying").GetProperty("speciesId").GetString());
        using var expected = JsonDocument.Parse(StoredProfile);
        Assert.True(JsonElement.DeepEquals(
            expected.RootElement, result.VerifiedInitialProfile.Value));
        Assert.NotNull(result.PersistenceEvidence);
        Assert.Equal("native-old", result.PersistenceEvidence.OriginFingerprint);
        Assert.Equal("construction:parent-creation-1",
            result.PersistenceEvidence.ReceiptEventId);
        Assert.Null(VerifiedOriginVisionReader.Read(
            result.EvaluatedVision, StoredProfile));
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("unverified-vision", serialized);
        Assert.DoesNotContain("EvaluatedVision", serialized);
        Assert.DoesNotContain("evaluatedVision", serialized);
        Assert.DoesNotContain("VerifiedInitialProfile", serialized);
        Assert.DoesNotContain("verifiedInitialProfile", serialized);
        Assert.DoesNotContain("PersistenceEvidence", serialized);
        Assert.DoesNotContain("persistenceEvidence", serialized);
        Assert.DoesNotContain("ReceiptEventId", serialized);
        Assert.DoesNotContain("\"underlying\"", serialized);
        Assert.DoesNotContain("species.orc", serialized);
        Assert.DoesNotContain("InherentGrants",
            JsonSerializer.Serialize(result.Review), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("independentGrants",
            JsonSerializer.Serialize(result.Review), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("native-old", native.LastRequest!.Value
            .GetProperty("fingerprint").GetString());
        Assert.Equal("species.orc", native.LastRequest!.Value
            .GetProperty("proposal").GetProperty("selectedSpeciesId").GetString());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task MissingEitherSiteOwnerNeverCallsNative(
        bool childOwned, bool parentOwned)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway();
        var refused = await Service(db, childId, parentId, native, childOwned,
                parentOwned).ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.NotFoundOrNotOwned,
            refused.Status);
        Assert.Null(refused.Review);
        Assert.Null(refused.VerifiedInitialProfile);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task NativeProfileContentChangeIsAnExplicitMismatch()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway(change: response =>
        {
            var profile = response["result"]!["profile"]!;
            // Keep Underlying and Current equivalent, so the new profile is
            // internally consistent but has an altered native source grant.
            var grants = JsonNode.Parse("""
                [{"id":"new-native-orc-grant"}]
                """)!;
            profile["underlying"]!["inherentGrants"] = grants.DeepClone();
            profile["current"]!["inherentGrants"] = grants.DeepClone();
        });
        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.NativeProfileChanged,
            result.Status);
        Assert.Null(result.Review);
        Assert.Null(result.VerifiedInitialProfile);
    }

    [Fact]
    public async Task UnresolvedNativeRulesNeverReuseOldAuthorizedProfile()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway(change: response =>
        {
            response["result"]!["status"] = 1;
            response["result"]!["issues"] = JsonNode.Parse("""
                [{"code":"required-origin-selection-missing"}]
                """);
        });
        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.NativeRulesUnresolved,
            result.Status);
        Assert.Null(result.Review);
        Assert.Null(result.VerifiedInitialProfile);
    }

    [Fact]
    public async Task ASecondPermanentEventRequiresASeparateReconstructionVerifier()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await InitializeAsync(db, childId, parentId);
        var updated = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new VerifiedOriginPersistenceCommand(
                parentId, "first-party-001", "native-second", StoredProfile,
                1, 1, "rite:changed-underlying",
                "rule.rite-of-rebirth", "ritual",
                "dm-ruling", "fixture-only-not-a-real-approval",
                DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, updated.Status);
        var native = new NativeGateway();
        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory,
            result.Status);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task InPlaceParentProfileRewriteDuringNativeReviewDoesNotYieldVerifiedMaterial()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var options = database.CreateOptions();
        await using var db = new CharacterSheetDbContext(options);
        await InitializeAsync(db, childId, parentId);

        // Simulate an out-of-band data rewrite that fails to advance Origin
        // revision or timestamp. The service must recheck actual content,
        // not rely solely on revision metadata after the native HTTP call.
        var native = new NativeGateway(beforeReturn: async () =>
        {
            await using var other = new CharacterSheetDbContext(options);
            var tampered = JsonNode.Parse(StoredProfile)!;
            tampered["unverifiedProfileMarker"] = "changed-mid-review";
            var changed = await other.Database.ExecuteSqlRawAsync(
                "UPDATE character_authorized_origins SET \"ProfileJson\" = {0}::jsonb WHERE \"CharacterId\" = {1}",
                tampered.ToJsonString(), parentId);
            Assert.Equal(1, changed);
        });

        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange,
            result.Status);
        Assert.Null(result.Review);
        Assert.Null(result.VerifiedInitialProfile);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("source-draft")]
    public async Task InPlaceReceiptOrSourceDraftRewriteDuringNativeReviewFailsClosed(
        string altered)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var options = database.CreateOptions();
        await using var db = new CharacterSheetDbContext(options);
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway(beforeReturn: async () =>
        {
            await using var other = new CharacterSheetDbContext(options);
            var rows = altered == "receipt"
                ? await other.Database.ExecuteSqlRawAsync(
                    "UPDATE character_origin_event_receipts SET \"AuthorizationEvidenceId\" = {0} WHERE \"CharacterId\" = {1}",
                    "native-catalog:forged", parentId)
                : await other.Database.ExecuteSqlRawAsync(
                    "UPDATE character_origin_drafts SET \"ProposalJson\" = jsonb_set(\"ProposalJson\", '{id}', to_jsonb({0}::text), true) WHERE \"CharacterId\" = {1}",
                    "different-parent-proposal", parentId);
            Assert.Equal(1, rows);
        });

        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange,
            result.Status);
        Assert.Null(result.Review);
        Assert.Null(result.VerifiedInitialProfile);
        Assert.Null(result.PersistenceEvidence);
    }

    [Fact]
    public async Task AConcurrentRiteCannotRetroactivelyValidateAnOlderParentRevision()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var options = database.CreateOptions();
        await using var db = new CharacterSheetDbContext(options);
        await InitializeAsync(db, childId, parentId);
        var native = new NativeGateway(beforeReturn: async () =>
        {
            await using var other = new CharacterSheetDbContext(options);
            var changed = await new PostgresCharacterAuthorizedOriginStore(other)
                .CommitVerifiedAsync(new VerifiedOriginPersistenceCommand(
                    parentId, "first-party-001", "native-second", StoredProfile,
                    1, 1, "rite:changed-during-preview",
                    "rule.rite-of-rebirth", "ritual",
                    "dm-ruling", "fixture-only-not-a-real-approval",
                    DateTimeOffset.UtcNow.AddMinutes(1)));
            Assert.Equal(AuthorizedOriginStoreStatus.Applied, changed.Status);
        });
        var result = await Service(db, childId, parentId, native)
            .ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange,
            result.Status);
        Assert.Null(result.Review);
        Assert.Null(result.VerifiedInitialProfile);
    }

    private sealed class Access(Guid childId, Guid parentId, bool childOwned,
        bool parentOwned, bool archivedParent) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            if (characterId == childId && childOwned)
                return Task.FromResult(SiteCharacterAccessResult.Authorized(
                    new SiteCharacterProjection(childId, "Child",
                        SiteCharacterLifecycleState.Active, null, [])));
            if (characterId == parentId && parentOwned)
                return Task.FromResult(SiteCharacterAccessResult.Authorized(
                    new SiteCharacterProjection(parentId, "Parent",
                        archivedParent ? SiteCharacterLifecycleState.Archived :
                            SiteCharacterLifecycleState.Active,
                        archivedParent ? DateTimeOffset.UtcNow : null, [])));
            return Task.FromResult(new SiteCharacterAccessResult(
                SiteCharacterAccessStatus.NotFoundOrNotOwned));
        }
    }

    private sealed class NativeGateway(
        Action<JsonNode>? change = null, Func<Task>? beforeReturn = null)
        : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }
        public JsonElement? LastRequest { get; private set; }

        public async Task<JsonNode> PreviewNativeOriginAsync(
            JsonElement request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request.Clone();
            if (beforeReturn is not null)
                await beforeReturn();
            var response = Response();
            change?.Invoke(response);
            return response;
        }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint,
            string category, string? query, int limit, int offset, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
