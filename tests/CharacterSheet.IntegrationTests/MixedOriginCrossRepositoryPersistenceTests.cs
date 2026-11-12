#if PILLAR2_PHASE5_ORIGIN
extern alias CoreHost;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RulesCore.Content;
using CoreApplication = CoreHost::RulesCore.Host.RulesCoreApplication;
using CoreTicketValidator = CoreHost::RulesCore.Host.CharacterSheetMechanicsTicketValidator;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Real cross-repository Rules Core Host HTTP evaluation plus actual PostgreSQL
/// Character Sheet transactions. Site actor/DM scope is supplied by a test
/// identity provider; Site's real ticket projection is verified separately in
/// its own integration tests. No mocked Authorized mechanics result is used.
/// </summary>
public sealed class MixedOriginCrossRepositoryPersistenceTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Guid Actor = Guid.Parse("b989ec77-e740-4412-87ab-fcd5c1049bcb");
    private static readonly Guid Campaign = Guid.Parse("55c09072-f61d-4c90-9d62-69e24398a881");
    private const string HumanElf = """
        {
          "id":"human-elf","intent":1,
          "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
          "contributors":[
            {"id":"parent-one","role":{"kind":0},
             "source":{"kind":0,"id":"species.human"}},
            {"id":"parent-two","role":{"kind":0},
             "source":{"kind":0,"id":"species.elf"}}
          ],
          "candidates":[],"manifestations":[],
          "selectedSpeciesId":null,"selectedAncestryId":null
        }
        """;

    [Fact]
    public async Task IndividualParentInitialProfileComesFromRealNativeCoreButIsNotInheritable()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var native = new HttpNativeGateway(core.Client);
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;
        const string directOrc = """
            {
              "id":"real-parent-orc","intent":0,
              "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
              "contributors":[],"candidates":[],"manifestations":[],
              "selectedSpeciesId":"species.orc","selectedAncestryId":null
            }
            """;

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        var roots = new PostgresCharacterSheetStore(db);
        await roots.GetOrCreateAsync(childId);
        await roots.GetOrCreateAsync(parentId);
        var savedDraft = await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            parentId, "first-party-001", fingerprint, directOrc, 0,
            DateTimeOffset.UtcNow);
        Assert.NotNull(savedDraft);
        using var proposal = JsonDocument.Parse(directOrc);
        var preview = await native.PreviewNativeOriginAsync(
            JsonSerializer.SerializeToElement(new
            {
                rulesetId = "first-party-001",
                fingerprint,
                proposal = proposal.RootElement
            }, Wire), CancellationToken.None);
        Assert.Equal(4, preview["result"]?["status"]?.GetValue<int>());
        Assert.Empty(preview["result"]?["issues"]?.AsArray() ??
            throw new InvalidOperationException("No real Core issues array."));
        var profileJson = preview["result"]?["profile"]?.ToJsonString() ??
            throw new InvalidOperationException("Real Core omitted the profile.");
        var origins = new PostgresCharacterAuthorizedOriginStore(db);
        var stored = await origins.CommitVerifiedAsync(new(
            parentId, "first-party-001", fingerprint, profileJson,
            0, savedDraft.Revision, "construction:real-parent-orc",
            "rule.creature-origination", "biological",
            "first-party", "native-catalog:" + fingerprint,
            DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, stored.Status);

        CharacterOriginParentNativeReviewService MakeParentReader(bool allowParent)
        {
            var site = new OwnerScopedParentAccess(childId, parentId, allowParent);
            var snapshot = new CharacterOriginParentSnapshotService(site,
                new PostgresCharacterBuildStore(db), origins);
            return new(snapshot, origins,
                new PostgresCharacterOriginDraftStore(db), native);
        }

        // Even a real individual Origin cannot be inspected without an
        // independent Site owner check for both the child and parent IDs.
        var forbidden = await MakeParentReader(false).ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.NotFoundOrNotOwned,
            forbidden.Status);
        Assert.Null(forbidden.VerifiedInitialProfile);

        var verified = await MakeParentReader(true).ReviewAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentNativeReviewStatus.Ready, verified.Status);
        Assert.Equal(fingerprint, verified.Review?.LatestNativeFingerprint);
        Assert.NotNull(verified.VerifiedInitialProfile);
        using var original = JsonDocument.Parse(profileJson);
        Assert.True(JsonElement.DeepEquals(original.RootElement,
            verified.VerifiedInitialProfile!.Value));
        var underlying = verified.VerifiedInitialProfile.Value
            .GetProperty("underlying");
        Assert.Equal("species.orc", underlying.GetProperty("speciesId").GetString());
        Assert.True(underlying.GetProperty("inherentGrants").GetArrayLength() > 0);
        Assert.True(JsonElement.DeepEquals(underlying,
            verified.VerifiedInitialProfile.Value.GetProperty("current")));

        // This verified request-local material is never a public parent
        // profile download, mixed contributor, or inheritance ruling.
        var serialized = JsonSerializer.Serialize(verified);
        Assert.DoesNotContain("VerifiedInitialProfile", serialized);
        Assert.DoesNotContain("underlying", serialized);
        Assert.DoesNotContain("inherentGrants", serialized);
        Assert.DoesNotContain("species.orc", serialized);
    }

    [Fact]
    public async Task ThreeDistinctOwnerVisibleParentsAlignWithRealNativeSourceWithoutInheritance()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var native = new HttpNativeGateway(core.Client);
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;
        var childId = Guid.NewGuid();
        // Three inputs intentionally demonstrate that source inspection does
        // not impose the two-parent limit of the DM Human–Elf example.
        var parents = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        var roots = new PostgresCharacterSheetStore(db);
        await roots.GetOrCreateAsync(childId);
        var drafts = new PostgresCharacterOriginDraftStore(db);
        var origins = new PostgresCharacterAuthorizedOriginStore(db);

        for (var i = 0; i < parents.Length; i++)
        {
            var parentId = parents[i];
            await roots.GetOrCreateAsync(parentId);
            var proposalId = "verified-orc-parent-" + i;
            var directProposal = JsonSerializer.Serialize(new
            {
                id = proposalId,
                intent = 0,
                method = new { ruleId = "rule.creature-origination", methodId = "biological" },
                contributors = Array.Empty<object>(),
                candidates = Array.Empty<object>(),
                manifestations = Array.Empty<object>(),
                selectedSpeciesId = "species.orc",
                selectedAncestryId = (string?)null
            }, Wire);
            var parentDraft = await drafts.SaveAsync(parentId,
                "first-party-001", fingerprint, directProposal, 0, DateTimeOffset.UtcNow);
            Assert.NotNull(parentDraft);
            using var parsed = JsonDocument.Parse(directProposal);
            var actual = await native.PreviewNativeOriginAsync(
                JsonSerializer.SerializeToElement(new
                {
                    rulesetId = "first-party-001", fingerprint,
                    proposal = parsed.RootElement
                }, Wire), CancellationToken.None);
            Assert.Equal(4, actual["result"]?["status"]?.GetValue<int>());
            var profileJson = actual["result"]?["profile"]?.ToJsonString() ??
                throw new InvalidOperationException("Native Core returned no parent profile.");
            var committed = await origins.CommitVerifiedAsync(new(
                parentId, "first-party-001", fingerprint, profileJson,
                0, parentDraft.Revision, "construction:" + proposalId,
                "rule.creature-origination", "biological",
                "first-party", "native-catalog:" + fingerprint,
                DateTimeOffset.UtcNow));
            Assert.Equal(AuthorizedOriginStoreStatus.Applied, committed.Status);
        }

        string MixedDraft(string thirdSource) => JsonSerializer.Serialize(new
        {
            id = "three-parent-source-inspection",
            intent = 1,
            method = new { ruleId = "rule.creature-origination", methodId = "biological" },
            contributors = parents.Select((_, index) => new
            {
                id = "parent-" + index,
                role = new { kind = 0 },
                source = new
                {
                    kind = 0,
                    id = index == 2 ? thirdSource : "species.orc"
                }
            }).ToArray(),
            candidates = Array.Empty<object>(),
            manifestations = Array.Empty<object>(),
            selectedSpeciesId = (string?)null,
            selectedAncestryId = (string?)null
        }, Wire);

        var saved = await drafts.SaveAsync(childId, "first-party-001",
            fingerprint, MixedDraft("species.orc"), 0, DateTimeOffset.UtcNow);
        Assert.NotNull(saved);
        var bindings = parents.Select((id, index) =>
            new CharacterOriginParentSourceBinding("parent-" + index, id)).ToArray();

        CharacterOriginParentSourceAlignmentService Reader(params Guid[] allowedParents) =>
            ReaderWith(new MultipleOwnerParentAccess(childId, allowedParents), native);

        CharacterOriginParentSourceAlignmentService ReaderWith(
            ISiteCharacterAccessGateway access, INativeRulesCoreGateway nativeGateway)
        {
            var builds = new PostgresCharacterBuildStore(db);
            var nativeReader = new CharacterOriginParentNativeReviewService(
                new CharacterOriginParentSnapshotService(access, builds, origins),
                origins, drafts, nativeGateway);
            return new(access, builds, drafts, origins, nativeReader);
        }

        // Parent 2 is unowned in this Site context. It must not be read simply
        // because its ID appears in an otherwise owner-visible child draft.
        var denied = await Reader(parents[0], parents[1]).ReviewAsync(
            childId, 1, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned,
            denied.Status);
        Assert.Null(denied.VerifiedSources);

        var reader = Reader(parents);
        var stale = await reader.ReviewAsync(childId, 2, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.DraftRevisionConflict,
            stale.Status);
        var duplicate = await reader.ReviewAsync(childId, 1,
            [bindings[0], bindings[0], bindings[2]]);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.InvalidBindings,
            duplicate.Status);
        var invented = await reader.ReviewAsync(childId, 1,
            [bindings[0], bindings[1], new("not-a-contributor", parents[2])]);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.InvalidBindings,
            invented.Status);

        var verified = await reader.ReviewAsync(childId, 1, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.Ready,
            verified.Status);
        Assert.Equal(1, verified.DraftRevision);
        Assert.Equal(3, verified.Matches?.Count);
        Assert.Equal(3, verified.VerifiedSources?.Count);
        Assert.All(verified.Matches!, match =>
        {
            Assert.Equal("species.orc", match.NativeSourceId);
            Assert.Equal(1, match.ParentOriginRevision);
        });
        Assert.All(verified.VerifiedSources!, source =>
        {
            var underlying = source.VerifiedInitialProfile.GetProperty("underlying");
            Assert.Equal("species.orc", underlying.GetProperty("speciesId").GetString());
            Assert.NotEmpty(underlying.GetProperty("inherentGrants").EnumerateArray());
        });
        var publicJson = JsonSerializer.Serialize(verified, Wire);
        Assert.DoesNotContain("verifiedSources", publicJson);
        Assert.DoesNotContain("verifiedInitialProfile", publicJson);
        Assert.DoesNotContain("underlying", publicJson);
        Assert.DoesNotContain("inherentGrants", publicJson);

        // A Site ownership decision can be withdrawn between separate native
        // parent evaluations; previous snapshots must not confer lasting access.
        // Initial child check + 2 per parent = 7 checks before final recheck.
        var revoked = await ReaderWith(
            new OwnerRevokedBeforeFinalSourceReturn(childId, parents, parents[0], 7),
            native).ReviewAsync(childId, 1, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned,
            revoked.Status);
        Assert.Null(revoked.VerifiedSources);

        // Parent 0 may have changed during the native HTTP evaluation of
        // parent 1, even while its own revision/profile bytes stayed constant.
        // Mutating a historical receipt in-place is database tampering, not an
        // allowed edit path; the inspection must nevertheless refuse it.
        var receiptTamper = new HttpNativeGateway(core.Client,
            afterPreview: async count =>
            {
                if (count != 2) return;
                await using var other = new CharacterSheetDbContext(
                    database.CreateOptions());
                var affected = await other.Database.ExecuteSqlRawAsync(
                    "UPDATE character_origin_event_receipts SET \"AuthorizationEvidenceId\" = {0} WHERE \"CharacterId\" = {1}",
                    "native-catalog:tampered", parents[0]);
                Assert.Equal(1, affected);
            });
        var changedReceipt = await ReaderWith(
            new MultipleOwnerParentAccess(childId, parents), receiptTamper)
            .ReviewAsync(childId, 1, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange,
            changedReceipt.Status);
        Assert.Null(changedReceipt.VerifiedSources);

        // Restore only the fixture, then mutate the source draft JSON without
        // incrementing its revision or UpdatedAt. A different source proposal
        // can not be accepted merely because the profile is unchanged.
        var repaired = await db.Database.ExecuteSqlRawAsync(
            "UPDATE character_origin_event_receipts SET \"AuthorizationEvidenceId\" = {0} WHERE \"CharacterId\" = {1}",
            "native-catalog:" + fingerprint, parents[0]);
        Assert.Equal(1, repaired);

        var draftTamper = new HttpNativeGateway(core.Client,
            afterPreview: async count =>
            {
                if (count != 2) return;
                await using var other = new CharacterSheetDbContext(
                    database.CreateOptions());
                var affected = await other.Database.ExecuteSqlRawAsync(
                    "UPDATE character_origin_drafts SET \"ProposalJson\" = jsonb_set(\"ProposalJson\", '{{id}}', to_jsonb({0}::text), true) WHERE \"CharacterId\" = {1}",
                    "tampered-parent-proposal-id", parents[0]);
                Assert.Equal(1, affected);
            });
        var changedSourceDraft = await ReaderWith(
            new MultipleOwnerParentAccess(childId, parents), draftTamper)
            .ReviewAsync(childId, 1, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange,
            changedSourceDraft.Status);
        Assert.Null(changedSourceDraft.VerifiedSources);

        var changed = await drafts.SaveAsync(childId, "first-party-001",
            fingerprint, MixedDraft("species.elf"), 1, DateTimeOffset.UtcNow);
        Assert.Equal(2, changed?.Revision);
        var mismatch = await reader.ReviewAsync(childId, 2, bindings);
        Assert.Equal(CharacterOriginParentSourceAlignmentStatus.ParentSourceMismatch,
            mismatch.Status);
        Assert.Null(mismatch.VerifiedSources);
        Assert.Null(await origins.GetAsync(childId));
        Assert.Empty(await origins.GetEventsAsync(childId));
    }

    [Fact]
    public async Task SiteScopedHumanElfApprovalEvaluatesPersistsAndReloadsRealNativeGrants()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var characterId = Guid.NewGuid();
        var native = new HttpNativeGateway(core.Client);
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;

        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await db.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
            var draft = await new PostgresCharacterOriginDraftStore(db).SaveAsync(
                characterId, "first-party-001", fingerprint,
                HumanElf, 0, DateTimeOffset.UtcNow);
            Assert.NotNull(draft);
            Assert.Equal(1, draft.Revision);

            var selected = await FiveNativeHumanElfOffers(native, fingerprint);
            var service = Service(db, native, allow: true);
            var denied = await Service(db, native, allow: false).ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.DmScopeDenied,
                denied.Status);
            Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
                .GetAsync(characterId));

            var stale = await service.ApplyAsync(
                characterId, Campaign, 0, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
                stale.Status);

            var forged = await service.ApplyAsync(
                characterId, Campaign, 1, ["made-up-candidate"]);
            Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
                forged.Status);
            Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
                .GetAsync(characterId));

            var applied = await service.ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.Applied, applied.Status);
            Assert.Equal(1, applied.Revision);

            var duplicate = await service.ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.OriginAlreadyCommitted,
                duplicate.Status);
        }

        // A fresh DB context must recover all five actual native grants, the
        // construction event, actor, campaign and exact DM ruling outcomes.
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var origins = new PostgresCharacterAuthorizedOriginStore(db);
            var saved = await origins.GetAsync(characterId);
            Assert.NotNull(saved);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(fingerprint, saved.Fingerprint);
            Assert.NotNull(saved.EvaluatedProjectionJson);
            using var projection = JsonDocument.Parse(saved.EvaluatedProjectionJson);
            Assert.Equal(fingerprint, projection.RootElement.GetProperty("fingerprint")
                .GetString());
            var supportedScalars = VerifiedOriginIntrinsicReader.Read(
                projection.RootElement.GetProperty("intrinsics"), saved.ProfileJson);
            Assert.Equal(new VerifiedOriginIntrinsics("humanoid", "medium", 30),
                supportedScalars);
            var supportedVision = VerifiedOriginVisionReader.Read(
                projection.RootElement.GetProperty("vision"), saved.ProfileJson);
            Assert.NotNull(supportedVision);
            Assert.Equal("species.elf", supportedVision.SourceId);
            Assert.Equal("/traits/0", supportedVision.OccurrencePath);
            Assert.Equal("dim", supportedVision.ActualLightLevel);
            Assert.Equal("bright", supportedVision.PerceivedLightLevel);
            using var profile = JsonDocument.Parse(saved.ProfileJson);
            Assert.Equal("custom.human-elf",
                profile.RootElement.GetProperty("underlying")
                    .GetProperty("speciesId").GetString());
            var grants = profile.RootElement.GetProperty("underlying")
                .GetProperty("inherentGrants").EnumerateArray().ToArray();
            Assert.Equal(5, grants.Length);
            Assert.Contains(grants, grant =>
                grant.GetProperty("identity").GetProperty("intrinsicSlotId")
                    .GetString() == "creatureType");
            Assert.Contains(grants, grant =>
                grant.GetProperty("identity").GetProperty("intrinsicSlotId")
                    .GetString() == "walkingSpeedFeet");
            var receipts = await origins.GetEventsAsync(characterId);
            var receipt = Assert.Single(receipts);
            Assert.Equal("construction:human-elf", receipt.EventId);
            Assert.Equal("campaign-dm", receipt.AuthorizationKind);
            Assert.Equal(Actor, receipt.ApprovalActorUserId);
            Assert.Equal(Campaign, receipt.ApprovalCampaignId);
            Assert.StartsWith("origin-dm:", receipt.AuthorizationEvidenceId);
            using var approved = JsonDocument.Parse(receipt.ApprovedDecisionsJson!);
            var decisions = approved.RootElement.EnumerateArray().ToArray();
            Assert.Equal(6, decisions.Length);
            // The stored profile and cached native projections are consumable
            // only with the current catalog and this exact immutable DM audit.
            var activeRuleset = JsonSerializer.SerializeToNode(new
            {
                rulesetId = saved.RulesetId,
                fingerprint = core.App.Services
                    .GetRequiredService<NativeCatalogStore>().Snapshot!.Fingerprint
            }, Wire)!;
            var verified = VerifiedCommittedMixedOriginReader.Read(
                saved, receipts, activeRuleset);
            Assert.NotNull(verified);
            Assert.Equal(new VerifiedOriginIntrinsics("humanoid", "medium", 30),
                verified.Intrinsics);
            Assert.Equal("species.elf", verified.LowLightVision?.SourceId);
            Assert.Null(VerifiedCommittedMixedOriginReader.Read(
                saved, [], activeRuleset));
            activeRuleset["fingerprint"] = "rules-have-changed";
            Assert.Null(VerifiedCommittedMixedOriginReader.Read(
                saved, receipts, activeRuleset));
            Assert.All(decisions, decision =>
            {
                Assert.Equal(Actor.ToString("D"), decision.GetProperty("authority")
                    .GetProperty("actorId").GetString());
                Assert.Equal(Campaign.ToString("D"), decision.GetProperty("scope")
                    .GetProperty("id").GetString());
                Assert.Equal(receipt.AuthorizationEvidenceId,
                    decision.GetProperty("authority")
                        .GetProperty("authorizationEvidenceId").GetString());
            });
        }
    }

    [Fact]
    public async Task ChangedNativeCatalogOrMissingSelectionDoesNotCommitAProfile()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var native = new HttpNativeGateway(core.Client);
        var characterId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", "previous-native-fingerprint",
            HumanElf, 0, DateTimeOffset.UtcNow);
        var selected = await FiveNativeHumanElfOffers(native,
            core.App.Services.GetRequiredService<NativeCatalogStore>().Snapshot!.Fingerprint);
        var service = Service(db, native, allow: true);
        Assert.Equal(CharacterOriginMixedActivationStatus.NativeContentChanged,
            (await service.ApplyAsync(characterId, Campaign, 1, selected)).Status);
        Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
            (await service.ApplyAsync(characterId, Campaign, 1, [])).Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetAsync(characterId));
        Assert.Empty(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetEventsAsync(characterId));
    }

    [Theory]
    [InlineData("preview-fingerprint")]
    [InlineData("evaluation-fingerprint")]
    [InlineData("preview-missing-freshness")]
    [InlineData("evaluation-missing-freshness")]
    [InlineData("preview-duplicate-offer")]
    public async Task UntrustedNativeResponseMetadataCannotCommitDmOrigin(string fault)
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var characterId = Guid.NewGuid();
        var native = new HttpNativeGateway(core.Client);
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", fingerprint, HumanElf, 0,
            DateTimeOffset.UtcNow);
        var selected = await FiveNativeHumanElfOffers(native, fingerprint);

        var result = await Service(db, new HttpNativeGateway(core.Client, fault),
            allow: true).ApplyAsync(characterId, Campaign, 1, selected);
        Assert.Equal(fault == "preview-duplicate-offer"
                ? CharacterOriginMixedActivationStatus.RulesCoreUnavailable
                : CharacterOriginMixedActivationStatus.NativeContentChanged,
            result.Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetAsync(characterId));
        Assert.Empty(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetEventsAsync(characterId));
    }

    [Fact]
    public async Task UnpinnedDraftCannotReceiveACompletedDmApproval()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var characterId = Guid.NewGuid();
        var native = new HttpNativeGateway(core.Client);
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", null, HumanElf, 0, DateTimeOffset.UtcNow);
        var selected = await FiveNativeHumanElfOffers(native, fingerprint);

        Assert.Equal(CharacterOriginMixedActivationStatus.NativeSnapshotUnpinned,
            (await Service(db, native, allow: true).ApplyAsync(
                characterId, Campaign, 1, selected)).Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetAsync(characterId));
        Assert.Empty(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetEventsAsync(characterId));
    }

    private static CharacterOriginMixedActivationService Service(
        CharacterSheetDbContext db, INativeRulesCoreGateway native, bool allow) =>
        new(new TestDmScope(allow),
            new PostgresCharacterBuildStore(db),
            new PostgresCharacterOriginDraftStore(db),
            new PostgresCharacterAuthorizedOriginStore(db),
            native, TimeProvider.System);

    /// <summary>
    /// This fixture grants child+parent access only when the Site projection
    /// explicitly owns both. A DM role never supplies private parent access.
    /// </summary>
    private sealed class OwnerScopedParentAccess(
        Guid childId, Guid parentId, bool allowParent) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            if (characterId == childId ||
                characterId == parentId && allowParent)
                return Task.FromResult(SiteCharacterAccessResult.Authorized(
                    new SiteCharacterProjection(characterId, "Verified parent input",
                        SiteCharacterLifecycleState.Active, null, [])));
            return Task.FromResult(new SiteCharacterAccessResult(
                SiteCharacterAccessStatus.NotFoundOrNotOwned));
        }
    }

    private sealed class OwnerRevokedBeforeFinalSourceReturn(
        Guid childId, IReadOnlyList<Guid> allowedParentIds,
        Guid revokedParentId, int revokeAfterReads) : ISiteCharacterAccessGateway
    {
        private int _reads;

        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _reads);
            if (characterId != childId && !allowedParentIds.Contains(characterId) ||
                characterId == revokedParentId && count > revokeAfterReads)
                return Task.FromResult(new SiteCharacterAccessResult(
                    SiteCharacterAccessStatus.NotFoundOrNotOwned));
            return Task.FromResult(SiteCharacterAccessResult.Authorized(
                new SiteCharacterProjection(characterId, "Owner-visible character",
                    SiteCharacterLifecycleState.Active, null, [])));
        }
    }

    private sealed class MultipleOwnerParentAccess(
        Guid childId, IReadOnlyList<Guid> allowedParentIds) : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken cancellationToken = default)
        {
            if (characterId == childId || allowedParentIds.Contains(characterId))
                return Task.FromResult(SiteCharacterAccessResult.Authorized(
                    new SiteCharacterProjection(characterId, "Owner-visible character",
                        SiteCharacterLifecycleState.Active, null, [])));
            return Task.FromResult(new SiteCharacterAccessResult(
                SiteCharacterAccessStatus.NotFoundOrNotOwned));
        }
    }

    private sealed class TestDmScope(bool allow) : ISiteOriginDmAccessGateway
    {
        public Task<SiteOriginDmAccessResult> CheckAsync(
            Guid campaignId, Guid characterId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allow && campaignId == Campaign
                ? new SiteOriginDmAccessResult(SiteOriginDmAccessStatus.Authorized,
                    Actor, campaignId, characterId)
                : new SiteOriginDmAccessResult(SiteOriginDmAccessStatus.NotAuthorized));
    }

    private static async Task<string[]> FiveNativeHumanElfOffers(
        INativeRulesCoreGateway native, string fingerprint)
    {
        using var document = JsonDocument.Parse(HumanElf);
        var preview = await native.PreviewNativeOriginAsync(
            JsonSerializer.SerializeToElement(new
            {
                rulesetId = "first-party-001",
                fingerprint,
                proposal = document.RootElement
            }, Wire), CancellationToken.None);
        var candidates = preview["result"]?["evaluatedProposal"]?["candidates"]?
            .AsArray() ?? throw new InvalidOperationException("No real native Origin candidates.");
        var paths = new (string Species, string Path)[]
        {
            ("species.human", "/creatureType"),
            ("species.elf", "/size"),
            ("species.human", "/walkingSpeedFeet"),
            ("species.elf", "/traits/0"),
            ("species.human", "/traits/2")
        };
        return paths.Select(choice =>
            candidates.Single(candidate =>
                candidate?["identity"]?["occurrence"]?["source"]?["id"]?
                    .GetValue<string>() == choice.Species &&
                candidate?["identity"]?["occurrence"]?["occurrencePath"]?
                    .GetValue<string>() == choice.Path)!["id"]!.GetValue<string>())
            .ToArray();
    }

    private sealed class HttpNativeGateway(HttpClient http, string? malformedReply = null,
        Func<int, Task>? afterPreview = null) : INativeRulesCoreGateway
    {
        private int _previewCount;

        public async Task<JsonNode> PreviewNativeOriginAsync(
            JsonElement request, CancellationToken cancellationToken)
        {
            var response = await SendAsync(
                "/api/mechanics/v1/origins/preview", request, cancellationToken);
            if (afterPreview is not null)
                await afterPreview(Interlocked.Increment(ref _previewCount));
            return response;
        }

        public Task<JsonNode> EvaluateApprovedMixedOriginAsync(
            JsonElement request, CancellationToken cancellationToken) =>
            SendAsync("/api/mechanics/v1/origins/evaluate-approved-mixed", request,
                cancellationToken);

        private async Task<JsonNode> SendAsync(
            string path, JsonElement request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(request, options: Wire)
            };
            message.Headers.Add("X-Dorks-Tool-Auth-Ticket", "site-ticket-for-character-sheet");
            message.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                "/tool-host/registrations/rules-core/api/introspect");
            using var response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new RulesCoreGatewayException(
                    $"Real Rules Core returned {(int)response.StatusCode} at {path}: {body}");
            var json = JsonNode.Parse(body) ??
                throw new RulesCoreGatewayException("Real Rules Core returned no JSON.");
            var preview = path.EndsWith("/origins/preview", StringComparison.Ordinal);
            var evaluation = path.EndsWith("/origins/evaluate-approved-mixed",
                StringComparison.Ordinal);
            if (malformedReply == "preview-fingerprint" && preview ||
                malformedReply == "evaluation-fingerprint" && evaluation)
                json["fingerprint"] = "unreviewed-native-fingerprint";
            else if (malformedReply == "preview-missing-freshness" && preview ||
                     malformedReply == "evaluation-missing-freshness" && evaluation)
                json.AsObject().Remove("contentChangedSinceRequest");
            else if (malformedReply == "preview-duplicate-offer" && preview)
            {
                var offers = json["result"]!["evaluatedProposal"]!["candidates"]!.AsArray();
                offers.Add(offers[0]!.DeepClone());
            }
            return json;
        }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> SearchNativeOptionsAsync(
            string fingerprint, string category, string? query, int limit, int offset,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SiteTicketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/tool-host/registrations/rules-core/api/introspect",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("site-ticket-for-character-sheet",
                request.Headers.Authorization?.Parameter);
            var body = JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                toolKey = "rules-core",
                siteMode = "dorks-and-dice",
                user = new { id = Actor.ToString("D") },
                delegatedFromToolKey = "character-sheet",
                privateTunnelSourceToolKey = (string?)null,
                campaigns = new[] { new { id = Campaign, name = "Real-core-test", role = "DM" } }
            }, Wire);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RealCore(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public HttpClient Client { get; } = client;

        public static async Task<RealCore> StartAsync()
        {
            var root = Environment.GetEnvironmentVariable("RULES_CORE_CONTENT_ROOT");
            if (string.IsNullOrWhiteSpace(root) ||
                !Directory.Exists(Path.Combine(root, "first-party-001", "native")))
                throw new InvalidOperationException(
                    "RULES_CORE_CONTENT_ROOT must contain the real Phase 5 native catalog.");
            var app = CoreApplication.Create([], builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["RulesCore:ApiSurface"] = "PublicOnly",
                        ["Rulesets:ContentRoot"] = root,
                        ["Rulesets:DefaultRuleset"] = "first-party-001",
                        ["ToolHost:BaseUrl"] = "https://site.test"
                    });
            }, services =>
            {
                services.AddHttpClient<CoreTicketValidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => new SiteTicketHandler());
            });
            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new RealCore(app, new HttpClient { BaseAddress = new Uri(url) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
#endif
