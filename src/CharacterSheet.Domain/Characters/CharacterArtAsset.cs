namespace CharacterSheet.Domain.Characters;

public sealed class CharacterArtAsset
{
    public const int MaxOriginalFileNameLength = 260;
    public const int MaxContentTypeLength = 100;
    public const int MaxStorageKeyLength = 200;

    private CharacterArtAsset() { }

    public CharacterArtAsset(
        Guid id,
        Guid characterId,
        string storageKey,
        string originalFileName,
        string contentType,
        long byteLength,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Art asset ID can not be empty.", nameof(id));
        if (characterId == Guid.Empty) throw new ArgumentException("Character ID can not be empty.", nameof(characterId));
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));

        Id = id;
        CharacterId = characterId;
        StorageKey = RequireText(storageKey, MaxStorageKeyLength, "Storage key");
        OriginalFileName = RequireText(originalFileName, MaxOriginalFileNameLength, "Original file name");
        ContentType = RequireText(contentType, MaxContentTypeLength, "Content type");
        ByteLength = byteLength;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid CharacterId { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long ByteLength { get; private set; }
    public bool IsPortrait { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetPortrait(bool isPortrait, DateTimeOffset changedAt)
    {
        IsPortrait = isPortrait;
        if (changedAt > UpdatedAt) UpdatedAt = changedAt;
    }

    private static string RequireText(string value, int maxLength, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{label} can not be blank.");
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentException($"{label} can not exceed {maxLength} characters.");
        return normalized;
    }
}
