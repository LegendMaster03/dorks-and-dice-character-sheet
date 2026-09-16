using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterSheetPersistenceTests
{
    [Fact]
    public async Task RootUsesSiteCharacterIdAsPrimaryKeyAndCreationIsDurablyIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-sheet-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var characterId = Guid.NewGuid();

        try
        {
            var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
                .UseSqlite(connectionString)
                .Options;

            DateTimeOffset createdAt;
            await using (var firstContext = new CharacterSheetDbContext(options))
            {
                await firstContext.Database.MigrateAsync();
                var entityType = firstContext.Model.FindEntityType("CharacterSheet.Domain.Characters.CharacterSheetRoot");
                Assert.NotNull(entityType);
                var primaryKey = entityType.FindPrimaryKey();
                Assert.NotNull(primaryKey);
                var property = Assert.Single(primaryKey.Properties);
                Assert.Equal("CharacterId", property.Name);

                var store = new SqliteCharacterSheetStore(firstContext);
                var first = await store.GetOrCreateAsync(characterId);
                var second = await store.GetOrCreateAsync(characterId);

                Assert.Equal(characterId, first.CharacterId);
                Assert.Equal(characterId, second.CharacterId);
                Assert.Equal(first.CreatedAt, second.CreatedAt);
                createdAt = first.CreatedAt;
                Assert.Equal(1, await firstContext.CharacterSheets.CountAsync());
            }

            await using (var secondContext = new CharacterSheetDbContext(options))
            {
                var store = new SqliteCharacterSheetStore(secondContext);
                var persisted = await store.GetAsync(characterId);

                Assert.NotNull(persisted);
                Assert.Equal(characterId, persisted.CharacterId);
                Assert.Equal(createdAt, persisted.CreatedAt);
                Assert.Equal(1, await secondContext.CharacterSheets.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        }
    }
}
