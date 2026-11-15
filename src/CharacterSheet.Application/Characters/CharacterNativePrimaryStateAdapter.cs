using System.Text.Json;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Constructs the supported subset of native primary mechanics exclusively from
/// Site-authorized Character Sheet views. It does not authorize Origin grants,
/// equipment modifiers, supernatural acquisition, or form-specific resource
/// and Hit Point policies. Those require separate verified source adapters.
/// </summary>
public static class CharacterNativePrimaryStateAdapter
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AbilityKeys = new(StringComparer.Ordinal)
    {
        "strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"
    };
    private static readonly HashSet<string> OptionalFields = new(StringComparer.Ordinal)
    {
        "skills", "competencies", "defenseContributions", "currentResources",
        "conditionIds", "hitPointGains", "baseWalkSpeed"
    };

    public sealed record Snapshot(
        JsonElement? Mechanics,
        IReadOnlyList<string> Issues,
        bool TransitionInputsVerified);

    public static Snapshot Assemble(
        Guid characterId, CharacterBuildView build, CharacterStateView state,
        CharacterAuthorizedOriginState? origin, string rulesetId, string fingerprint)
    {
        var issues = new List<string>();
        if (characterId == Guid.Empty || build.CharacterId != characterId ||
            state.CharacterId != characterId ||
            string.IsNullOrWhiteSpace(rulesetId) || string.IsNullOrWhiteSpace(fingerprint))
            return new(null, ["native-primary-inputs-unresolved"], false);

        var abilities = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var score in build.BaseAbilityScoreInputs)
        {
            if (!AbilityKeys.Contains(score.AbilityKey) ||
                score.Score is < 0 or > 1000 ||
                !abilities.TryAdd(score.AbilityKey, score.Score))
                return new(null, ["native-primary-inputs-unresolved"], false);
        }

        var advancements = new List<object>();
        var occurrences = new HashSet<Guid>();
        foreach (var entry in build.ProgressionEntries)
        {
            if (entry.Kind != CharacterBuildAdvancementKinds.Class)
            {
                // A feat, subclass or prestige selection must not eliminate
                // otherwise supported Class levels from a read-only projection.
                // Its own effects remain explicitly unresolved.
                if (!issues.Contains("nonclass-advancement-effects-unverified"))
                    issues.Add("nonclass-advancement-effects-unverified");
                continue;
            }
            if (entry.Id == Guid.Empty || !occurrences.Add(entry.Id) ||
                entry.Level is not > 0 or > 1000 ||
                !entry.RuleConceptKey.StartsWith("class.", StringComparison.Ordinal))
                return new(null, ["native-primary-inputs-unresolved"], false);

            advancements.Add(new
            {
                classId = entry.RuleConceptKey, level = entry.Level.Value,
                occurrenceId = entry.Id.ToString("D")
            });
        }

        var choices = NativeCharacterOptionSelections.FromState(state);
        if (choices.HasUnmappedChoices)
            return new(null, ["native-primary-inputs-unresolved"], false);

        if (origin is not null)
            issues.Add("origin-underlying-grants-not-yet-revalidated");
        if (state.InventoryItemOccurrences.Any(item => item.IsEquipped || item.IsAttuned))
            issues.Add("equipped-item-effects-unverified");
        if (state.Conditions.Count > 0 || state.RulesInputs?.Any(x =>
                x.Kind is not CharacterRulesInputKinds.Choice) == true)
            issues.Add("additional-primary-mechanics-unverified");
        if (state.CurrentHitPoints is not null || state.HitPointGains?.Count > 0)
            issues.Add("current-hit-points-and-gains-form-policy-unverified");

        var mechanics = JsonSerializer.SerializeToElement(new
        {
            rulesetId, fingerprint, abilityScores = abilities,
            advancements, selectedOptions = choices.Ids
        }, Wire);
        // Full transition authority requires an independently validated
        // Origin, equipment, and HP policy. Until available, only the narrow
        // source-free synthetic input shape may reach planning at all.
        var transitionInputsVerified = issues.Count == 0 &&
            abilities.Count == AbilityKeys.Count &&
            build.ProgressionEntries.Count <= 100 &&
            advancements.Count <= 100;
        return new(mechanics, issues, transitionInputsVerified);
    }

    /// <summary>
    /// Checks that a trusted source did not substitute primary levels, ability
    /// scores or options, or introduce unsupported native mechanics fields.
    /// Exact validation is required even when Rules Core rechecks the host.
    /// </summary>
    public static bool Matches(JsonElement submitted, Snapshot current)
    {
        if (!current.TransitionInputsVerified || current.Mechanics is not JsonElement expected ||
            submitted.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var field in submitted.EnumerateObject())
        {
            if (OptionalFields.Contains(field.Name))
            {
                if (field.Value.ValueKind != JsonValueKind.Null) return false;
                continue;
            }
            if (field.Name == "selectedOptions")
            {
                if (field.Value.ValueKind == JsonValueKind.Null &&
                    expected.GetProperty("selectedOptions").GetArrayLength() == 0)
                    continue;
            }
            if (field.Name is not ("rulesetId" or "fingerprint" or
                "abilityScores" or "advancements" or "selectedOptions") ||
                !expected.TryGetProperty(field.Name, out var original) ||
                !JsonElement.DeepEquals(original, field.Value))
                return false;
        }
        // Every required input must exist. Null empty selectedOptions is an
        // accepted serializer representation, never an extra entitlement.
        foreach (var required in new[] { "rulesetId", "fingerprint",
                     "abilityScores", "advancements" })
            if (!submitted.TryGetProperty(required, out _))
                return false;
        return expected.GetProperty("selectedOptions").GetArrayLength() == 0 ||
               submitted.TryGetProperty("selectedOptions", out _);
    }
}
