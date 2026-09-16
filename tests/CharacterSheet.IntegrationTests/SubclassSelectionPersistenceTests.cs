using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

public sealed class SubclassSelectionPersistenceTests
{
    [Fact]
    public async Task SubclassPersistsAsCharacterOwnedChildOfClassAndIsRemovedWithParentClass()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var options = database.CreateOptions();
        var characterId = Guid.NewGuid();
        Guid startingClassId;

        await using (var setup = new CharacterSheetDbContext(options))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(characterId);
            var store = new PostgresCharacterBuildStore(setup);
            var withClass = await store.SetStartingClassAsync(
                characterId,
                "class.wizard",
                DateTimeOffset.UtcNow);
            startingClassId = Assert.Single(withClass!.AdvancementEntries).Id;

            var withSubclass = await store.SetSubclassAsync(
                characterId,
                startingClassId,
                "subclass.wizard.evocation",
                DateTimeOffset.UtcNow.AddMinutes(1));
            var subclass = Assert.Single(
                withSubclass!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);
            Assert.Equal(startingClassId, subclass.ParentAdvancementEntryId);
        }

        await using (var verify = new CharacterSheetDbContext(options))
        {
            var store = new PostgresCharacterBuildStore(verify);
            var persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            var subclass = Assert.Single(
                persisted!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);
            Assert.Equal(startingClassId, subclass.ParentAdvancementEntryId);
            Assert.Equal("subclass.wizard.evocation", subclass.RuleConceptKey);

            await store.SetStartingClassAsync(
                characterId,
                "class.fighter",
                DateTimeOffset.UtcNow.AddMinutes(2));
            persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            Assert.DoesNotContain(
                persisted!.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Subclass);

            var fighter = Assert.Single(
                persisted.AdvancementEntries,
                value => value.Kind == CharacterAdvancementKind.Class);
            await store.SetSubclassAsync(
                characterId,
                fighter.Id,
                "subclass.fighter.champion",
                DateTimeOffset.UtcNow.AddMinutes(3));
            await store.ClearStartingClassAsync(
                characterId,
                DateTimeOffset.UtcNow.AddMinutes(4));

            persisted = await store.GetAsync(characterId);
            Assert.NotNull(persisted);
            Assert.Empty(persisted!.AdvancementEntries);
        }
    }
}
