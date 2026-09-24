using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Lifecycle;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class ProcessedLifecycleEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public Guid SubjectId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public sealed class CharacterSheetLifecycleProcessor(
    CharacterSheetDbContext dbContext,
    ICharacterArtStorage artStorage,
    TimeProvider timeProvider) : ICharacterSheetLifecycleProcessor
{
    public async Task<LifecycleProcessingStatus> ProcessAsync(
        ToolLifecycleContext lifecycleEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycleEvent);

        if (!ToolLifecycleEventTypes.IsSupported(lifecycleEvent.EventType))
        {
            return LifecycleProcessingStatus.Unsupported;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (await dbContext.ProcessedLifecycleEvents.AnyAsync(
                    item => item.EventId == lifecycleEvent.EventId,
                    cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return LifecycleProcessingStatus.AlreadyProcessed;
            }

            switch (lifecycleEvent.EventType)
            {
                case ToolLifecycleEventTypes.CharacterDeleted:
                    await DeleteCharacterOwnedStateAsync(lifecycleEvent.SubjectId, cancellationToken);
                    break;
                case ToolLifecycleEventTypes.CampaignDeleted:
                    await DeleteCampaignScopedStateAsync(lifecycleEvent.SubjectId, cancellationToken);
                    break;
                default:
                    await transaction.RollbackAsync(cancellationToken);
                    return LifecycleProcessingStatus.Unsupported;
            }

            dbContext.ProcessedLifecycleEvents.Add(new ProcessedLifecycleEvent
            {
                EventId = lifecycleEvent.EventId,
                EventType = lifecycleEvent.EventType,
                SubjectId = lifecycleEvent.SubjectId,
                ProcessedAt = timeProvider.GetUtcNow()
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return LifecycleProcessingStatus.Processed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task DeleteCharacterOwnedStateAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var artKeys = await dbContext.CharacterArtAssets
            .Where(item => item.CharacterId == characterId)
            .Select(item => item.StorageKey)
            .ToArrayAsync(cancellationToken);
        foreach (var storageKey in artKeys)
        {
            await artStorage.DeleteAsync(storageKey, cancellationToken);
        }

        var root = await dbContext.CharacterSheets.SingleOrDefaultAsync(
            item => item.CharacterId == characterId,
            cancellationToken);
        if (root is not null)
        {
            dbContext.CharacterSheets.Remove(root);
        }

        // Future Character-owned tables belong behind this boundary so the lifecycle wire
        // contract remains unchanged as Character Sheet persistence grows.
    }

    private static Task DeleteCampaignScopedStateAsync(
        Guid campaignId,
        CancellationToken cancellationToken)
    {
        _ = campaignId;
        _ = cancellationToken;

        // No campaign-scoped Character Sheet rows exist yet. Future state keyed by
        // (CharacterId, CampaignId, ModuleKey) belongs behind this boundary. Base character
        // sheet roots must not be deleted by campaign lifecycle events.
        return Task.CompletedTask;
    }
}
