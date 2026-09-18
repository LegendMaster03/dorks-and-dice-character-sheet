using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class SubclassSelectionPersistenceTests
{
    [Fact]
    public async Task SubclassPersistsAsCharacterOwnedChildOfClassAndIsRemovedWithParentClass()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid startingClassId;

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var store = new PostgresCharacterBuildStore(setup);
            var withClass = await store.SetStartingClassAsync(
                characterId,
                "class.wizard",
                DateTimeOffset.UtcNow);
            startingClassId = Assert.Single(withClass!.AdvancementEntries).Id;

            var withSubclass = await store.SetSubclassAsync(
                characterId,
                startingClassId,
                "subclass.wizard.evocation",
                DateTimeOffset.UtcNow.AddMinutes(1));
            var subclass = Assert.Single(
                withSubclass!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);
            Assert.Equal(startingClassId, subclass.ParentAdvancementEntryId);
        }

        await using (var verify = new CharacterSheetDbContext(options))
        {
            var store = new PostgresCharacterBuildStore(verify);
            var persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            var subclass = Assert.Single(
                persisted!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);
            Assert.Equal(startingClassId, subclass.ParentAdvancementEntryId);
            Assert.Equal("subclass.wizard.evocation", subclass.RuleConceptKey);

            await store.SetStartingClassAsync(
                characterId,
                "class.fighter",
                DateTimeOffset.UtcNow.AddMinutes(2));
            persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            Assert.DoesNotContain(
                persisted!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);

            var fighter = Assert.Single(
                persisted.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Class);
            await store.SetSubclassAsync(
                characterId,
                fighter.Id,
                "subclass.fighter.champion",
                DateTimeOffset.UtcNow.AddMinutes(3));
            await store.ClearStartingClassAsync(
                characterId,
                DateTimeOffset.UtcNow.AddMinutes(4));

            persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            Assert.Empty(persisted!.AdvancementEntries);
        }
    }

    [Fact]
    public async Task SubclassUniqueIndexRejectsConcurrentWritersAndAllowsOneSubclassPerDistinctClass()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid startingClassId;

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var store = new PostgresCharacterBuildStore(setup);
            var withClass = await store.SetStartingClassAsync(
                characterId,
                "class.wizard",
                DateTimeOffset.UtcNow);
            startingClassId = Assert.Single(
                withClass!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Class).Id;

            var indexSql = await ExecuteScalarStringAsync(
                setup,
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'UX_character_advancement_entries_SubclassPerClass';");
            Assert.NotNull(indexSql);
            Assert.Contains("UNIQUE INDEX", indexSql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("(\"CharacterId\", \"ParentAdvancementEntryId\")", indexSql, StringComparison.Ordinal);
            Assert.Contains("\"Kind\"", indexSql, StringComparison.Ordinal);
            Assert.Contains("Subclass", indexSql, StringComparison.Ordinal);
            Assert.Contains("\"ParentAdvancementEntryId\" IS NOT NULL", indexSql, StringComparison.Ordinal);
        }

        await using (var writerA = new CharacterSheetDbContext(options))
        await using (var writerB = new CharacterSheetDbContext(options))
        {
            var rootA = await writerA.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);
            var rootB = await writerB.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);

            rootA.SetSubclassForClass(
                startingClassId,
                "subclass.wizard.evocation",
                DateTimeOffset.UtcNow.AddMinutes(1));
            rootB.SetSubclassForClass(
                startingClassId,
                "subclass.wizard.abjuration",
                DateTimeOffset.UtcNow.AddMinutes(2));

            await writerA.SaveChangesAsync();
            await Assert.ThrowsAsync<DbUpdateException>(() => writerB.SaveChangesAsync());
        }

        Guid secondClassId;
        await using (var addSecondClass = new CharacterSheetDbContext(options))
        {
            var root = await addSecondClass.CharacterSheets
                .Include(value => value.AdvancementEntries)
                .SingleAsync(value => value.CharacterId == characterId);
            var secondClass = root.AddAdvancement(
                CharacterAdvancementKind.Class,
                "class.fighter",
                1,
                null,
                DateTimeOffset.UtcNow.AddMinutes(3));
            secondClassId = secondClass.Id;
            root.SetSubclassForClass(
                secondClassId,
                "subclass.fighter.champion",
                DateTimeOffset.UtcNow.AddMinutes(4));
            await addSecondClass.SaveChangesAsync();
        }

        await using var verify = new CharacterSheetDbContext(options);
        var subclasses = await verify.CharacterAdvancementEntries
            .Where(value =>
                value.CharacterId == characterId &&
                value.Kind == CharacterAdvancementKind.Subclass)
            .ToListAsync();

        Assert.Equal(2, subclasses.Count);
        Assert.Single(subclasses, value =>
            value.ParentAdvancementEntryId == startingClassId &&
            value.RuleConceptKey == "subclass.wizard.evocation");
        Assert.Single(subclasses, value =>
            value.ParentAdvancementEntryId == secondClassId &&
            value.RuleConceptKey == "subclass.fighter.champion");
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
