using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public interface ICharacterArtStorage
{
    Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

public enum CharacterArtAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized,
    ArchivedReadOnly,
    EntryNotFound
}

public sealed record CharacterArtAssetView(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long ByteLength,
    bool IsPortrait,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterArtResult(
    CharacterArtAccessStatus Status,
    IReadOnlyList<CharacterArtAssetView>? Assets = null);

public sealed record CharacterArtContentResult(
    CharacterArtAccessStatus Status,
    Stream? Content = null,
    string? ContentType = null,
    string? OriginalFileName = null);

public sealed class CharacterArtService(
    ISiteCharacterAccessGateway siteCharacterAccess,
    ICharacterSheetStore characterSheetStore,
    ICharacterArtStore artStore,
    ICharacterArtStorage storage,
    TimeProvider timeProvider)
{
    public const long MaxUploadBytes = 8 * 1024 * 1024;

    public async Task<CharacterArtResult> ListAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: false, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);
        return new(CharacterArtAccessStatus.Ready, ToViews(await artStore.ListAsync(characterId, cancellationToken)));
    }

    public async Task<CharacterArtContentResult> OpenAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: false, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);

        var asset = await artStore.GetAsync(characterId, assetId, cancellationToken);
        if (asset is null) return new(CharacterArtAccessStatus.EntryNotFound);
        var content = await storage.OpenReadAsync(asset.StorageKey, cancellationToken);
        return content is null
            ? new(CharacterArtAccessStatus.EntryNotFound)
            : new(CharacterArtAccessStatus.Ready, content, asset.ContentType, asset.OriginalFileName);
    }

    public async Task<CharacterArtResult> UploadAsync(
        Guid characterId,
        string originalFileName,
        string declaredContentType,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: true, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);

        var validated = await ValidateAndBufferAsync(content, declaredContentType, cancellationToken);
        await using var bufferedContent = validated.Stream;
        var id = Guid.NewGuid();
        var storageKey = $"{characterId:N}/{id:N}";
        var asset = new CharacterArtAsset(
            id,
            characterId,
            storageKey,
            Path.GetFileName(originalFileName),
            validated.ContentType,
            validated.Length,
            timeProvider.GetUtcNow());

        await storage.WriteAsync(storageKey, bufferedContent, cancellationToken);
        try
        {
            await artStore.AddAsync(asset, cancellationToken);
        }
        catch
        {
            await SafeDeleteStorageAsync(storageKey, cancellationToken);
            throw;
        }

        return new(CharacterArtAccessStatus.Ready, ToViews(await artStore.ListAsync(characterId, cancellationToken)));
    }

    public async Task<CharacterArtResult> SetPortraitAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: true, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);
        if (await artStore.SetPortraitAsync(characterId, assetId, timeProvider.GetUtcNow(), cancellationToken) is null)
            return new(CharacterArtAccessStatus.EntryNotFound);
        return new(CharacterArtAccessStatus.Ready, ToViews(await artStore.ListAsync(characterId, cancellationToken)));
    }

    public async Task<CharacterArtResult> ClearPortraitAsync(Guid characterId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: true, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);
        await artStore.ClearPortraitAsync(characterId, timeProvider.GetUtcNow(), cancellationToken);
        return new(CharacterArtAccessStatus.Ready, ToViews(await artStore.ListAsync(characterId, cancellationToken)));
    }

    public async Task<CharacterArtResult> DeleteAsync(Guid characterId, Guid assetId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(characterId, requireEdit: true, cancellationToken);
        if (access.Status is not CharacterArtAccessStatus.Ready) return new(access.Status);
        var removed = await artStore.DeleteAsync(characterId, assetId, cancellationToken);
        if (removed is null) return new(CharacterArtAccessStatus.EntryNotFound);
        await SafeDeleteStorageAsync(removed.StorageKey, cancellationToken);
        return new(CharacterArtAccessStatus.Ready, ToViews(await artStore.ListAsync(characterId, cancellationToken)));
    }

    private async Task<(CharacterArtAccessStatus Status, SiteCharacterProjection? Character)> AuthorizeAsync(
        Guid characterId,
        bool requireEdit,
        CancellationToken cancellationToken)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var status = access.Status switch
        {
            SiteCharacterAccessStatus.Authorized => CharacterArtAccessStatus.Ready,
            SiteCharacterAccessStatus.NotFoundOrNotOwned => CharacterArtAccessStatus.NotFoundOrNotOwned,
            SiteCharacterAccessStatus.ProjectionUnavailable => CharacterArtAccessStatus.ProjectionUnavailable,
            SiteCharacterAccessStatus.Unauthenticated => CharacterArtAccessStatus.Unauthenticated,
            _ => CharacterArtAccessStatus.ProjectionUnavailable
        };
        if (status is not CharacterArtAccessStatus.Ready) return (status, null);
        if (await characterSheetStore.GetAsync(characterId, cancellationToken) is null)
            return (CharacterArtAccessStatus.SheetNotInitialized, access.Character);
        if (requireEdit && !access.Character!.AllowsOrdinaryEditingByLifecycle)
            return (CharacterArtAccessStatus.ArchivedReadOnly, access.Character);
        return (CharacterArtAccessStatus.Ready, access.Character);
    }

    private async Task SafeDeleteStorageAsync(string storageKey, CancellationToken cancellationToken)
    {
        try { await storage.DeleteAsync(storageKey, cancellationToken); }
        catch { /* Metadata is authoritative; an orphaned file is safer than corrupt Character state. */ }
    }

    private static IReadOnlyList<CharacterArtAssetView> ToViews(IReadOnlyList<CharacterArtAsset> assets) =>
        assets.Select(value => new CharacterArtAssetView(
            value.Id, value.OriginalFileName, value.ContentType, value.ByteLength,
            value.IsPortrait, value.CreatedAt, value.UpdatedAt)).ToArray();

    private static async Task<(MemoryStream Stream, string ContentType, long Length)> ValidateAndBufferAsync(
        Stream content,
        string declaredContentType,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes)
            {
                await buffer.DisposeAsync();
                throw new ArgumentException($"Character art can not exceed {MaxUploadBytes / (1024 * 1024)} MiB.");
            }
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }
        if (buffer.Length == 0)
        {
            await buffer.DisposeAsync();
            throw new ArgumentException("Character art file can not be empty.");
        }

        var bytes = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
        var detected = DetectContentType(bytes);
        if (detected is null)
        {
            await buffer.DisposeAsync();
            throw new ArgumentException("Character art must be a valid PNG, JPEG, WebP, or GIF image.");
        }
        if (!string.Equals(declaredContentType?.Trim(), detected, StringComparison.OrdinalIgnoreCase))
        {
            await buffer.DisposeAsync();
            throw new ArgumentException("Uploaded image content does not match its declared MIME type.");
        }
        buffer.Position = 0;
        return (buffer, detected, buffer.Length);
    }

    internal static string? DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        return null;
    }
}
