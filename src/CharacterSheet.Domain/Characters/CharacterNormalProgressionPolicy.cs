namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Guards aggregate Character progression arithmetic without imposing game-rule maxima.
/// Rules Core owns rules-derived Class, Prestige Class, and Character progression limits;
/// Character Sheet retains only technical persistence invariants.
/// </summary>
public static class CharacterNormalProgressionPolicy
{
    public static int TotalCharacterLevel(IEnumerable<CharacterAdvancementEntry> entries) =>
        entries
            .Where(value => value.Kind is CharacterAdvancementKind.Class or CharacterAdvancementKind.PrestigeClass)
            .Sum(value => value.Level ?? 0);

    public static void EnsureCharacterLevelChangeAllowed(
        int currentCharacterLevel,
        int proposedCharacterLevel)
    {
        if (currentCharacterLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentCharacterLevel),
                "Current Character level can not be negative.");
        }
        if (proposedCharacterLevel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposedCharacterLevel),
                "Proposed Character level can not be negative.");
        }
    }

    public static void EnsureLevelChangeAllowed(
        IEnumerable<CharacterAdvancementEntry> entries,
        CharacterAdvancementEntry entry,
        int proposedLevel)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Kind is not CharacterAdvancementKind.Class
            and not CharacterAdvancementKind.PrestigeClass)
        {
            return;
        }
        if (proposedLevel <= 0 || proposedLevel > CharacterAdvancementEntry.MaxSupportedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(proposedLevel),
                $"Advancement level must be from 1 through {CharacterAdvancementEntry.MaxSupportedLevel}.");
        }

        var currentTotal = TotalCharacterLevel(entries);
        var currentLevel = entry.Level ?? 0;
        var proposedTotal = checked(currentTotal - currentLevel + proposedLevel);
        EnsureCharacterLevelChangeAllowed(currentTotal, proposedTotal);
    }

    public static void EnsureNewProgressionAllowed(
        IEnumerable<CharacterAdvancementEntry> entries,
        CharacterAdvancementKind kind,
        int level = 1)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (kind is not CharacterAdvancementKind.Class
            and not CharacterAdvancementKind.PrestigeClass)
        {
            return;
        }
        if (level <= 0 || level > CharacterAdvancementEntry.MaxSupportedLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                $"Advancement level must be from 1 through {CharacterAdvancementEntry.MaxSupportedLevel}.");
        }

        var currentTotal = TotalCharacterLevel(entries);
        var proposedTotal = checked(currentTotal + level);
        EnsureCharacterLevelChangeAllowed(currentTotal, proposedTotal);
    }
}
