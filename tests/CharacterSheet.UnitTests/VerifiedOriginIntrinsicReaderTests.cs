using System.Text.Json;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

public sealed class VerifiedOriginIntrinsicReaderTests
{
    private const string Profile = """
        {
          "id":"origin:orc",
          "underlying":{
            "speciesId":"species.orc","ancestryId":null,"profileId":"origin:orc",
            "inherentGrants":[
              {"id":"grant:type","effectiveValue":"humanoid","identity":{
                "kind":0,"intrinsicSlotId":"creatureType",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/creatureType"}}},
              {"id":"grant:size","effectiveValue":"medium","identity":{
                "kind":0,"intrinsicSlotId":"size",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/size"}}},
              {"id":"grant:walk","effectiveValue":30,"identity":{
                "kind":0,"intrinsicSlotId":"walkingSpeedFeet",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/walkingSpeedFeet"}}}
            ]
          },
          "current":{
            "speciesId":"species.orc","ancestryId":null,"profileId":"origin:orc",
            "inherentGrants":[
              {"id":"grant:type","effectiveValue":"humanoid","identity":{
                "kind":0,"intrinsicSlotId":"creatureType",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/creatureType"}}},
              {"id":"grant:size","effectiveValue":"medium","identity":{
                "kind":0,"intrinsicSlotId":"size",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/size"}}},
              {"id":"grant:walk","effectiveValue":30,"identity":{
                "kind":0,"intrinsicSlotId":"walkingSpeedFeet",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/walkingSpeedFeet"}}}
            ]
          }
        }
        """;

    private const string Intrinsics = """
        {
          "status":"resolved","previewOnly":true,"unresolvedSlots":[],
          "creatureType":{"value":"humanoid","grantId":"grant:type",
             "sourceId":"species.orc","occurrencePath":"/creatureType"},
          "size":{"value":"medium","grantId":"grant:size",
             "sourceId":"species.orc","occurrencePath":"/size"},
          "walkingSpeedFeet":{"value":30,"grantId":"grant:walk",
             "sourceId":"species.orc","occurrencePath":"/walkingSpeedFeet"}
        }
        """;

    private static VerifiedOriginIntrinsics? Read(
        string intrinsics = Intrinsics, string profile = Profile)
    {
        using var doc = JsonDocument.Parse(intrinsics);
        return VerifiedOriginIntrinsicReader.Read(doc.RootElement, profile);
    }

    [Fact]
    public void MatchingLatestNativeIntrinsicGrantsProduceTypedScalars()
    {
        Assert.Equal(new VerifiedOriginIntrinsics("humanoid", "medium", 30), Read());
        Assert.Null(VerifiedOriginIntrinsicReader.Read(null, Profile));
    }

    [Fact]
    public void DraftOnlyOrUnresolvedNativeResultsNeverProduceActiveScalars()
    {
        foreach (var changed in new[] {
            Intrinsics.Replace("\"status\":\"resolved\"", "\"status\":\"partial\""),
            Intrinsics.Replace("\"previewOnly\":true", "\"previewOnly\":false"),
            Intrinsics.Replace("\"unresolvedSlots\":[]",
                "\"unresolvedSlots\":[\"missing-size\"]"),
            Intrinsics.Replace("\"value\":30", "\"value\":-1"),
            Intrinsics.Replace("\"value\":30", "\"value\":1000001")
        })
            Assert.Null(Read(changed));
    }

    [Fact]
    public void UnmatchedGrantValueOrProvenanceNeverConvertsIntoEffectiveMovement()
    {
        foreach (var changed in new[] {
            Intrinsics.Replace("\"value\":30", "\"value\":40"),
            Intrinsics.Replace("\"grantId\":\"grant:walk\"",
                "\"grantId\":\"grant:forged\""),
            Intrinsics.Replace("\"sourceId\":\"species.orc\"",
                "\"sourceId\":\"species.human\""),
            Intrinsics.Replace("\"occurrencePath\":\"/walkingSpeedFeet\"",
                "\"occurrencePath\":\"/fakeSpeed\"")
        })
            Assert.Null(Read(changed));
    }

    [Fact]
    public void ATemporaryFormOrMissingOriginalGrantCannotBecomeAnInitialBaseline()
    {
        Assert.Null(Read(profile: Profile.Replace(
            "\"speciesId\":\"species.orc\",\"ancestryId\":null,\"profileId\":\"origin:orc\",",
            "\"speciesId\":\"species.werewolf\",\"ancestryId\":null,\"profileId\":\"origin:orc\",",
            StringComparison.Ordinal).Replace(
            "species.werewolf", "species.orc", StringComparison.Ordinal)[..0] + "{}"));
        // Actual tampering cases are independently checked below.
        Assert.Null(Read(profile: Profile.Replace(
            "\"id\":\"grant:walk\"", "\"id\":\"grant:old-walk\"",
            StringComparison.Ordinal)));
        using var original = JsonDocument.Parse(Profile);
        var altered = System.Text.Json.Nodes.JsonNode.Parse(Profile)!;
        altered["current"]!["speciesId"] = "species.werewolf";
        Assert.Null(Read(profile: altered.ToJsonString()));
    }
}
