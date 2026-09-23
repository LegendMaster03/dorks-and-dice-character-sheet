namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Character-authored descriptive profile state. This intentionally excludes Site-owned identity
/// and Rules Core-derived selections or mechanics such as Background and Size.
/// </summary>
public sealed class CharacterProfileState
{
    public const int MaxShortTextLength = 300;
    public const int MaxDescriptionLength = 4000;
    public const int MaxLongTextLength = 20000;

    private CharacterProfileState() { }

    internal CharacterProfileState(
        Guid characterId,
        string? alignment,
        string? deity,
        string? age,
        string? height,
        string? weight,
        string? appearance,
        string? personalityTraits,
        string? ideals,
        string? bonds,
        string? flaws,
        string? backstory,
        string? alliesAndOrganizations,
        string? symbol,
        DateTimeOffset createdAt)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        CharacterId = characterId;
        Apply(alignment, deity, age, height, weight, appearance, personalityTraits, ideals, bonds,
            flaws, backstory, alliesAndOrganizations, symbol);
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid CharacterId { get; private set; }
    public string? Alignment { get; private set; }
    public string? Deity { get; private set; }
    public string? Age { get; private set; }
    public string? Height { get; private set; }
    public string? Weight { get; private set; }
    public string? Appearance { get; private set; }
    public string? PersonalityTraits { get; private set; }
    public string? Ideals { get; private set; }
    public string? Bonds { get; private set; }
    public string? Flaws { get; private set; }
    public string? Backstory { get; private set; }
    public string? AlliesAndOrganizations { get; private set; }
    public string? Symbol { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void Replace(
        string? alignment,
        string? deity,
        string? age,
        string? height,
        string? weight,
        string? appearance,
        string? personalityTraits,
        string? ideals,
        string? bonds,
        string? flaws,
        string? backstory,
        string? alliesAndOrganizations,
        string? symbol,
        DateTimeOffset changedAt)
    {
        Apply(alignment, deity, age, height, weight, appearance, personalityTraits, ideals, bonds,
            flaws, backstory, alliesAndOrganizations, symbol);
        if (changedAt > UpdatedAt) UpdatedAt = changedAt;
    }

    private void Apply(
        string? alignment,
        string? deity,
        string? age,
        string? height,
        string? weight,
        string? appearance,
        string? personalityTraits,
        string? ideals,
        string? bonds,
        string? flaws,
        string? backstory,
        string? alliesAndOrganizations,
        string? symbol)
    {
        Alignment = NormalizeOptional(alignment, MaxShortTextLength, "Alignment");
        Deity = NormalizeOptional(deity, MaxShortTextLength, "Deity");
        Age = NormalizeOptional(age, MaxShortTextLength, "Age");
        Height = NormalizeOptional(height, MaxShortTextLength, "Height");
        Weight = NormalizeOptional(weight, MaxShortTextLength, "Weight");
        Appearance = NormalizeOptional(appearance, MaxDescriptionLength, "Appearance");
        PersonalityTraits = NormalizeOptional(personalityTraits, MaxDescriptionLength, "Personality traits");
        Ideals = NormalizeOptional(ideals, MaxDescriptionLength, "Ideals");
        Bonds = NormalizeOptional(bonds, MaxDescriptionLength, "Bonds");
        Flaws = NormalizeOptional(flaws, MaxDescriptionLength, "Flaws");
        Backstory = NormalizeOptional(backstory, MaxLongTextLength, "Backstory");
        AlliesAndOrganizations = NormalizeOptional(
            alliesAndOrganizations,
            MaxLongTextLength,
            "Allies and organizations");
        Symbol = NormalizeOptional(symbol, MaxDescriptionLength, "Symbol");
    }

    private static string? NormalizeOptional(string? value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{label} can not exceed {maxLength} characters.");
        }

        return normalized;
    }
}
