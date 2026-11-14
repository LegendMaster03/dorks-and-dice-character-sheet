using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Persistence;

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
        CancellationToken cancellationToken,
        bool hasCommittedOrigin = false,
        VerifiedOriginProjectionContext? originProjection = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(build);

        // The caller may only use these native scalars alongside authenticated
        // committed Origin state. Exact catalog identity is checked again
        // below when the character projector independently loads its snapshot.
        var originIntrinsics = hasCommittedOrigin ? originProjection?.Intrinsics : null;
        var lowLightVision = hasCommittedOrigin ? originProjection?.LowLightVision : null;

        // Even with no Class or abilities, an authorized native Origin's
        // walking speed must go through the Core kernel: active conditions can
        // suppress or change effective movement. Never display a raw base
        // speed in the effective Movement collection as a shortcut.
        if (build.ProgressionEntries.Count == 0 && build.BaseAbilityScoreInputs.Count == 0 &&
            build.FoundationalSelections.Count == 0 &&
            !(hasCommittedOrigin && originIntrinsics is not null))
            return hasCommittedOrigin
                ? new("partial", null,
                    "An initial Origin is committed, but its native Species/Ancestry characteristics and other trait effects remain unverified or unprojected.")
                : new("unconfigured", null,
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

        // The native catalog can change between the authorized Origin review
        // and this character projection. Keep independently supported Class
        // values, but never combine a stale Origin base speed with a different
        // native condition or Character projection catalog.
        var originCatalogChanged = originIntrinsics is not null &&
            (originProjection!.RulesetId != rulesetId ||
             originProjection.Fingerprint != fingerprint);
        if (originCatalogChanged)
        {
            originIntrinsics = null;
            lowLightVision = null;
        }

        // A Sheet deploy may precede Core's manual release. Only expose native
        // condition numbers when Core advertises per-value invalidation; older
        // Core versions keep the previous conservative presentation behavior.
        var isolatesConditionValues = metadata["capabilities"]?["conditionValueIsolation"]
            is JsonValue isolation && isolation.TryGetValue<bool>(out var supported) && supported;
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
        // No Origin traits, movement, size, or training may be inferred from
        // stored identity. An authorized Origin remains unprojected until
        // the native character kernel accepts server-verified Origin input.
        var unmodeledSelections = hasCommittedOrigin ||
            build.FoundationalSelections.Count != 0 ||
            state?.Conditions.Any(condition => !isolatesConditionValues || condition.RuleConceptKey is null ||
                !condition.RuleConceptKey.StartsWith("condition.", StringComparison.Ordinal)) == true ||
            state?.InventoryItemOccurrences.Any(item => item.IsEquipped) == true ||
            state?.RulesInputs?.Any(input => input.Kind is not (
                CharacterRulesInputKinds.Resource or
                CharacterRulesInputKinds.CompetencyRank or
                CharacterRulesInputKinds.Training or
                CharacterRulesInputKinds.KnownSpell or
                CharacterRulesInputKinds.Choice) &&
                !(input.Kind == CharacterRulesInputKinds.BooleanFact &&
                    input.Key == CharacterStartingEquipmentReceipt.Key)) == true ||
            nativeChoices.HasUnmappedChoices ||
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
            conditionIds = (state?.Conditions ?? []).Select(condition => condition.RuleConceptKey)
                .Where(id => id is not null && id.StartsWith("condition.", StringComparison.Ordinal)).Distinct().ToArray(),
            selectedOptions = nativeChoices.Ids,
            currentResources,
            skills,
            competencies,
            // Supplied only after server-owned Origin and current Rules Core
            // verification; this is not an untrusted Species-ID shortcut.
            baseWalkSpeed = originIntrinsics?.BaseWalkingSpeedFeet
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

        // Core resolves proficiency from the accepted native Class progression.
        // Unrepresented equipment, conditions, ancestry, or unrelated training
        // do not erase that independently verified class-level value.
        var proficiency = Numeric(values, "proficiency.bonus");
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
        var presentedTraining = ReadTrainingPresentation(projection, trainingCatalog);
        var calculatedTraining = new CompetencyCollectionPresentationView(
            presentedTraining.Select(item => {
                // Unknown training identities can influence training totals, not
                // the independently resolved class proficiency or base abilities.
                var total = unmodeledSelections || unknownTraining
                    ? null : Numeric(values, item.Id + ".total");
                return new CompetencyPresentationView(
                    item.Id, item.Name, (object?)total ?? "—",
                    FormattedValue: total is null ? "Unresolved" : null,
                    Kind: item.Kind,
                    Ranks: item.IsFamily ? null : storedRanks.GetValueOrDefault(item.Id),
                    GoverningAbility: item.GoverningAbility,
                    Training: trained.Contains(item.Id) ? "proficient" : null,
                    Family: item.IsFamily ? item.Id : item.FamilyId,
                    SupportsRanks: item.SupportsRanks,
                    IsFamily: item.IsFamily,
                    ChildCompetencyKeys: item.IsFamily
                        ? presentedTraining.Where(child => child.FamilyId == item.Id)
                            .Select(child => child.Id).ToArray()
                        : item.ComponentIds,
                    PresentationCategory: item.Kind is "skill" or "skill-family" ? "skill" : "competency",
                    RankInputKey: item.SupportsRanks == false ? null : item.Id);
            }).ToArray(),
            presentedTraining.Where(item => item.ComponentIds?.Count > 0)
                .Select(item => new CompetencyRelationshipPresentationView(
                    item.Id, item.ComponentIds!, Composition: item.Composition)).ToArray());
        var resolvedWalkingSpeed = originIntrinsics is null
            ? null : Numeric(values, "movement.walk");
        var mechanics = new CharacterMechanicsPresentationView(
            AbilityValues: abilityValues.Count == 0 ? null : abilityValues,
            Training: training,
            SavingThrows: savingThrows.Count == 0 ? null : savingThrows,
            CombatFundamentals: combat,
            HealthTracks: health,
            Competencies: calculatedTraining,
            SpellcastingProfiles: spellcasting,
            Senses: lowLightVision is null
                ? null : [new CalculatedMechanicalValuePresentationView(
                    "sense.low-light-vision", "Low-Light Vision",
                    "Dim Light perceived as Bright Light; Darkness unchanged",
                    SourceAttributions: [new SourceAttributionPresentationView(
                        "origin-grant:" + lowLightVision.GrantId,
                        "Native Species/Ancestry trait",
                        Detail: lowLightVision.SourceId +
                            lowLightVision.OccurrencePath,
                        PresentationRequired: true)])],
            Movement: resolvedWalkingSpeed is int walk
                ? [new CalculatedMechanicalValuePresentationView(
                    "movement.walk", "Walking Speed", walk, Unit: "ft")]
                : null,
            CharacterMetadata: originIntrinsics is null
                ? null : IntrinsicMetadata(originIntrinsics));
        var partial = unmodeledSelections || unknownTraining ||
            invalidHitPointHistory ||
            ReadText(projection, "status") != "resolved" ||
            (projection["issues"] is JsonArray issues && issues.Count != 0) ||
            (projection["requiredChoices"] is JsonArray choices && choices.Count != 0);
        return new(partial ? "partial" : "resolved", mechanics,
            partial
                ? hasCommittedOrigin
                    ? originCatalogChanged
                        ? "Native rules changed after Origin verification. Origin-dependent values are withheld pending revalidation; independently calculated Class values remain available."
                        : originIntrinsics is null
                            ? "The committed Origin's Species/Ancestry characteristics and trait effects have not been verified for native character projection."
                            : "Verified base Species/Ancestry characteristics, supported passive senses and any resolved native movement are shown. Other Origin traits and dependent mechanics remain unresolved."
                    : "Native Rules Core calculated only supported values. Unmapped selections, missing inputs, or rule effects remain unresolved."
                : null,
            classNames);
    }

    private static IReadOnlyList<CalculatedMechanicalValuePresentationView>
        IntrinsicMetadata(VerifiedOriginIntrinsics intrinsics) =>
        [
            new("origin.base-creature-type", "Base Creature Type", intrinsics.CreatureType),
            new("origin.base-size", "Base Size", intrinsics.Size),
            new("origin.base-walk-speed", "Base Walking Speed",
                intrinsics.BaseWalkingSpeedFeet, Unit: "ft")
        ];

    private sealed record NativeTrainingSummary(
        string Id, string Name, string Kind, string? GoverningAbility = null,
        string? FamilyId = null, IReadOnlyList<string>? ComponentIds = null,
        string? Composition = null, bool IsFamily = false, bool? SupportsRanks = null);

    private static IReadOnlyList<NativeTrainingSummary> ReadTrainingPresentation(
        JsonNode projection, IReadOnlyList<NativeTrainingSummary> identities)
    {
        // Older native responses remain readable during coordinated deployment.
        // Structure is supplied by Core, never reconstructed from display names.
        if (projection["training"] is null) return identities;
        if (projection["training"] is not JsonArray entries)
            throw new RulesCoreGatewayException("Native training presentation is invalid.");
        NativeTrainingSummary[] items;
        try
        {
            items = entries.Select(entry => entry?.Deserialize<NativeTrainingSummary>(JsonOptions)
                ?? throw new JsonException("Missing training entry.")).ToArray();
        }
        catch (JsonException)
        {
            throw new RulesCoreGatewayException("Native training presentation is invalid.");
        }
        var byId = items.Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .DistinctBy(item => item.Id).ToDictionary(item => item.Id, StringComparer.Ordinal);
        var known = identities.ToDictionary(item => item.Id, StringComparer.Ordinal);
        if (byId.Count != items.Length ||
            identities.Any(item => !byId.TryGetValue(item.Id, out var presented) ||
                presented.Kind != item.Kind || presented.Name != item.Name) ||
            items.Any(item => string.IsNullOrWhiteSpace(item.Name) ||
                (item.IsFamily ? item.Kind != "skill-family" || !item.Id.StartsWith("skill.", StringComparison.Ordinal)
                    : !known.ContainsKey(item.Id)) ||
                (item.FamilyId is not null && (!byId.TryGetValue(item.FamilyId, out var family) || !family.IsFamily)) ||
                item.ComponentIds?.Any(id => id == item.Id || !byId.TryGetValue(id, out var child) ||
                    child.Kind != item.Kind || child.IsFamily) == true))
            throw new RulesCoreGatewayException("Native training presentation does not match its catalog.");
        return items;
    }

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
