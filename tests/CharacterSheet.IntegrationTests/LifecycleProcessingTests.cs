using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class LifecycleProcessingTests
{
    [Fact]
    public async Task CharacterDeletedPermanentlyRemovesRootAndRecordsInboxEvent()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var characterId = Guid.NewGuid();
        db.CharacterSheets.Add(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var lifecycleEvent = Event(ToolLifecycleEventTypes.CharacterDeleted, characterId);
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        var result = await processor.ProcessAsync(lifecycleEvent);

        Assert.Equal(LifecycleProcessingStatus.Processed, result);
        Assert.False(await db.CharacterSheets.AnyAsync(item => item.CharacterId == characterId));
        var processed = Assert.Single(await db.ProcessedLifecycleEvents.ToListAsync());
        Assert.Equal(lifecycleEvent.EventId, processed.EventId);
        Assert.Equal(ToolLifecycleEventTypes.CharacterDeleted, processed.EventType);
        Assert.Equal(characterId, processed.SubjectId);
    }

    [Fact]
    public async Task CharacterDeletedSucceedsWhenNoLocalRootExists()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var lifecycleEvent = Event(ToolLifecycleEventTypes.CharacterDeleted, Guid.NewGuid());
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        var result = await processor.ProcessAsync(lifecycleEvent);

        Assert.Equal(LifecycleProcessingStatus.Processed, result);
        Assert.True(await db.ProcessedLifecycleEvents.AnyAsync(item => item.EventId == lifecycleEvent.EventId));
    }

    [Fact]
    public async Task DuplicateEventIdDoesNotRepeatCharacterCleanup()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var characterId = Guid.NewGuid();
        db.CharacterSheets.Add(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var lifecycleEvent = Event(ToolLifecycleEventTypes.CharacterDeleted, characterId);
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        Assert.Equal(LifecycleProcessingStatus.Processed, await processor.ProcessAsync(lifecycleEvent));

        db.CharacterSheets.Add(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        Assert.Equal(LifecycleProcessingStatus.AlreadyProcessed, await processor.ProcessAsync(lifecycleEvent));
        Assert.True(await db.CharacterSheets.AnyAsync(item => item.CharacterId == characterId));
        Assert.Equal(1, await db.ProcessedLifecycleEvents.CountAsync(item => item.EventId == lifecycleEvent.EventId));
    }

    [Fact]
    public async Task CleanupAndInboxRecordingRollBackTogether()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var characterId = Guid.NewGuid();
        db.CharacterSheets.Add(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION fail_lifecycle_inbox_insert()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'forced lifecycle inbox failure';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER fail_lifecycle_inbox
            BEFORE INSERT ON processed_lifecycle_events
            FOR EACH ROW
            EXECUTE FUNCTION fail_lifecycle_inbox_insert();
            """);
        var lifecycleEvent = Event(ToolLifecycleEventTypes.CharacterDeleted, characterId);
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        await Assert.ThrowsAsync<DbUpdateException>(() => processor.ProcessAsync(lifecycleEvent));

        db.ChangeTracker.Clear();
        Assert.True(await db.CharacterSheets.AnyAsync(item => item.CharacterId == characterId));
        Assert.False(await db.ProcessedLifecycleEvents.AnyAsync(item => item.EventId == lifecycleEvent.EventId));
    }

    [Fact]
    public async Task CampaignDeletedDoesNotDeleteBaseCharacterRootAndIsDurablyRecorded()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var characterId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        db.CharacterSheets.Add(new CharacterSheetRoot(characterId, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var lifecycleEvent = Event(ToolLifecycleEventTypes.CampaignDeleted, campaignId);
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        var result = await processor.ProcessAsync(lifecycleEvent);

        Assert.Equal(LifecycleProcessingStatus.Processed, result);
        Assert.True(await db.CharacterSheets.AnyAsync(item => item.CharacterId == characterId));
        var processed = Assert.Single(await db.ProcessedLifecycleEvents.ToListAsync());
        Assert.Equal(ToolLifecycleEventTypes.CampaignDeleted, processed.EventType);
        Assert.Equal(campaignId, processed.SubjectId);
    }

    [Fact]
    public async Task UnsupportedEventTypeIsNotMarkedProcessed()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using var db = await CreateDbAsync(database);
        var lifecycleEvent = Event("future.event", Guid.NewGuid());
        var processor = new CharacterSheetLifecycleProcessor(db, TimeProvider.System);

        var result = await processor.ProcessAsync(lifecycleEvent);

        Assert.Equal(LifecycleProcessingStatus.Unsupported, result);
        Assert.Empty(await db.ProcessedLifecycleEvents.ToListAsync());
    }

    private static ToolLifecycleContext Event(string eventType, Guid subjectId) => new(
        1,
        "character-sheet",
        Guid.NewGuid(),
        eventType,
        subjectId,
        DateTimeOffset.UtcNow);

    private static async Task<CharacterSheetDbContext> CreateDbAsync(PostgresTestDatabase database)
    {
        var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        return db;
    }
}
