using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Read-only native projection for verified native class identities. This does not
/// convert saved legacy concept keys, alter persistence, or infer missing rules.
/// </summary>
public static class NativeCharacterPresentationBridge
{
    private static readonly string[] Abilities =
        ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];
    private static readonly string[] Saves =
        ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma",
         "fortitude", "reflex", "will"];
    private static readonly Regex ClassId = new(
        @"^class\.[a-z0-9-]+(?:\.[a-z0-9-]+)*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record Outcome(
        string Status,
        CharacterMechanicsPresentationView? Mechanics,
        string? Message = null,
        IReadOnlyDictionary<string, string>? NativeClassNames = null);

    public static async Task<Outcome> ProjectAsync(
        INativeRulesCoreGateway gateway,
        CharacterBuildView build,
        CharacterStateView? state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(build);

        if (build.ProgressionEntries.Count == 0 && build.BaseAbilityScoreInputs.Count == 0 &&
            build.FoundationalSelections.Count == 0)
            return new("unconfigured", null,
                "Set base ability scores and choose a starting class to calculate character mechanics.");

        // Preserve every saved selection. If a record can not be translated
        // unambiguously, do not submit a partial class history as a complete one.
        var unsupportedProgression = build.ProgressionEntries.Any(entry =>
            entry.Kind != CharacterBuildAdvancementKinds.Class ||
            entry.Level is not > 0 ||
            !ClassId.IsMatch(entry.RuleConceptKey));
        if (unsupportedProgression)
            return new("unavailable", null,
                "Saved advancement contains rule identities or progression types that are not yet mapped to native Rules Core.");

        var metadata = await gateway.GetNativeRulesetAsync(cancellationToken);
        var rulesetId = ReadText(metadata, "rulesetId");
        var fingerprint = ReadText(metadata, "fingerprint");
        if (ReadInt(metadata, "contractVersion") != 1 ||
            string.IsNullOrWhiteSpace(rulesetId) ||
            fingerprint is null || !Regex.IsMatch(fingerprint, "^[0-9a-f]{64}$") ||
            metadata["capabilities"]?["characterProjection"]?.GetValue<bool>() != true)
            throw new RulesCoreGatewayException("Rules Core returned invalid native projection metadata.");

        var classEntries = build.ProgressionEntries
            .OrderBy(entry => entry.Ordinal ?? int.MaxValue)
            .ThenBy(entry => entry.Id)
            .ToArray();
        var classNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var classId in classEntries.Select(entry => entry.RuleConceptKey)
                     .Distinct(StringComparer.Ordinal))
        {
            var options = await gateway.SearchNativeOptionsAsync(
                fingerprint, "class", classId, 100, 0, cancellationToken);
            if (ReadText(options, "fingerprint") != fingerprint ||
                ReadText(options, "rulesetId") != rulesetId ||
                options["options"] is not JsonArray items)
                throw new RulesCoreGatewayException("Rules Core returned a mismatched native class catalog.");
            var matchingClass = items.FirstOrDefault(option =>
                ReadText(option, "id") == classId && ReadText(option, "kind") == "class");
            var name = ReadText(matchingClass, "name");
            if (string.IsNullOrWhiteSpace(name))
                return new("unavailable", null,
                    "A saved class does not have a verified native class identity; no automatic conversion was applied.");
            classNames.Add(classId, name);
        }

        var abilities = build.BaseAbilityScoreInputs
            .Where(entry => Abilities.Contains(entry.AbilityKey, StringComparer.Ordinal))
            .GroupBy(entry => entry.AbilityKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(entry => entry.UpdatedAt).First().Score,
                StringComparer.Ordinal);

        var trainingCatalog = await LoadNativeTrainingCatalogAsync(
            gateway, rulesetId, fingerprint, cancellationToken);
        var nativeChoices = NativeCharacterOptionSelections.FromState(state);
        var storedRanks = (state?.RulesInputs ?? [])
            .Where(input => input.Kind == CharacterRulesInputKinds.CompetencyRank &&
                input.IntegerValue is not null)
            .GroupBy(input => input.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(input => input.UpdatedAt)
                    .First().IntegerValue!.Value, StringComparer.Ordinal);
        var trained = (state?.RulesInputs ?? [])
            .Where(input => input.Kind == CharacterRulesInputKinds.Training &&
                input.BooleanValue != false)
            .Select(input => input.Key).ToHashSet(StringComparer.Ordinal);
        var skillIds = trainingCatalog.Where(item => item.Kind == "skill")
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var competencyIds = trainingCatalog.Where(item => item.Kind == "competency")
            .Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var trainingIds = skillIds.Concat(competencyIds).ToHashSet(StringComparer.Ordinal);
        var unknownTraining = storedRanks.Keys.Concat(trained)
            .Any(key => !trainingIds.Contains(key));
        var skills = skillIds.ToDictionary(id => id,
            id => (object)new { ranks = storedRanks.GetValueOrDefault(id),
                proficient = trained.Contains(id), miscModifier = 0 },
            StringComparer.Ordinal);
        var competencies = competencyIds.ToDictionary(id => id,
            id => (object)new { ranks = storedRanks.GetValueOrDefault(id),
                proficient = trained.Contains(id), miscModifier = 0 },
            StringComparer.Ordinal);
        var unmodeledSelections = build.FoundationalSelections.Count != 0 ||
            state?.Conditions.Count > 0 ||
            state?.InventoryItemOccurrences.Any(item => item.IsEquipped) == true ||
            state?.RulesInputs?.Any(input => input.Kind is not (
                CharacterRulesInputKinds.Resource or
                CharacterRulesInputKinds.CompetencyRank or
                CharacterRulesInputKinds.Training or
                CharacterRulesInputKinds.KnownSpell)) == true ||
            unknownTraining ||
            build.BaseAbilityScoreInputs.Count != abilities.Count;

        // A feat, subclass, or prestige class requires its own effective rule.
        // This stage does not silently omit such a progression entry.
        var hitPointGains = (state?.HitPointGains ?? [])
            .Where(gain => gain.ClassLevel > 0 && gain.HitDieValue >= 0)
            .GroupBy(gain => gain.AdvancementOccurrenceId.ToString("D") + ":" + gain.ClassLevel,
                StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().HitDieValue,
                StringComparer.Ordinal);
        // Inconsistent HP history invalidates derived maximum HP, not otherwise
        // independent abilities, saves, or class proficiency.
        var invalidHitPointHistory =
            hitPointGains.Count != (state?.HitPointGains?.Count ?? 0);

        var currentResources = (state?.RulesInputs ?? [])
            .Where(input => input.Kind == CharacterRulesInputKinds.Resource &&
                input.IntegerValue is not null)
            .GroupBy(input => input.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.OrderByDescending(input => input.UpdatedAt)
                    .First().IntegerValue!.Value, StringComparer.Ordinal);
        var request = JsonSerializer.SerializeToElement(new
        {
            rulesetId,
            fingerprint,
            abilityScores = abilities,
            advancements = classEntries.Select(entry => new
            {
                classId = entry.RuleConceptKey,
                level = entry.Level!.Value,
                occurrenceId = entry.Id.ToString("D")
            }).ToArray(),
            hitPointGains = hitPointGains.Count == 0 ? null : hitPointGains,
            selectedOptions = nativeChoices.Ids,
            currentResources,
            skills,
            competencies
        }, JsonOptions);
        var projection = await gateway.ProjectNativeCharacterAsync(request, cancellationToken);
        if (ReadText(projection, "rulesetId") != rulesetId ||
            ReadText(projection, "fingerprint") != fingerprint ||
            ReadInt(projection, "contractVersion") != 1 ||
            projection["values"] is not JsonObject values)
            throw new RulesCoreGatewayException("Rules Core returned an invalid or mismatched native character projection.");
        if (ReadText(projection, "status") == "invalid")
            return new("unavailable", null,
                "Rules Core rejected the native character projection. The saved build was not changed.");

        var abilityValues = new List<CalculatedMechanicalValuePresentationView>();
        var savingThrows = new List<SavingThrowPresentationView>();
        if (!unmodeledSelections)
        {
            foreach (var ability in Abilities)
            {
                var score = Numeric(values, "ability." + ability + ".score");
                if (score is not int number) continue;
                var modifier = Numeric(values, "ability." + ability + ".modifier");
                abilityValues.Add(new CalculatedMechanicalValuePresentationView(
                    ability, Display(ability), number,
                    RelatedValues: modifier is int mod
                        ? [new RelatedMechanicalValuePresentationView("modifier", "Modifier", mod)]
                        : null));
            }

            foreach (var save in Saves)
            {
                if (Numeric(values, "save." + save) is not int number) continue;
                savingThrows.Add(new SavingThrowPresentationView(
                    "save." + save, Display(save) + " Save", number,
                    GoverningAbility: Abilities.Contains(save, StringComparer.Ordinal) ? save : null));
            }
        }

        // Until ancestry, equipment and other selections are modeled, even a
        // numeric proficiency bonus can be affected by an unrepresented rule.
        var proficiency = unmodeledSelections ? null : Numeric(values, "proficiency.bonus");
        var training = proficiency is int prof
            ? new[] { new CalculatedMechanicalValuePresentationView(
                "proficiency.bonus", "Proficiency Bonus", prof) }
            : null;
        var initiative = unmodeledSelections
            ? null : Numeric(values, "initiative");
        var combat = initiative is int init
            ? new[] { new CalculatedMechanicalValuePresentationView("initiative", "Initiative", init) }
            : null;
        var maximumHp = unmodeledSelections || invalidHitPointHistory
            ? null : Numeric(values, "hit-points.maximum");
        // Current HP is persisted Character Sheet state and does not require a
        // resolved Rules Core maximum in order to remain visible.
        var health = maximumHp is not null || state?.CurrentHitPoints is not null
            ? new[] { new HealthTrackPresentationView(
                "hit-points", "Hit Points", "hit-points",
                Current: state?.CurrentHitPoints, Maximum: maximumHp) }
            : null;

        var spellcasting = ProjectNativeResources(projection["resources"] as JsonArray);
        var calculatedTraining = new CompetencyCollectionPresentationView(
            trainingCatalog.Select(item => {
                var total = unmodeledSelections ? null : Numeric(values, item.Id + ".total");
                return new CompetencyPresentationView(
                    item.Id, item.Name, (object?)total ?? "—",
                    FormattedValue: total is null ? "Unresolved" : null,
                    Kind: item.Kind,
                    Ranks: storedRanks.GetValueOrDefault(item.Id),
                    Training: trained.Contains(item.Id) ? "proficient" : null,
                    PresentationCategory: item.Kind == "skill" ? "skill" : "competency",
                    RankInputKey: item.Id);
            }).ToArray());
        var mechanics = new CharacterMechanicsPresentationView(
            AbilityValues: abilityValues.Count == 0 ? null : abilityValues,
            Training: training,
            SavingThrows: savingThrows.Count == 0 ? null : savingThrows,
            CombatFundamentals: combat,
            HealthTracks: health,
            Competencies: calculatedTraining,
            SpellcastingProfiles: spellcasting);
        var partial = unmodeledSelections || nativeChoices.HasUnmappedChoices ||
            invalidHitPointHistory ||
            ReadText(projection, "status") != "resolved" ||
            (projection["issues"] is JsonArray issues && issues.Count != 0) ||
            (projection["requiredChoices"] is JsonArray choices && choices.Count != 0);
        return new(partial ? "partial" : "resolved", mechanics,
            partial
                ? "Native Rules Core calculated only supported values. Unmapped selections, missing inputs, or rule effects remain unresolved."
                : null,
            classNames);
    }

    private sealed record NativeTrainingSummary(string Id, string Name, string Kind);

    private static async Task<IReadOnlyList<NativeTrainingSummary>> LoadNativeTrainingCatalogAsync(
        INativeRulesCoreGateway gateway, string rulesetId, string fingerprint,
        CancellationToken cancellationToken)
    {
        var result = new List<NativeTrainingSummary>();
        foreach (var category in new[] { "skill", "competency" })
        {
            for (var offset = 0; offset < 500; offset += 100)
            {
                var page = await gateway.SearchNativeOptionsAsync(
                    fingerprint, category, null, 100, offset, cancellationToken);
                if (ReadText(page, "rulesetId") != rulesetId ||
                    ReadText(page, "fingerprint") != fingerprint ||
                    page["options"] is not JsonArray entries)
                    throw new RulesCoreGatewayException(
                        "Native Skill/Competency catalog does not match the active snapshot.");
                foreach (var entry in entries)
                {
                    var id = ReadText(entry, "id");
                    var name = ReadText(entry, "name");
                    if (ReadText(entry, "kind") != category ||
                        string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                        throw new RulesCoreGatewayException(
                            "Native Skill/Competency catalog contains an invalid summary.");
                    result.Add(new(id, name, category));
                }
                if (entries.Count < 100) break;
                if (offset == 400)
                    throw new RulesCoreGatewayException(
                        "Native Skill/Competency catalog exceeds the supported page limit.");
            }
        }
        if (result.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != result.Count)
            throw new RulesCoreGatewayException("Native Skill/Competency identities are duplicated.");
        return result;
    }

    /// <summary>
    /// Render only resource systems that the native class projection actually
    /// grants. A missing current value stays unconfigured; recovery is never
    /// inferred for resources whose native schedule is unsupported.
    /// </summary>
    private static IReadOnlyList<SpellcastingProfilePresentationView>? ProjectNativeResources(
        JsonArray? resources)
    {
        if (resources is null || resources.Count == 0) return null;
        var groups = new Dictionary<string, List<SpellcastingResourcePresentationView>>(
            StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            var id = ReadText(resource, "id");
            var maximum = ReadInt(resource, "maximum");
            if (string.IsNullOrWhiteSpace(id) ||
                !id.StartsWith("resource.", StringComparison.Ordinal) ||
                maximum is null or < 0)
                continue;
            var (family, label) = id switch
            {
                "resource.pact-magic" => ("pact-magic", "Pact Magic"),
                "resource.sorcery-points" => ("sorcery-points", "Sorcery Points"),
                _ when id.StartsWith("resource.spell-slot.", StringComparison.Ordinal) =>
                    ("spell-slots", "Spell Slots"),
                _ => (id, Display(id["resource.".Length..].Replace('-', ' ')))
            };
            if (!groups.TryGetValue(family, out var entries))
            {
                entries = [];
                groups.Add(family, entries);
            }
            var level = ReadInt(resource, "spellLevel");
            var resourceLabel = family == "spell-slots" && level is > 0
                ? "Level " + level + " Slots" : label;
            var current = ReadInt(resource, "current");
            var recovery = ReadText(resource, "recovery");
            entries.Add(new SpellcastingResourcePresentationView(
                id, resourceLabel, current is null ? "unconfigured" : "resolved",
                Current: current, Maximum: maximum,
                RecoveryProcedureKey: recovery is "long-rest" or "short-or-long-rest"
                    ? recovery : null));
        }
        if (groups.Count == 0) return null;
        return groups.Select(group => new SpellcastingProfilePresentationView(
            "native." + group.Key,
            group.Key switch {
                "pact-magic" => "Pact Magic",
                "sorcery-points" => "Sorcery Points",
                "spell-slots" => "Spell Slots",
                _ => group.Value[0].Label
            },
            ResourceSystem: new DisplayFieldPresentationView(
                "native-resource-system", "Resource System", group.Key),
            Resources: group.Value)).ToArray();
    }

    private static string Display(string key) =>
        char.ToUpperInvariant(key[0]) + key[1..];

    private static string? ReadText(JsonNode? node, string key)
    {
        try { return node?[key]?.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }

    private static int? ReadInt(JsonNode? node, string key)
    {
        try { return node?[key]?.GetValue<int>(); }
        catch (InvalidOperationException) { return null; }
    }

    private static int? Numeric(JsonObject values, string key)
    {
        try
        {
            var entry = values[key];
            return ReadText(entry, "status") == "resolved"
                ? entry?["value"]?.GetValue<int>()
                : null;
        }
        catch (InvalidOperationException) { return null; }
    }
}
