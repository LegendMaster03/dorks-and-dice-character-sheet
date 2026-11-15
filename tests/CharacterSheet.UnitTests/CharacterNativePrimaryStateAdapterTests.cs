using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.UnitTests;

/// <summary>
/// The native primary projection is a deliberately bounded Character-owned
/// subset, not an Origin, equipment, HP, or supernatural entitlement source.
/// </summary>
public sealed class CharacterNativePrimaryStateAdapterTests
{
    private const string Ruleset = "first-party-001";
    private const string Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static (CharacterBuildView Build, CharacterStateView State) Fixture(
        Guid id, bool feat = false, int? currentHp = null)
    {
        var scores = new[]
        {
            "strength", "dexterity", "constitution",
            "intelligence", "wisdom", "charisma"
        }.Select(key => new BaseAbilityScoreInputView(
            Guid.NewGuid(), key, 12, Now, Now)).ToArray();
        var classOccurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, CharacterBuildAdvancementKinds.Class,
            "class.barbarian", null, Now, Now, 2);
        var rows = feat
            ? new[]
            {
                classOccurrence,
                new CharacterAdvancementEntryView(Guid.NewGuid(), 1,
                    CharacterBuildAdvancementKinds.Feat, "feat.synthetic",
                    null, Now, Now)
            }
            : [classOccurrence];
        var build = new CharacterBuildView(id, "ready", false, [], scores, rows);
        var state = new CharacterStateView(id, false, currentHp,
            new CharacterDeathSavesView(0, 0), [], [], []);
        return (build, state);
    }

    [Fact]
    public void ClassAndAbilitiesComeFromCurrentOwnedOccurrences()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, state, null, Ruleset, Fingerprint);
        Assert.NotNull(snapshot.Mechanics);
        Assert.True(snapshot.TransitionInputsVerified);
        Assert.Empty(snapshot.Issues);
        var input = snapshot.Mechanics!.Value;
        Assert.Equal(6, input.GetProperty("abilityScores").EnumerateObject().Count());
        var classRow = Assert.Single(input.GetProperty("advancements").EnumerateArray());
        Assert.Equal("class.barbarian", classRow.GetProperty("classId").GetString());
        Assert.Equal(2, classRow.GetProperty("level").GetInt32());
        Assert.Equal(build.ProgressionEntries[0].Id.ToString("D"),
            classRow.GetProperty("occurrenceId").GetString());
        Assert.True(CharacterNativePrimaryStateAdapter.Matches(input, snapshot));
    }

    [Fact]
    public void UnsupportedFeatAndCurrentHpStayVisibleButDoNotEraseClassProjection()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id, feat: true, currentHp: 7);
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, state, null, Ruleset, Fingerprint);
        Assert.NotNull(snapshot.Mechanics);
        Assert.Single(snapshot.Mechanics.Value.GetProperty("advancements").EnumerateArray());
        Assert.Contains("nonclass-advancement-effects-unverified", snapshot.Issues);
        Assert.Contains("current-hit-points-and-gains-form-policy-unverified",
            snapshot.Issues);
        Assert.False(snapshot.TransitionInputsVerified);
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            snapshot.Mechanics.Value, snapshot));
    }

    [Fact]
    public void SourceCannotOverridePrimaryValuesOrSupplyUnverifiedResources()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, state, null, Ruleset, Fingerprint);
        var original = snapshot.Mechanics!.Value;
        var substitutedScore = JsonNode.Parse(original.GetRawText())!.AsObject();
        substitutedScore["abilityScores"]!["strength"] = 99;
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            JsonSerializer.SerializeToElement(substitutedScore), snapshot));

        var inventedResource = JsonNode.Parse(original.GetRawText())!.AsObject();
        inventedResource["currentResources"] = new JsonObject { ["invented.pool"] = 10 };
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            JsonSerializer.SerializeToElement(inventedResource), snapshot));

        var hiddenDuplicate = original.GetRawText().TrimEnd('}');
        var duplicate = JsonDocument.Parse(hiddenDuplicate +
            ",\"rulesetId\":\"first-party-001\"}");
        using (duplicate)
            Assert.False(CharacterNativePrimaryStateAdapter.Matches(
                duplicate.RootElement, snapshot));
    }

    [Fact]
    public void InvalidAbilityOwnershipAndCharacterMismatchFailClosed()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var duplicated = build with
        {
            BaseAbilityScoreInputs = build.BaseAbilityScoreInputs.Concat(
                [build.BaseAbilityScoreInputs[0]]).ToArray()
        };
        Assert.Null(CharacterNativePrimaryStateAdapter.Assemble(
            id, duplicated, state, null, Ruleset, Fingerprint).Mechanics);
        Assert.Null(CharacterNativePrimaryStateAdapter.Assemble(
            Guid.NewGuid(), build, state, null, Ruleset, Fingerprint).Mechanics);
    }
}
