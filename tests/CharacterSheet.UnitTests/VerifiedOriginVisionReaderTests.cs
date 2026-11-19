using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

public sealed class VerifiedOriginVisionReaderTests
{
    private const string Profile = """
        {
          "id":"origin:orc",
          "underlying":{"speciesId":"species.orc","profileId":"origin:orc",
            "inherentGrants":[{
              "id":"grant:vision","effectiveValue":{
                "id":"low-light-vision","name":"Low-Light Vision",
                "description":"Dim Light appears Bright.","rule":"rule.low-light-vision"
              },
              "identity":{"kind":1,
                "mechanicalDefinitionId":"species.orc#trait:low-light-vision",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                              "occurrencePath":"/traits/0"}}
            }]
          },
          "current":{"speciesId":"species.orc","profileId":"origin:orc",
            "inherentGrants":[{
              "id":"grant:vision","effectiveValue":{
                "id":"low-light-vision","name":"Low-Light Vision",
                "description":"Dim Light appears Bright.","rule":"rule.low-light-vision"
              },
              "identity":{"kind":1,
                "mechanicalDefinitionId":"species.orc#trait:low-light-vision",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                              "occurrencePath":"/traits/0"}}
            }]
          }
        }
        """;

    private const string Preview = """
        {
          "status":"resolved","previewOnly":true,"unresolvedReasons":[],
          "lowLightVision":{
            "grantId":"grant:vision","sourceId":"species.orc",
            "occurrencePath":"/traits/0","ruleId":"rule.low-light-vision",
            "actualLightLevel":"dim","perceivedLightLevel":"bright"
          }
        }
        """;

    private static VerifiedOriginLowLightVision? Read(
        string vision = Preview, string stored = Profile)
    {
        using var document = JsonDocument.Parse(vision);
        return VerifiedOriginVisionReader.Read(document.RootElement, stored);
    }

    [Fact]
    public void AuthoredDimLightEffectMatchesExactCommittedTraitOccurrence()
    {
        var value = Assert.IsType<VerifiedOriginLowLightVision>(Read());
        Assert.Equal("grant:vision", value.GrantId);
        Assert.Equal("species.orc", value.SourceId);
        Assert.Equal("/traits/0", value.OccurrencePath);
        Assert.Equal("dim", value.ActualLightLevel);
        Assert.Equal("bright", value.PerceivedLightLevel);
    }

    [Fact]
    public void CanonicalSharedTraitIdMatchesExactNativeGrantAndDoesNotAllowOtherDefinitions()
    {
        var canonical = Profile.Replace(
            "species.orc#trait:low-light-vision",
            "trait.shared.low-light-vision", StringComparison.Ordinal);
        var view = Assert.IsType<VerifiedOriginLowLightVision>(
            Read(stored: canonical));
        Assert.Equal("species.orc", view.SourceId);
        Assert.Equal("grant:vision", view.GrantId);

        var mismatched = canonical.Replace(
            "trait.shared.low-light-vision",
            "trait.other.low-light-vision", StringComparison.Ordinal);
        Assert.Null(Read(stored: mismatched));

        var forgedSource = JsonNode.Parse(canonical)!;
        foreach (var state in new[] { "underlying", "current" })
            forgedSource[state]!["inherentGrants"]![0]!["identity"]!
                ["occurrence"]!["source"]!["id"] = "species.elf";
        Assert.Null(Read(stored: forgedSource.ToJsonString()));

        var malformed = JsonNode.Parse(canonical)!;
        foreach (var state in new[] { "underlying", "current" })
            malformed[state]!["inherentGrants"]![0]!["effectiveValue"]!
                ["extraEffect"] = true;
        Assert.Null(Read(stored: malformed.ToJsonString()));
    }

    [Fact]
    public void CanonicalSharedVisionTraitRetainsExactNativeOccurrence()
    {
        var normalized = JsonNode.Parse(Profile)!;
        normalized["underlying"]!["inherentGrants"]![0]!["identity"]!
            ["mechanicalDefinitionId"] = "trait.shared.low-light-vision";
        normalized["current"]!["inherentGrants"]![0]!["identity"]!
            ["mechanicalDefinitionId"] = "trait.shared.low-light-vision";
        var projected = Assert.IsType<VerifiedOriginLowLightVision>(
            Read(stored: normalized.ToJsonString()));
        Assert.Equal("species.orc", projected.SourceId);
        Assert.Equal("/traits/0", projected.OccurrencePath);

        normalized["underlying"]!["inherentGrants"]![0]!["identity"]!
            ["mechanicalDefinitionId"] = "trait.unrelated.low-light-vision";
        normalized["current"]!["inherentGrants"]![0]!["identity"]!
            ["mechanicalDefinitionId"] = "trait.unrelated.low-light-vision";
        Assert.Null(Read(stored: normalized.ToJsonString()));
    }

    [Fact]
    public void UnresolvedOrBrowserClaimedSenseCannotBecomeActive()
    {
        Assert.Null(VerifiedOriginVisionReader.Read(null, Profile));
        foreach (var mutate in new Action<JsonNode>[] {
            json => json["status"] = "unresolved",
            json => json["previewOnly"] = false,
            json => json["unresolvedReasons"] = new JsonArray("invalid-source"),
            json => json["lowLightVision"]!["ruleId"] = "rule.darkvision",
            json => json["lowLightVision"]!["actualLightLevel"] = "darkness",
            json => json["lowLightVision"]!["perceivedLightLevel"] = "normal",
            json => json["lowLightVision"]!["grantId"] = "grant:forged",
            json => json["lowLightVision"]!["sourceId"] = "species.elf",
            json => json["lowLightVision"]!["occurrencePath"] = "/traits/9"
        })
        {
            var altered = JsonNode.Parse(Preview)!;
            mutate(altered);
            Assert.Null(Read(altered.ToJsonString()));
        }
    }

    [Fact]
    public void OldOrTransformedSpeciesTraitsCannotCertifyCurrentVision()
    {
        foreach (var mutate in new Action<JsonNode>[] {
            json => json["current"]!["speciesId"] = "species.werewolf",
            json => json["current"]!["inherentGrants"]![0]!["id"] = "grant:lost",
            json => json["current"]!["inherentGrants"]![0]!["identity"]!
                ["mechanicalDefinitionId"] = "species.orc#trait:darkvision",
            json => json["current"]!["inherentGrants"]![0]!["effectiveValue"]!
                ["rule"] = "rule.unapproved",
            json => json["current"]!["inherentGrants"]![0]!["effectiveValue"]!
                ["extraEffect"] = true,
            json => json["current"]!["inherentGrants"]!.AsArray().Add(
                json["current"]!["inherentGrants"]![0]!.DeepClone())
        })
        {
            var altered = JsonNode.Parse(Profile)!;
            mutate(altered);
            Assert.Null(Read(stored: altered.ToJsonString()));
        }
    }
}
