using System.Data.Common;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterStartingEquipmentInventoryStore(
    CharacterSheetDbContext dbContext)
    : ICharacterStartingEquipmentInventoryStore
{
    public const int MaxSourceGrantKeyLength = 800;

    public async Task<CharacterSheetRoot?> SynchronizeAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> items,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> currencies,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(currencies);
        var desired = items.Select(Normalize).ToArray();
        var desiredCurrencies = currencies.Select(Normalize).ToArray();
        ThrowIfDuplicateSources(desired.Select(value => value.SourceGrantKey), nameof(items));
        ThrowIfDuplicateSources(desiredCurrencies.Select(value => value.SourceGrantKey), nameof(currencies));

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var root = await dbContext.CharacterSheets
            .Include(value => value.InventoryItemOccurrences)
            .Include(value => value.CurrencyBalances)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);
        if (root is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await SynchronizeInventoryAsync(
            root,
            characterId,
            desired,
            changedAt,
            cancellationToken);
        await SynchronizeCurrencyAsync(
            root,
            characterId,
            desiredCurrencies,
            changedAt,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return root;
    }

    private async Task SynchronizeInventoryAsync(
        CharacterSheetRoot root,
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> desired,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var existingMappings = await LoadMappingsAsync(characterId, cancellationToken);
        var existingBySource = existingMappings.ToDictionary(
            value => value.SourceGrantKey,
            StringComparer.Ordinal);
        var desiredBySource = desired.ToDictionary(
            value => value.SourceGrantKey,
            StringComparer.Ordinal);
        var mappingsToInsert = new List<StartingEquipmentMapping>();

        foreach (var mapping in existingMappings)
        {
            if (desiredBySource.ContainsKey(mapping.SourceGrantKey))
            {
                continue;
            }

            root.RemoveInventoryItemOccurrence(mapping.InventoryOccurrenceId, changedAt);
        }

        foreach (var item in desired)
        {
            if (existingBySource.TryGetValue(item.SourceGrantKey, out var mapping))
            {
                var occurrence = root.InventoryItemOccurrences.SingleOrDefault(value =>
                    value.Id == mapping.InventoryOccurrenceId);
                if (occurrence is not null && SameIdentity(occurrence, item))
                {
                    if (occurrence.Quantity != item.Quantity)
                    {
                        root.UpdateInventoryItemOccurrence(
                            occurrence.Id,
                            item.Quantity,
                            occurrence.IsCarried,
                            occurrence.IsEquipped,
                            occurrence.IsAttuned,
                            occurrence.ContainerOccurrenceId,
                            changedAt);
                    }
                    continue;
                }

                if (occurrence is not null)
                {
                    root.RemoveInventoryItemOccurrence(occurrence.Id, changedAt);
                }
            }

            var added = item.RuleConceptKey is not null
                ? root.AddInventoryItemOccurrence(
                    item.RuleConceptKey,
                    changedAt,
                    item.Quantity)
                : root.AddCustomInventoryItemOccurrence(
                    item.CustomName!,
                    changedAt,
                    item.Quantity);
            mappingsToInsert.Add(new StartingEquipmentMapping(
                characterId,
                added.Id,
                item.SourceGrantKey,
                changedAt));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        foreach (var mapping in mappingsToInsert)
        {
            await InsertMappingAsync(mapping, cancellationToken);
        }
    }

    private async Task SynchronizeCurrencyAsync(
        CharacterSheetRoot root,
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> desired,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var existing = await LoadCurrencyMappingsAsync(characterId, cancellationToken);
        var existingBySource = existing.ToDictionary(value => value.SourceGrantKey, StringComparer.Ordinal);
        var desiredBySource = desired.ToDictionary(value => value.SourceGrantKey, StringComparer.Ordinal);
        var deltas = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var mapping in existing)
        {
            if (desiredBySource.TryGetValue(mapping.SourceGrantKey, out var next)
                && string.Equals(mapping.CurrencyKey, next.CurrencyKey, StringComparison.Ordinal))
            {
                AddCurrencyDelta(
                    deltas,
                    mapping.CurrencyKey,
                    checked(next.Amount - mapping.AppliedAmount));
                continue;
            }

            AddCurrencyDelta(deltas, mapping.CurrencyKey, checked(-mapping.AppliedAmount));
        }

        foreach (var grant in desired)
        {
            if (existingBySource.TryGetValue(grant.SourceGrantKey, out var previous)
                && string.Equals(previous.CurrencyKey, grant.CurrencyKey, StringComparison.Ordinal))
            {
                continue;
            }

            AddCurrencyDelta(deltas, grant.CurrencyKey, grant.Amount);
        }

        foreach (var (currencyKey, delta) in deltas)
        {
            if (delta == 0) continue;
            var current = root.CurrencyBalances.SingleOrDefault(value =>
                value.CurrencyKey == currencyKey)?.Amount ?? 0;
            root.SetCurrencyBalance(currencyKey, checked(current + delta), changedAt);
        }

        await ReplaceCurrencyMappingsAsync(
            characterId,
            desired,
            changedAt,
            cancellationToken);
    }

    private async Task<IReadOnlyList<StartingEquipmentMapping>> LoadMappingsAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT "CharacterId", "InventoryOccurrenceId", "SourceGrantKey", "CreatedAt"
            FROM "character_starting_equipment_inventory_grants"
            WHERE "CharacterId" = @characterId
            ORDER BY "SourceGrantKey";
            """;
        AddParameter(command, "characterId", characterId);

        var rows = new List<StartingEquipmentMapping>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StartingEquipmentMapping(
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return rows;
    }

    private async Task<IReadOnlyList<StartingEquipmentCurrencyMapping>> LoadCurrencyMappingsAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT "SourceGrantKey", "CurrencyKey", "AppliedAmount"
            FROM "character_starting_equipment_currency_grants"
            WHERE "CharacterId" = @characterId
            ORDER BY "SourceGrantKey";
            """;
        AddParameter(command, "characterId", characterId);

        var rows = new List<StartingEquipmentCurrencyMapping>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new StartingEquipmentCurrencyMapping(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2)));
        }
        return rows;
    }

    private async Task InsertMappingAsync(
        StartingEquipmentMapping mapping,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO "character_starting_equipment_inventory_grants"
                ("CharacterId", "InventoryOccurrenceId", "SourceGrantKey", "CreatedAt")
            VALUES
                (@characterId, @inventoryOccurrenceId, @sourceGrantKey, @createdAt);
            """;
        AddParameter(command, "characterId", mapping.CharacterId);
        AddParameter(command, "inventoryOccurrenceId", mapping.InventoryOccurrenceId);
        AddParameter(command, "sourceGrantKey", mapping.SourceGrantKey);
        AddParameter(command, "createdAt", mapping.CreatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task ReplaceCurrencyMappingsAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> desired,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            delete.CommandText = """
                DELETE FROM "character_starting_equipment_currency_grants"
                WHERE "CharacterId" = @characterId;
                """;
            AddParameter(delete, "characterId", characterId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var grant in desired)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            insert.CommandText = """
                INSERT INTO "character_starting_equipment_currency_grants"
                    ("CharacterId", "SourceGrantKey", "CurrencyKey", "AppliedAmount", "CreatedAt", "UpdatedAt")
                VALUES
                    (@characterId, @sourceGrantKey, @currencyKey, @appliedAmount, @createdAt, @updatedAt);
                """;
            AddParameter(insert, "characterId", characterId);
            AddParameter(insert, "sourceGrantKey", grant.SourceGrantKey);
            AddParameter(insert, "currencyKey", grant.CurrencyKey);
            AddParameter(insert, "appliedAmount", grant.Amount);
            AddParameter(insert, "createdAt", changedAt);
            AddParameter(insert, "updatedAt", changedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void AddCurrencyDelta(
        Dictionary<string, long> deltas,
        string currencyKey,
        long delta)
    {
        if (delta == 0) return;
        deltas.TryGetValue(currencyKey, out var current);
        deltas[currencyKey] = checked(current + delta);
    }

    private static void ThrowIfDuplicateSources(IEnumerable<string> sourceGrantKeys, string parameterName)
    {
        var duplicate = sourceGrantKeys
            .GroupBy(value => value, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Starting-equipment source grant '{duplicate.Key}' was materialized more than once.",
                parameterName);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static CharacterStartingEquipmentInventoryItem Normalize(
        CharacterStartingEquipmentInventoryItem item)
    {
        var sourceGrantKey = NormalizeSourceGrantKey(item.SourceGrantKey);
        if (item.Quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(item),
                "Starting-equipment inventory quantity must be positive.");
        }

        var hasRule = !string.IsNullOrWhiteSpace(item.RuleConceptKey);
        var hasCustom = !string.IsNullOrWhiteSpace(item.CustomName);
        if (hasRule == hasCustom)
        {
            throw new ArgumentException(
                "A starting-equipment inventory item must identify exactly one Rules Core item or custom item name.");
        }

        return item with
        {
            SourceGrantKey = sourceGrantKey,
            RuleConceptKey = hasRule
                ? CharacterRuleReference.NormalizeConceptKey(item.RuleConceptKey!)
                : null,
            CustomName = hasCustom ? item.CustomName!.Trim() : null
        };
    }

    private static CharacterStartingEquipmentCurrencyGrant Normalize(
        CharacterStartingEquipmentCurrencyGrant grant)
    {
        if (grant.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(grant),
                "Starting-equipment currency amount must be positive.");
        }

        return grant with
        {
            SourceGrantKey = NormalizeSourceGrantKey(grant.SourceGrantKey),
            CurrencyKey = CharacterCurrencyBalance.NormalizeKey(grant.CurrencyKey)
        };
    }

    private static string NormalizeSourceGrantKey(string sourceGrantKey)
    {
        if (string.IsNullOrWhiteSpace(sourceGrantKey))
        {
            throw new ArgumentException("Starting-equipment source grant key can not be blank.");
        }
        var normalized = sourceGrantKey.Trim();
        if (normalized.Length > MaxSourceGrantKeyLength)
        {
            throw new ArgumentException(
                $"Starting-equipment source grant key can not exceed {MaxSourceGrantKeyLength} characters.");
        }
        return normalized;
    }

    private static bool SameIdentity(
        CharacterInventoryItemOccurrence occurrence,
        CharacterStartingEquipmentInventoryItem item) =>
        item.RuleConceptKey is not null
            ? string.Equals(
                occurrence.RuleConceptKey,
                item.RuleConceptKey,
                StringComparison.Ordinal)
            : occurrence.RuleConceptKey is null
                && string.Equals(
                    occurrence.CustomName,
                    item.CustomName,
                    StringComparison.Ordinal);

    private sealed record StartingEquipmentMapping(
        Guid CharacterId,
        Guid InventoryOccurrenceId,
        string SourceGrantKey,
        DateTimeOffset CreatedAt);

    private sealed record StartingEquipmentCurrencyMapping(
        string SourceGrantKey,
        string CurrencyKey,
        long AppliedAmount);
}