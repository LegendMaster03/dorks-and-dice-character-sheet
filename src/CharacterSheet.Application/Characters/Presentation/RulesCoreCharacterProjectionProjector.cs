using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

internal static class RulesCoreCharacterProjectionProjector
{
    private static readonly string[] AbilityKeys =
        ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];

    internal static CharacterMechanicsPresentationView Apply(
        CharacterMechanicsPresentationView? fallback,
        RulesCoreCharacterRulesProjectionView projection,
        CharacterStateView? state = null)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var mechanicsByKey = projection.Mechanics.ToDictionary(
            value => value.MechanicKey,
            StringComparer.Ordinal);

        var abilityValues = projection.Mechanics
            .Where(IsEffectiveAbilityScore)
            .Select(value => ProjectAbility(value, mechanicsByKey))
            .ToArray();
        var saves = projection.Mechanics
            .Where(value => string.Equals(value.Kind, "saving-throw", StringComparison.Ordinal))
            .Select(ProjectSavingThrow)
            .ToArray();
        var defenses = projection.Mechanics
            .Where(value =>
                string.Equals(value.Kind, "defense", StringComparison.Ordinal)
                || (string.Equals(value.Kind, "passive", StringComparison.Ordinal)
                    && value.MechanicKey.StartsWith("defense.", StringComparison.Ordinal)))
            .Select(value => new DefensePresentationView(
                value.MechanicKey,
                value.DisplayName,
                EffectiveValue(value),
                Unit: value.Unit,
                Breakdown: ProjectContributions(value.Contributions),
                SourceAttributions: SourceAttributionMapper.Map(value.Provenance)))
            .ToArray();
        var passiveValues = projection.Mechanics
            .Where(value =>
                string.Equals(value.Kind, "passive", StringComparison.Ordinal)
                && !value.MechanicKey.StartsWith("defense.", StringComparison.Ordinal))
            .Select(ProjectCalculated)
            .ToArray();
        var training = projection.Mechanics
            .Where(value => string.Equals(value.Kind, "proficiency", StringComparison.Ordinal))
            .Select(ProjectCalculated)
            .ToArray();
        var combat = projection.Mechanics
            .Where(value => string.Equals(value.Kind, "combat-value", StringComparison.Ordinal))
            .Select(ProjectCalculated)
            .ToArray();
        var movement = projection.Movement.Select(ProjectMovement).ToArray();
        var health = ProjectHealthTracks(projection, mechanicsByKey);
        var actions = projection.Actions.Select(value => ProjectAction(value, mechanicsByKey)).ToArray();
        var spellcasting = projection.Spellcasting
            .Select(value => ProjectSpellcasting(value, projection.Resources, mechanicsByKey))
            .ToArray();
        var features = projection.Features.Select(ProjectFeature).ToArray();
        var metadata = projection.Mechanics
            .Where(value => string.Equals(value.Kind, "character-metadata", StringComparison.Ordinal))
            .Select(ProjectCalculated)
            .ToArray();
        var choices = projection.Choices
            .Select(value => new CharacterRuleChoicePresentationView(
                value.ChoiceKey,
                value.GroupKey,
                value.DisplayName,
                value.Kind,
                value.State,
                value.Options.Select(option => new CharacterRuleChoiceOptionPresentationView(
                    option.Value,
                    option.DisplayName,
                    option.ConceptKey)).ToArray(),
                value.SelectedValue,
                value.SourceConceptKey,
                SourceAttributionMapper.Map(value.Provenance)))
            .ToArray();
        var conflicts = projection.Conflicts
            .Select(value => new CharacterProjectionConflictPresentationView(
                value.ConflictKey,
                value.Kind,
                value.Message,
                value.RelatedMechanicKeys,
                value.RelatedConceptKeys))
            .ToArray();
        var effectiveCompetencyFallback = fallback?.Competencies
            ?? ProjectEffectiveCompetencyMetadata(
                projection.Competencies,
                projection.CompetencyRelationships);
        var competencies = ProjectCompetencies(
            effectiveCompetencyFallback,
            projection.Mechanics,
            state);
        var inventory = ProjectInventory(state, projection.Equipment);

        return (fallback ?? new CharacterMechanicsPresentationView()) with
        {
            AbilityValues = abilityValues.Length == 0 ? null : abilityValues,
            PassiveValues = passiveValues.Length == 0 ? null : passiveValues,
            Training = training.Length == 0 ? null : training,
            SavingThrows = saves.Length == 0 ? null : saves,
            Defenses = defenses.Length == 0
                ? null
                : new DefenseGroupPresentationView(
                    defenses,
                    defenses.Any(value => value.Key == "defense.ac.total")
                        ? "defense.ac.total"
                        : null),
            CombatFundamentals = combat.Length == 0 ? null : combat,
            HealthTracks = health.Length == 0 ? null : health,
            Competencies = competencies,
            Inventory = inventory,
            Movement = movement.Length == 0 ? null : movement,
            Actions = actions.Length == 0 ? null : actions,
            SpellcastingProfiles = spellcasting.Length == 0 ? null : spellcasting,
            Features = features.Length == 0 ? null : features,
            CharacterMetadata = metadata.Length == 0 ? null : metadata,
            RuleChoices = choices.Length == 0 ? null : choices,
            ProjectionConflicts = conflicts.Length == 0 ? null : conflicts
        };
    }

    private static bool IsEffectiveAbilityScore(RulesCoreCharacterResolvedMechanicView value)
    {
        if (!string.Equals(value.Kind, "ability-score", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = value.MechanicKey.Split('.');
        return segments.Length == 3
            && string.Equals(segments[0], "ability", StringComparison.Ordinal)
            && string.Equals(segments[2], "score", StringComparison.Ordinal);
    }

    private static CalculatedMechanicalValuePresentationView ProjectAbility(
        RulesCoreCharacterResolvedMechanicView value,
        IReadOnlyDictionary<string, RulesCoreCharacterResolvedMechanicView> byKey)
    {
        var prefix = value.MechanicKey[..^".score".Length];
        var related = new List<RelatedMechanicalValuePresentationView>();
        AddRelated("modifier", "Modifier", prefix + ".modifier");
        AddRelated("ordinary-score", "Ordinary Score", prefix + ".ordinary-score");
        AddRelated("ordinary-modifier", "Ordinary Modifier", prefix + ".ordinary-modifier");
        AddRelated("temporary-score", "Temporary Score", prefix + ".temporary-score");
        AddRelated("temporary-modifier", "Temporary Modifier", prefix + ".temporary-modifier");

        var abilityKey = value.MechanicKey["ability.".Length..^".score".Length];
        return ProjectCalculated(value) with
        {
            Key = abilityKey,
            RelatedValues = related.Count == 0 ? null : related
        };

        void AddRelated(string key, string label, string mechanicKey)
        {
            if (!byKey.TryGetValue(mechanicKey, out var relatedMechanic))
            {
                return;
            }

            related.Add(new RelatedMechanicalValuePresentationView(
                key,
                label,
                EffectiveValue(relatedMechanic)));
        }
    }

    private static SavingThrowPresentationView ProjectSavingThrow(
        RulesCoreCharacterResolvedMechanicView value)
    {
        var ability = value.MechanicKey.StartsWith("save.", StringComparison.Ordinal)
            ? value.MechanicKey["save.".Length..]
            : null;
        if (ability is not null && !AbilityKeys.Contains(ability, StringComparer.Ordinal))
        {
            ability = null;
        }

        return new SavingThrowPresentationView(
            value.MechanicKey,
            value.DisplayName,
            EffectiveValue(value),
            Unit: value.Unit,
            GoverningAbility: ability,
            Breakdown: ProjectContributions(value.Contributions),
            SourceAttributions: SourceAttributionMapper.Map(value.Provenance));
    }

    private static CalculatedMechanicalValuePresentationView ProjectCalculated(
        RulesCoreCharacterResolvedMechanicView value) =>
        new(
            value.MechanicKey,
            value.DisplayName,
            EffectiveValue(value),
            Unit: value.Unit,
            Breakdown: ProjectContributions(value.Contributions),
            SourceAttributions: SourceAttributionMapper.Map(value.Provenance));

    private static CalculatedMechanicalValuePresentationView ProjectMovement(
        RulesCoreCharacterMovementModeView value) =>
        new(
            value.MovementKey,
            value.DisplayName,
            value.Value is int numeric ? numeric : CharacterMechanicsProjector.Unconfigured,
            Unit: value.Unit,
            Breakdown: ProjectContributions(value.Contributions),
            SourceAttributions: SourceAttributionMapper.Map(value.Provenance));

    private static HealthTrackPresentationView[] ProjectHealthTracks(
        RulesCoreCharacterRulesProjectionView projection,
        IReadOnlyDictionary<string, RulesCoreCharacterResolvedMechanicView> mechanicsByKey)
    {
        var values = new List<HealthTrackPresentationView>();
        if (mechanicsByKey.TryGetValue("health.maximum-hp", out var maximumHp))
        {
            values.Add(new HealthTrackPresentationView(
                maximumHp.MechanicKey,
                "Hit Points",
                "hit-points",
                Maximum: maximumHp.NumericValue is int maximum
                    ? maximum
                    : CharacterMechanicsProjector.Unconfigured,
                Detail: maximumHp.State,
                SourceAttributions: SourceAttributionMapper.Map(maximumHp.Provenance)));
        }

        foreach (var resource in projection.Resources.Where(IsHealthResource))
        {
            values.Add(new HealthTrackPresentationView(
                resource.ResourceKey,
                resource.DisplayName,
                ResolveHealthRole(resource.ResourceKey),
                Current: resource.CurrentValue,
                Maximum: resource.MaximumValue,
                Detail: resource.RecoveryProcedureKey,
                SourceAttributions: SourceAttributionMapper.Map(resource.Provenance)));
        }

        return values.ToArray();
    }

    private static bool IsHealthResource(RulesCoreCharacterResourceView value) =>
        value.ResourceKey.StartsWith("resource.hit-die.", StringComparison.Ordinal)
        || value.ResourceKey is "resource.hit-dice" or "resource.nonlethal-damage"
        || value.ResourceKey.StartsWith("resource.temporary-hit-points", StringComparison.Ordinal);

    private static string ResolveHealthRole(string key) =>
        key.StartsWith("resource.hit-die.", StringComparison.Ordinal) || key == "resource.hit-dice"
            ? "hit-dice"
            : key == "resource.nonlethal-damage"
                ? "nonlethal-damage"
                : key.StartsWith("resource.temporary-hit-points", StringComparison.Ordinal)
                    ? "temporary-hit-points"
                    : "resource";

    private static ActionAttackPresentationView ProjectAction(
        RulesCoreCharacterActionView value,
        IReadOnlyDictionary<string, RulesCoreCharacterResolvedMechanicView> mechanicsByKey)
    {
        CalculatedMechanicalValuePresentationView? attack = null;
        if (value.AttackMechanicKey is not null
            && mechanicsByKey.TryGetValue(value.AttackMechanicKey, out var mechanic))
        {
            attack = ProjectCalculated(mechanic);
        }

        var notes = new[]
        {
            value.SpellLevel is int level ? $"Spell level {level}" : null,
            value.SpellSchool is null ? null : $"School: {value.SpellSchool}",
            value.CastingTime is null ? null : $"Casting time: {value.CastingTime}",
            value.Duration is null ? null : $"Duration: {value.Duration}",
            value.Concentration == true ? "Concentration" : null,
            value.Ritual == true ? "Ritual" : null
        }.Where(note => note is not null).Cast<string>().ToArray();

        return new ActionAttackPresentationView(
            value.ActionKey,
            value.DisplayName,
            value.ActionType,
            attack,
            value.DamageExpression,
            value.DamageType,
            value.Range,
            value.Reach,
            value.Target,
            notes.Length == 0 ? null : notes,
            SourceAttributionMapper.Map(value.Provenance));
    }

    private static SpellcastingProfilePresentationView ProjectSpellcasting(
        RulesCoreCharacterSpellcastingView value,
        IReadOnlyList<RulesCoreCharacterResourceView> resources,
        IReadOnlyDictionary<string, RulesCoreCharacterResolvedMechanicView> mechanicsByKey)
    {
        CalculatedMechanicalValuePresentationView? saveDc = null;
        if (value.SaveDcMechanicKey is not null
            && mechanicsByKey.TryGetValue(value.SaveDcMechanicKey, out var saveDcMechanic))
        {
            saveDc = ProjectCalculated(saveDcMechanic);
        }

        CalculatedMechanicalValuePresentationView? spellAttack = null;
        if (value.SpellAttackMechanicKey is not null
            && mechanicsByKey.TryGetValue(value.SpellAttackMechanicKey, out var spellAttackMechanic))
        {
            spellAttack = ProjectCalculated(spellAttackMechanic);
        }

        var sourceConceptKey = value.SpellcastingKey.StartsWith("spellcasting.", StringComparison.Ordinal)
            ? value.SpellcastingKey["spellcasting.".Length..]
            : value.SpellcastingKey;
        var spellResources = resources
            .Where(resource => ResourceBelongsToSpellcasting(
                resource.ResourceKey,
                value.ResourceSystemKey,
                sourceConceptKey))
            .Select(resource => new SpellcastingResourcePresentationView(
                resource.ResourceKey,
                resource.DisplayName,
                resource.State,
                resource.CurrentValue,
                resource.MaximumValue,
                resource.RecoveryProcedureKey,
                SourceAttributionMapper.Map(resource.Provenance)))
            .ToArray();

        var metadata = new List<DisplayFieldPresentationView>();
        if (value.RequiredChoices.Count > 0)
        {
            metadata.Add(new DisplayFieldPresentationView(
                "required-choices",
                "Required Choices",
                string.Join(", ", value.RequiredChoices)));
        }
        if (value.SpellListConceptKeys.Count > 0)
        {
            metadata.Add(new DisplayFieldPresentationView(
                "spell-list",
                "Spell List",
                string.Join(", ", value.SpellListConceptKeys)));
        }

        return new SpellcastingProfilePresentationView(
            value.SpellcastingKey,
            value.DisplayName,
            sourceConceptKey,
            value.CastingAbilityKey,
            saveDc,
            spellAttack,
            value.ResourceSystemKey is null
                ? null
                : new DisplayFieldPresentationView(
                    "resource-system",
                    "Resource System",
                    value.ResourceSystemKey),
            spellResources.Length == 0 ? null : spellResources,
            metadata.Count == 0 ? null : metadata,
            SourceAttributionMapper.Map(value.Provenance));
    }

    private static bool ResourceBelongsToSpellcasting(
        string resourceKey,
        string? resourceSystemKey,
        string sourceConceptKey)
    {
        return resourceSystemKey switch
        {
            "spell-points" => resourceKey == "resource.spell-points",
            "pact-magic" => resourceKey.StartsWith("resource.pact-slot.", StringComparison.Ordinal)
                && resourceKey.Contains(sourceConceptKey, StringComparison.Ordinal),
            "spell-slots" => resourceKey.StartsWith("resource.spell-slot.", StringComparison.Ordinal),
            null => false,
            _ => resourceKey.Contains(resourceSystemKey, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static CharacterFeaturePresentationView ProjectFeature(
        RulesCoreCharacterFeatureView value)
    {
        var effects = value.Effects
            .Select(effect => new DisplayFieldPresentationView(
                effect.EffectKey,
                effect.TargetKey,
                effect.NumericValue?.ToString()
                    ?? effect.TextValue
                    ?? effect.Operation))
            .ToArray();

        return new CharacterFeaturePresentationView(
            value.FeatureKey,
            value.DisplayName,
            value.Kind,
            value.State,
            value.SourceConceptKey,
            value.GrantingSourceKind,
            value.AcquisitionLevel,
            effects.Length == 0 ? null : effects,
            SourceAttributionMapper.Map(value.FeatureProvenance ?? value.Provenance));
    }

    private static InventoryMechanicsPresentationView? ProjectInventory(
        CharacterStateView? state,
        IReadOnlyList<RulesCoreCharacterEquipmentDefinitionView> equipment)
    {
        if (state is null || state.InventoryItemOccurrences.Count == 0)
        {
            return null;
        }

        var byConcept = equipment
            .GroupBy(value => value.ConceptKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var occurrences = state.InventoryItemOccurrences
            .Select(occurrence =>
            {
                if (string.IsNullOrWhiteSpace(occurrence.RuleConceptKey)
                    || !byConcept.TryGetValue(occurrence.RuleConceptKey, out var definition))
                {
                    return null;
                }

                var facts = new List<DisplayFieldPresentationView>();
                AddFact("item-type", "Item Type", definition.ItemType);
                AddFact("equipment-category", "Equipment Category", definition.EquipmentCategory);
                AddFact("armor-role", "Armor Role", definition.ArmorRole);
                if (definition.Weight is decimal weight)
                {
                    AddFact(
                        "weight",
                        "Weight",
                        string.IsNullOrWhiteSpace(definition.WeightUnit)
                            ? weight.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : $"{weight.ToString(System.Globalization.CultureInfo.InvariantCulture)} {definition.WeightUnit}");
                }
                AddFact("ammunition-type", "Ammunition", definition.AmmunitionType);
                AddFact("capacity", "Capacity", definition.Capacity);
                if (definition.RequiresAttunement is bool requiresAttunement)
                {
                    AddFact(
                        "requires-attunement",
                        "Requires Attunement",
                        requiresAttunement ? "Yes" : "No");
                }
                AddFact(
                    "attunement-requirement",
                    "Attunement Requirement",
                    definition.AttunementRequirement);
                if (definition.PropertyKeys.Count > 0)
                {
                    AddFact(
                        "properties",
                        "Properties",
                        string.Join(", ", definition.PropertyKeys));
                }

                return new ItemOccurrenceMechanicsPresentationView(
                    occurrence.Id,
                    Facts: facts.Count == 0 ? null : facts,
                    SourceAttributions: SourceAttributionMapper.Map(definition.Provenance));

                void AddFact(string key, string label, string? value)
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        facts.Add(new DisplayFieldPresentationView(key, label, value));
                    }
                }
            })
            .Where(value => value is not null)
            .Cast<ItemOccurrenceMechanicsPresentationView>()
            .ToArray();

        return occurrences.Length == 0
            ? null
            : new InventoryMechanicsPresentationView(occurrences);
    }

    private static CompetencyCollectionPresentationView? ProjectEffectiveCompetencyMetadata(
        IReadOnlyList<RulesCoreUniversalCompetencyView>? competencies,
        IReadOnlyList<RulesCoreMechanicRelationshipView>? competencyRelationships)
    {
        if (competencies is null || competencies.Count == 0)
        {
            return null;
        }

        var entries = competencies
            .Select(value =>
            {
                var mechanics = value.Mechanics;
                var governingAbility = mechanics?.GoverningAbility is null
                    ? null
                    : ResolveEffectiveGoverningAbility(mechanics.GoverningAbility);
                var rankInputKey = ResolveUniversalRankInputKey(value);

                return new CompetencyPresentationView(
                    value.SemanticKey,
                    value.DisplayName,
                    CharacterMechanicsProjector.Unconfigured,
                    Kind: value.IsFamily
                        ? "family"
                        : mechanics?.CompetencyKind ?? "competency",
                    GoverningAbility: governingAbility,
                    TrainedOnly: mechanics?.TrainedOnly,
                    ArmorCheckPenalty: mechanics?.ArmorCheckPenaltyApplies is bool applies
                        ? new ArmorCheckPenaltyPresentationView(applies)
                        : null,
                    Family: value.FamilyName,
                    Specialty: value.IsFamily || value.FamilyName is null
                        ? null
                        : value.DisplayName,
                    SupportsRanks:
                        !value.IsFamily
                        && (mechanics?.SupportsRanks == true
                            || value.Facets.Any(facet => facet.SupportsRanks)),
                    SupportsClassSkillState:
                        !value.IsFamily
                        && (mechanics?.SupportsClassSkillState == true
                            || value.Facets.Any(facet => facet.SupportsClassSkillState)),
                    SupportsTrainingState:
                        !value.IsFamily
                        && (mechanics?.SupportsTrainingState == true
                            || value.Facets.Any(facet => facet.SupportsTrainingState)
                            || !string.IsNullOrWhiteSpace(value.TrainingStateKey)),
                    SourceAttributions: SourceAttributionMapper.Map(value.SourceAttributions),
                    IdentityKey: value.IdentityKey,
                    IdentityName: value.DisplayName,
                    SharedTrainingKey: value.TrainingStateKey,
                    IsFamily: value.IsFamily,
                    Facets: CompetencyProjector.ProjectFacets(value.Facets),
                    RelatedCompetencies:
                        CompetencyProjector.ProjectRelatedCompetencies(
                            value.RelatedCompetencies),
                    ChildCompetencyKeys: value.ChildCompetencyKeys.Count == 0
                        ? null
                        : value.ChildCompetencyKeys,
                    MechanicKeys: value.MechanicKeys.Count == 0
                        ? null
                        : value.MechanicKeys,
                    CompatibilityMechanicKeys:
                        value.CompatibilityMechanicKeys.Count == 0
                            ? null
                            : value.CompatibilityMechanicKeys,
                    PresentationCategory: value.PresentationCategory,
                    RankInputKey: rankInputKey);
            })
            .ToArray();

        var relationships = CompetencyProjector.ProjectUniversalRelationships(
            competencies,
            competencyRelationships);
        return new CompetencyCollectionPresentationView(entries, relationships);
    }

    private static string? ResolveUniversalRankInputKey(
        RulesCoreUniversalCompetencyView competency)
    {
        var rankMechanicKeys = competency.Facets
            .Where(facet => facet.SupportsRanks)
            .SelectMany(facet => facet.MechanicKeys ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (rankMechanicKeys.Length == 0
            && competency.Mechanics?.SupportsRanks == true)
        {
            rankMechanicKeys = competency.MechanicKeys
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var conceptKeys = rankMechanicKeys
            .Select(ConceptKeyFromCompetencyMechanicKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return conceptKeys.Length == 1 ? conceptKeys[0] : null;
    }

    private static string? ResolveEffectiveGoverningAbility(
        RulesCoreUniversalGoverningAbilityView governingAbility)
    {
        if (string.Equals(governingAbility.ResolutionKind, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(governingAbility.FixedAbilityKey))
        {
            return governingAbility.FixedAbilityKey;
        }

        var values = governingAbility.AbilityKeys
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return values.Length == 0 ? null : string.Join(" / ", values);
    }

    private static CompetencyCollectionPresentationView? ProjectCompetencies(
        CompetencyCollectionPresentationView? fallback,
        IReadOnlyList<RulesCoreCharacterResolvedMechanicView> projectedMechanics,
        CharacterStateView? state)
    {
        if (fallback is null)
        {
            return null;
        }

        var rankByConcept = (state?.RulesInputs ?? [])
            .Where(value => value.Kind == CharacterRulesInputKinds.CompetencyRank
                && value.IntegerValue is not null)
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().IntegerValue!.Value,
                StringComparer.Ordinal);

        var projectedByMechanicKey = projectedMechanics
            .Where(value => string.Equals(value.Kind, "competency", StringComparison.Ordinal))
            .ToDictionary(value => value.MechanicKey, StringComparer.Ordinal);

        if (projectedByMechanicKey.Count == 0)
        {
            return fallback;
        }

        return fallback with
        {
            Entries = fallback.Entries.Select(entry =>
            {
                var mechanicKeys = (entry.MechanicKeys ?? [])
                    .Concat(entry.CompatibilityMechanicKeys ?? [])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (mechanicKeys.Length == 0 && !entry.Key.StartsWith("competency.", StringComparison.Ordinal))
                {
                    mechanicKeys = [$"competency.{entry.Key}"];
                }

                var projectedCandidates = mechanicKeys
                    .Select(key => projectedByMechanicKey.GetValueOrDefault(key))
                    .Where(value => value is not null)
                    .Cast<RulesCoreCharacterResolvedMechanicView>()
                    .ToArray();
                RulesCoreCharacterResolvedMechanicView? projected = null;
                if (projectedCandidates.Length > 0)
                {
                    var bestPriority = projectedCandidates.Min(value => ProjectionPriority(value.State));
                    var best = projectedCandidates
                        .Where(value => ProjectionPriority(value.State) == bestPriority)
                        .ToArray();
                    if (best.Length == 1)
                    {
                        projected = best[0];
                    }
                }

                var rankInputKeys = new List<string>();
                if (!string.IsNullOrWhiteSpace(entry.RankInputKey))
                {
                    rankInputKeys.Add(entry.RankInputKey);
                }
                foreach (var key in mechanicKeys)
                {
                    var conceptKey = ConceptKeyFromCompetencyMechanicKey(key);
                    if (conceptKey is not null)
                    {
                        rankInputKeys.Add(conceptKey);
                    }
                }
                if (!entry.Key.StartsWith("competency.", StringComparison.Ordinal))
                {
                    rankInputKeys.Add(entry.Key);
                }

                int? ranks = null;
                foreach (var key in rankInputKeys.Distinct(StringComparer.Ordinal))
                {
                    if (rankByConcept.TryGetValue(key, out var value))
                    {
                        ranks = value;
                        break;
                    }
                }

                if (projected is null && ranks is null)
                {
                    return entry;
                }

                return entry with
                {
                    EffectiveValue = projected is null
                        ? entry.EffectiveValue
                        : EffectiveValue(projected),
                    Ranks = ranks ?? entry.Ranks,
                    SourceAttributions = projected is null
                        ? entry.SourceAttributions
                        : SourceAttributionMapper.Map(projected.Provenance)
                            ?? entry.SourceAttributions,
                    Breakdown = projected is null
                        ? entry.Breakdown
                        : ProjectContributions(projected.Contributions)
                };
            }).ToArray()
        };
    }

    private static int ProjectionPriority(string state) =>
        string.Equals(state, "resolved", StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(state, "applicable-unresolved", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 2;

    private static string? ConceptKeyFromCompetencyMechanicKey(string mechanicKey)
    {
        const string prefix = "competency.";
        return mechanicKey.StartsWith(prefix, StringComparison.Ordinal)
            && mechanicKey.Length > prefix.Length
                ? mechanicKey[prefix.Length..]
                : null;
    }

    private static IReadOnlyList<MechanicalContributionPresentationView>? ProjectContributions(
        IReadOnlyList<RulesCoreCharacterMechanicContributionView> contributions)
    {
        var values = contributions
            .Select(value => new MechanicalContributionPresentationView(
                value.ContributionKey,
                value.Label,
                value.NumericValue is int numeric
                    ? numeric
                    : value.TextValue ?? value.Operation,
                SourceAttributions: SourceAttributionMapper.Map(value.Provenance)))
            .ToArray();
        return values.Length == 0 ? null : values;
    }

    private static object EffectiveValue(RulesCoreCharacterResolvedMechanicView value) =>
        value.NumericValue is int numeric
            ? numeric
            : value.TextValue ?? CharacterMechanicsProjector.Unconfigured;
}
