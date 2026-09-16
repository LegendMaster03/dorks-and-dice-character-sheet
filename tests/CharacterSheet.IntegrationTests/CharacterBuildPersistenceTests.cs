using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
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
                    "  Race:ELF  ",
                    DateTimeOffset.UtcNow);
                await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    "RACE:elf",
                    DateTimeOffset.UtcNow.AddSeconds(30));
                await buildStore.SetFoundationalSelectionAsync(
                    characterId,
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    "race:dwarf",
                    DateTimeOffset.UtcNow.AddMinutes(1));
                await buildStore.SetStartingClassAsync(
                    characterId,
                    "  CLASS:FIGHTER  ",
                    DateTimeOffset.UtcNow.AddMinutes(2));
                await buildStore.SetStartingClassAsync(
                    characterId,
                    "Class:Fighter",
                    DateTimeOffset.UtcNow.AddMinutes(2).AddSeconds(30));
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
    public async Task StartingClassUniqueIndexRejectsConcurrentWritersAndAllowsLaterProgression()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-build-starting-class-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var characterId = Guid.NewGuid();

        try
        {
            await using (var setup = new CharacterSheetDbContext(options))
            {
                await setup.Database.MigrateAsync();
                await new SqliteCharacterSheetStore(setup).GetOrCreateAsync(characterId);

                var indexSql = await ExecuteScalarStringAsync(
                    setup,
                    "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'UX_character_advancement_entries_StartingClass';");
                Assert.NotNull(indexSql);
                Assert.Contains("UNIQUE INDEX", indexSql, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("\"Kind\" = 'Class'", indexSql, StringComparison.Ordinal);
                Assert.Contains("\"Ordinal\" = 0", indexSql, StringComparison.Ordinal);
                Assert.Contains("\"ParentAdvancementEntryId\" IS NULL", indexSql, StringComparison.Ordinal);
            }

            await using var writerA = new CharacterSheetDbContext(options);
            await using var writerB = new CharacterSheetDbContext(options);
            var rootA = await writerA.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);
            var rootB = await writerB.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);

            rootA.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);
            rootB.SetStartingClass("class:wizard", DateTimeOffset.UtcNow.AddSeconds(1));
            await writerA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateException>(() => writerB.SaveChangesAsync());

            await using var later = new CharacterSheetDbContext(options);
            var persisted = await later.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);
            persisted.AddAdvancement(
                CharacterAdvancementKind.Class,
                "class:wizard",
                1,
                null,
                DateTimeOffset.UtcNow.AddMinutes(1));
            persisted.AddAdvancement(
                CharacterAdvancementKind.PrestigeClass,
                "prestigeclass:arcane-archer",
                2,
                null,
                DateTimeOffset.UtcNow.AddMinutes(2));
            await later.SaveChangesAsync();

            Assert.Equal(
                2,
                await later.CharacterAdvancementEntries.CountAsync(value =>
                    value.CharacterId == characterId && value.Kind == CharacterAdvancementKind.Class));
            Assert.Equal(
                1,
                await later.CharacterAdvancementEntries.CountAsync(value =>
                    value.CharacterId == characterId && value.Kind == CharacterAdvancementKind.PrestigeClass));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task DatabaseRejectsCrossCharacterAdvancementParent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-build-parent-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var characterAId = Guid.NewGuid();
        var characterBId = Guid.NewGuid();

        try
        {
            await using var db = new CharacterSheetDbContext(options);
            await db.Database.MigrateAsync();
            var sheetStore = new SqliteCharacterSheetStore(db);
            var characterA = await sheetStore.GetOrCreateAsync(characterAId);
            await sheetStore.GetOrCreateAsync(characterBId);
            var parent = characterA.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();

            var now = DateTimeOffset.UtcNow.AddMinutes(1);
            await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO character_advancement_entries
                    (Id, CharacterId, Kind, RuleConceptKey, Ordinal, ParentAdvancementEntryId, CreatedAt, UpdatedAt)
                VALUES
                    ({Guid.NewGuid()}, {characterBId}, {"Subclass"}, {"subclass:evoker"}, {1}, {parent.Id}, {now}, {now});
                """));

            Assert.Equal(
                0,
                await db.CharacterAdvancementEntries.CountAsync(value => value.CharacterId == characterBId));
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    [Fact]
    public async Task CharacterDeletedCascadesParentedAdvancementGraphAndOtherBuilderRowsWhileCampaignDeletedDoesNot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"character-build-lifecycle-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<CharacterSheetDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        var characterId = Guid.NewGuid();
        var characterDeletedEventId = Guid.NewGuid();

        try
        {
            await using (var setup = new CharacterSheetDbContext(options))
            {
                await setup.Database.MigrateAsync();
                var root = await new SqliteCharacterSheetStore(setup).GetOrCreateAsync(characterId);
                root.SetFoundationalSelection(
                    CharacterFoundationalSelectionCategory.RaceSpecies,
                    "race:elf",
                    DateTimeOffset.UtcNow);
                var parentClass = root.SetStartingClass(
                    "class:fighter",
                    DateTimeOffset.UtcNow.AddMinutes(1));
                root.AddAdvancement(
                    CharacterAdvancementKind.Subclass,
                    "subclass:champion",
                    null,
                    parentClass.Id,
                    DateTimeOffset.UtcNow.AddMinutes(2));
                root.AddAdvancement(
                    CharacterAdvancementKind.Feat,
                    "feat:alert",
                    null,
                    null,
                    DateTimeOffset.UtcNow.AddMinutes(3));
                await setup.SaveChangesAsync();

                var campaignProcessor = new CharacterSheetLifecycleProcessor(setup, TimeProvider.System);
                Assert.Equal(
                    LifecycleProcessingStatus.Processed,
                    await campaignProcessor.ProcessAsync(new ToolLifecycleContext(
                        1,
                        "character-sheet",
                        Guid.NewGuid(),
                        ToolLifecycleEventTypes.CampaignDeleted,
                        Guid.NewGuid(),
                        DateTimeOffset.UtcNow)));
                Assert.Equal(1, await setup.CharacterSheets.CountAsync());
                Assert.Equal(1, await setup.FoundationalRuleSelections.CountAsync());
                Assert.Equal(3, await setup.CharacterAdvancementEntries.CountAsync());
            }

            await using (var deletion = new CharacterSheetDbContext(options))
            {
                var processor = new CharacterSheetLifecycleProcessor(deletion, TimeProvider.System);
                Assert.Equal(
                    LifecycleProcessingStatus.Processed,
                    await processor.ProcessAsync(new ToolLifecycleContext(
                        1,
                        "character-sheet",
                        characterDeletedEventId,
                        ToolLifecycleEventTypes.CharacterDeleted,
                        characterId,
                        DateTimeOffset.UtcNow)));

                Assert.Equal(0, await deletion.CharacterSheets.CountAsync());
                Assert.Equal(0, await deletion.FoundationalRuleSelections.CountAsync());
                Assert.Equal(0, await deletion.CharacterAdvancementEntries.CountAsync());

                var recorded = await deletion.ProcessedLifecycleEvents
                    .SingleAsync(value => value.EventId == characterDeletedEventId);
                Assert.Equal(ToolLifecycleEventTypes.CharacterDeleted, recorded.EventType);
                Assert.Equal(characterId, recorded.SubjectId);
            }
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static async Task<string?> ExecuteScalarStringAsync(CharacterSheetDbContext db, string sql)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync() as string;
        }
        finally
        {
            await connection.CloseAsync();
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
