using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Domain.Characters;

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
    public void UnverifiedFoundationalOriginsAndBackgroundsRemainPartialAndBlockTransitions()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var selections = new[]
        {
            new FoundationalRuleSelectionView(Guid.NewGuid(), "species",
                "species.synthetic", Now, Now),
            new FoundationalRuleSelectionView(Guid.NewGuid(), "background",
                "background.synthetic", Now, Now),
            new FoundationalRuleSelectionView(Guid.NewGuid(), "deity",
                "deity.synthetic", Now, Now)
        };
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build with { FoundationalSelections = selections },
            state, null, Ruleset, Fingerprint);
        Assert.NotNull(snapshot.Mechanics);
        Assert.False(snapshot.TransitionInputsVerified);
        Assert.Contains("legacy-species-origin-selection-unverified", snapshot.Issues);
        Assert.Contains("background-upbringing-grants-unverified", snapshot.Issues);
        Assert.Contains("foundational-selection-effects-unverified", snapshot.Issues);
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            snapshot.Mechanics.Value, snapshot));

        var legacy = CharacterNativePrimaryStateAdapter.Assemble(
            id, build with
            {
                FoundationalSelections =
                [
                    new FoundationalRuleSelectionView(Guid.NewGuid(), "raceSpecies",
                        "race.synthetic", Now, Now)
                ]
            }, state, null, Ruleset, Fingerprint);
        Assert.Contains("legacy-species-origin-selection-unverified", legacy.Issues);
        Assert.False(legacy.TransitionInputsVerified);
    }

    [Fact]
    public void UnequippedInventoryAndDeathSavesDoNotGrantUnmodeledFormPolicies()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var inventory = new CharacterInventoryItemOccurrenceView(
            Guid.NewGuid(), "item.synthetic", Now,
            IsCarried: false, IsEquipped: false, IsAttuned: false);
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, state with
            {
                InventoryItemOccurrences = [inventory],
                DeathSaves = new CharacterDeathSavesView(1, 0)
            }, null, Ruleset, Fingerprint);
        Assert.NotNull(snapshot.Mechanics);
        Assert.False(snapshot.TransitionInputsVerified);
        Assert.Contains("inventory-form-interaction-unverified", snapshot.Issues);
        Assert.Contains("death-saves-form-policy-unverified", snapshot.Issues);
        Assert.DoesNotContain("equipped-item-effects-unverified", snapshot.Issues);
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            snapshot.Mechanics.Value, snapshot));
    }

    [Fact]
    public void NonNativeChoicesTrainedSkillsAndOwnedResourcesBlockTransitionsWithoutHidingClass()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        var updated = state with
        {
            RulesInputs =
            [
                new CharacterRulesInputStateView(Guid.NewGuid(),
                    CharacterRulesInputKinds.Choice, "starting-equipment.choice",
                    null, null, "custom-kit", Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(),
                    CharacterRulesInputKinds.Training, "skill.athletics",
                    null, null, null, Now, Now),
                new CharacterRulesInputStateView(Guid.NewGuid(),
                    CharacterRulesInputKinds.Resource, "resource.synthetic",
                    3, null, null, Now, Now)
            ],
            CurrencyBalances =
            [
                new CharacterCurrencyBalanceView(Guid.NewGuid(),
                    "gold", 25, Now, Now)
            ],
            AdvancementProgress = 2
        };
        var snapshot = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, updated, null, Ruleset, Fingerprint);
        Assert.NotNull(snapshot.Mechanics);
        Assert.Single(snapshot.Mechanics.Value.GetProperty("advancements").EnumerateArray());
        Assert.False(snapshot.TransitionInputsVerified);
        Assert.Contains("non-native-choice-effects-unverified", snapshot.Issues);
        Assert.Contains("skill-competency-grants-unverified", snapshot.Issues);
        Assert.Contains("primary-resource-balance-policy-unverified", snapshot.Issues);
        Assert.Contains("currency-form-interaction-unverified", snapshot.Issues);
        Assert.Contains("advancement-progress-form-policy-unverified", snapshot.Issues);
        Assert.False(CharacterNativePrimaryStateAdapter.Matches(
            snapshot.Mechanics.Value, snapshot));
    }

    [Fact]
    public void AuthorizedOriginRequiresMatchingIdentityAndUnambiguousUnderlying()
    {
        var id = Guid.NewGuid();
        var (build, state) = Fixture(id);
        const string valid = """
            {"id":"origin:synthetic","underlying":{"processCreatureTypeId":"creature.synthetic"},"current":{"processCreatureTypeId":"creature.synthetic"}}
            """;
        CharacterAuthorizedOriginState Saved(Guid owner, string ruleset, string fingerprint,
            string profile) => new(owner, ruleset, fingerprint, profile, null, Now);

        var acceptedShape = CharacterNativePrimaryStateAdapter.Assemble(
            id, build, state, Saved(id, Ruleset, Fingerprint, valid), Ruleset, Fingerprint);
        Assert.DoesNotContain("authorized-origin-state-mismatch", acceptedShape.Issues);
        Assert.DoesNotContain("origin-underlying-structure-unverified", acceptedShape.Issues);
        Assert.Contains("origin-underlying-grants-not-yet-revalidated",
            acceptedShape.Issues);
        Assert.False(acceptedShape.TransitionInputsVerified);

        foreach (var invalid in new[]
        {
            """{"id":"origin:synthetic","underlying":{}}""",
            """{"id":"origin:synthetic","underlying":null,"current":{}}""",
            """{"underlying":{},"current":{},"underlying":{"speciesId":"species.forged"}}""",
            """{"underlying":{"speciesId":"species.human","speciesId":"species.forged"},"current":{}}""",
            """{"underlying":{},"current":{"ancestryId":"ancestry.elf","ancestryId":"ancestry.forged"}}"""
        })
        {
            var malformed = CharacterNativePrimaryStateAdapter.Assemble(
                id, build, state, Saved(id, Ruleset, Fingerprint, invalid),
                Ruleset, Fingerprint);
            Assert.Contains("origin-underlying-structure-unverified", malformed.Issues);
            Assert.False(malformed.TransitionInputsVerified);
        }

        foreach (var mismatch in new[]
        {
            Saved(Guid.NewGuid(), Ruleset, Fingerprint, valid),
            Saved(id, "other-ruleset", Fingerprint, valid),
            Saved(id, Ruleset, new string('b', 64), valid)
        })
        {
            var stale = CharacterNativePrimaryStateAdapter.Assemble(
                id, build, state, mismatch, Ruleset, Fingerprint);
            Assert.Contains("authorized-origin-state-mismatch", stale.Issues);
            Assert.False(stale.TransitionInputsVerified);
        }
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
