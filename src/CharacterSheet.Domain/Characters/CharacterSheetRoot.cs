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

    public ICollection<CharacterFoundationalRuleSelection> FoundationalSelections { get; private set; } =
        new List<CharacterFoundationalRuleSelection>();

    public ICollection<CharacterAdvancementEntry> AdvancementEntries { get; private set; } =
        new List<CharacterAdvancementEntry>();

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

    public CharacterAdvancementEntry SetStartingClass(string ruleConceptKey, DateTimeOffset changedAt)
    {
        var normalizedConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        var matches = AdvancementEntries
            .Where(value => value.Kind == CharacterAdvancementKind.Class
                && value.Ordinal == 0
                && value.ParentAdvancementEntryId is null)
            .ToArray();
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

    public bool ClearStartingClass(DateTimeOffset changedAt)
    {
        var matches = AdvancementEntries
            .Where(value => value.Kind == CharacterAdvancementKind.Class
                && value.Ordinal == 0
                && value.ParentAdvancementEntryId is null)
            .ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException("Character has more than one starting Class advancement entry.");
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

        var entry = new CharacterAdvancementEntry(
            Guid.NewGuid(),
            CharacterId,
            kind,
            CharacterRuleReference.NormalizeConceptKey(ruleConceptKey),
            ordinal,
            parentAdvancementEntryId,
            createdAt);
        AdvancementEntries.Add(entry);
        Touch(createdAt);
        return entry;
    }

    private void Touch(DateTimeOffset changedAt)
    {
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
