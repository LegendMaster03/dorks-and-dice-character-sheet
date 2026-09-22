namespace CharacterSheet.Domain.Characters;

public enum CharacterBuilderStatus
{
    BuildInProgress = 1
}

/// <summary>
/// Root of Character Sheet-owned state. CharacterId is exactly the canonical identifier issued by
/// the Dorks & Dice Site; Character Sheet has no second character ID.
/// </summary>
public sealed class CharacterSheetRoot
{
    public const int CurrentSchemaVersion = 1;

    private CharacterSheetRoot()
    {
    }

    public CharacterSheetRoot(Guid characterId, DateTimeOffset createdAt)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        CharacterId = characterId;
        SchemaVersion = CurrentSchemaVersion;
        BuilderStatus = CharacterBuilderStatus.BuildInProgress;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid CharacterId { get; private set; }

    public int SchemaVersion { get; private set; }

    public CharacterBuilderStatus BuilderStatus { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public int? CurrentHitPoints { get; private set; }

    public int DeathSaveSuccesses { get; private set; }

    public int DeathSaveFailures { get; private set; }

    public ICollection<CharacterFoundationalRuleSelection> FoundationalSelections { get; private set; } =
        new List<CharacterFoundationalRuleSelection>();

    public ICollection<CharacterAdvancementEntry> AdvancementEntries { get; private set; } =
        new List<CharacterAdvancementEntry>();

    public ICollection<CharacterBaseAbilityScoreInput> BaseAbilityScoreInputs { get; private set; } =
        new List<CharacterBaseAbilityScoreInput>();

    public ICollection<CharacterInventoryItemOccurrence> InventoryItemOccurrences { get; private set; } =
        new List<CharacterInventoryItemOccurrence>();

    public ICollection<CharacterNote> Notes { get; private set; } =
        new List<CharacterNote>();

    public ICollection<CharacterConditionOccurrence> Conditions { get; private set; } =
        new List<CharacterConditionOccurrence>();

    public ICollection<CharacterRulesInputState> RulesInputs { get; private set; } =
        new List<CharacterRulesInputState>();

    public ICollection<CharacterHitPointGainState> HitPointGains { get; private set; } =
        new List<CharacterHitPointGainState>();

    public CharacterRulesInputState SetRulesInput(
        CharacterRulesInputKind kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset changedAt)
    {
        var normalizedKey = CharacterRulesInputKey.Normalize(key);
        var input = RulesInputs.SingleOrDefault(value =>
            value.Kind == kind && value.Key == normalizedKey);
        if (input is null)
        {
            input = new CharacterRulesInputState(
                Guid.NewGuid(),
                CharacterId,
                kind,
                normalizedKey,
                integerValue,
                booleanValue,
                textValue,
                changedAt);
            RulesInputs.Add(input);
        }
        else
        {
            input.Replace(integerValue, booleanValue, textValue, changedAt);
        }

        Touch(changedAt);
        return input;
    }

    public bool RemoveRulesInput(
        CharacterRulesInputKind kind,
        string key,
        DateTimeOffset changedAt)
    {
        var normalizedKey = CharacterRulesInputKey.Normalize(key);
        var input = RulesInputs.SingleOrDefault(value =>
            value.Kind == kind && value.Key == normalizedKey);
        if (input is null)
        {
            return false;
        }

        RulesInputs.Remove(input);
        Touch(changedAt);
        return true;
    }

    public CharacterHitPointGainState SetHitPointGain(
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        DateTimeOffset changedAt)
    {
        var advancement = AdvancementEntries.SingleOrDefault(value =>
            value.Id == advancementOccurrenceId)
            ?? throw new KeyNotFoundException("Character advancement entry was not found.");
        if (advancement.Kind is not CharacterAdvancementKind.Class
            and not CharacterAdvancementKind.PrestigeClass)
        {
            throw new InvalidOperationException(
                "Hit point gains must belong to a Class or Prestige Class advancement occurrence.");
        }
        if (classLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classLevel), "Class level must be positive.");
        }
        if (advancement.Level is not int advancementLevel || classLevel > advancementLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(classLevel),
                "Hit point gain level can not exceed the advancement occurrence level.");
        }

        var gain = HitPointGains.SingleOrDefault(value =>
            value.AdvancementOccurrenceId == advancementOccurrenceId
            && value.ClassLevel == classLevel);
        if (gain is null)
        {
            gain = new CharacterHitPointGainState(
                Guid.NewGuid(),
                CharacterId,
                advancementOccurrenceId,
                classLevel,
                hitDieValue,
                changedAt);
            HitPointGains.Add(gain);
        }
        else
        {
            gain.ReplaceValue(hitDieValue, changedAt);
        }

        Touch(changedAt);
        return gain;
    }

    public bool RemoveHitPointGain(
        Guid advancementOccurrenceId,
        int classLevel,
        DateTimeOffset changedAt)
    {
        if (classLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(classLevel), "Class level must be positive.");
        }

        var gain = HitPointGains.SingleOrDefault(value =>
            value.AdvancementOccurrenceId == advancementOccurrenceId
            && value.ClassLevel == classLevel);
        if (gain is null)
        {
            return false;
        }

        HitPointGains.Remove(gain);
        Touch(changedAt);
        return true;
    }

    public CharacterFoundationalRuleSelection SetFoundationalSelection(
        CharacterFoundationalSelectionCategory category,
        string ruleConceptKey,
        DateTimeOffset changedAt)
    {
        var normalizedConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        var selection = FoundationalSelections.SingleOrDefault(value => value.Category == category);
        if (selection is null)
        {
            selection = new CharacterFoundationalRuleSelection(
                Guid.NewGuid(),
                CharacterId,
                category,
                normalizedConceptKey,
                changedAt);
            FoundationalSelections.Add(selection);
        }
        else
        {
            selection.ReplaceRule(normalizedConceptKey, changedAt);
        }

        Touch(changedAt);
        return selection;
    }

    public bool ClearFoundationalSelection(
        CharacterFoundationalSelectionCategory category,
        DateTimeOffset changedAt)
    {
        var selection = FoundationalSelections.SingleOrDefault(value => value.Category == category);
        if (selection is null)
        {
            return false;
        }

        FoundationalSelections.Remove(selection);
        Touch(changedAt);
        return true;
    }

    public CharacterBaseAbilityScoreInput SetBaseAbilityScoreInput(
        string abilityKey,
        int score,
        DateTimeOffset changedAt)
    {
        var normalizedAbilityKey = CharacterAbilityKey.Normalize(abilityKey);
        var input = BaseAbilityScoreInputs.SingleOrDefault(value =>
            value.AbilityKey == normalizedAbilityKey);
        if (input is null)
        {
            input = new CharacterBaseAbilityScoreInput(
                Guid.NewGuid(),
                CharacterId,
                normalizedAbilityKey,
                score,
                changedAt);
            BaseAbilityScoreInputs.Add(input);
        }
        else
        {
            input.ReplaceScore(score, changedAt);
        }

        Touch(changedAt);
        return input;
    }

    public bool ClearBaseAbilityScoreInput(string abilityKey, DateTimeOffset changedAt)
    {
        var normalizedAbilityKey = CharacterAbilityKey.Normalize(abilityKey);
        var input = BaseAbilityScoreInputs.SingleOrDefault(value =>
            value.AbilityKey == normalizedAbilityKey);
        if (input is null)
        {
            return false;
        }

        BaseAbilityScoreInputs.Remove(input);
        Touch(changedAt);
        return true;
    }

    public CharacterAdvancementEntry SetStartingClass(string ruleConceptKey, DateTimeOffset changedAt)
    {
        var normalizedConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        var matches = AdvancementEntries.Where(IsStartingClass).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Character has more than one starting Class advancement entry.");
        }

        var entry = matches.SingleOrDefault();
        if (entry is null)
        {
            entry = new CharacterAdvancementEntry(
                Guid.NewGuid(),
                CharacterId,
                CharacterAdvancementKind.Class,
                normalizedConceptKey,
                0,
                null,
                changedAt,
                level: 1);
            AdvancementEntries.Add(entry);
        }
        else
        {
            if (!string.Equals(entry.RuleConceptKey, normalizedConceptKey, StringComparison.Ordinal))
            {
                RemoveSubclassChildren(entry.Id);
                RemoveHitPointGainsForAdvancement(entry.Id);
            }
            entry.ReplaceRule(normalizedConceptKey, changedAt);
        }

        Touch(changedAt);
        return entry;
    }

    public bool ClearStartingClass(DateTimeOffset changedAt)
    {
        var matches = AdvancementEntries.Where(IsStartingClass).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Character has more than one starting Class advancement entry.");
        }

        var entry = matches.SingleOrDefault();
        if (entry is null)
        {
            return false;
        }

        RemoveSubclassChildren(entry.Id);
        RemoveHitPointGainsForAdvancement(entry.Id);
        AdvancementEntries.Remove(entry);
        Touch(changedAt);
        return true;
    }

    public CharacterAdvancementEntry SetSubclassForClass(
        Guid classAdvancementEntryId,
        string ruleConceptKey,
        DateTimeOffset changedAt)
    {
        var parent = RequireClassAdvancement(classAdvancementEntryId);
        var matches = AdvancementEntries
            .Where(value => value.Kind == CharacterAdvancementKind.Subclass
                && value.ParentAdvancementEntryId == parent.Id)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Class advancement has more than one Subclass advancement entry.");
        }

        var normalizedConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        var entry = matches.SingleOrDefault();
        if (entry is null)
        {
            entry = new CharacterAdvancementEntry(
                Guid.NewGuid(),
                CharacterId,
                CharacterAdvancementKind.Subclass,
                normalizedConceptKey,
                null,
                parent.Id,
                changedAt);
            AdvancementEntries.Add(entry);
        }
        else
        {
            entry.ReplaceRule(normalizedConceptKey, changedAt);
        }

        Touch(changedAt);
        return entry;
    }

    public bool ClearSubclassForClass(Guid classAdvancementEntryId, DateTimeOffset changedAt)
    {
        var parent = RequireClassAdvancement(classAdvancementEntryId);
        var matches = AdvancementEntries
            .Where(value => value.Kind == CharacterAdvancementKind.Subclass
                && value.ParentAdvancementEntryId == parent.Id)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Class advancement has more than one Subclass advancement entry.");
        }

        var entry = matches.SingleOrDefault();
        if (entry is null)
        {
            return false;
        }

        AdvancementEntries.Remove(entry);
        Touch(changedAt);
        return true;
    }

    /// <summary>
    /// Adds one Character-owned Feat occurrence. Duplicate Rules Core concept keys are intentional:
    /// occurrence identity, not concept identity, distinguishes multiple grants of the same Feat.
    /// Acquisition provenance is deliberately deferred until grant/level-up semantics are defined.
    /// </summary>
    public CharacterAdvancementEntry AddFeatOccurrence(
        string ruleConceptKey,
        DateTimeOffset createdAt) =>
        AddAdvancement(
            CharacterAdvancementKind.Feat,
            ruleConceptKey,
            null,
            null,
            createdAt);

    public bool RemoveFeatOccurrence(Guid featAdvancementEntryId, DateTimeOffset changedAt)
    {
        if (featAdvancementEntryId == Guid.Empty)
        {
            throw new ArgumentException(
                "Feat advancement entry ID can not be empty.",
                nameof(featAdvancementEntryId));
        }

        var entry = AdvancementEntries.SingleOrDefault(value => value.Id == featAdvancementEntryId);
        if (entry is null)
        {
            return false;
        }

        if (entry.Kind != CharacterAdvancementKind.Feat)
        {
            throw new InvalidOperationException(
                "Advancement entry does not identify a Feat occurrence for this Character.");
        }

        AdvancementEntries.Remove(entry);
        Touch(changedAt);
        return true;
    }

    /// <summary>
    /// General progression primitive for later level-up, prestige-class, subclass, and feat work.
    /// This deliberately does not impose mature level-slot or prerequisite semantics.
    /// </summary>
    public CharacterAdvancementEntry AddAdvancement(
        CharacterAdvancementKind kind,
        string ruleConceptKey,
        int? ordinal,
        Guid? parentAdvancementEntryId,
        DateTimeOffset createdAt)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Advancement ordinal can not be negative.");
        }

        if (IsStartingClass(kind, ordinal, parentAdvancementEntryId)
            && AdvancementEntries.Any(IsStartingClass))
        {
            throw new InvalidOperationException("Character can have at most one starting Class advancement entry.");
        }

        CharacterAdvancementEntry? parent = null;
        if (parentAdvancementEntryId is Guid parentId)
        {
            parent = AdvancementEntries.SingleOrDefault(value => value.Id == parentId);
            if (parent is null || parent.CharacterId != CharacterId)
            {
                throw new InvalidOperationException(
                    "Parent advancement entry must belong to the same Character.");
            }
        }

        if (kind == CharacterAdvancementKind.Subclass)
        {
            if (parent is null || parent.Kind != CharacterAdvancementKind.Class)
            {
                throw new InvalidOperationException("A Subclass advancement must belong to a Class advancement entry.");
            }
            if (AdvancementEntries.Any(value => value.Kind == CharacterAdvancementKind.Subclass
                && value.ParentAdvancementEntryId == parent.Id))
            {
                throw new InvalidOperationException("A Class advancement can have at most one Subclass advancement entry.");
            }
        }

        var entry = new CharacterAdvancementEntry(
            Guid.NewGuid(),
            CharacterId,
            kind,
            CharacterRuleReference.NormalizeConceptKey(ruleConceptKey),
            ordinal,
            parentAdvancementEntryId,
            createdAt,
            level: kind is CharacterAdvancementKind.Class or CharacterAdvancementKind.PrestigeClass
                ? 1
                : null);
        AdvancementEntries.Add(entry);
        Touch(createdAt);
        return entry;
    }

    public CharacterAdvancementEntry SetAdvancementLevel(
        Guid advancementEntryId,
        int level,
        DateTimeOffset changedAt)
    {
        if (advancementEntryId == Guid.Empty)
        {
            throw new ArgumentException("Advancement entry ID can not be empty.", nameof(advancementEntryId));
        }

        var entry = AdvancementEntries.SingleOrDefault(value => value.Id == advancementEntryId)
            ?? throw new KeyNotFoundException("Character advancement entry was not found.");

        if (entry.Kind is not CharacterAdvancementKind.Class
            and not CharacterAdvancementKind.PrestigeClass)
        {
            throw new InvalidOperationException(
                "Only Class and Prestige Class advancement occurrences own an independent level.");
        }

        entry.SetLevel(level, changedAt);
        foreach (var staleGain in HitPointGains
            .Where(value =>
                value.AdvancementOccurrenceId == advancementEntryId
                && value.ClassLevel > level)
            .ToArray())
        {
            HitPointGains.Remove(staleGain);
        }

        Touch(changedAt);
        return entry;
    }

    public void SetCurrentHitPoints(int? currentHitPoints, DateTimeOffset changedAt)
    {
        CurrentHitPoints = currentHitPoints;
        Touch(changedAt);
    }

    public void SetDeathSaves(int successes, int failures, DateTimeOffset changedAt)
    {
        ValidateDeathSaveCount(successes, nameof(successes));
        ValidateDeathSaveCount(failures, nameof(failures));

        DeathSaveSuccesses = successes;
        DeathSaveFailures = failures;
        Touch(changedAt);
    }

    private static void ValidateDeathSaveCount(int value, string parameterName)
    {
        if (value is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Death save successes and failures must be between 0 and 3.");
        }
    }

    public CharacterInventoryItemOccurrence AddInventoryItemOccurrence(
        string ruleConceptKey,
        DateTimeOffset createdAt)
    {
        var occurrence = new CharacterInventoryItemOccurrence(
            Guid.NewGuid(),
            CharacterId,
            CharacterRuleReference.NormalizeConceptKey(ruleConceptKey),
            createdAt);
        InventoryItemOccurrences.Add(occurrence);
        Touch(createdAt);
        return occurrence;
    }

    public bool RemoveInventoryItemOccurrence(
        Guid occurrenceId,
        DateTimeOffset changedAt)
    {
        if (occurrenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inventory occurrence ID can not be empty.",
                nameof(occurrenceId));
        }

        var occurrence = InventoryItemOccurrences.SingleOrDefault(value => value.Id == occurrenceId);
        if (occurrence is null)
        {
            return false;
        }

        foreach (var child in InventoryItemOccurrences.Where(
            value => value.ContainerOccurrenceId == occurrence.Id))
        {
            child.ReplaceState(
                child.Quantity,
                child.IsCarried,
                child.IsEquipped,
                child.IsAttuned,
                null,
                changedAt);
        }

        InventoryItemOccurrences.Remove(occurrence);
        Touch(changedAt);
        return true;
    }

    public CharacterInventoryItemOccurrence UpdateInventoryItemOccurrence(
        Guid occurrenceId,
        int quantity,
        bool isCarried,
        bool isEquipped,
        bool isAttuned,
        Guid? containerOccurrenceId,
        DateTimeOffset changedAt)
    {
        if (occurrenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Inventory occurrence ID can not be empty.",
                nameof(occurrenceId));
        }

        var occurrence = InventoryItemOccurrences.SingleOrDefault(value => value.Id == occurrenceId)
            ?? throw new KeyNotFoundException("Inventory occurrence was not found.");

        if (containerOccurrenceId is Guid containerId)
        {
            var container = InventoryItemOccurrences.SingleOrDefault(value => value.Id == containerId)
                ?? throw new KeyNotFoundException("Inventory container occurrence was not found.");
            if (container.CharacterId != CharacterId)
            {
                throw new InvalidOperationException(
                    "Inventory container must belong to the same Character.");
            }

            var cursor = container;
            while (cursor.ContainerOccurrenceId is Guid parentId)
            {
                if (parentId == occurrence.Id)
                {
                    throw new InvalidOperationException(
                        "Inventory containers can not form a cycle.");
                }

                cursor = InventoryItemOccurrences.SingleOrDefault(value => value.Id == parentId)
                    ?? throw new InvalidOperationException(
                        "Inventory container hierarchy contains an unavailable occurrence.");
            }
        }

        occurrence.ReplaceState(
            quantity,
            isCarried,
            isEquipped,
            isAttuned,
            containerOccurrenceId,
            changedAt);
        Touch(changedAt);
        return occurrence;
    }

    public CharacterNote AddNote(string content, DateTimeOffset createdAt)
    {
        var note = new CharacterNote(
            Guid.NewGuid(),
            CharacterId,
            content,
            createdAt);
        Notes.Add(note);
        Touch(createdAt);
        return note;
    }

    public CharacterNote UpdateNote(
        Guid noteId,
        string content,
        DateTimeOffset changedAt)
    {
        if (noteId == Guid.Empty)
        {
            throw new ArgumentException("Note ID can not be empty.", nameof(noteId));
        }

        var note = Notes.SingleOrDefault(value => value.Id == noteId)
            ?? throw new KeyNotFoundException("Character note was not found.");
        note.ReplaceContent(content, changedAt);
        Touch(changedAt);
        return note;
    }

    public bool RemoveNote(Guid noteId, DateTimeOffset changedAt)
    {
        if (noteId == Guid.Empty)
        {
            throw new ArgumentException("Note ID can not be empty.", nameof(noteId));
        }

        var note = Notes.SingleOrDefault(value => value.Id == noteId);
        if (note is null)
        {
            return false;
        }

        Notes.Remove(note);
        Touch(changedAt);
        return true;
    }

    public CharacterConditionOccurrence AddCondition(
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset createdAt)
    {
        var occurrence = new CharacterConditionOccurrence(
            Guid.NewGuid(),
            CharacterId,
            ruleConceptKey,
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            createdAt);
        Conditions.Add(occurrence);
        Touch(createdAt);
        return occurrence;
    }

    public CharacterConditionOccurrence UpdateCondition(
        Guid conditionId,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt)
    {
        if (conditionId == Guid.Empty)
        {
            throw new ArgumentException("Condition occurrence ID can not be empty.", nameof(conditionId));
        }

        var condition = Conditions.SingleOrDefault(value => value.Id == conditionId)
            ?? throw new KeyNotFoundException("Character condition was not found.");
        condition.ReplaceState(
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            changedAt);
        Touch(changedAt);
        return condition;
    }

    public bool RemoveCondition(Guid conditionId, DateTimeOffset changedAt)
    {
        if (conditionId == Guid.Empty)
        {
            throw new ArgumentException("Condition occurrence ID can not be empty.", nameof(conditionId));
        }

        var condition = Conditions.SingleOrDefault(value => value.Id == conditionId);
        if (condition is null)
        {
            return false;
        }

        Conditions.Remove(condition);
        Touch(changedAt);
        return true;
    }

    private CharacterAdvancementEntry RequireClassAdvancement(Guid classAdvancementEntryId)
    {
        if (classAdvancementEntryId == Guid.Empty)
        {
            throw new ArgumentException("Class advancement entry ID can not be empty.", nameof(classAdvancementEntryId));
        }

        var parent = AdvancementEntries.SingleOrDefault(value => value.Id == classAdvancementEntryId);
        if (parent is null || parent.CharacterId != CharacterId || parent.Kind != CharacterAdvancementKind.Class)
        {
            throw new InvalidOperationException("Subclass parent must be a Class advancement entry for the same Character.");
        }
        return parent;
    }

    private void RemoveHitPointGainsForAdvancement(Guid advancementEntryId)
    {
        foreach (var gain in HitPointGains
            .Where(value => value.AdvancementOccurrenceId == advancementEntryId)
            .ToArray())
        {
            HitPointGains.Remove(gain);
        }
    }

    private void RemoveSubclassChildren(Guid classAdvancementEntryId)
    {
        foreach (var subclass in AdvancementEntries
                     .Where(value => value.Kind == CharacterAdvancementKind.Subclass
                         && value.ParentAdvancementEntryId == classAdvancementEntryId)
                     .ToArray())
        {
            AdvancementEntries.Remove(subclass);
        }
    }

    private static bool IsStartingClass(CharacterAdvancementEntry value) =>
        IsStartingClass(value.Kind, value.Ordinal, value.ParentAdvancementEntryId);

    private static bool IsStartingClass(
        CharacterAdvancementKind kind,
        int? ordinal,
        Guid? parentAdvancementEntryId) =>
        kind == CharacterAdvancementKind.Class
        && ordinal == 0
        && parentAdvancementEntryId is null;

    private void Touch(DateTimeOffset changedAt)
    {
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
