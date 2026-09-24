using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using CharacterSheet.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterArtPersistenceTests
{
    [Fact]
    public async Task ArtMetadataPersistsAndPortraitDesignationMovesWithoutDuplicatingAssets()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var store = new PostgresCharacterArtStore(setup);
            var now = DateTimeOffset.UtcNow;

            await store.AddAsync(new CharacterArtAsset(
                firstId,
                characterId,
                $"{characterId:N}/{firstId:N}",
                "portrait.png",
                "image/png",
                42,
                now));
            await store.AddAsync(new CharacterArtAsset(
                secondId,
                characterId,
                $"{characterId:N}/{secondId:N}",
                "scene.webp",
                "image/webp",
                84,
                now.AddSeconds(1)));

            await store.SetPortraitAsync(characterId, firstId, now.AddMinutes(1));
            await store.SetPortraitAsync(characterId, secondId, now.AddMinutes(2));
        }

        await using (var verification = new CharacterSheetDbContext(options))
        {
            var assets = await new PostgresCharacterArtStore(verification).ListAsync(characterId);
            Assert.Equal(2, assets.Count);
            Assert.False(Assert.Single(assets, value => value.Id == firstId).IsPortrait);
            Assert.True(Assert.Single(assets, value => value.Id == secondId).IsPortrait);
            Assert.Equal(2, await verification.CharacterArtAssets.CountAsync());
        }
    }

    [Fact]
    public async Task FileStoragePersistsBytesAndDeletesOnlyTheRequestedObject()
    {
        var root = Path.Combine(Path.GetTempPath(), $"character-art-test-{Guid.NewGuid():N}");
        try
        {
            var storage = new FileSystemCharacterArtStorage(root);
            var firstKey = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";
            var secondKey = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}";

            await storage.WriteAsync(firstKey, new MemoryStream([1, 2, 3, 4]));
            await storage.WriteAsync(secondKey, new MemoryStream([5, 6, 7]));

            await using (var read = await storage.OpenReadAsync(firstKey))
            {
                Assert.NotNull(read);
                using var copy = new MemoryStream();
                await read.CopyToAsync(copy);
                Assert.Equal([1, 2, 3, 4], copy.ToArray());
            }

            await storage.DeleteAsync(firstKey);
            Assert.Null(await storage.OpenReadAsync(firstKey));

            await using var second = await storage.OpenReadAsync(secondKey);
            Assert.NotNull(second);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
