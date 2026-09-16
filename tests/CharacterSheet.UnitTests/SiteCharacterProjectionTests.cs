using CharacterSheet.Application.Site;

namespace CharacterSheet.UnitTests;

public sealed class SiteCharacterProjectionTests
{
    [Fact]
    public void ArchivedCharacterDisallowsOrdinaryEditingByLifecycle()
    {
        var projection = new SiteCharacterProjection(
            Guid.NewGuid(),
            "Archived Character",
            SiteCharacterLifecycleState.Archived,
            DateTimeOffset.UtcNow,
            [Guid.NewGuid(), Guid.NewGuid()]);

        Assert.False(projection.AllowsOrdinaryEditingByLifecycle);
        Assert.Equal(2, projection.CampaignIds.Count);
    }

    [Fact]
    public void ActiveCharacterMayParticipateInMultipleCampaigns()
    {
        var projection = new SiteCharacterProjection(
            Guid.NewGuid(),
            "Active Character",
            SiteCharacterLifecycleState.Active,
            null,
            [Guid.NewGuid(), Guid.NewGuid()]);

        Assert.True(projection.AllowsOrdinaryEditingByLifecycle);
        Assert.Equal(2, projection.CampaignIds.Count);
    }
}
