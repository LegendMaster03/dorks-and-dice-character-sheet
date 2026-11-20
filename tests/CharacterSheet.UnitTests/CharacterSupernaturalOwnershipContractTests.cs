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

    private static JsonNode OverlapPlanned() => JsonNode.Parse("""
        {
          "contractVersion":1,
          "rulesetId":"rules",
          "fingerprint":"catalog",
          "status":"planned",
          "committed":false,
          "effectiveMechanicsReady":false,
          "plan":{
            "status":"planned",
            "characterId":"11111111-2222-3333-4444-555555555555",
            "eventId":"overlap:1",
            "intentId":"intent:overlap:1",
            "authorizationEvidenceId":"fixture:overlap",
            "expectedSupernaturalRevision":1,
            "expectedManifestationRevision":0,
            "expectedProgressionRevision":0,
            "decisionId":"fixture.decision",
            "replacedDecisionId":null,
            "proposedState":{
              "characterId":"11111111-2222-3333-4444-555555555555",
              "primaryMechanics":{},
              "alternateSheets":[],
              "persistentInfluences":[],
              "influenceOverlapSelections":[{
                "id":"fixture.decision",
                "authorizationEvidenceId":"fixture:overlap"
              }]
            }
          }
        }
        """)!;

    private static bool AcceptOverlap(JsonNode response) =>
        CharacterInfluenceOverlapDecisionService.TryReadVerifiedPlan(
            response, Character, "intent:overlap:1", "overlap:1",
            "rules", "catalog", 1, 0, 0, "fixture:overlap",
            out _, out _, out _);

    [Fact]
    public void OverlapPlanRequiresExactIdentityAndImmutableDecisionEvidence()
    {
        var accepted = OverlapPlanned();
        Assert.True(CharacterInfluenceOverlapDecisionService.TryReadVerifiedPlan(
            accepted, Character, "intent:overlap:1", "overlap:1",
            "rules", "catalog", 1, 0, 0, "fixture:overlap",
            out var selections, out var decision, out var replaced));
        Assert.Single(selections!);
        Assert.Equal("fixture.decision", decision);
        Assert.Null(replaced);

        foreach (var (scope, key, value) in new[]
        {
            ("root", "committed", "true"),
            ("root", "effectiveMechanicsReady", "true"),
            ("root", "fingerprint", "\"stale\""),
            ("plan", "eventId", "\"forged\""),
            ("plan", "intentId", "\"forged\""),
            ("plan", "authorizationEvidenceId", "\"forged\""),
            ("plan", "decisionId", "\"forged\""),
            ("plan", "expectedSupernaturalRevision", "2"),
            ("plan", "expectedProgressionRevision", "1")
        })
        {
            var forged = OverlapPlanned();
            var target = scope == "root" ? forged : forged["plan"]!;
            target.AsObject()[key] = JsonNode.Parse(value);
            Assert.False(AcceptOverlap(forged), scope + "." + key);
        }

        var missing = OverlapPlanned();
        missing["plan"]!["proposedState"]!["influenceOverlapSelections"] = new JsonArray();
        Assert.False(AcceptOverlap(missing));

        var forgedEvidence = OverlapPlanned();
        forgedEvidence["plan"]!["proposedState"]!
            ["influenceOverlapSelections"]![0]!["authorizationEvidenceId"] = "forged";
        Assert.False(AcceptOverlap(forgedEvidence));
    }

    [Fact]
    public void OverlapSourceMustBeBoundToTheExactOwnedCharacterAndIntent()
    {
        TrustedInfluenceOverlapDecisionMaterial Material(
            string intent = "intent:overlap:1", string characterId = "") => new(
            JsonSerializer.SerializeToElement(new
            {
                characterId = characterId == "" ? Character.ToString("D") : characterId,
                eventId = "overlap:1", intentId = intent,
                rulesetId = "rules", fingerprint = "catalog",
                decisionId = "fixture.decision", replacedDecisionId = (string?)null,
                authorizationEvidenceId = "fixture:overlap",
                sourceOccurrence = new
                {
                    source = new { kind = 0, id = "fixture.supernatural", version = "v1" },
                    occurrencePath = "overlaps.fixture"
                }
            }), "fixture:overlap");
        bool Matches(TrustedInfluenceOverlapDecisionMaterial material) =>
            CharacterInfluenceOverlapDecisionService.MatchesTrustedSource(material,
                Character, "intent:overlap:1", "overlap:1", "rules", "catalog");

        Assert.True(Matches(Material()));
        Assert.False(Matches(Material(intent: "forged")));
        Assert.False(Matches(Material(characterId: Guid.NewGuid().ToString("D"))));
    }

}
