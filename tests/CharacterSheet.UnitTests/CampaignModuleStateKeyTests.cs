using CharacterSheet.Domain.Campaigns;

namespace CharacterSheet.UnitTests;

public sealed class CampaignModuleStateKeyTests
{
    [Fact]
    public void OverlayKeyUsesSiteCharacterAndCampaignIds()
    {
        var characterId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();

        var key = new CampaignModuleStateKey(characterId, campaignId, "crafting");

        Assert.Equal(characterId, key.CharacterId);
        Assert.Equal(campaignId, key.CampaignId);
        Assert.Equal("crafting", key.ModuleKey);
    }
}
