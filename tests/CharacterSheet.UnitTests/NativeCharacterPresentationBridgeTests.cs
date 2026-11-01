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
        Assert.Equal(5, gateway.Calls); // Ruleset, Class, Skill, Competency, projection.
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
    public async Task InvalidHitPointHistoryKeepsCurrentHitPointsAndIndependentClassValues()
    {
        var gateway = new FixtureGateway();
        var occurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var build = Build(occurrence) with
        {
            BaseAbilityScoreInputs = [
                new BaseAbilityScoreInputView(Guid.NewGuid(), "strength", 14, Now, Now)
            ]
        };
        var state = new CharacterStateView(
            Guid.NewGuid(), false, 7, new CharacterDeathSavesView(0, 0), [], [], [],
            HitPointGains: [new CharacterHitPointGainStateView(
                Guid.NewGuid(), occurrence.Id, 0, 5, Now, Now)]);

        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, state, CancellationToken.None);

        Assert.Equal("partial", result.Status);
        Assert.Equal(14, Assert.Single(result.Mechanics!.AbilityValues!).EffectiveValue);
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
        var health = Assert.Single(result.Mechanics.HealthTracks!);
        Assert.Equal(7, health.Current);
        Assert.Null(health.Maximum);
    }

    [Fact]
    public async Task NativeChoicesAreForwardedToProjectionAndUnknownChoicesRemainPartial()
    {
        var gateway = new FixtureGateway();
        var occurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var build = Build(occurrence);
        var state = new CharacterStateView(
            Guid.NewGuid(), false, null, new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.patron", null, null,
                    "patron.warlock.fiend", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.pact", null, null,
                    "pact.warlock.blade", Now, Now)
            ]);

        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, state, CancellationToken.None);
        Assert.Equal("partial", result.Status);
        Assert.Equal(["patron.warlock.fiend", "pact.warlock.blade"],
            gateway.LastSelectedOptions);
        // Valid native choices are evaluated by Rules Core, and must not be
        // mistaken for unknown legacy input that hides independent numeric facts.
        Assert.Equal(14, Assert.Single(result.Mechanics!.AbilityValues!).EffectiveValue);
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
    }

    [Fact]
    public async Task UnmappedChoiceWithholdsIndependentNativeCalculations()
    {
        var gateway = new FixtureGateway();
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [new CharacterRulesInputStateView(
                Guid.NewGuid(), "choice", "legacy.selection", null, null,
                "source:unknown", Now, Now)]);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(entry), state, CancellationToken.None);
        Assert.Equal("partial", result.Status);
        Assert.Null(result.Mechanics!.AbilityValues);
        Assert.Null(result.Mechanics.Training);
    }

    [Fact]
    public void DuplicateOrUnmappedNativeRuleChoicesAreNotAcceptedAsValidSelections()
    {
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.patron", null, null,
                    "patron.warlock.fiend", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.other", null, null,
                    "patron.warlock.fiend", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "legacy.choice", null, null, "source:unmapped", Now, Now)
            ]);
        var selected = NativeCharacterOptionSelections.FromState(state);
        Assert.True(selected.HasUnmappedChoices);
        Assert.Equal(["patron.warlock.fiend"], selected.Ids);
    }

    [Fact]
    public async Task NativeResourceProjectionKeepsPactSlotsAndSpellSlotsSeparate()
    {
        var gateway = new FixtureGateway { IncludeResources = true };
        var occurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var state = new CharacterStateView(
            Guid.NewGuid(), false, null, new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [
                new CharacterRulesInputStateView(Guid.NewGuid(),
                    CharacterRulesInputKinds.Resource, "resource.pact-magic",
                    1, null, null, Now, Now)
            ]);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(occurrence), state, CancellationToken.None);
        var profiles = result.Mechanics!.SpellcastingProfiles!;
        var pact = Assert.Single(profiles, profile => profile.Key == "native.pact-magic");
        var pactResource = Assert.Single(pact.Resources!);
        Assert.Equal("resource.pact-magic", pactResource.Key);
        Assert.Equal(1, pactResource.Current);
        Assert.Equal(2, pactResource.Maximum);
        var slots = Assert.Single(profiles, profile => profile.Key == "native.spell-slots");
        Assert.Equal("resource.spell-slot.1", Assert.Single(slots.Resources!).Key);
        Assert.Null(Assert.Single(slots.Resources!).RecoveryProcedureKey);
        Assert.Equal(1, gateway.LastCurrentResources["resource.pact-magic"]);
    }

    [Fact]
    public async Task NativeSkillsAndCompetenciesStaySeparateAndUnresolvedValuesStayExplicit()
    {
        var gateway = new FixtureGateway();
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var build = Build(entry);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, null, CancellationToken.None);
        var items = result.Mechanics!.Competencies!.Entries;
        var skill = Assert.Single(items, item => item.PresentationCategory == "skill");
        Assert.Equal("Arcana", skill.Label);
        Assert.Equal(3, skill.EffectiveValue);
        var competency = Assert.Single(items, item => item.PresentationCategory == "competency");
        Assert.Equal("Alchemy", competency.Label);
        Assert.Equal("Unresolved", competency.FormattedValue);
        Assert.Equal("competency.alchemy", competency.RankInputKey);
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
        public bool IncludeResources { get; init; }
        public Dictionary<string, int> LastCurrentResources { get; private set; } = [];
        public string? LastAdvancementClassId { get; private set; }
        public int? LastAdvancementLevel { get; private set; }
        public string[] LastSelectedOptions { get; private set; } = [];

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
            var options = category switch {
                "class" => ReturnClass
                    ? new[] { new { id = query, name = "Warlock", kind = "class" } }
                    : [],
                "skill" => new[] { new { id = "skill.arcana", name = "Arcana", kind = "skill" } },
                "competency" => new[] { new { id = "competency.alchemy", name = "Alchemy", kind = "competency" } },
                _ => throw new InvalidOperationException("Unexpected native catalog category.")
            };
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                rulesetId = "first-party-001", fingerprint = Fingerprint, options
            }))!);
        }

        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken token)
        {
            Calls++;
            var item = request.GetProperty("advancements").EnumerateArray().Single();
            LastAdvancementClassId = item.GetProperty("classId").GetString();
            LastAdvancementLevel = item.GetProperty("level").GetInt32();
            LastSelectedOptions = request.GetProperty("selectedOptions")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
            LastCurrentResources = request.GetProperty("currentResources")
                .EnumerateObject().ToDictionary(
                    entry => entry.Name, entry => entry.Value.GetInt32());
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                contractVersion = 1, rulesetId = "first-party-001", fingerprint = Fingerprint,
                status = "partial",
                values = new Dictionary<string, object> {
                    ["ability.strength.score"] = new { status = "resolved", value = 14 },
                    ["ability.strength.modifier"] = new { status = "resolved", value = 2 },
                    ["proficiency.bonus"] = new { status = "resolved", value = 2 },
                    ["skill.arcana.total"] = new { status = "resolved", value = 3 },
                    ["competency.alchemy.total"] = new { status = "unsupported", value = (int?)null },
                    ["ac.normal"] = new { status = "unsupported", value = (int?)null }
                },
                issues = new[] { new { code = "defense-input-required" } },
                requiredChoices = Array.Empty<string>(),
                resources = IncludeResources
                    ? new[] {
                        new { id = "resource.pact-magic", maximum = 2,
                            current = (int?)1, recovery = "short-or-long-rest",
                            spellLevel = (int?)1 },
                        new { id = "resource.spell-slot.1", maximum = 4,
                            current = (int?)null, recovery = "unsupported",
                            spellLevel = (int?)1 }
                    }
                    : []
            }))!);
        }

        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken token) =>
            throw new InvalidOperationException("Not needed for projection.");
    }
}
