using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.UnitTests;

public sealed class NativeCharacterPresentationBridgeTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-30T00:00:00Z");
    private static readonly string Fingerprint = new('a', 64);

    [Fact]
    public async Task EmptyBuildIsUnconfiguredWithoutAnyCoreRequest()
    {
        var gateway = new FixtureGateway();
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(), null, CancellationToken.None);

        Assert.Equal("unconfigured", result.Status);
        Assert.Null(result.Mechanics);
        Assert.Equal(0, gateway.Calls);
    }

    [Theory]
    [InlineData("class:wizard")]
    [InlineData("feat:alert")]
    public async Task LegacyOrNonclassProgressionIsNotSilentlyConverted(string key)
    {
        var gateway = new FixtureGateway();
        var build = Build(new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0,
            key.StartsWith("feat:", StringComparison.Ordinal) ? "feat" : "class",
            key, null, Now, Now, 1));
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, null, CancellationToken.None);

        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.Mechanics);
        Assert.Contains("not yet mapped", result.Message!, StringComparison.Ordinal);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task VerifiedNativeClassProjectsOnlyResolvedValuesWithoutChangingSavedBuild()
    {
        var gateway = new FixtureGateway();
        var occurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var build = Build(occurrence) with
        {
            BaseAbilityScoreInputs = [
                new BaseAbilityScoreInputView(
                    Guid.NewGuid(), "strength", 14, Now, Now),
                new BaseAbilityScoreInputView(
                    Guid.NewGuid(), "dexterity", 12, Now, Now)
            ]
        };
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, null, CancellationToken.None);

        Assert.Equal("partial", result.Status);
        Assert.NotNull(result.Mechanics);
        var strength = Assert.Single(result.Mechanics.AbilityValues!, value => value.Key == "strength");
        Assert.Equal(14, strength.EffectiveValue);
        Assert.Equal(2, Assert.Single(strength.RelatedValues!).EffectiveValue);
        Assert.Null(result.Mechanics.Defenses); // No invented armor/equipment contributions.
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
        Assert.Equal("class.warlock", occurrence.RuleConceptKey);
        Assert.Equal(1, occurrence.Level);
        Assert.Equal(3, gateway.Calls); // Ruleset, exact class search, projection.
        Assert.Equal("class.warlock", gateway.LastAdvancementClassId);
        Assert.Equal(1, gateway.LastAdvancementLevel);
        Assert.Equal("Warlock", result.NativeClassNames!["class.warlock"]);
    }

    [Fact]
    public async Task UnrepresentedFoundationalSelectionWithholdsDependentCalculations()
    {
        var gateway = new FixtureGateway();
        var now = Now;
        var build = Build(new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, now, now, 1)) with
        {
            BaseAbilityScoreInputs = [
                new BaseAbilityScoreInputView(
                    Guid.NewGuid(), "strength", 14, now, now)
            ],
            FoundationalSelections = [
                new FoundationalRuleSelectionView(
                    Guid.NewGuid(), "species", "species.demo", now, now)
            ]
        };
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, null, CancellationToken.None);

        Assert.Equal("partial", result.Status);
        Assert.Null(result.Mechanics!.Training);
        Assert.Null(result.Mechanics.AbilityValues);
        Assert.Null(result.Mechanics.SavingThrows);
    }

    [Fact]
    public async Task CatalogWithoutExactNativeIdentityDoesNotProject()
    {
        var gateway = new FixtureGateway { ReturnClass = false };
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway,
            Build(new CharacterAdvancementEntryView(
                Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1)),
            null, CancellationToken.None);

        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.Mechanics);
        Assert.Equal(2, gateway.Calls);
    }

    private static CharacterBuildView Build(params CharacterAdvancementEntryView[] entries) =>
        new(Guid.NewGuid(), "in-progress", false, [], [], entries);

    private sealed class FixtureGateway : INativeRulesCoreGateway
    {
        public int Calls { get; private set; }
        public bool ReturnClass { get; init; } = true;
        public string? LastAdvancementClassId { get; private set; }
        public int? LastAdvancementLevel { get; private set; }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                contractVersion = 1, rulesetId = "first-party-001", fingerprint = Fingerprint,
                capabilities = new { characterProjection = true }
            }))!);
        }

        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint, string category,
            string? query, int limit, int offset, CancellationToken token)
        {
            Calls++;
            Assert.Equal(Fingerprint, fingerprint);
            Assert.Equal("class", category);
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                rulesetId = "first-party-001", fingerprint = Fingerprint,
                options = ReturnClass ? new[] { new { id = query, name = "Warlock", kind = "class" } } : []
            }))!);
        }

        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken token)
        {
            Calls++;
            var item = request.GetProperty("advancements").EnumerateArray().Single();
            LastAdvancementClassId = item.GetProperty("classId").GetString();
            LastAdvancementLevel = item.GetProperty("level").GetInt32();
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                contractVersion = 1, rulesetId = "first-party-001", fingerprint = Fingerprint,
                status = "partial",
                values = new Dictionary<string, object> {
                    ["ability.strength.score"] = new { status = "resolved", value = 14 },
                    ["ability.strength.modifier"] = new { status = "resolved", value = 2 },
                    ["proficiency.bonus"] = new { status = "resolved", value = 2 },
                    ["ac.normal"] = new { status = "unsupported", value = (int?)null }
                },
                issues = new[] { new { code = "defense-input-required" } },
                requiredChoices = Array.Empty<string>()
            }))!);
        }

        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken token) =>
            throw new InvalidOperationException("Not needed for projection.");
    }
}
