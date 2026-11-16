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
        {
            // Origin is owned and stored separately from Character Build. The
            // latest saved revision is the only candidate for a future
            // reconstitution; an old catalog identity or malformed Underlying
            // must never be treated as a verified primary entitlement.
            if (origin.CharacterId != characterId ||
                origin.Revision < 1 ||
                origin.RulesetId != rulesetId ||
                origin.Fingerprint != fingerprint)
                issues.Add("authorized-origin-state-mismatch");
            if (!HasUnderlyingAndCurrent(origin.ProfileJson))
                issues.Add("origin-underlying-structure-unverified");

            // Structural validation is not a reconstruction. Even a valid
            // stored Underlying cannot authorize dependent traits, parent
            // history, background grants or transformation applicability.
            issues.Add("origin-underlying-grants-not-yet-revalidated");
        }

        // Legacy builder selections remain canonical owned input but do not
        // establish independent Origin, Background, Upbringing or deity grants.
        // The native Class/ability baseline may still be projected as partial;
        // an active form transition can not silently ignore these entitlements.
        foreach (var selection in build.FoundationalSelections)
        {
            if (selection.Category is CharacterBuildSelectionCategories.Species
                or CharacterBuildSelectionCategories.Subspecies or "raceSpecies")
            {
                if (!issues.Contains("legacy-species-origin-selection-unverified"))
                    issues.Add("legacy-species-origin-selection-unverified");
            }
            else if (selection.Category == CharacterBuildSelectionCategories.Background)
            {
                if (!issues.Contains("background-upbringing-grants-unverified"))
                    issues.Add("background-upbringing-grants-unverified");
            }
            else if (!issues.Contains("foundational-selection-effects-unverified"))
                issues.Add("foundational-selection-effects-unverified");
        }

        // An item need not be equipped to be subject to a transformation's
        // carry, storage, containment or equipment policy. Do not quietly
        // treat it as unaffected merely because its modifiers are inactive.
        if (state.InventoryItemOccurrences.Count > 0)
            issues.Add("inventory-form-interaction-unverified");
        if (state.InventoryItemOccurrences.Any(item => item.IsEquipped || item.IsAttuned))
            issues.Add("equipped-item-effects-unverified");
        if (state.Conditions.Count > 0 || state.RulesInputs?.Any(x =>
                x.Kind is not CharacterRulesInputKinds.Choice) == true)
            issues.Add("additional-primary-mechanics-unverified");
        // Character-owned choices outside the native class-option namespace
        // may carry their own entitlement or starting-equipment consequences.
        // They remain absent from the read-only native Class projection, but
        // must not be treated as verified inputs for a form transition.
        if (state.RulesInputs?.Any(x =>
                x.Kind == CharacterRulesInputKinds.Choice &&
                !x.Key.StartsWith(NativeCharacterOptionSelections.SlotPrefix,
                    StringComparison.Ordinal)) == true)
            issues.Add("non-native-choice-effects-unverified");
        if (state.RulesInputs?.Any(x => x.Kind is
                CharacterRulesInputKinds.Training or CharacterRulesInputKinds.ClassSkill or
                CharacterRulesInputKinds.CompetencyRank) == true)
            issues.Add("skill-competency-grants-unverified");
        if (state.RulesInputs?.Any(x => x.Kind == CharacterRulesInputKinds.Resource) == true)
            issues.Add("primary-resource-balance-policy-unverified");
        // Currency is a separately owned quantity; no transformation has an
        // approved carry, merge, or containment rule for it yet.
        if (state.CurrencyBalances?.Count > 0)
            issues.Add("currency-form-interaction-unverified");
        if (state.AdvancementProgress is not null)
            issues.Add("advancement-progress-form-policy-unverified");
        if (state.CurrentHitPoints is not null || state.HitPointGains?.Count > 0)
            issues.Add("current-hit-points-and-gains-form-policy-unverified");
        if (state.DeathSaves.Successes != 0 || state.DeathSaves.Failures != 0)
            issues.Add("death-saves-form-policy-unverified");

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
    /// Rejects ambiguous or incomplete stored Origin configuration structure
    /// without assuming biological ancestry, a Species ID, or a particular
    /// origination method. The Rules Core reconstruction and authoritative
    /// source verifier remain separate and are not implemented here.
    /// </summary>
    internal static bool HasUnderlyingAndCurrent(string profileJson)
    {
        try
        {
            using var profile = JsonDocument.Parse(profileJson);
            if (profile.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            // Source identities and grants can be nested inside arrays or
            // adjudication/history objects. Reject duplicate JSON keys at
            // every depth, not only at the configuration roots, before model
            // binding chooses one of two conflicting occurrence values.
            if (!HasUniqueJsonKeys(profile.RootElement))
                return false;
            return profile.RootElement.TryGetProperty("underlying", out var underlying) &&
                underlying.ValueKind == JsonValueKind.Object &&
                profile.RootElement.TryGetProperty("current", out var current) &&
                current.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool HasUniqueJsonKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().All(HasUniqueJsonKeys);
        if (value.ValueKind != JsonValueKind.Object)
            return true;

        // Native profile model binding uses case-insensitive Web JSON
        // property names. Two spellings differing only by case can still
        // override one another at deserialization time.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || !HasUniqueJsonKeys(property.Value))
                return false;
        return true;
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

        var seenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in submitted.EnumerateObject())
        {
            // JSON permits duplicate object property names, but model binding
            // can take the last value. Reject that ambiguity before Core sees it.
            if (!seenFields.Add(field.Name))
                return false;
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
