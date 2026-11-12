using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class ParentOriginSnapshotTests
{
    private const string ParentProfile = """
        {
          "id":"origin:parent-initial",
          "underlying":{
            "speciesId":"species.human",
            "ancestryId":null,
            "profileId":"origin:original-parent",
            "inherentGrants":[{"id":"underlying-grant-not-exposed"}]
          },
          "current":{
            "speciesId":"species.werewolf",
            "ancestryId":null,
            "profileId":"origin:current-parent-form",
            "inherentGrants":[{"id":"current-grant-not-exposed"}]
          },
          "independentGrants":[{"id":"independent-grant-not-exposed"}],
          "history":[{"id":"construction:parent-initial","stage":2}]
        }
        """;

    private static async Task PrepareAsync(CharacterSheetDbContext db,
        Guid childId, Guid parentId, string profile = ParentProfile,
        bool commitParent = true)
    {
        await db.Database.MigrateAsync();
        var sheet = new PostgresCharacterSheetStore(db);
        await sheet.GetOrCreateAsync(childId);
        await sheet.GetOrCreateAsync(parentId);
        if (!commitParent) return;
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(parentId,
            "first-party-001", "stored-native", """
                {"id":"parent-initial","selectedSpeciesId":"species.human"}
                """, 0, DateTimeOffset.UtcNow);
        var stored = await new PostgresCharacterAuthorizedOriginStore(db)
            .CommitVerifiedAsync(new VerifiedOriginPersistenceCommand(
                parentId, "first-party-001", "stored-native", profile,
                0, 1, "construction:parent-initial",
                "rule.creature-origination", "biological",
                "first-party", "native-catalog:stored-native",
                DateTimeOffset.UtcNow));
        Assert.Equal(AuthorizedOriginStoreStatus.Applied, stored.Status);
    }

    private static CharacterOriginParentSnapshotService Service(
        CharacterSheetDbContext db, Guid childId, Guid parentId,
        bool childOwned = true, bool parentOwned = true,
        bool parentArchived = false) =>
        new(new SiteProjectionStub(childId, parentId, childOwned,
                parentOwned, parentArchived),
            new PostgresCharacterBuildStore(db),
            new PostgresCharacterAuthorizedOriginStore(db));

    [Fact]
    public async Task BothOwnerVisibleCharactersMayInspectStoredCurrentButNotInheritGrants()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, childId, parentId);
        var result = await Service(db, childId, parentId, parentArchived: true)
            .InspectAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentSnapshotStatus.Ready, result.Status);
        var snapshot = Assert.IsType<CharacterOriginParentSnapshot>(result.Snapshot);
        Assert.Equal(parentId, snapshot.ParentCharacterId);
        Assert.Equal("first-party-001", snapshot.RulesetId);
        Assert.Equal("stored-native", snapshot.Fingerprint);
        Assert.Equal(1, snapshot.Revision);
        Assert.True(snapshot.SnapshotUpdatedAt <= DateTimeOffset.UtcNow);
        Assert.Equal("species.human", snapshot.Underlying.SpeciesId);
        Assert.Equal("origin:original-parent", snapshot.Underlying.ProfileId);
        Assert.Equal("species.werewolf", snapshot.Current.SpeciesId);
        Assert.Equal("origin:current-parent-form", snapshot.Current.ProfileId);

        // The public DTO contains identity/revision metadata only, even
        // when the stored profile has inherent and independent grants.
        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        Assert.DoesNotContain("inherentGrants", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("independentGrants", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("current-grant-not-exposed", json, StringComparison.Ordinal);
        Assert.DoesNotContain("underlying-grant-not-exposed", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task EitherUnownedCharacterPreventsParentInspection(
        bool childOwned, bool parentOwned)
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, childId, parentId);
        var result = await Service(db, childId, parentId,
                childOwned: childOwned, parentOwned: parentOwned)
            .InspectAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentSnapshotStatus.NotFoundOrNotOwned,
            result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task NoAuthorizedOriginOnParentIsNotPromotedFromAnUntrustedDraft()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, childId, parentId, commitParent: false);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            parentId, "first-party-001", null, """
                {"id":"untrusted-parent","selectedSpeciesId":"species.elf"}
                """, 0, DateTimeOffset.UtcNow);
        var result = await Service(db, childId, parentId)
            .InspectAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentSnapshotStatus.ParentOriginMissing,
            result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task MissingCurrentOrUnderlyingNeverProducesParentEvidence()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, childId, parentId, """
            {
              "id":"origin:incomplete",
              "underlying":{"speciesId":"species.human","inherentGrants":[]}
            }
            """);
        var result = await Service(db, childId, parentId)
            .InspectAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentSnapshotStatus.StoredProfileUnverifiable,
            result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task MissingCompletedEventReceiptCannotProduceParentEvidence()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var childId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await PrepareAsync(db, childId, parentId);
        await db.OriginEventReceipts.Where(item => item.CharacterId == parentId)
            .ExecuteDeleteAsync();

        var result = await Service(db, childId, parentId)
            .InspectAsync(childId, parentId);
        Assert.Equal(CharacterOriginParentSnapshotStatus.StoredProfileUnverifiable,
            result.Status);
        Assert.Null(result.Snapshot);
    }

    private sealed class SiteProjectionStub(
        Guid childId, Guid parentId, bool childOwned,
        bool parentOwned, bool archived) : ISiteCharacterAccessGateway
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
                        archived ? SiteCharacterLifecycleState.Archived :
                            SiteCharacterLifecycleState.Active,
                        archived ? DateTimeOffset.UtcNow : null, [])));
            return Task.FromResult(new SiteCharacterAccessResult(
                SiteCharacterAccessStatus.NotFoundOrNotOwned));
        }
    }
}
