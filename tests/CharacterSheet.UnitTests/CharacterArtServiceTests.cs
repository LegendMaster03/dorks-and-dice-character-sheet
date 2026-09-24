using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

public sealed class CharacterArtServiceTests
{
    [Fact]
    public async Task ArchivedCharacterMayReadArtButCanNotMutateIt()
    {
        var characterId = Guid.NewGuid();
        var asset = Asset(characterId);
        var access = new FakeSiteAccessGateway(Authorized(characterId, SiteCharacterLifecycleState.Archived));
        var sheets = new FakeSheetStore(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow.AddHours(-1)));
        var art = new FakeArtStore(asset);
        var storage = new FakeArtStorage();
        storage.Files[asset.StorageKey] = [1, 2, 3];
        var service = new CharacterArtService(access, sheets, art, storage, TimeProvider.System);

        var list = await service.ListAsync(characterId);
        Assert.Equal(CharacterArtAccessStatus.Ready, list.Status);
        Assert.Single(list.Assets!);

        await using var opened = (await service.OpenAsync(characterId, asset.Id)).Content;
        Assert.NotNull(opened);

        var upload = await service.UploadAsync(
            characterId,
            "new.png",
            "image/png",
            new MemoryStream(ValidPng()));
        Assert.Equal(CharacterArtAccessStatus.ArchivedReadOnly, upload.Status);

        Assert.Equal(
            CharacterArtAccessStatus.ArchivedReadOnly,
            (await service.SetPortraitAsync(characterId, asset.Id)).Status);
        Assert.Equal(
            CharacterArtAccessStatus.ArchivedReadOnly,
            (await service.ClearPortraitAsync(characterId)).Status);
        Assert.Equal(
            CharacterArtAccessStatus.ArchivedReadOnly,
            (await service.DeleteAsync(characterId, asset.Id)).Status);

        Assert.Equal(0, art.MutationCount);
        Assert.Equal(0, storage.WriteCount);
        Assert.Equal(0, storage.DeleteCount);
    }

    [Fact]
    public async Task UnauthorizedCharacterDoesNotProbeLocalSheetArtOrStorage()
    {
        var characterId = Guid.NewGuid();
        var sheets = new FakeSheetStore(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        var art = new FakeArtStore();
        var storage = new FakeArtStorage();
        var service = new CharacterArtService(
            new FakeSiteAccessGateway(new SiteCharacterAccessResult(SiteCharacterAccessStatus.NotFoundOrNotOwned)),
            sheets,
            art,
            storage,
            TimeProvider.System);

        Assert.Equal(
            CharacterArtAccessStatus.NotFoundOrNotOwned,
            (await service.ListAsync(characterId)).Status);
        Assert.Equal(
            CharacterArtAccessStatus.NotFoundOrNotOwned,
            (await service.OpenAsync(characterId, Guid.NewGuid())).Status);
        Assert.Equal(
            CharacterArtAccessStatus.NotFoundOrNotOwned,
            (await service.UploadAsync(
                characterId,
                "hidden.png",
                "image/png",
                new MemoryStream(ValidPng()))).Status);

        Assert.Equal(0, sheets.GetCount);
        Assert.Equal(0, art.ReadCount);
        Assert.Equal(0, art.MutationCount);
        Assert.Equal(0, storage.OpenCount);
        Assert.Equal(0, storage.WriteCount);
    }

    [Fact]
    public async Task ActiveUploadValidatesImageBytesBeforePersistingMetadata()
    {
        var characterId = Guid.NewGuid();
        var access = new FakeSiteAccessGateway(Authorized(characterId, SiteCharacterLifecycleState.Active));
        var sheets = new FakeSheetStore(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow.AddHours(-1)));
        var art = new FakeArtStore();
        var storage = new FakeArtStorage();
        var service = new CharacterArtService(access, sheets, art, storage, TimeProvider.System);

        var valid = await service.UploadAsync(
            characterId,
            "../portrait.png",
            "image/png",
            new MemoryStream(ValidPng()));

        Assert.Equal(CharacterArtAccessStatus.Ready, valid.Status);
        var saved = Assert.Single(valid.Assets!);
        Assert.Equal("portrait.png", saved.OriginalFileName);
        Assert.Equal("image/png", saved.ContentType);
        Assert.Equal(1, art.MutationCount);
        Assert.Equal(1, storage.WriteCount);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadAsync(
            characterId,
            "fake.png",
            "image/png",
            new MemoryStream([1, 2, 3, 4])));

        Assert.Equal(1, art.MutationCount);
        Assert.Equal(1, storage.WriteCount);
    }

    private static byte[] ValidPng() =>
        [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];

    private static CharacterArtAsset Asset(Guid characterId)
    {
        var id = Guid.NewGuid();
        return new CharacterArtAsset(
            id,
            characterId,
            $"{characterId:N}/{id:N}",
            "portrait.png",
            "image/png",
            3,
            DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    private static SiteCharacterAccessResult Authorized(
        Guid characterId,
        SiteCharacterLifecycleState lifecycle) =>
        SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
            characterId,
            "Fixture Character",
            lifecycle,
            lifecycle == SiteCharacterLifecycleState.Archived
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : null,
            []));

    private sealed class FakeSiteAccessGateway(SiteCharacterAccessResult result)
        : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId,
            CancellationToken cancellationToken = default)
        {
            _ = characterId;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeSheetStore(CharacterSheetRoot? root) : ICharacterSheetStore
    {
        public int GetCount { get; private set; }

        public Task<CharacterSheetRoot?> GetAsync(
            Guid characterId,
            CancellationToken cancellationToken = default)
        {
            GetCount++;
            return Task.FromResult(
                root?.CharacterId == characterId ? root : null);
        }

        public Task<CharacterSheetRoot> GetOrCreateAsync(
            Guid characterId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeArtStore(params CharacterArtAsset[] assets) : ICharacterArtStore
    {
        private readonly List<CharacterArtAsset> _assets = [.. assets];

        public int ReadCount { get; private set; }
        public int MutationCount { get; private set; }

        public Task<IReadOnlyList<CharacterArtAsset>> ListAsync(
            Guid characterId,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult<IReadOnlyList<CharacterArtAsset>>(
                _assets.Where(value => value.CharacterId == characterId).ToArray());
        }

        public Task<CharacterArtAsset?> GetAsync(
            Guid characterId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_assets.SingleOrDefault(value =>
                value.CharacterId == characterId && value.Id == assetId));
        }

        public Task<CharacterArtAsset> AddAsync(
            CharacterArtAsset asset,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            _assets.Add(asset);
            return Task.FromResult(asset);
        }

        public Task<CharacterArtAsset?> SetPortraitAsync(
            Guid characterId,
            Guid assetId,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            var selected = _assets.SingleOrDefault(value =>
                value.CharacterId == characterId && value.Id == assetId);
            if (selected is null) return Task.FromResult<CharacterArtAsset?>(null);
            foreach (var asset in _assets.Where(value => value.CharacterId == characterId))
                asset.SetPortrait(asset.Id == assetId, changedAt);
            return Task.FromResult<CharacterArtAsset?>(selected);
        }

        public Task ClearPortraitAsync(
            Guid characterId,
            DateTimeOffset changedAt,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            foreach (var asset in _assets.Where(value => value.CharacterId == characterId))
                asset.SetPortrait(false, changedAt);
            return Task.CompletedTask;
        }

        public Task<CharacterArtAsset?> DeleteAsync(
            Guid characterId,
            Guid assetId,
            CancellationToken cancellationToken = default)
        {
            MutationCount++;
            var selected = _assets.SingleOrDefault(value =>
                value.CharacterId == characterId && value.Id == assetId);
            if (selected is not null) _assets.Remove(selected);
            return Task.FromResult<CharacterArtAsset?>(selected);
        }
    }

    private sealed class FakeArtStorage : ICharacterArtStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public int OpenCount { get; private set; }
        public int WriteCount { get; private set; }
        public int DeleteCount { get; private set; }

        public async Task WriteAsync(
            string storageKey,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Files[storageKey] = copy.ToArray();
        }

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            return Task.FromResult<Stream?>(
                Files.TryGetValue(storageKey, out var bytes)
                    ? new MemoryStream(bytes, writable: false)
                    : null);
        }

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            Files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
}
