using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterBuildPersistenceTests
{
    [Fact]
    public async Task BuilderSelectionsPersistAcrossDbContextRecreationAndReplaceWithoutDuplicates()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            var sheetStore = new PostgresCharacterSheetStore(firstContext);
            await sheetStore.GetOrCreateAsync(characterId);
            var buildStore = new PostgresCharacterBuildStore(firstContext);

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
            var buildStore = new PostgresCharacterBuildStore(secondContext);
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

    [Fact]
    public async Task FeatOccurrencesPersistAcrossDbContextRecreationAndDuplicateConceptsRemainDistinct()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid firstAlertId;
        Guid secondAlertId;

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(firstContext).GetOrCreateAsync(characterId);
            var buildStore = new PostgresCharacterBuildStore(firstContext);

            await buildStore.AddFeatOccurrenceAsync(characterId, "  FEAT:ALERT  ", DateTimeOffset.UtcNow);
            await buildStore.AddFeatOccurrenceAsync(characterId, "feat:alert", DateTimeOffset.UtcNow.AddSeconds(1));
            await buildStore.AddFeatOccurrenceAsync(characterId, "feat:tough", DateTimeOffset.UtcNow.AddSeconds(2));

            var feats = (await buildStore.GetAsync(characterId))!.AdvancementEntries
                .Where(value => value.Kind == CharacterAdvancementKind.Feat)
                .ToArray();
            Assert.Equal(3, feats.Length);
            var alerts = feats.Where(value => value.RuleConceptKey == "feat:alert").ToArray();
            Assert.Equal(2, alerts.Length);
            firstAlertId = alerts[0].Id;
            secondAlertId = alerts[1].Id;
            Assert.NotEqual(firstAlertId, secondAlertId);
        }

        await using (var secondContext = new CharacterSheetDbContext(options))
        {
            var buildStore = new PostgresCharacterBuildStore(secondContext);
            var persisted = await buildStore.GetAsync(characterId);
            Assert.NotNull(persisted);

            var feats = persisted.AdvancementEntries
                .Where(value => value.Kind == CharacterAdvancementKind.Feat)
                .ToArray();
            Assert.Equal(3, feats.Length);
            Assert.All(feats, value =>
            {
                Assert.Null(value.Ordinal);
                Assert.Null(value.ParentAdvancementEntryId);
            });
            Assert.Contains(feats, value => value.Id == firstAlertId);
            Assert.Contains(feats, value => value.Id == secondAlertId);

            await buildStore.RemoveFeatOccurrenceAsync(
                characterId,
                firstAlertId,
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var thirdContext = new CharacterSheetDbContext(options))
        {
            var persisted = await new PostgresCharacterBuildStore(thirdContext).GetAsync(characterId);
            Assert.NotNull(persisted);
            var feats = persisted.AdvancementEntries
                .Where(value => value.Kind == CharacterAdvancementKind.Feat)
                .ToArray();
            Assert.Equal(2, feats.Length);
            Assert.DoesNotContain(feats, value => value.Id == firstAlertId);
            Assert.Contains(feats, value => value.Id == secondAlertId);
        }
    }

    [Fact]
    public async Task ConcurrentWritersMayAddTheSameFeatConceptAsDistinctOccurrences()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
        }

        await using var writerA = new CharacterSheetDbContext(options);
        await using var writerB = new CharacterSheetDbContext(options);
        var rootA = await writerA.CharacterSheets
            .Include(value => value.AdvancementEntries)
            .SingleAsync(value => value.CharacterId == characterId);
        var rootB = await writerB.CharacterSheets
            .Include(value => value.AdvancementEntries)
            .SingleAsync(value => value.CharacterId == characterId);

        var featA = rootA.AddFeatOccurrence("feat:alert", DateTimeOffset.UtcNow);
        var featB = rootB.AddFeatOccurrence("feat:alert", DateTimeOffset.UtcNow.AddMilliseconds(1));

        await Task.WhenAll(writerA.SaveChangesAsync(), writerB.SaveChangesAsync());

        await using var verification = new CharacterSheetDbContext(options);
        var feats = await verification.CharacterAdvancementEntries
            .Where(value => value.CharacterId == characterId
                && value.Kind == CharacterAdvancementKind.Feat)
            .ToArrayAsync();

        Assert.Equal(2, feats.Length);
        Assert.NotEqual(featA.Id, featB.Id);
        Assert.All(feats, value => Assert.Equal("feat:alert", value.RuleConceptKey));
    }

    [Fact]
    public async Task StartingClassUniqueIndexRejectsConcurrentWritersAndAllowsLaterProgression()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);

            var indexSql = await ExecuteScalarStringAsync(
                setup,
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'UX_character_advancement_entries_StartingClass';");
            Assert.NotNull(indexSql);
            Assert.Contains("UNIQUE INDEX", indexSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Kind\"", indexSql, StringComparison.Ordinal);
            Assert.Contains("\"Ordinal\"", indexSql, StringComparison.Ordinal);
            Assert.Contains("\"ParentAdvancementEntryId\"", indexSql, StringComparison.Ordinal);
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

    [Fact]
    public async Task DatabaseRejectsCrossCharacterAdvancementParent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterAId = Guid.NewGuid();
        var characterBId = Guid.NewGuid();

        await using var db = new CharacterSheetDbContext(options);
        await db.Database.MigrateAsync();
        var sheetStore = new PostgresCharacterSheetStore(db);
        var characterA = await sheetStore.GetOrCreateAsync(characterAId);
        await sheetStore.GetOrCreateAsync(characterBId);
        var parent = characterA.SetStartingClass("class:fighter", DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_advancement_entries
                ("Id", "CharacterId", "Kind", "RuleConceptKey", "Ordinal", "ParentAdvancementEntryId", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {characterBId}, {"Subclass"}, {"subclass:evoker"}, {1}, {parent.Id}, {now}, {now});
            """));

        Assert.Equal(
            0,
            await db.CharacterAdvancementEntries.CountAsync(value => value.CharacterId == characterBId));
    }

    [Fact]
    public async Task CharacterDeletedCascadesParentedAdvancementGraphAndOtherBuilderRowsWhileCampaignDeletedDoesNot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        var characterDeletedEventId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var root = await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
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
            root.AddFeatOccurrence(
                "feat:alert",
                DateTimeOffset.UtcNow.AddMinutes(3));
            root.AddFeatOccurrence(
                "feat:alert",
                DateTimeOffset.UtcNow.AddMinutes(4));
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
            Assert.Equal(4, await setup.CharacterAdvancementEntries.CountAsync());
            Assert.Equal(
                2,
                await setup.CharacterAdvancementEntries.CountAsync(value =>
                    value.Kind == CharacterAdvancementKind.Feat));
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
}
