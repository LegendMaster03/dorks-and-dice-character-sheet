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
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        var desired = items.Select(Normalize).ToArray();
        var duplicate = desired
            .GroupBy(value => value.SourceGrantKey, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Starting-equipment source grant '{duplicate.Key}' was materialized more than once.",
                nameof(items));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var root = await dbContext.CharacterSheets
            .Include(value => value.InventoryItemOccurrences)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);
        if (root is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

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
        await transaction.CommitAsync(cancellationToken);
        return root;
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
        if (string.IsNullOrWhiteSpace(item.SourceGrantKey))
        {
            throw new ArgumentException("Starting-equipment source grant key can not be blank.");
        }
        var sourceGrantKey = item.SourceGrantKey.Trim();
        if (sourceGrantKey.Length > MaxSourceGrantKeyLength)
        {
            throw new ArgumentException(
                $"Starting-equipment source grant key can not exceed {MaxSourceGrantKeyLength} characters.");
        }
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
}
