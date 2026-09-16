using System.Text.Json;
using CharacterSheet.Application.Hosting;

namespace CharacterSheet.UnitTests;

public sealed class ToolHostCharacterProjectionContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void AuthenticationDtoDeserializesCharacterProjectionWithoutChangingContractVersion()
    {
        var firstCampaignId = Guid.NewGuid();
        var secondCampaignId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var json = $$"""
        {
          "contractVersion": 1,
          "toolSlug": "character-sheet",
          "siteMode": "dorks-and-dice",
          "user": { "id": "{{Guid.NewGuid():D}}", "displayName": "Owner" },
          "globalRoles": [],
          "campaigns": [],
          "characters": [
            {
              "id": "{{characterId:D}}",
              "name": "Projected Hero",
              "status": "Active",
              "archivedAt": null,
              "campaignIds": ["{{firstCampaignId:D}}", "{{secondCampaignId:D}}"]
            }
          ]
        }
        """;

        var context = JsonSerializer.Deserialize<ToolHostAuthenticationContext>(json, JsonOptions);

        Assert.NotNull(context);
        Assert.Equal(1, context.ContractVersion);
        var character = Assert.Single(context.Characters!);
        Assert.Equal(characterId, character.Id);
        Assert.Equal("Projected Hero", character.Name);
        Assert.Equal("Active", character.Status);
        Assert.Null(character.ArchivedAt);
        Assert.Equal(new[] { firstCampaignId, secondCampaignId }, character.CampaignIds);
    }

    [Fact]
    public void EmptyCharactersAndMissingCharactersRemainDifferentStates()
    {
        const string prefix = """
        {
          "contractVersion": 1,
          "toolSlug": "character-sheet",
          "siteMode": "dorks-and-dice",
          "user": { "id": "00000000-0000-0000-0000-000000000001", "displayName": "Owner" },
          "globalRoles": [],
          "campaigns": []
        """;

        var missing = JsonSerializer.Deserialize<ToolHostAuthenticationContext>(prefix + "}", JsonOptions);
        var empty = JsonSerializer.Deserialize<ToolHostAuthenticationContext>(prefix + ", \"characters\": [] }", JsonOptions);

        Assert.NotNull(missing);
        Assert.NotNull(empty);
        Assert.Null(missing.Characters);
        Assert.NotNull(empty.Characters);
        Assert.Empty(empty.Characters);
    }
}
