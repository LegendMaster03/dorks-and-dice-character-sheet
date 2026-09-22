namespace CharacterSheet.Domain.Characters;

public enum CharacterFoundationalSelectionCategory
{
    RaceSpecies = 1
}

public enum CharacterAdvancementKind
{
    Class = 1,
    Subclass = 2,
    PrestigeClass = 3,
    Feat = 4
}

public static class CharacterRuleReference
{
    public const int MaxConceptKeyLength = 300;

    public static string NormalizeConceptKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Rules Core concept key can not be blank.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxConceptKeyLength)
        {
            throw new ArgumentException(
                $"Rules Core concept key can not exceed {MaxConceptKeyLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed class CharacterFoundationalRuleSelection
{
    private CharacterFoundationalRuleSelection()
    {
    }

    internal CharacterFoundationalRuleSelection(
        Guid id,
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        string ruleConceptKey,
        DateTimeOffset createdAt)
    {
        Id = id;
        CharacterId = characterId;
        Category = category;
        RuleConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CharacterId { get; private set; }

    public CharacterFoundationalSelectionCategory Category { get; private set; }

    /// <summary>
    /// Stable Rules Core RuleConcept.Key. Display names, source revision IDs, and rule JSON are not
    /// Character Sheet identity and are intentionally not persisted here.
    /// </summary>
    public string RuleConceptKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceRule(string ruleConceptKey, DateTimeOffset changedAt)
    {
        RuleConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}

public sealed class CharacterAdvancementEntry
{
    // Technical safety ceiling, not a game-rule maximum. It prevents pathological
    // allocations/projections while remaining far above official advancement ranges.
    public const int MaxSupportedLevel = 1000;
    private CharacterAdvancementEntry()
    {
    }

    internal CharacterAdvancementEntry(
        Guid id,
        Guid characterId,
        CharacterAdvancementKind kind,
        string ruleConceptKey,
        int? ordinal,
        Guid? parentAdvancementEntryId,
        DateTimeOffset createdAt,
        int? level = null)
    {
        if (ordinal < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Advancement ordinal can not be negative.");
        }
        if (level is <= 0 or > MaxSupportedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                $"Advancement level must be from 1 through {MaxSupportedLevel} when supplied.");
        }

        Id = id;
        CharacterId = characterId;
        Kind = kind;
        RuleConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        Ordinal = ordinal;
        ParentAdvancementEntryId = parentAdvancementEntryId;
        Level = level;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    /// <summary>
    /// Character-owned identity for this advancement occurrence. Multiple entries may reference the
    /// same Rules Core concept without collapsing distinct progression decisions.
    /// </summary>
    public Guid Id { get; private set; }

    public Guid CharacterId { get; private set; }

    public CharacterAdvancementKind Kind { get; private set; }

    public string RuleConceptKey { get; private set; } = string.Empty;

    public int? Ordinal { get; private set; }

    /// <summary>
    /// Optional Character-owned parent advancement identity. This allows a future Subclass entry to
    /// point at the relevant Class entry without storing subclass fields on the Class row. The parent
    /// must belong to the same Character.
    /// </summary>
    public Guid? ParentAdvancementEntryId { get; private set; }

    /// <summary>
    /// Character-owned level in this Class or Prestige Class occurrence. Subclass effective level
    /// follows its parent Class occurrence when building the Rules Core projection.
    /// </summary>
    public int? Level { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void SetLevel(int level, DateTimeOffset changedAt)
    {
        if (level <= 0 || level > MaxSupportedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                $"Advancement level must be from 1 through {MaxSupportedLevel}.");
        }

        Level = level;
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }

    internal void ReplaceRule(string ruleConceptKey, DateTimeOffset changedAt)
    {
        RuleConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
