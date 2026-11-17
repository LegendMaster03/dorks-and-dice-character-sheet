using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

/// <summary>
/// The Character Sheet projection must not treat an active composite as an
/// unmanifested primary character, or accept a fabricated contributor list.
/// These are wire consistency checks, not new entitlement or security rules.
/// </summary>
public sealed class CompositeTransformationProjectionTests
{
    private const string Sheets = """
        [{"id":"wolf"},{"id":"hybrid"}]
        """;
    private const string Ledger = """
        {
          "instances":[
            {
              "id":"i.wolf","sheetId":"wolf","lifecycle":0,"contribution":0,
              "contributionSlots":[{"characteristic":0,"key":"strength"}]
            },
            {
              "id":"i.hybrid","sheetId":"hybrid","lifecycle":0,"contribution":0,
              "contributionSlots":[{"characteristic":0,"key":"dexterity"}]
            }
          ],
          "interactions":[
            {
              "kind":0,"instanceIds":["i.wolf","i.hybrid"],
              "selectedInstanceId":null
            }
          ]
        }
        """;

    private static bool Check(string sheetId, string effectiveJson,
        string ledgerJson, out string? selected, out IReadOnlyList<string>? contributors)
    {
        using var sheets = JsonDocument.Parse(Sheets);
        using var ledger = JsonDocument.Parse(ledgerJson);
        return CharacterEffectiveStateService.VerifySelectedManifestation(
            sheetId, JsonNode.Parse(effectiveJson), sheets.RootElement,
            ledger.RootElement, out selected, out contributors);
    }

    [Fact]
    public void SourceVerifiedCompositeRetainsBothPersistedManifestationIdentities()
    {
        var valid = Check("@composite", """
            {
              "manifestationInstanceId":null,
              "contributingManifestationInstanceIds":["i.hybrid","i.wolf"]
            }
            """, Ledger, out var selected, out var contributors);
        Assert.True(valid);
        Assert.Null(selected);
        Assert.Equal(new[] { "i.hybrid", "i.wolf" }, contributors);
    }

    [Fact]
    public void PrimaryProjectionCanNotMasqueradeAsInactiveDuringCoexistence()
    {
        Assert.False(Check("@primary", """
            {"manifestationInstanceId":null}
            """, Ledger, out _, out _));
    }

    [Theory]
    [InlineData("""
        {"manifestationInstanceId":null,
         "contributingManifestationInstanceIds":["i.hybrid","i.hybrid"]}
        """)]
    [InlineData("""
        {"manifestationInstanceId":null,
         "contributingManifestationInstanceIds":["i.hybrid","foreign"]}
        """)]
    [InlineData("""
        {"manifestationInstanceId":null,
         "contributingManifestationInstanceIds":["i.wolf","i.hybrid"]}
        """)]
    [InlineData("""
        {"manifestationInstanceId":"i.wolf",
         "contributingManifestationInstanceIds":["i.hybrid","i.wolf"]}
        """)]
    public void ChangedOrMisorderedCompositeContributorClaimsAreRejected(
        string response)
    {
        Assert.False(Check("@composite", response, Ledger, out _, out _));
    }

    [Fact]
    public void CoexistenceMustHaveDistinctActuallyPersistedSlots()
    {
        var duplicate = Ledger.Replace(
            "\"characteristic\":0,\"key\":\"dexterity\"",
            "\"characteristic\":0,\"key\":\"strength\"");
        Assert.False(Check("@composite", """
            {"contributingManifestationInstanceIds":["i.hybrid","i.wolf"]}
            """, duplicate, out _, out _));

        var noSlots = Ledger.Replace(
            "\"contributionSlots\":[{\"characteristic\":0,\"key\":\"strength\"}]",
            "\"contributionSlots\":[]");
        Assert.False(Check("@composite", """
            {"contributingManifestationInstanceIds":["i.hybrid","i.wolf"]}
            """, noSlots, out _, out _));
    }

    [Fact]
    public void SelectionOrMissingInteractionDoesNotBecomeCoexistence()
    {
        var selecting = Ledger.Replace("\"kind\":0", "\"kind\":2");
        Assert.False(Check("@composite", """
            {"contributingManifestationInstanceIds":["i.hybrid","i.wolf"]}
            """, selecting, out _, out _));
        var missing = Ledger.Replace("\"interactions\":[", "\"otherInteractions\":[");
        Assert.False(Check("@composite", """
            {"contributingManifestationInstanceIds":["i.hybrid","i.wolf"]}
            """, missing, out _, out _));
    }
}
