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

    [Fact]
    public async Task CommittedOriginAloneIsNotSilentlyPresentedAsAnEmptyCharacter()
    {
        var gateway = new FixtureGateway();
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(), null, CancellationToken.None, hasCommittedOrigin: true);

        Assert.Equal("partial", result.Status);
        Assert.Null(result.Mechanics);
        Assert.Contains("Origin", result.Message!, StringComparison.Ordinal);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task CommittedOriginWithholdsUnprojectedSpeciesEffectsButPreservesClassProficiency()
    {
        var gateway = new FixtureGateway();
        var build = Build(new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1)) with
        {
            BaseAbilityScoreInputs = [
                new BaseAbilityScoreInputView(Guid.NewGuid(), "strength", 14, Now, Now)
            ]
        };

        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, null, CancellationToken.None, hasCommittedOrigin: true);

        Assert.Equal("partial", result.Status);
        Assert.NotNull(result.Mechanics);
        Assert.Null(result.Mechanics.AbilityValues);
        Assert.Null(result.Mechanics.SavingThrows);
        Assert.Null(result.Mechanics.CombatFundamentals);
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
        Assert.Contains("Species/Ancestry", result.Message!, StringComparison.Ordinal);
        Assert.Equal(5, gateway.Calls);
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
        Assert.Equal(2, Assert.Single(result.Mechanics!.Training!).EffectiveValue);
        Assert.Null(result.Mechanics.AbilityValues);
        Assert.Null(result.Mechanics.SavingThrows);
    }

    [Fact]
    public async Task UnknownTrainingKeepsUnrelatedNativeCalculations()
    {
        var gateway = new FixtureGateway();
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var build = Build(entry) with {
            BaseAbilityScoreInputs = [
                new BaseAbilityScoreInputView(Guid.NewGuid(), "strength", 14, Now, Now)
            ]
        };
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [new CharacterRulesInputStateView(
                Guid.NewGuid(), CharacterRulesInputKinds.Training, "skill.unknown",
                null, true, null, Now, Now)]);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, state, CancellationToken.None);
        Assert.Equal("partial", result.Status);
        Assert.Equal(14, Assert.Single(result.Mechanics!.AbilityValues!).EffectiveValue);
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
        Assert.All(result.Mechanics.Competencies!.Entries,
            entry => Assert.Equal("Unresolved", entry.FormattedValue));
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
    public async Task StartingEquipmentChoiceDoesNotInvalidateNativeClassOptions()
    {
        var gateway = new FixtureGateway();
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.patron", null, null,
                    "patron.warlock.fiend", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "starting-equipment." + entry.Id.ToString("D"), null, null,
                    "gold", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "native-option.class.warlock.pact", null, null,
                    "pact.warlock.blade", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(), "choice",
                    "another-sheet-choice", null, null, "value", Now, Now)
            ]);
        var selected = NativeCharacterOptionSelections.FromState(state);
        Assert.False(selected.HasUnmappedChoices);
        Assert.Equal(["patron.warlock.fiend", "pact.warlock.blade"], selected.Ids);

        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(entry), state, CancellationToken.None);
        Assert.Equal("partial", result.Status);
        Assert.Equal(14, Assert.Single(result.Mechanics!.AbilityValues!).EffectiveValue);
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
        Assert.Equal(selected.Ids, gateway.LastSelectedOptions);
    }

    [Theory]
    [InlineData("native-option", "patron.warlock.fiend")]
    [InlineData("native-option.", "patron.warlock.fiend")]
    [InlineData("native-option.class.warlock.patron", "  ")]
    public void MalformedNativeChoicesRemainUnmapped(string key, string value)
    {
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [new CharacterRulesInputStateView(
                Guid.NewGuid(), "choice", key, null, null, value, Now, Now)]);
        var selected = NativeCharacterOptionSelections.FromState(state);
        Assert.True(selected.HasUnmappedChoices);
        Assert.Empty(selected.Ids);
    }

    [Fact]
    public async Task MalformedNativeChoiceWithholdsIndependentNativeCalculations()
    {
        var gateway = new FixtureGateway();
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var state = new CharacterStateView(Guid.NewGuid(), false, null,
            new CharacterDeathSavesView(0, 0), [], [], [],
            RulesInputs: [new CharacterRulesInputStateView(
                Guid.NewGuid(), "choice", "native-option.", null, null,
                "source:unknown", Now, Now)]);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(entry), state, CancellationToken.None);
        Assert.Equal("partial", result.Status);
        Assert.Null(result.Mechanics!.AbilityValues);
        // Native class-level proficiency is independent of invalid class options.
        Assert.Equal(2, Assert.Single(result.Mechanics.Training!).EffectiveValue);
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

    [Fact]
    public async Task NativeProjectionPreservesFamiliesAndCompositeChildrenWithUnresolvedTotals()
    {
        var gateway = new FixtureGateway { IncludeHierarchy = true };
        var entry = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 1);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, Build(entry), null, CancellationToken.None);
        var collection = result.Mechanics!.Competencies!;
        var family = Assert.Single(collection.Entries, item => item.Key == "skill.perform");
        Assert.True(family.IsFamily);
        Assert.Equal("skill", family.PresentationCategory);
        Assert.Equal(new[] { "skill.perform-act" }, family.ChildCompetencyKeys);
        Assert.Null(family.RankInputKey);
        var member = Assert.Single(collection.Entries, item => item.Key == "skill.perform-act");
        Assert.Equal(family.Key, member.Family);
        Assert.True(member.SupportsRanks);
        Assert.Equal("Unresolved", member.FormattedValue);
        var relationship = Assert.Single(collection.Relationships!);
        Assert.Equal("skill.perception", relationship.ParentKey);
        Assert.Equal(new[] { "skill.listen", "skill.spot" }, relationship.ComponentKeys);
        Assert.Equal("arithmetic-mean", relationship.Composition);
        Assert.Null(Assert.Single(collection.Entries,
            item => item.Key == "skill.perception").RankInputKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeWetRequiresPerValueSupportBeforePublishingNumbers(bool isolated)
    {
        var gateway = new FixtureGateway { IncludeConditionValueIsolation = isolated };
        var occurrence = new CharacterAdvancementEntryView(
            Guid.NewGuid(), 0, "class", "class.warlock", null, Now, Now, 2);
        var build = Build(occurrence) with { BaseAbilityScoreInputs = [
            new BaseAbilityScoreInputView(Guid.NewGuid(), "strength", 14, Now, Now)] };
        var state = new CharacterStateView(build.CharacterId, false, 15,
            new CharacterDeathSavesView(0, 0), [], [], [new CharacterConditionOccurrenceView(
                Guid.NewGuid(), "condition.wet", null, null, null, null, null, null, Now, Now)]);
        var result = await NativeCharacterPresentationBridge.ProjectAsync(
            gateway, build, state, CancellationToken.None);
        Assert.Equal(new[] { "condition.wet" }, gateway.LastConditionIds);
        if (isolated) Assert.Equal(14, Assert.Single(result.Mechanics!.AbilityValues!).EffectiveValue);
        else Assert.Null(result.Mechanics!.AbilityValues);
        Assert.Equal(15, Assert.Single(result.Mechanics.HealthTracks!).Current);
        Assert.Equal("partial", result.Status);
    }

    private sealed class FixtureGateway : INativeRulesCoreGateway
    {
        public bool IncludeConditionValueIsolation { get; init; } = true;
        public int Calls { get; private set; }
        public bool ReturnClass { get; init; } = true;
        public bool IncludeResources { get; init; }
        public bool IncludeHierarchy { get; init; }
        public Dictionary<string, int> LastCurrentResources { get; private set; } = [];
        public string? LastAdvancementClassId { get; private set; }
        public int? LastAdvancementLevel { get; private set; }
        public string[] LastSelectedOptions { get; private set; } = [];
        public string[] LastConditionIds { get; private set; } = [];

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken token)
        {
            Calls++;
            return Task.FromResult<JsonNode>(JsonNode.Parse(JsonSerializer.Serialize(new {
                contractVersion = 1, rulesetId = "first-party-001", fingerprint = Fingerprint,
                capabilities = new { characterProjection = true, conditionValueIsolation = IncludeConditionValueIsolation }
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
            var page = JsonNode.Parse(JsonSerializer.Serialize(new {
                rulesetId = "first-party-001", fingerprint = Fingerprint, options
            }))!;
            if (IncludeHierarchy && category == "skill")
                foreach (var (id, name) in new[] {
                    ("skill.perform-act", "Perform (Act)"), ("skill.perception", "Perception"),
                    ("skill.listen", "Listen"), ("skill.spot", "Spot") })
                    page["options"]!.AsArray().Add(new JsonObject {
                        ["id"] = id, ["name"] = name, ["kind"] = "skill" });
            return Task.FromResult(page);
        }

        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken token)
        {
            Calls++;
            LastConditionIds = request.GetProperty("conditionIds").EnumerateArray()
                .Select(value => value.GetString()!).ToArray();
            var item = request.GetProperty("advancements").EnumerateArray().Single();
            LastAdvancementClassId = item.GetProperty("classId").GetString();
            LastAdvancementLevel = item.GetProperty("level").GetInt32();
            LastSelectedOptions = request.GetProperty("selectedOptions")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
            LastCurrentResources = request.GetProperty("currentResources")
                .EnumerateObject().ToDictionary(
                    entry => entry.Name, entry => entry.Value.GetInt32());
            var projection = JsonNode.Parse(JsonSerializer.Serialize(new {
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
            }))!;
            if (IncludeHierarchy)
                projection["training"] = JsonNode.Parse("""
                    [{"id":"skill.arcana","name":"Arcana","kind":"skill","governingAbility":"intelligence","supportsRanks":true},
                     {"id":"competency.alchemy","name":"Alchemy","kind":"competency","supportsRanks":true},
                     {"id":"skill.perform","name":"Perform","kind":"skill-family","isFamily":true,"supportsRanks":false},
                     {"id":"skill.perform-act","name":"Perform (Act)","kind":"skill","familyId":"skill.perform","supportsRanks":true},
                     {"id":"skill.perception","name":"Perception","kind":"skill","componentIds":["skill.listen","skill.spot"],"composition":"arithmetic-mean","supportsRanks":false},
                     {"id":"skill.listen","name":"Listen","kind":"skill","supportsRanks":true},
                     {"id":"skill.spot","name":"Spot","kind":"skill","supportsRanks":true}]
                    """);
            return Task.FromResult(projection);
        }

        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken token) =>
            throw new InvalidOperationException("Not needed for projection.");
    }
}
