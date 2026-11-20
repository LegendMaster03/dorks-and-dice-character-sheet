using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

/// <summary>
/// Canonical version-2 Core ownership contract correlation only. Synthetic source and
/// Core plan payloads are not production entitlement authorization.
/// </summary>
public sealed class CharacterSupernaturalOwnershipContractTests
{
    private static readonly Guid Character = Guid.Parse(
        "11111111-2222-3333-4444-555555555555");

    private static TrustedSupernaturalOwnershipMaterial Source(
        string eventId = "acq-1", string operation = "Acquire",
        string catalog = "catalog") => new(
            JsonSerializer.SerializeToElement(new
            {
                characterId = Character.ToString("D"),
                operation, eventId,
                rulesetId = "rules", fingerprint = catalog,
                authorizationEvidenceId = "fixture:trusted",
                sourceOccurrence = new
                {
                    source = new { kind = 0, id = "fixture.wolf", version = "v1" },
                    occurrencePath = "fixture.wolf.acquisition"
                }
            }), "fixture:trusted");

    private static bool Matches(TrustedSupernaturalOwnershipMaterial source) =>
        CharacterSupernaturalOwnershipService.MatchesTrustedSource(
            source, Character, CharacterSupernaturalOwnershipOperation.Acquire,
            "acq-1", "rules", "catalog");

    private static JsonNode Planned() => JsonNode.Parse("""
        {
          "contractVersion":2,
          "rulesetId":"rules",
          "fingerprint":"catalog",
          "status":"planned",
          "committed":false,
          "effectiveMechanicsReady":false,
          "plan":{
            "status":"planned",
            "characterId":"11111111-2222-3333-4444-555555555555",
            "operation":"Acquire",
            "eventId":"acq-1",
            "intentId":"intent-1",
            "authorizationEvidenceId":"fixture:trusted",
            "expectedSupernaturalRevision":0,
            "expectedManifestationRevision":0,
            "expectedProgressionRevision":0,
            "proposedState":{
              "characterId":"11111111-2222-3333-4444-555555555555",
              "primaryMechanics":{},
              "alternateSheets":[{"id":"wolf","progression":[]}],
              "persistentInfluences":[],
              "influenceOverlapSelections":[]
            },
            "proposedJournal":{
              "characterId":"11111111-2222-3333-4444-555555555555",
              "revision":1,
              "processedEventIds":["acq-1"],
              "consumedEligibilityEvidenceIds":[]
            }
          }
        }
        """)!;

    private static bool Accept(JsonNode response) =>
        CharacterSupernaturalOwnershipService.TryReadVerifiedPlan(
            response, Character, CharacterSupernaturalOwnershipOperation.Acquire,
            "acq-1", "intent-1", "rules", "catalog", 0, 0, 0, "fixture:trusted",
            out _, out _, out _, out _);

    [Fact]
    public void SyntheticSourceMustBeBoundToCharacterOperationEventAndCatalog()
    {
        Assert.True(Matches(Source()));
        Assert.False(Matches(Source(eventId: "other")));
        Assert.False(Matches(Source(operation: "Advance")));
        Assert.False(Matches(Source(catalog: "stale")));

        var wrongCharacter = new TrustedSupernaturalOwnershipMaterial(
            JsonSerializer.SerializeToElement(new
            {
                characterId = Guid.NewGuid().ToString("D"),
                operation = "Acquire", eventId = "acq-1",
                rulesetId = "rules", fingerprint = "catalog",
                authorizationEvidenceId = "fixture:trusted",
                sourceOccurrence = new { id = "wolf" }
            }), "fixture:trusted");
        Assert.False(Matches(wrongCharacter));

        var missingOccurrence = new TrustedSupernaturalOwnershipMaterial(
            JsonSerializer.SerializeToElement(new
            {
                characterId = Character.ToString("D"),
                operation = "Acquire", eventId = "acq-1",
                rulesetId = "rules", fingerprint = "catalog",
                authorizationEvidenceId = "fixture:trusted"
            }), "fixture:trusted");
        Assert.False(Matches(missingOccurrence));
    }

    [Fact]
    public void VerifiedPlanOnlyReturnsExactRevisionBoundOwnedPayload()
    {
        var source = Planned();
        Assert.True(CharacterSupernaturalOwnershipService.TryReadVerifiedPlan(
            source, Character, CharacterSupernaturalOwnershipOperation.Acquire,
            "acq-1", "intent-1", "rules", "catalog", 0, 0, 0, "fixture:trusted",
            out var sheets, out var influences, out var journal,
            out var overlap));
        Assert.Single(sheets!);
        Assert.Empty(influences!);
        Assert.Equal(1, journal!["revision"]!.GetValue<long>());
        Assert.Empty(overlap!);

        sheets[0]!["id"] = "modified";
        Assert.Equal("wolf", source["plan"]!["proposedState"]!
            ["alternateSheets"]![0]!["id"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("committed", true)]
    [InlineData("effectiveMechanicsReady", true)]
    public void ClientFacingOrPrematurePlayableClaimsDoNotAuthorizePlan(
        string field, bool value)
    {
        var response = Planned();
        response[field] = value;
        Assert.False(Accept(response));
    }

    [Theory]
    [InlineData("characterId", "other")]
    [InlineData("eventId", "spoof")]
    [InlineData("intentId", "forged-intent")]
    [InlineData("operation", "Advance")]
    [InlineData("authorizationEvidenceId", "forged")]
    public void ForgedCorePlanIdentityFailsClosed(string field, string value)
    {
        var response = Planned();
        response["plan"]![field] = value;
        Assert.False(Accept(response));
    }

    [Fact]
    public void StaleRevisionsAndCatalogIdentityFailClosed()
    {
        var response = Planned();
        response["plan"]!["expectedSupernaturalRevision"] = 3;
        Assert.False(Accept(response));

        response = Planned();
        response["plan"]!["expectedManifestationRevision"] = 7;
        Assert.False(Accept(response));

        response = Planned();
        response["plan"]!["proposedJournal"]!["revision"] = 7;
        Assert.False(Accept(response));

        response = Planned();
        response["fingerprint"] = "stale";
        Assert.False(Accept(response));
    }

    [Fact]
    public void MissingOrInvalidOwnedPayloadFailsClosed()
    {
        var response = Planned();
        response["plan"]!["proposedState"]!.AsObject().Remove("persistentInfluences");
        Assert.False(Accept(response));

        response = Planned();
        response["plan"]!["proposedState"]!["alternateSheets"] = new JsonObject();
        Assert.False(Accept(response));

        response = Planned();
        response["plan"]!["proposedJournal"]!["processedEventIds"] = "forged";
        // Missing canonical progression evidence can not produce a committable
        // supernatural ownership plan.
        Assert.False(Accept(response));
    }

    [Fact]
    public void MalformedSourceOccurrenceIsRejectedBeforeCoreDelegation()
    {
        var invalid = new TrustedSupernaturalOwnershipMaterial(
            JsonSerializer.SerializeToElement(new
            {
                characterId = Character.ToString("D"),
                operation = "Acquire", eventId = "acq-1",
                rulesetId = "rules", fingerprint = "catalog",
                authorizationEvidenceId = "fixture:trusted",
                sourceOccurrence = new { occurrencePath = "fixture.invalid", source = new { id = "wolf" } }
            }), "fixture:trusted");
        Assert.False(Matches(invalid));
    }
}
