namespace CharacterSheet.Domain.Characters;

public enum CharacterRulesInputKind
{
    Choice = 1,
    CompetencyRank = 2,
    Training = 3,
    ClassSkill = 4,
    KnownSpell = 5,
    Resource = 6,
    IntegerFact = 7,
    BooleanFact = 8,
    StringFact = 9
}

public static class CharacterRulesInputKey
{
    public const int MaxLength = 300;
    public const int MaxTextValueLength = 2000;

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Rules input key can not be blank.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxLength)
        {
            throw new ArgumentException(
                $"Rules input key can not exceed {MaxLength} characters.",
                nameof(value));
        }

        return normalized;
    }

    public static string NormalizeTextValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Rules input text value can not be blank.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > MaxTextValueLength)
        {
            throw new ArgumentException(
                $"Rules input text value can not exceed {MaxTextValueLength} characters.",
                nameof(value));
        }

        return normalized;
    }
}

public sealed class CharacterRulesInputState
{
    private CharacterRulesInputState()
    {
    }

    internal CharacterRulesInputState(
        Guid id,
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Rules input ID can not be empty.", nameof(id));
        }
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        Id = id;
        CharacterId = characterId;
        Kind = kind;
        Key = CharacterRulesInputKey.Normalize(key);
        ApplyValue(kind, integerValue, booleanValue, textValue);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CharacterId { get; private set; }
    public CharacterRulesInputKind Kind { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public int? IntegerValue { get; private set; }
    public bool? BooleanValue { get; private set; }
    public string? TextValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void Replace(
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset changedAt)
    {
        ApplyValue(Kind, integerValue, booleanValue, textValue);
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }

    private void ApplyValue(
        CharacterRulesInputKind kind,
        int? integerValue,
        bool? booleanValue,
        string? textValue)
    {
        switch (kind)
        {
            case CharacterRulesInputKind.Choice:
            case CharacterRulesInputKind.StringFact:
                IntegerValue = null;
                BooleanValue = null;
                TextValue = CharacterRulesInputKey.NormalizeTextValue(
                    textValue ?? throw new ArgumentException(
                        "This rules input requires a text value.",
                        nameof(textValue)));
                break;
            case CharacterRulesInputKind.CompetencyRank:
                if (integerValue is null || integerValue < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(integerValue),
                        "Competency rank input must be zero or greater.");
                }
                IntegerValue = integerValue;
                BooleanValue = null;
                TextValue = null;
                break;
            case CharacterRulesInputKind.Resource:
            case CharacterRulesInputKind.IntegerFact:
                IntegerValue = integerValue
                    ?? throw new ArgumentException(
                        "This rules input requires an integer value.",
                        nameof(integerValue));
                BooleanValue = null;
                TextValue = null;
                break;
            case CharacterRulesInputKind.BooleanFact:
                IntegerValue = null;
                BooleanValue = booleanValue
                    ?? throw new ArgumentException(
                        "This rules input requires a boolean value.",
                        nameof(booleanValue));
                TextValue = null;
                break;
            case CharacterRulesInputKind.Training:
            case CharacterRulesInputKind.ClassSkill:
            case CharacterRulesInputKind.KnownSpell:
                IntegerValue = null;
                BooleanValue = null;
                TextValue = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported rules input kind.");
        }
    }
}

public sealed class CharacterHitPointGainState
{
    private CharacterHitPointGainState()
    {
    }

    internal CharacterHitPointGainState(
        Guid id,
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Hit point gain ID can not be empty.", nameof(id));
        }
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }
        if (advancementOccurrenceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Advancement occurrence ID can not be empty.",
                nameof(advancementOccurrenceId));
        }
        if (classLevel <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(classLevel),
                "Class level must be positive.");
        }

        Id = id;
        CharacterId = characterId;
        AdvancementOccurrenceId = advancementOccurrenceId;
        ClassLevel = classLevel;
        HitDieValue = hitDieValue;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CharacterId { get; private set; }
    public Guid AdvancementOccurrenceId { get; private set; }
    public int ClassLevel { get; private set; }
    public int HitDieValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceValue(int hitDieValue, DateTimeOffset changedAt)
    {
        HitDieValue = hitDieValue;
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
