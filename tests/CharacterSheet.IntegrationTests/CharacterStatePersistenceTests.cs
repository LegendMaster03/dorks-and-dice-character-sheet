using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterStatePersistenceTests
{
    [Fact]
    public async Task RoutineStatePersistsAcrossDbContextRecreationAndDuplicateItemsRemainDistinct()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid firstItemId;
        Guid secondItemId;
        Guid noteId;

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(firstContext).GetOrCreateAsync(characterId);
            var stateStore = new PostgresCharacterStateStore(firstContext);

            await stateStore.SetCurrentHitPointsAsync(
                characterId,
                -2,
                DateTimeOffset.UtcNow.AddMilliseconds(500));
            await stateStore.SetDeathSavesAsync(
                characterId,
                2,
                1,
                DateTimeOffset.UtcNow.AddMilliseconds(750));
            await stateStore.AddInventoryItemOccurrenceAsync(
                characterId,
                "  ITEM:TORCH  ",
                DateTimeOffset.UtcNow);
            await stateStore.AddInventoryItemOccurrenceAsync(
                characterId,
                "item:torch",
                DateTimeOffset.UtcNow.AddSeconds(1));
            await stateStore.AddNoteAsync(
                characterId,
                "Initial note",
                DateTimeOffset.UtcNow.AddSeconds(2));

            var state = await stateStore.GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(-2, state.CurrentHitPoints);
            Assert.Equal(2, state.DeathSaveSuccesses);
            Assert.Equal(1, state.DeathSaveFailures);
            var items = state.InventoryItemOccurrences.ToArray();
            Assert.Equal(2, items.Length);
            Assert.All(items, value => Assert.Equal("item:torch", value.RuleConceptKey));
            firstItemId = items[0].Id;
            secondItemId = items[1].Id;
            Assert.NotEqual(firstItemId, secondItemId);
            await stateStore.UpdateInventoryItemOccurrenceAsync(
                characterId,
                secondItemId,
                quantity: 4,
                isCarried: true,
                isEquipped: true,
                isAttuned: true,
                containerOccurrenceId: firstItemId,
                DateTimeOffset.UtcNow.AddSeconds(3));
            noteId = Assert.Single(state.Notes).Id;
        }

        await using (var secondContext = new CharacterSheetDbContext(options))
        {
            var stateStore = new PostgresCharacterStateStore(secondContext);
            var state = await stateStore.GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(2, state.DeathSaveSuccesses);
            Assert.Equal(1, state.DeathSaveFailures);
            Assert.Equal(2, state.InventoryItemOccurrences.Count);
            var equipped = Assert.Single(
                state.InventoryItemOccurrences,
                value => value.Id == secondItemId);
            Assert.Equal(4, equipped.Quantity);
            Assert.True(equipped.IsCarried);
            Assert.True(equipped.IsEquipped);
            Assert.True(equipped.IsAttuned);
            Assert.Equal(firstItemId, equipped.ContainerOccurrenceId);
            Assert.Equal("Initial note", Assert.Single(state.Notes).Content);

            await stateStore.RemoveInventoryItemOccurrenceAsync(
                characterId,
                firstItemId,
                DateTimeOffset.UtcNow.AddMinutes(1));
            await stateStore.UpdateNoteAsync(
                characterId,
                noteId,
                "Updated note",
                DateTimeOffset.UtcNow.AddMinutes(2));
        }

        await using (var thirdContext = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(thirdContext).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(-2, state.CurrentHitPoints);
            Assert.Equal(2, state.DeathSaveSuccesses);
            Assert.Equal(1, state.DeathSaveFailures);
            var item = Assert.Single(state.InventoryItemOccurrences);
            Assert.Equal(secondItemId, item.Id);
            Assert.Equal(4, item.Quantity);
            Assert.True(item.IsEquipped);
            Assert.True(item.IsAttuned);
            Assert.Null(item.ContainerOccurrenceId);
            var note = Assert.Single(state.Notes);
            Assert.Equal(noteId, note.Id);
            Assert.Equal("Updated note", note.Content);
        }
    }

    [Fact]
    public async Task CurrencyBalancesPersistWithoutApplyingConversionRules()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(firstContext).GetOrCreateAsync(characterId);
            var stateStore = new PostgresCharacterStateStore(firstContext);
            await stateStore.SetCurrencyBalanceAsync(
                characterId,
                "GP",
                125,
                DateTimeOffset.UtcNow);
            await stateStore.SetCurrencyBalanceAsync(
                characterId,
                "third-party-scrip",
                -4,
                DateTimeOffset.UtcNow.AddSeconds(1));
        }

        await using (var secondContext = new CharacterSheetDbContext(options))
        {
            var stateStore = new PostgresCharacterStateStore(secondContext);
            var state = await stateStore.GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(2, state.CurrencyBalances.Count);
            Assert.Contains(state.CurrencyBalances, value =>
                value.CurrencyKey == "gp" && value.Amount == 125);
            Assert.Contains(state.CurrencyBalances, value =>
                value.CurrencyKey == "third-party-scrip" && value.Amount == -4);

            await stateStore.RemoveCurrencyBalanceAsync(
                characterId,
                "gp",
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var thirdContext = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(thirdContext).GetAsync(characterId);
            Assert.NotNull(state);
            var remaining = Assert.Single(state.CurrencyBalances);
            Assert.Equal("third-party-scrip", remaining.CurrencyKey);
            Assert.Equal(-4, remaining.Amount);
        }
    }

    [Fact]
    public async Task CharacterProfilePersistsAndCascadesWithCharacterRoot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(firstContext).GetOrCreateAsync(characterId);
            await new PostgresCharacterStateStore(firstContext).SetProfileAsync(
                characterId,
                "Chaotic good",
                "The Traveler",
                "34",
                "5 ft. 11 in.",
                "180 lb.",
                "Scar over the left eyebrow.",
                "Curious",
                "Freedom",
                "Old adventuring company",
                "Impatient",
                "A long-form history.",
                "The Cartographers Guild",
                "Silver compass rose",
                DateTimeOffset.UtcNow);
        }

        await using (var secondContext = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(secondContext).GetAsync(characterId);
            Assert.NotNull(state?.Profile);
            Assert.Equal("Chaotic good", state.Profile.Alignment);
            Assert.Equal("The Traveler", state.Profile.Deity);
            Assert.Equal("A long-form history.", state.Profile.Backstory);

            await secondContext.CharacterSheets
                .Where(value => value.CharacterId == characterId)
                .ExecuteDeleteAsync();
        }

        await using (var thirdContext = new CharacterSheetDbContext(options))
        {
            Assert.Equal(0, await thirdContext.CharacterProfiles.CountAsync());
        }
    }

    [Fact]
    public async Task RulesInputsAndHitPointGainsPersistAcrossDbContextRecreation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid classId;

        await using (var firstContext = new CharacterSheetDbContext(options))
        {
            await firstContext.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(firstContext).GetOrCreateAsync(characterId);

            var buildStore = new PostgresCharacterBuildStore(firstContext);
            var build = await buildStore.SetStartingClassAsync(
                characterId,
                "class:fighter",
                DateTimeOffset.UtcNow);
            Assert.NotNull(build);
            classId = Assert.Single(build.AdvancementEntries).Id;
            await buildStore.SetAdvancementLevelAsync(
                characterId,
                classId,
                2,
                DateTimeOffset.UtcNow.AddSeconds(1));

            var stateStore = new PostgresCharacterStateStore(firstContext);
            await stateStore.SetRulesInputAsync(
                characterId,
                CharacterRulesInputKind.Choice,
                "spellcasting.resource-system",
                null,
                null,
                "spell-points",
                DateTimeOffset.UtcNow.AddSeconds(2));
            await stateStore.SetRulesInputAsync(
                characterId,
                CharacterRulesInputKind.Resource,
                "resource.spell-points",
                11,
                null,
                null,
                DateTimeOffset.UtcNow.AddSeconds(3));
            await stateStore.SetRulesInputAsync(
                characterId,
                CharacterRulesInputKind.KnownSpell,
                "spell.magic-missile",
                null,
                null,
                null,
                DateTimeOffset.UtcNow.AddSeconds(4));
            await stateStore.SetHitPointGainAsync(
                characterId,
                classId,
                1,
                10,
                DateTimeOffset.UtcNow.AddSeconds(5));
            await stateStore.SetHitPointGainAsync(
                characterId,
                classId,
                2,
                7,
                DateTimeOffset.UtcNow.AddSeconds(6));
        }

        await using (var secondContext = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(secondContext).GetAsync(characterId);
            Assert.NotNull(state);
            Assert.Equal(3, state.RulesInputs.Count);
            Assert.Contains(
                state.RulesInputs,
                value => value.Kind == CharacterRulesInputKind.Choice
                    && value.Key == "spellcasting.resource-system"
                    && value.TextValue == "spell-points");
            Assert.Contains(
                state.RulesInputs,
                value => value.Kind == CharacterRulesInputKind.Resource
                    && value.Key == "resource.spell-points"
                    && value.IntegerValue == 11);
            Assert.Contains(
                state.RulesInputs,
                value => value.Kind == CharacterRulesInputKind.KnownSpell
                    && value.Key == "spell.magic-missile");
            Assert.Collection(
                state.HitPointGains.OrderBy(value => value.ClassLevel),
                value =>
                {
                    Assert.Equal(classId, value.AdvancementOccurrenceId);
                    Assert.Equal(1, value.ClassLevel);
                    Assert.Equal(10, value.HitDieValue);
                },
                value =>
                {
                    Assert.Equal(classId, value.AdvancementOccurrenceId);
                    Assert.Equal(2, value.ClassLevel);
                    Assert.Equal(7, value.HitDieValue);
                });

            await new PostgresCharacterBuildStore(secondContext).SetAdvancementLevelAsync(
                characterId,
                classId,
                1,
                DateTimeOffset.UtcNow.AddMinutes(1));
        }

        await using (var thirdContext = new CharacterSheetDbContext(options))
        {
            var state = await new PostgresCharacterStateStore(thirdContext).GetAsync(characterId);
            Assert.NotNull(state);
            var remainingGain = Assert.Single(state.HitPointGains);
            Assert.Equal(classId, remainingGain.AdvancementOccurrenceId);
            Assert.Equal(1, remainingGain.ClassLevel);
            Assert.Equal(10, remainingGain.HitDieValue);
        }
    }

    [Fact]
    public async Task RoutineStateForeignKeysRequireAnExistingCharacterRoot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();

        await using var db = new CharacterSheetDbContext(options);
        await db.Database.MigrateAsync();
        var missingCharacterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_inventory_item_occurrences
                ("Id", "CharacterId", "RuleConceptKey", "CreatedAt")
            VALUES
                ({Guid.NewGuid()}, {missingCharacterId}, {"item:torch"}, {now});
            """));

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO character_notes
                ("Id", "CharacterId", "Content", "CreatedAt", "UpdatedAt")
            VALUES
                ({Guid.NewGuid()}, {missingCharacterId}, {"orphan"}, {now}, {now});
            """));
    }

    [Fact]
    public async Task CharacterDeletionCascadesRoutineStateWhileCampaignDeletionDoesNot()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var stateStore = new PostgresCharacterStateStore(setup);
            await stateStore.AddInventoryItemOccurrenceAsync(
                characterId,
                "item:rope",
                DateTimeOffset.UtcNow);
            await stateStore.AddNoteAsync(
                characterId,
                "Keep this through Campaign deletion.",
                DateTimeOffset.UtcNow.AddSeconds(1));

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

            Assert.Equal(1, await setup.InventoryItemOccurrences.CountAsync());
            Assert.Equal(1, await setup.CharacterNotes.CountAsync());
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
            Assert.Equal(0, await deletion.InventoryItemOccurrences.CountAsync());
            Assert.Equal(0, await deletion.CharacterNotes.CountAsync());
        }
    }
}
