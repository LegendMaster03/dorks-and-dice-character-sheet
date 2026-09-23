namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Character-owned currency balance. Currency conversion, denomination relationships, encumbrance,
/// and other mechanics remain Rules Core concerns when such semantics are available.
/// </summary>
public sealed class CharacterCurrencyBalance
{
    public const int MaxKeyLength = 160;

    private CharacterCurrencyBalance() { }

    internal CharacterCurrencyBalance(
        Guid id,
        Guid characterId,
        string currencyKey,
        long amount,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Currency balance ID can not be empty.", nameof(id));
        if (characterId == Guid.Empty) throw new ArgumentException("Site CharacterId can not be empty.", nameof(characterId));

        Id = id;
        CharacterId = characterId;
        CurrencyKey = NormalizeKey(currencyKey);
        Amount = amount;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CharacterId { get; private set; }
    public string CurrencyKey { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    internal void ReplaceAmount(long amount, DateTimeOffset changedAt)
    {
        Amount = amount;
        if (changedAt > UpdatedAt) UpdatedAt = changedAt;
    }

    public static string NormalizeKey(string currencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currencyKey);
        var normalized = currencyKey.Trim().ToLowerInvariant();
        if (normalized.Length > MaxKeyLength)
        {
            throw new ArgumentException(
                $"Currency key can not exceed {MaxKeyLength} characters.",
                nameof(currencyKey));
        }

        return normalized;
    }
}
