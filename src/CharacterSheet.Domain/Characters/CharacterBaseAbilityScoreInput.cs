namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Stable Character Sheet-owned ability identities for the current playable baseline.
/// These are not Rules Core concept keys. Persistence uses normalized string keys so
/// additional ability axes can be supported later without a database redesign.
/// </summary>
public static class CharacterAbilityKey
{
    public const int MaxKeyLength = 64;

    public const string Strength = "strength";
    public const string Dexterity = "dexterity";
    public const string Constitution = "constitution";
    public const string Intelligence = "intelligence";
    public const string Wisdom = "wisdom";
    public const string Charisma = "charisma";

    private static readonly HashSet<string> PlayableBaseline =
    [
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma
    ];

    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Ability key can not be blank.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxKeyLength)
        {
            throw new ArgumentException(
                $"Ability key can not exceed {MaxKeyLength} characters.",
                nameof(value));
        }

        if (!PlayableBaseline.Contains(normalized))
        {
            throw new ArgumentException(
                $"Ability key '{normalized}' is not supported by the current playable baseline.",
                nameof(value));
        }

        return normalized;
    }
}

/// <summary>
/// Character-owned current base/input score for one ability axis. This is a persisted
/// builder decision only; it is not an effective score, derived grant, modifier, or override.
/// </summary>
public sealed class CharacterBaseAbilityScoreInput
{
    private CharacterBaseAbilityScoreInput()
    {
    }

    internal CharacterBaseAbilityScoreInput(
        Guid id,
        Guid characterId,
        string abilityKey,
        int score,
        DateTimeOffset createdAt)
    {
        if (characterId == Guid.Empty)
        {
            throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));
        }

        Id = id;
        CharacterId = characterId;
        AbilityKey = CharacterAbilityKey.Normalize(abilityKey);
        Score = score;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid CharacterId { get; private set; }

    public string AbilityKey { get; private set; } = string.Empty;

    /// <summary>
    /// Directly assigned/base input. No final/effective semantics are implied.
    /// </summary>
    public int Score { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceScore(int score, DateTimeOffset changedAt)
    {
        Score = score;
        if (changedAt > UpdatedAt)
        {
            UpdatedAt = changedAt;
        }
    }
}
