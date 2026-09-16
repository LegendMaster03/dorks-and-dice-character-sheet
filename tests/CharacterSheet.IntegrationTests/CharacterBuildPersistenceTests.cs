using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterBuildPersistenceTests
{
    [Fact]
    public async Task BuilderSelectionsPersistAcrossDbContextRecreationAndReplaceWithoutDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-build-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        var characterId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseSqlite(connectionString)
            .Options;

        try
        {
            await using (var firstContext = new CharacterSheetDbContext(options))
            {
                await firstContext.Database.MigrateAsync();
                var sheetStore = new SqliteCharacterSheetStore(firstContext);
                await sheetStore.GetOrCreateAsync(characterId);
                var buildStore = new SqliteCharacterBuildStore(firstContext);

                await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    "race:elf",
                    DateTimeOffset.UtcNow);
                await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    "race:dwarf",
                    DateTimeOffset.UtcNow.AddMinutes(1));
                await buildStore.SetStartingClassAsync(
                    characterId,
                    "class:fighter",
                    DateTimeOffset.UtcNow.AddMinutes(2));
                await buildStore.SetStartingClassAsync(
                    characterId,
                    "class:wizard",
                    DateTimeOffset.UtcNow.AddMinutes(3));

                Assert.Equal(1, await firstContext.FoundationalRuleSelections.CountAsync());
                Assert.Equal(1, await firstContext.CharacterAdvancementEntries.CountAsync());
            }

            await using (var secondContext = new CharacterSheetDbContext(options))
            {
                var buildStore = new SqliteCharacterBuildStore(secondContext);
                var persisted = await buildStore.GetAsync(characterId);

                Assert.NotNull(persisted);
                var race = Assert.Single(persisted.FoundationalSelections);
                Assert.Equal(CharacterFoundationalSelectionCategory.RaceSpecies, race.Category);
                Assert.Equal("race:dwarf", race.RuleConceptKey);
                var startingClass = Assert.Single(persisted.AdvancementEntries);
                Assert.Equal(CharacterAdvancementKind.Class, startingClass.Kind);
                Assert.Equal(0, startingClass.Ordinal);
                Assert.Equal("class:wizard", startingClass.RuleConceptKey);

                await buildStore.ClearFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    DateTimeOffset.UtcNow.AddMinutes(4));
                await buildStore.ClearStartingClassAsync(
                    characterId,
                    DateTimeOffset.UtcNow.AddMinutes(5));

                Assert.Empty((await buildStore.GetAsync(characterId))!.FoundationalSelections);
                Assert.Empty((await buildStore.GetAsync(characterId))!.AdvancementEntries);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CharacterDeletedCascadesAllBuilderRowsWhileCampaignDeletedDoesNot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-build-lifecycle-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var characterId = Guid.NewGuid();

        try
        {
            await using var db = new CharacterSheetDbContext(options);
            await db.Database.MigrateAsync();
            await new SqliteCharacterSheetStore(db).GetOrCreateAsync(characterId);
            var buildStore = new SqliteCharacterBuildStore(db);
            await buildStore.SetFoundationalSelectionAsync(
                characterId,
                CharacterFoundationalSelectionCategory.RaceSpecies,
                "race:elf",
                DateTimeOffset.UtcNow);
            await buildStore.SetStartingClassAsync(
                characterId,
                "class:fighter",
                DateTimeOffset.UtcNow);
            var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

            Assert.Equal(
                LifecycleProcessingStatus.Processed,
                await processor.ProcessAsync(new ToolLifecycleContext(
                    1,
                    "character-sheet",
                    Guid.NewGuid(),
                    ToolLifecycleEventTypes.CampaignDeleted,
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow)));
            Assert.Equal(1, await db.FoundationalRuleSelections.CountAsync());
            Assert.Equal(1, await db.CharacterAdvancementEntries.CountAsync());

            Assert.Equal(
                LifecycleProcessingStatus.Processed,
                await processor.ProcessAsync(new ToolLifecycleContext(
                    1,
                    "character-sheet",
                    Guid.NewGuid(),
                    ToolLifecycleEventTypes.CharacterDeleted,
                    characterId,
                    DateTimeOffset.UtcNow)));
            Assert.Equal(0, await db.CharacterSheets.CountAsync());
            Assert.Equal(0, await db.FoundationalRuleSelections.CountAsync());
            Assert.Equal(0, await db.CharacterAdvancementEntries.CountAsync());
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static void DeleteDatabase(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        }
        catch (IOException)
        {
        }
    }
}
