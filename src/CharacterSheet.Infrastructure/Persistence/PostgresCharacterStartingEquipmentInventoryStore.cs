using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Awards starting equipment once into ordinary Character inventory and currency.
/// It does not maintain, synchronize, replace, or reclaim acquired items afterward.
/// A receipt is stored with ordinary Character state solely to prevent duplicate
/// awards when the player or browser repeats the application request.
/// </summary>
public sealed class PostgresCharacterStartingEquipmentInventoryStore(
    CharacterSheetDbContext dbContext) : ICharacterStartingEquipmentInventoryStore
{
    public const int MaxSourceGrantKeyLength = 800;

    public async Task<CharacterSheetRoot?> ApplyOnceAsync(
        Guid characterId,
        IReadOnlyList<CharacterStartingEquipmentInventoryItem> items,
        IReadOnlyList<CharacterStartingEquipmentCurrencyGrant> currencies,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(currencies);
        var desired = items.Select(Validate).ToArray();
        var money = currencies.Select(Validate).ToArray();
        RejectDuplicateSources(desired.Select(value => value.SourceGrantKey));
        RejectDuplicateSources(money.Select(value => value.SourceGrantKey));

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Take a database row lock before checking the receipt. Two concurrent
        // requests cannot both observe an unapplied award and grant items.
        var locked = await dbContext.CharacterSheets
            .Where(value => value.CharacterId == characterId)
            .ExecuteUpdateAsync(update => update
                .SetProperty(value => value.UpdatedAt, value => value.UpdatedAt),
                cancellationToken);
        if (locked != 1)
            return null;

        var root = await dbContext.CharacterSheets
            .Include(value => value.InventoryItemOccurrences)
            .Include(value => value.CurrencyBalances)
            .Include(value => value.RulesInputs)
            .SingleAsync(value => value.CharacterId == characterId, cancellationToken);
        if (root.RulesInputs.Any(value =>
            value.Kind == CharacterRulesInputKind.BooleanFact &&
            value.Key == CharacterStartingEquipmentReceipt.Key &&
            value.BooleanValue == true))
        {
            await transaction.CommitAsync(cancellationToken);
            return root;
        }

        foreach (var item in desired)
        {
            if (item.RuleConceptKey is string id)
                root.AddInventoryItemOccurrence(id, changedAt, item.Quantity);
            else
                root.AddCustomInventoryItemOccurrence(item.CustomName!, changedAt,
                    item.Quantity);
        }

        foreach (var group in money.GroupBy(value => value.CurrencyKey,
                     StringComparer.Ordinal))
        {
            var amount = group.Aggregate(0L,
                (total, value) => checked(total + value.Amount));
            var previous = root.CurrencyBalances.SingleOrDefault(value =>
                value.CurrencyKey == group.Key)?.Amount ?? 0L;
            root.SetCurrencyBalance(group.Key, checked(previous + amount), changedAt);
        }

        root.SetRulesInput(CharacterRulesInputKind.BooleanFact,
            CharacterStartingEquipmentReceipt.Key,
            integerValue: null, booleanValue: true, textValue: null, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return root;
    }

    private static CharacterStartingEquipmentInventoryItem Validate(
        CharacterStartingEquipmentInventoryItem item)
    {
        var key = ValidateSource(item.SourceGrantKey);
        if (item.Quantity < 1)
            throw new ArgumentOutOfRangeException(nameof(item),
                "Starting item quantity must be positive.");
        var id = !string.IsNullOrWhiteSpace(item.RuleConceptKey);
        var custom = !string.IsNullOrWhiteSpace(item.CustomName);
        if (id == custom)
            throw new ArgumentException(
                "Each grant must identify one native item or custom item name.");
        return item with
        {
            SourceGrantKey = key,
            RuleConceptKey = id ? CharacterRuleReference.NormalizeConceptKey(
                item.RuleConceptKey!) : null,
            CustomName = custom ? item.CustomName!.Trim() : null
        };
    }

    private static CharacterStartingEquipmentCurrencyGrant Validate(
        CharacterStartingEquipmentCurrencyGrant value)
    {
        if (value.Amount < 1)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Starting currency amount must be positive.");
        return value with
        {
            SourceGrantKey = ValidateSource(value.SourceGrantKey),
            CurrencyKey = CharacterCurrencyBalance.NormalizeKey(value.CurrencyKey)
        };
    }

    private static string ValidateSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source) ||
            source.Length > MaxSourceGrantKeyLength)
            throw new ArgumentException("Invalid starting-equipment grant source.");
        return source.Trim();
    }

    private static void RejectDuplicateSources(IEnumerable<string> sources)
    {
        if (sources.GroupBy(value => value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
            throw new ArgumentException("Duplicate starting-equipment grant source.");
    }
}
