using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class AbilityScoreInputPersistenceTests
{
    [Fact]
    public async Task InputsPersistAcrossContextsReplaceWithoutDuplicatesAndClearIndependently()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterAId = Guid.NewGuid();
        var characterBId = Guid.NewGuid();
        Guid originalStrengthId;
        DateTimeOffset originalStrengthCreatedAt;

        await using (var first = new CharacterSheetDbContext(options))
        {
            await first.Database.MigrateAsync();
            var sheetStore = new PostgresCharacterSheetStore(first);
            await sheetStore.GetOrCreateAsync(characterAId);
            await sheetStore.GetOrCreateAsync(characterBId);
            var buildStore = new PostgresCharacterBuildStore(first);

            var afterFirst = await buildStore.SetBaseAbilityScoreInputAsync(
                characterAId,
                " STRENGTH ",
                15,
                DateTimeOffset.UtcNow);
            var strength = Assert.Single(afterFirst!.BaseAbilityScoreInputs);
            originalStrengthId = strength.Id;
            originalStrengthCreatedAt = strength.CreatedAt;

            await buildStore.SetBaseAbilityScoreInputAsync(
                characterAId,
                "dexterity",
                14,
                DateTimeOffset.UtcNow.AddSeconds(1));
            await buildStore.SetBaseAbilityScoreInputAsync(
                characterAId,
                "strength",
                18,
                DateTimeOffset.UtcNow.AddSeconds(2));
            await buildStore.SetBaseAbilityScoreInputAsync(
                characterBId,
                "strength",
                8,
                DateTimeOffset.UtcNow.AddSeconds(3));

            Assert.Equal(3, await first.BaseAbilityScoreInputs.CountAsync());
            Assert.Equal(
                2,
                await first.BaseAbilityScoreInputs.CountAsync(value => value.CharacterId == characterAId));
            Assert.Equal(
                1,
                await first.BaseAbilityScoreInputs.CountAsync(value => value.CharacterId == characterBId));
        }

        await using (var second = new CharacterSheetDbContext(options))
        {
            var buildStore = new PostgresCharacterBuildStore(second);
            var characterA = await buildStore.GetAsync(characterAId);
            Assert.NotNull(characterA);
            Assert.Equal(2, characterA.BaseAbilityScoreInputs.Count);

            var strength = Assert.Single(
                characterA.BaseAbilityScoreInputs,
                value => value.AbilityKey == CharacterAbilityKey.Strength);
            Assert.Equal(originalStrengthId, strength.Id);
            Assert.Equal(originalStrengthCreatedAt.Ticks / 10, strength.CreatedAt.Ticks / 10);
            Assert.Equal(18, strength.Score);

            var dexterity = Assert.Single(
                characterA.BaseAbilityScoreInputs,
                value => value.AbilityKey == CharacterAbilityKey.Dexterity);
            Assert.Equal(14, dexterity.Score);

            await buildStore.ClearBaseAbilityScoreInputAsync(
                characterAId,
                "DEXTERITY",
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var third = new CharacterSheetDbContext(options))
        {
            var buildStore = new PostgresCharacterBuildStore(third);
            var characterA = await buildStore.GetAsync(characterAId);
            var characterB = await buildStore.GetAsync(characterBId);
            Assert.NotNull(characterA);
            Assert.NotNull(characterB);

            var remainingA = Assert.Single(characterA.BaseAbilityScoreInputs);
            Assert.Equal(CharacterAbilityKey.Strength, remainingA.AbilityKey);
            Assert.Equal(18, remainingA.Score);

            var remainingB = Assert.Single(characterB.BaseAbilityScoreInputs);
            Assert.Equal(CharacterAbilityKey.Strength, remainingB.AbilityKey);
            Assert.Equal(8, remainingB.Score);
        }
    }

    [Fact]
    public async Task PostgreSqlUniqueIndexRejectsConcurrentCurrentRowsForSameCharacterAndAbility()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);

            var indexDefinition = await ExecuteScalarStringAsync(
                setup,
                "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexname = 'IX_character_base_ability_score_inputs_CharacterId_AbilityKey';");
            Assert.NotNull(indexDefinition);
            Assert.Contains("UNIQUE INDEX", indexDefinition, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"CharacterId\"", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("\"AbilityKey\"", indexDefinition, StringComparison.Ordinal);
        }

        await using var writerA = new CharacterSheetDbContext(options);
        await using var writerB = new CharacterSheetDbContext(options);
        var rootA = await writerA.CharacterSheets
            .Include(value => value.BaseAbilityScoreInputs)
            .SingleAsync(value => value.CharacterId == characterId);
        var rootB = await writerB.CharacterSheets
            .Include(value => value.BaseAbilityScoreInputs)
            .SingleAsync(value => value.CharacterId == characterId);

        rootA.SetBaseAbilityScoreInput("strength", 15, DateTimeOffset.UtcNow);
        rootB.SetBaseAbilityScoreInput("strength", 16, DateTimeOffset.UtcNow.AddMilliseconds(1));

        await writerA.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => writerB.SaveChangesAsync());

        await using var verification = new CharacterSheetDbContext(options);
        var rows = await verification.BaseAbilityScoreInputs
            .Where(value => value.CharacterId == characterId
                && value.AbilityKey == CharacterAbilityKey.Strength)
            .ToArrayAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task CharacterDeletionCascadesInputsWhileCampaignDeletionPreservesThem()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            var root = await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            root.SetBaseAbilityScoreInput("strength", 15, DateTimeOffset.UtcNow);
            root.SetBaseAbilityScoreInput("wisdom", 12, DateTimeOffset.UtcNow.AddSeconds(1));
            await setup.SaveChangesAsync();

            var processor = new CharacterSheetLifecycleProcessor(setup, TimeProvider.System);
            Assert.Equal(
                LifecycleProcessingStatus.Processed,
                await processor.ProcessAsync(new ToolLifecycleContext(
                    1,
                    "character-sheet",
                    Guid.NewGuid(),
                    ToolLifecycleEventTypes.CampaignDeleted,
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow)));

            Assert.Equal(1, await setup.CharacterSheets.CountAsync());
            Assert.Equal(2, await setup.BaseAbilityScoreInputs.CountAsync());
        }

        await using (var deletion = new CharacterSheetDbContext(options))
        {
            var processor = new CharacterSheetLifecycleProcessor(deletion, TimeProvider.System);
            Assert.Equal(
                LifecycleProcessingStatus.Processed,
                await processor.ProcessAsync(new ToolLifecycleContext(
                    1,
                    "character-sheet",
                    Guid.NewGuid(),
                    ToolLifecycleEventTypes.CharacterDeleted,
                    characterId,
                    DateTimeOffset.UtcNow)));

            Assert.Equal(0, await deletion.CharacterSheets.CountAsync());
            Assert.Equal(0, await deletion.BaseAbilityScoreInputs.CountAsync());
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
