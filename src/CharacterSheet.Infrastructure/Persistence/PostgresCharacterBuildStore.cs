using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

public sealed class PostgresCharacterBuildStore(CharacterSheetDbContext dbContext)
    : ICharacterBuildStore
{
    public Task<CharacterSheetRoot?> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default) =>
        BuildQuery(tracking: false)
            .SingleOrDefaultAsync(value => value.CharacterId == characterId, cancellationToken);

    public async Task<CharacterSheetRoot?> SetFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetFoundationalSelection(category, ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearFoundationalSelectionAsync(
        Guid characterId,
        CharacterFoundationalSelectionCategory category,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ClearFoundationalSelection(category, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetBaseAbilityScoreInputAsync(
        Guid characterId,
        string abilityKey,
        int score,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetBaseAbilityScoreInput(abilityKey, score, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearBaseAbilityScoreInputAsync(
        Guid characterId,
        string abilityKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ClearBaseAbilityScoreInput(abilityKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetStartingClassAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        if (!root.AdvancementEntries.Any(value =>
                value.Kind == CharacterAdvancementKind.Class
                && value.Ordinal == 0
                && value.ParentAdvancementEntryId is null))
        {
            CharacterNormalProgressionPolicy.EnsureNewProgressionAllowed(
                root.AdvancementEntries,
                CharacterAdvancementKind.Class);
        }
        root.SetStartingClass(ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearStartingClassAsync(
        Guid characterId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ClearStartingClass(changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.SetSubclassForClass(classAdvancementEntryId, ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ClearSubclassAsync(
        Guid characterId,
        Guid classAdvancementEntryId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.ClearSubclassForClass(classAdvancementEntryId, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> SetAdvancementLevelAsync(
        Guid characterId,
        Guid advancementEntryId,
        int level,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        var entry = root.AdvancementEntries.SingleOrDefault(value => value.Id == advancementEntryId)
            ?? throw new KeyNotFoundException("Character advancement entry was not found.");
        CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
            root.AdvancementEntries,
            entry,
            level);
        root.SetAdvancementLevel(advancementEntryId, level, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> ApplyProgressionAdvancementAsync(
        Guid characterId,
        Guid? advancementEntryId,
        CharacterAdvancementKind kind,
        string conceptKey,
        int? hitDieValue,
        string? subclassConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        if (kind is not CharacterAdvancementKind.Class and not CharacterAdvancementKind.PrestigeClass)
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                "Only Class and Prestige Class progression can be advanced through this transaction.");
        }
        if (kind != CharacterAdvancementKind.Class && !string.IsNullOrWhiteSpace(subclassConceptKey))
        {
            throw new InvalidOperationException("Only a base Class advancement can acquire a Subclass.");
        }

        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;

        var normalizedConceptKey = CharacterRuleReference.NormalizeConceptKey(conceptKey);
        CharacterAdvancementEntry progressionEntry;
        if (advancementEntryId is Guid occurrenceId)
        {
            progressionEntry = root.AdvancementEntries.SingleOrDefault(value => value.Id == occurrenceId)
                ?? throw new KeyNotFoundException("Character progression advancement entry was not found.");
            if (progressionEntry.Kind != kind)
            {
                throw new InvalidOperationException(
                    "The progression occurrence kind no longer matches the reviewed advancement plan.");
            }
            if (!string.Equals(progressionEntry.RuleConceptKey, normalizedConceptKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The progression occurrence no longer matches the reviewed advancement plan.");
            }
            var currentLevel = progressionEntry.Level
                ?? throw new InvalidOperationException("Progression advancement does not have a level.");
            var targetLevel = checked(currentLevel + 1);
            CharacterNormalProgressionPolicy.EnsureLevelChangeAllowed(
                root.AdvancementEntries,
                progressionEntry,
                targetLevel);
            root.SetAdvancementLevel(progressionEntry.Id, targetLevel, changedAt);
        }
        else
        {
            if (root.AdvancementEntries.Any(value =>
                    value.Kind == kind
                    && string.Equals(value.RuleConceptKey, normalizedConceptKey, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Character already has that progression occurrence.");
            }
            CharacterNormalProgressionPolicy.EnsureNewProgressionAllowed(
                root.AdvancementEntries,
                kind);
            var nextOrdinal = root.AdvancementEntries
                .Where(value => value.Kind == kind)
                .Select(value => value.Ordinal ?? -1)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            if (kind == CharacterAdvancementKind.Class && nextOrdinal == 0)
            {
                nextOrdinal = 1;
            }
            progressionEntry = root.AddAdvancement(
                kind,
                normalizedConceptKey,
                nextOrdinal,
                null,
                changedAt);
        }

        var appliedLevel = progressionEntry.Level
            ?? throw new InvalidOperationException("Applied progression advancement does not have a level.");
        if (hitDieValue is int gain)
        {
            root.SetHitPointGain(progressionEntry.Id, appliedLevel, gain, changedAt);
        }
        if (!string.IsNullOrWhiteSpace(subclassConceptKey))
        {
            root.SetSubclassForClass(progressionEntry.Id, subclassConceptKey, changedAt);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public Task<CharacterSheetRoot?> ApplyClassAdvancementAsync(
        Guid characterId,
        Guid? classAdvancementEntryId,
        string classConceptKey,
        int? hitDieValue,
        string? subclassConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default) =>
        ApplyProgressionAdvancementAsync(
            characterId,
            classAdvancementEntryId,
            CharacterAdvancementKind.Class,
            classConceptKey,
            hitDieValue,
            subclassConceptKey,
            changedAt,
            cancellationToken);

    public async Task<CharacterSheetRoot?> AddFeatOccurrenceAsync(
        Guid characterId,
        string ruleConceptKey,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.AddFeatOccurrence(ruleConceptKey, changedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return root;
    }

    public async Task<CharacterSheetRoot?> RemoveFeatOccurrenceAsync(
        Guid characterId,
        Guid featAdvancementEntryId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        var root = await GetTrackedAsync(characterId, cancellationToken);
        if (root is null) return null;
        root.RemoveFeatOccurrence(featAdvancementEntryId, changedAt);
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
            .Include(value => value.FoundationalSelections)
            .Include(value => value.BaseAbilityScoreInputs)
            .Include(value => value.AdvancementEntries)
            .Include(value => value.HitPointGains)
            .AsQueryable();
        return tracking ? query : query.AsNoTracking();
    }
}
