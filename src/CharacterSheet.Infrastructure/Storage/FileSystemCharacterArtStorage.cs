using CharacterSheet.Application.Characters;

namespace CharacterSheet.Infrastructure.Storage;

public sealed class FileSystemCharacterArtStorage(string rootPath) : ICharacterArtStorage
{
    private readonly string _rootPath = Path.GetFullPath(rootPath);

    public async Task WriteAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".upload-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        Stream? result = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(result);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path)) File.Delete(path);
        var directory = Path.GetDirectoryName(path);
        if (directory is not null && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            Directory.Delete(directory);
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) throw new ArgumentException("Storage key can not be blank.", nameof(storageKey));
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(_rootPath, relative));
        var prefix = _rootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid Character art storage key.");
        return resolved;
    }
}
