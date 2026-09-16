namespace CharacterSheet.Domain.Campaigns;

/// <summary>
/// Future identity for Character Sheet-owned campaign module state.
/// Campaign membership and character-campaign association remain Site-owned.
/// </summary>
public readonly record struct CampaignModuleStateKey
{
    public CampaignModuleStateKey(Guid characterId, Guid campaignId, string moduleKey)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        if (campaignId == Guid.Empty)
        {
            throw new ArgumentException("Site CampaignId can not be empty.", nameof(campaignId));
        }

        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            throw new ArgumentException("Module key can not be blank.", nameof(moduleKey));
        }

        CharacterId = characterId;
        CampaignId = campaignId;
        ModuleKey = moduleKey.Trim();
    }

    public Guid CharacterId { get; }
    public Guid CampaignId { get; }
    public string ModuleKey { get; }
}
