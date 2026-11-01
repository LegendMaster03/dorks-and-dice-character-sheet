using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterStateStore(CharacterSheetDbContext dbContext)
    : ICharacterStateStore
{
    public Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        BuildQuery(tracking: false)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    public async Task<CharacterSheetRoot?> SetAdvancementProgressAsync(
        Guid characterId,
        int? value,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;

        root.SetAdvancementProgress(value, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetCurrencyBalanceAsync(
        Guid characterId,
        string currencyKey,
        long amount,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;

        root.SetCurrencyBalance(currencyKey, amount, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveCurrencyBalanceAsync(
        Guid characterId,
        string currencyKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;

        root.RemoveCurrencyBalance(currencyKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetProfileAsync(
        Guid characterId,
        string? alignment,
        string? deity,
        string? age,
        string? height,
        string? weight,
        string? appearance,
        string? personalityTraits,
        string? ideals,
        string? bonds,
        string? flaws,
        string? backstory,
        string? alliesAndOrganizations,
        string? symbol,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;

        root.SetProfile(
            alignment,
            deity,
            age,
            height,
            weight,
            appearance,
            personalityTraits,
            ideals,
            bonds,
            flaws,
            backstory,
            alliesAndOrganizations,
            symbol,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetCurrentHitPointsAsync(
        Guid characterId,
        int? currentHitPoints,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.SetCurrentHitPoints(currentHitPoints, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetDeathSavesAsync(
        Guid characterId,
        int successes,
        int failures,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.SetDeathSaves(successes, failures, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ApplyIntegerStateMutationsAsync(
        Guid characterId,
        IReadOnlyList<CharacterIntegerStateMutation> mutations,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutations);
        // Resource transitions already evaluated by Rules Core must compare
        // against persisted PostgreSQL state, not merely an EF-tracked snapshot.
        // ExecuteUpdate compiles to a conditional UPDATE and obtains the row lock
        // before testing the expected current value. Concurrent requests using
        // the same expected value can therefore never both commit.
        if (mutations.Count == 1 &&
            mutations[0] is {
                Target: CharacterIntegerStateMutationTarget.Resource,
                Operation: CharacterIntegerStateMutationOperation.Set,
                ExpectedValue: int expected
            } guarded)
        {
            var key = CharacterRulesInputKey.Normalize(guarded.ResourceKey
                ?? throw new ArgumentException(
                    "An expected-current resource mutation requires a resource key.",
                    nameof(mutations)));
            if (guarded.Amount < 0)
                throw new ArgumentOutOfRangeException(nameof(mutations),
                    "A native resource current value can not be negative.");

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var changed = await dbContext.CharacterRulesInputs
                .Where(value => value.CharacterId == characterId &&
                    value.Kind == CharacterRulesInputKind.Resource &&
                    value.Key == key &&
                    value.IntegerValue == expected)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(value => value.IntegerValue, (int?)guarded.Amount)
                    .SetProperty(value => value.UpdatedAt, changedAt),
                    cancellationToken);
            if (changed != 1)
            {
                var exists = await dbContext.CharacterSheets.AsNoTracking()
                    .AnyAsync(value => value.CharacterId == characterId, cancellationToken);
                throw new InvalidOperationException(exists
                    ? $"Resource '{key}' changed since the operation was evaluated."
                    : "Character Sheet does not exist for this resource mutation.");
            }

            await dbContext.CharacterSheets
                .Where(value => value.CharacterId == characterId &&
                    value.UpdatedAt < changedAt)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(value => value.UpdatedAt, changedAt), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            // Do not return the tracked root from before the conditional UPDATE.
            return await GetAsync(characterId, cancellationToken);
        }
        if (mutations.Any(value => value.ExpectedValue is not null))
            throw new InvalidOperationException(
                "Expected-current mutations must be one native resource Set operation.");

        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ApplyIntegerStateMutations(mutations, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddInventoryItemOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.AddInventoryItemOccurrence(ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddCustomInventoryItemOccurrenceAsync(
        Guid characterId,
        string customName,
        int quantity,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.AddCustomInventoryItemOccurrence(customName, changedAt, quantity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ApplyInventoryTransactionAsync(
        Guid characterId,
        IReadOnlyList<CharacterInventoryConsumption> consumptions,
        IReadOnlyList<CharacterInventoryAddition> additions,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.ApplyInventoryTransaction(consumptions, additions, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        int quantity,
        bool isCarried,
        bool isEquipped,
        bool isAttuned,
        Guid? containerOccurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.UpdateInventoryItemOccurrence(
            occurrenceId,
            quantity,
            isCarried,
            isEquipped,
            isAttuned,
            containerOccurrenceId,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveInventoryItemOccurrenceAsync(
        Guid characterId,
        Guid occurrenceId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.RemoveInventoryItemOccurrence(occurrenceId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        int? integerValue,
        bool? booleanValue,
        string? textValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetRulesInput(kind, key, integerValue, booleanValue, textValue, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveRulesInputAsync(
        Guid characterId,
        CharacterRulesInputKind kind,
        string key,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveRulesInput(kind, key, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        int hitDieValue,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetHitPointGain(advancementOccurrenceId, classLevel, hitDieValue, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveHitPointGainAsync(
        Guid characterId,
        Guid advancementOccurrenceId,
        int classLevel,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveHitPointGain(advancementOccurrenceId, classLevel, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddNoteAsync(
        Guid characterId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.AddNote(content, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateNoteAsync(
        Guid characterId,
        Guid noteId,
        string content,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.UpdateNote(noteId, content, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveNoteAsync(
        Guid characterId,
        Guid noteId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null)
        {
            return null;
        }

        root.RemoveNote(noteId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> AddConditionAsync(
        Guid characterId,
        string? ruleConceptKey,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.AddCondition(
            ruleConceptKey,
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> UpdateConditionAsync(
        Guid characterId,
        Guid conditionId,
        string? customName,
        int? level,
        int? counterCurrent,
        int? counterMaximum,
        string? duration,
        string? notes,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.UpdateCondition(
            conditionId,
            customName,
            level,
            counterCurrent,
            counterMaximum,
            duration,
            notes,
            changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveConditionAsync(
        Guid characterId,
        Guid conditionId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveCondition(conditionId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    private Task<CharacterSheetRoot?> GetTrackedAsync(
        Guid characterId,
        CancellationToken cancellationToken) =>
        BuildQuery(tracking: true)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    private IQueryable<CharacterSheetRoot> BuildQuery(bool tracking)
    {
        var query = dbContext.CharacterSheets
            .Include(value => value.Profile)
            .Include(value => value.CurrencyBalances)
            .Include(value => value.InventoryItemOccurrences)
            .Include(value => value.Notes)
            .Include(value => value.Conditions)
            .Include(value => value.RulesInputs)
            .Include(value => value.HitPointGains)
            .Include(value => value.AdvancementEntries)
            .AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }
}
