using CharacterSheet.Application.Persistence;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class StartingEquipmentInventoryPersistenceTests
{
    [Fact]
    public async Task GrantSynchronizationTablesAreRetiredWithoutDeletingOrdinaryInventory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var id = Guid.NewGuid();
        await using var context = new CharacterSheetDbContext(options);
        await context.Database.MigrateAsync();
        var state = new PostgresCharacterStateStore(context);
        await new PostgresCharacterSheetStore(context).GetOrCreateAsync(id);
        await state.AddInventoryItemOccurrenceAsync(id, "item:dagger",
            DateTimeOffset.UtcNow);
        await state.SetCurrencyBalanceAsync(id, "gp", 8, DateTimeOffset.UtcNow);

        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT to_regclass('public.character_starting_equipment_inventory_grants'),
                   to_regclass('public.character_starting_equipment_currency_grants');
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(await reader.IsDBNullAsync(0));
        Assert.True(await reader.IsDBNullAsync(1));
        await reader.DisposeAsync();

        var saved = await state.GetAsync(id);
        Assert.Single(saved!.InventoryItemOccurrences);
        Assert.Equal(8, Assert.Single(saved.CurrencyBalances).Amount);
    }

    [Fact]
    public async Task ConcurrentStartingEquipmentRequestsGrantOnlyOneOrdinaryInventorySet()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var id = Guid.NewGuid();
        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(id);
        }

        async Task AwardAsync()
        {
            await using var context = new CharacterSheetDbContext(options);
            await new PostgresCharacterStartingEquipmentInventoryStore(context).ApplyOnceAsync(
                id,
                [new CharacterStartingEquipmentInventoryItem(
                    "native.test.item", "item.demo.knife", null, 2)],
                [new CharacterStartingEquipmentCurrencyGrant(
                    "native.test.gold", "gp", 50)],
                DateTimeOffset.UtcNow);
        }

        await Task.WhenAll(AwardAsync(), AwardAsync());

        await using var verify = new CharacterSheetDbContext(options);
        var state = await new PostgresCharacterStateStore(verify).GetAsync(id);
        Assert.NotNull(state);
        Assert.Equal(2, Assert.Single(state.InventoryItemOccurrences).Quantity);
        Assert.Equal(50, Assert.Single(state.CurrencyBalances).Amount);
        Assert.Single(state.RulesInputs, input =>
            input.Kind == CharacterSheet.Domain.Characters.CharacterRulesInputKind.BooleanFact &&
            input.Key == CharacterStartingEquipmentReceipt.Key &&
            input.BooleanValue == true);
    }

    [Fact]
    public async Task StartingEquipmentIsAwardedOnceAsOrdinaryInventory()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid manualDaggerId;

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var ordinary = new PostgresCharacterStateStore(setup);
            await ordinary.AddInventoryItemOccurrenceAsync(
                characterId,
                "item:dagger",
                DateTimeOffset.UtcNow);
            var state = await ordinary.GetAsync(characterId);
            manualDaggerId = Assert.Single(state!.InventoryItemOccurrences).Id;
        }

        var firstProjection = new CharacterStartingEquipmentInventoryItem[]
        {
            new(
                "grant.class-fighter.starting-equipment.0.fixed.0.item",
                "item:dagger",
                null,
                2),
            new(
                "grant.background-soldier.starting-equipment.0.fixed.1.custom",
                null,
                "A letter of rank",
                1)
        };

        await using (var first = new CharacterSheetDbContext(options))
        {
            var materializer = new PostgresCharacterStartingEquipmentInventoryStore(first);
            var root = await materializer.ApplyOnceAsync(
                characterId,
                firstProjection,
                [],
                DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.NotNull(root);
        }

        await using (var verifyFirst = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(verifyFirst).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(3, state.InventoryItemOccurrences.Count);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.Id == manualDaggerId
                && value.RuleConceptKey == "item:dagger"
                && value.Quantity == 1);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.Id != manualDaggerId
                && value.RuleConceptKey == "item:dagger"
                && value.Quantity == 2);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.CustomName == "A letter of rank"
                && value.Quantity == 1);
        }

        await using (var repeat = new CharacterSheetDbContext(options))
        {
            var materializer = new PostgresCharacterStartingEquipmentInventoryStore(repeat);
            await materializer.ApplyOnceAsync(
                characterId,
                firstProjection,
                [],
                DateTimeOffset.UtcNow.AddMinutes(2));
        }

        await using (var verifyRepeat = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(verifyRepeat).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(3, state.InventoryItemOccurrences.Count);
        }

        var changedProjection = new CharacterStartingEquipmentInventoryItem[]
        {
            new(
                "grant.class-fighter.starting-equipment.0.a.0.item",
                "item:longsword",
                null,
                1)
        };

        await using (var changed = new CharacterSheetDbContext(options))
        {
            var materializer = new PostgresCharacterStartingEquipmentInventoryStore(changed);
            await materializer.ApplyOnceAsync(
                characterId,
                changedProjection,
                [],
                DateTimeOffset.UtcNow.AddMinutes(3));
        }

        await using (var verifyChanged = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(verifyChanged).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(3, state.InventoryItemOccurrences.Count);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.Id == manualDaggerId
                && value.RuleConceptKey == "item:dagger"
                && value.Quantity == 1);
            Assert.DoesNotContain(state.InventoryItemOccurrences, value =>
                value.RuleConceptKey == "item:longsword");
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.CustomName == "A letter of rank"
                && value.Quantity == 1);
            Assert.Contains(state.RulesInputs, value =>
                value.Key == CharacterStartingEquipmentReceipt.Key &&
                value.BooleanValue == true);
        }
    }

    [Fact]
    public async Task RemovingStartingItemDoesNotCauseRegrant()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        var projection = new CharacterStartingEquipmentInventoryItem[]
        {
            new(
                "grant.background-acolyte.starting-equipment.0.fixed.0.item",
                "item:holy-symbol",
                null,
                1)
        };
        Guid generatedId;

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var materializer = new PostgresCharacterStartingEquipmentInventoryStore(setup);
            var root = await materializer.ApplyOnceAsync(
                characterId,
                projection,
                [],
                DateTimeOffset.UtcNow);
            generatedId = Assert.Single(root!.InventoryItemOccurrences).Id;
        }

        await using (var remove = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStateStore(remove).RemoveInventoryItemOccurrenceAsync(
                characterId,
                generatedId,
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var reapply = new CharacterSheetDbContext(options))
        {
            var root = await new PostgresCharacterStartingEquipmentInventoryStore(reapply)
                .ApplyOnceAsync(
                    characterId,
                    projection,
                    [],
                    DateTimeOffset.UtcNow.AddMinutes(2));
            Assert.Empty(root!.InventoryItemOccurrences);
            Assert.Contains(root.RulesInputs, value =>
                value.Key == CharacterStartingEquipmentReceipt.Key &&
                value.BooleanValue == true);
        }
    }

    [Fact]
    public async Task StartingCurrencyIsAwardedOnceAndBecomesAnOrdinaryBalance()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        var initialGrant = new CharacterStartingEquipmentCurrencyGrant[]
        {
            new("grant.class-fighter.starting-equipment.gold", "gp", 100)
        };

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            await new PostgresCharacterStateStore(setup).SetCurrencyBalanceAsync(
                characterId,
                "gp",
                25,
                DateTimeOffset.UtcNow);

            await new PostgresCharacterStartingEquipmentInventoryStore(setup).ApplyOnceAsync(
                characterId,
                [],
                initialGrant,
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var verifyInitial = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(verifyInitial).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(125, Assert.Single(state.CurrencyBalances).Amount);
        }

        await using (var repeat = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStartingEquipmentInventoryStore(repeat).ApplyOnceAsync(
                characterId,
                [],
                initialGrant,
                DateTimeOffset.UtcNow.AddMinutes(2));
            var state = await new PostgresCharacterStateStore(repeat).GetAsync(characterId);
            Assert.Equal(125, Assert.Single(state!.CurrencyBalances).Amount);
        }

        await using (var spend = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStateStore(spend).SetCurrencyBalanceAsync(
                characterId,
                "gp",
                105,
                DateTimeOffset.UtcNow.AddMinutes(3));
        }

        var changedGrant = new CharacterStartingEquipmentCurrencyGrant[]
        {
            new("grant.class-fighter.starting-equipment.gold", "gp", 120)
        };
        await using (var changed = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStartingEquipmentInventoryStore(changed).ApplyOnceAsync(
                characterId,
                [],
                changedGrant,
                DateTimeOffset.UtcNow.AddMinutes(4));
            var state = await new PostgresCharacterStateStore(changed).GetAsync(characterId);
            Assert.Equal(105, Assert.Single(state!.CurrencyBalances).Amount);
        }

        await using (var removeGrant = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStartingEquipmentInventoryStore(removeGrant).ApplyOnceAsync(
                characterId,
                [],
                [],
                DateTimeOffset.UtcNow.AddMinutes(5));
            var state = await new PostgresCharacterStateStore(removeGrant).GetAsync(characterId);
            Assert.Equal(105, Assert.Single(state!.CurrencyBalances).Amount);
        }
    }
}