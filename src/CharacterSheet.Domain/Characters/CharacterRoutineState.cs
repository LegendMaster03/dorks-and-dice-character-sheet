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
