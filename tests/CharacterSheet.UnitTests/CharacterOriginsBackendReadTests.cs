using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterOriginsBackendReadTests
{
    private static readonly Guid CharacterId = Guid.Parse(
        "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static CharacterEffectiveStateResult Effective(
        long? revision = 3, long ledger = 4, string fingerprint = "catalog",
        bool readOnly = false) => new(CharacterEffectiveStateStatus.Ready,
        new CharacterEffectiveStateView(
            CharacterId, readOnly, "partial", "rules", fingerprint, 7,
            revision, ledger, revision is null ? "@primary" : "@composite",
            null, false, new JsonObject(), ["mechanics-unresolved"],
            revision is null ? null : ["instance-a", "instance-b"]));

    private static CharacterSupernaturalInspectionResult Inspection(
        long? revision = 3, long ledger = 4, string fingerprint = "catalog",
        bool readOnly = false) => new(CharacterEffectiveStateStatus.Ready,
        new CharacterSupernaturalInspectionView(
            CharacterId, readOnly,
            revision is null ? "unconfigured" : "stored-unverified",
            revision is null ? null : "rules",
            revision is null ? null : fingerprint,
            revision, ledger,
            revision is null ? new JsonArray() :
                new JsonArray(new JsonObject { ["id"] = "wolf" }),
            new JsonArray(),
            revision is null ? null : new JsonObject(),
            [], false));

    [Fact]
    public void OwnedFormsAreNotMistakenForEligibleActions()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(), Inspection());
        Assert.Equal(CharacterEffectiveStateStatus.Ready, response.Status);
        Assert.NotNull(response.View);
        Assert.Equal(1, response.View.ContractVersion);
        Assert.Equal(3, response.View.SupernaturalRevision);
        Assert.Equal(4, response.View.ManifestationRevision);
        Assert.Equal(new[] { "instance-a", "instance-b" },
            response.View.ActiveManifestationIds);
        Assert.Single(((JsonArray)response.View.OwnedAlternateSheets)!);
        Assert.All(response.View.Actions, action =>
            Assert.Equal("not-evaluated", action.Status));
        Assert.Contains("mechanics-unresolved", response.View.Unresolved);
    }

    [Fact]
    public void OrdinaryCharacterDoesNotGainSyntheticOwnership()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(null, 0), Inspection(null, 0));
        Assert.Equal(CharacterEffectiveStateStatus.Ready, response.Status);
        Assert.Equal("unconfigured", response.View!.OwnershipStatus);
        Assert.Null(response.View.SupernaturalRevision);
        Assert.Null(response.View.ManifestationLedger);
        Assert.Empty((JsonArray)response.View.OwnedAlternateSheets);
        Assert.Empty(response.View.ActiveManifestationIds);
    }

    [Fact]
    public void ConcurrentSupernaturalRevisionChangeFailsClosed()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(3), Inspection(4));
        Assert.Equal(CharacterEffectiveStateStatus.ProjectionUnavailable, response.Status);
        Assert.Null(response.View);
    }

    [Fact]
    public void ConcurrentManifestationChangeFailsClosed()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(3, 4), Inspection(3, 5));
        Assert.Equal(CharacterEffectiveStateStatus.ProjectionUnavailable, response.Status);
    }

    [Fact]
    public void CatalogMismatchFailsClosed()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(fingerprint: "old"), Inspection(fingerprint: "new"));
        Assert.Equal(CharacterEffectiveStateStatus.ProjectionUnavailable, response.Status);
    }

    [Fact]
    public void LifecycleOrAuthorizationMismatchFailsClosed()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(readOnly: true), Inspection());
        Assert.Equal(CharacterEffectiveStateStatus.ProjectionUnavailable, response.Status);
        response = CharacterOriginsBackendReadService.Compose(
            Effective(), new(CharacterEffectiveStateStatus.NotFoundOrNotOwned));
        Assert.Equal(CharacterEffectiveStateStatus.NotFoundOrNotOwned, response.Status);
        Assert.Null(response.View);
    }

    [Fact]
    public void UnconfiguredStateCanNotClaimAnotherCatalogOrGrant()
    {
        var response = CharacterOriginsBackendReadService.Compose(
            Effective(null, 0), Inspection(null, 0, fingerprint: "catalog"));
        Assert.Equal(CharacterEffectiveStateStatus.Ready, response.Status);
        // A primary catalog fingerprint belongs to the underlying ruleset,
        // not to a supernatural entitlement.
        Assert.Null(response.View!.SupernaturalRevision);
    }
}
