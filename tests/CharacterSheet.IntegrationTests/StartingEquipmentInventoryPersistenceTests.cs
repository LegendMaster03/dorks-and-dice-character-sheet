using CharacterSheet.Application.Persistence;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class StartingEquipmentInventoryPersistenceTests
{
    [Fact]
    public async Task SynchronizationIsIdempotentAndDoesNotTouchIdenticalManualInventory()
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
            var root = await materializer.SynchronizeAsync(
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
            await materializer.SynchronizeAsync(
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
            await materializer.SynchronizeAsync(
                characterId,
                changedProjection,
                [],
                DateTimeOffset.UtcNow.AddMinutes(3));
        }

        await using (var verifyChanged = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(verifyChanged).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(2, state.InventoryItemOccurrences.Count);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.Id == manualDaggerId
                && value.RuleConceptKey == "item:dagger"
                && value.Quantity == 1);
            Assert.Contains(state.InventoryItemOccurrences, value =>
                value.RuleConceptKey == "item:longsword"
                && value.Quantity == 1);
            Assert.DoesNotContain(state.InventoryItemOccurrences, value =>
                value.CustomName == "A letter of rank");
        }
    }

    [Fact]
    public async Task ManualRemovalDeletesOnlyItsProvenanceSoExplicitReapplyCanAcquireAgain()
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
            var root = await materializer.SynchronizeAsync(
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
                .SynchronizeAsync(
                    characterId,
                    projection,
                    [],
                    DateTimeOffset.UtcNow.AddMinutes(2));
            var reacquired = Assert.Single(root!.InventoryItemOccurrences);
            Assert.NotEqual(generatedId, reacquired.Id);
            Assert.Equal("item:holy-symbol", reacquired.RuleConceptKey);
        }
    }

    [Fact]
    public async Task CurrencySynchronizationUsesProvenanceDeltasAndPreservesLaterBalanceChanges()
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

            await new PostgresCharacterStartingEquipmentInventoryStore(setup).SynchronizeAsync(
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
            await new PostgresCharacterStartingEquipmentInventoryStore(repeat).SynchronizeAsync(
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
            await new PostgresCharacterStartingEquipmentInventoryStore(changed).SynchronizeAsync(
                characterId,
                [],
                changedGrant,
                DateTimeOffset.UtcNow.AddMinutes(4));
            var state = await new PostgresCharacterStateStore(changed).GetAsync(characterId);
            Assert.Equal(125, Assert.Single(state!.CurrencyBalances).Amount);
        }

        await using (var removeGrant = new CharacterSheetDbContext(options))
        {
            await new PostgresCharacterStartingEquipmentInventoryStore(removeGrant).SynchronizeAsync(
                characterId,
                [],
                [],
                DateTimeOffset.UtcNow.AddMinutes(5));
            var state = await new PostgresCharacterStateStore(removeGrant).GetAsync(characterId);
            Assert.Equal(5, Assert.Single(state!.CurrencyBalances).Amount);
        }
    }
}