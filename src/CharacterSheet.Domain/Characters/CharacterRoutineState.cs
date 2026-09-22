namespace CharacterSheet.Domain.Characters;

public sealed class CharacterInventoryItemOccurrence
{
    private CharacterInventoryItemOccurrence()
    {
    }

    internal CharacterInventoryItemOccurrence(
        Guid id,
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Inventory occurrence ID can not be empty.", nameof(id));
        }
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        Id = id;
        CharacterId = characterId;
        RuleConceptKey = CharacterRuleReference.NormalizeConceptKey(ruleConceptKey);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CharacterId { get; private set; }

    /// <summary>
    /// Stable Rules Core RuleConcept.Key. This occurrence means only that the Character owns the
    /// referenced item occurrence. Display names and copied mechanics are intentionally not persisted.
    /// </summary>
    public string RuleConceptKey { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class CharacterNote
{
    private CharacterNote()
    {
    }

    internal CharacterNote(
        Guid id,
        Guid characterId,
        string content,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Note ID can not be empty.", nameof(id));
        }
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        Id = id;
        CharacterId = characterId;
        Content = RequireContent(content);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CharacterId { get; private set; }

    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceContent(string content, DateTimeOffset changedAt)
    {
        Content = RequireContent(content);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }

    private static string RequireContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Character note content can not be blank.", nameof(content));
        }

        return content;
    }
}


public sealed class CharacterConditionOccurrence
{
    public const int MaxCustomNameLength = 120;
    public const int MaxDurationLength = 160;
    public const int MaxNotesLength = 2000;

    private CharacterConditionOccurrence()
    {
    }

    internal CharacterConditionOccurrence(
        Guid id,
        Guid characterId,
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Condition occurrence ID can not be empty.", nameof(id));
        }
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        var hasRule = !string.IsNullOrWhiteSpace(ruleConceptKey);
        var hasCustom = !string.IsNullOrWhiteSpace(customName);
        if (hasRule == hasCustom)
        {
            throw new ArgumentException(
                "A condition must identify exactly one Rules Core condition or custom condition name.");
        }

        Id = id;
        CharacterId = characterId;
        RuleConceptKey = hasRule
            ? CharacterRuleReference.NormalizeConceptKey(ruleConceptKey!)
            : null;
        CustomName = hasCustom
            ? RequireBoundedText(customName!, MaxCustomNameLength, "Custom condition name")
            : null;
        ApplyState(level, counterCurrent, counterMaximum, duration, notes);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CharacterId { get; private set; }
    public string? RuleConceptKey { get; private set; }
    public string? CustomName { get; private set; }
    public int? Level { get; private set; }
    public int? CounterCurrent { get; private set; }
    public int? CounterMaximum { get; private set; }
    public string? Duration { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceState(
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt)
    {
        if (RuleConceptKey is null && customName is not null)
        {
            CustomName = RequireBoundedText(
                customName,
                MaxCustomNameLength,
                "Custom condition name");
        }
        else if (RuleConceptKey is not null && customName is not null)
        {
            throw new ArgumentException(
                "Rules-defined conditions can not be renamed as custom conditions.",
                nameof(customName));
        }

        ApplyState(level, counterCurrent, counterMaximum, duration, notes);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }

    private void ApplyState(
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes)
    {
        if (level < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Condition level can not be negative.");
        }
        if (counterCurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counterCurrent), "Condition counter can not be negative.");
        }
        if (counterMaximum < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(counterMaximum), "Condition counter maximum can not be negative.");
        }
        if (counterCurrent is int current
            && counterMaximum is int maximum
            && current > maximum)
        {
            throw new ArgumentException("Condition counter can not exceed its maximum.");
        }

        Level = level;
        CounterCurrent = counterCurrent;
        CounterMaximum = counterMaximum;
        Duration = OptionalBoundedText(duration, MaxDurationLength, "Condition duration");
        Notes = OptionalBoundedText(notes, MaxNotesLength, "Condition notes");
    }

    private static string RequireBoundedText(string value, int maxLength, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{label} can not be blank.");
        }
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{label} can not exceed {maxLength} characters.");
        }
        return normalized;
    }

    private static string? OptionalBoundedText(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return RequireBoundedText(value, maxLength, label);
    }
}
