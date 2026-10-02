namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Defines the Character Sheet progression range supported by the current non-epic workflow.
/// Existing higher-level records remain readable and may be corrected downward. The planned
/// level-21+ rules update can replace this temporary ceiling with epic progression semantics.
/// </summary>
public static class CharacterNormalProgressionPolicy
{
    public const int MaximumCharacterLevel = 20;

    public static int TotalCharacterLevel(IEnumerable<CharacterAdvancementEntry> entries) =>
        entries
            .Where(value => value.Kind is CharacterAdvancementKind.Class or CharacterAdvancementKind.PrestigeClass)
            .Sum(value => value.Level ?? 0);

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

        var currentTotal = TotalCharacterLevel(entries);
        var currentLevel = entry.Level ?? 0;
        var proposedTotal = checked(currentTotal - currentLevel + proposedLevel);
        EnsureSupportedIncrease(currentTotal, proposedTotal);
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

        var currentTotal = TotalCharacterLevel(entries);
        var proposedTotal = checked(currentTotal + level);
        EnsureSupportedIncrease(currentTotal, proposedTotal);
    }

    private static void EnsureSupportedIncrease(int currentTotal, int proposedTotal)
    {
        if (proposedTotal <= MaximumCharacterLevel)
        {
            return;
        }

        // Legacy/future epic records must remain editable enough to be corrected downward. Do not
        // permit an unsupported >20 Character to increase farther before epic rules are implemented.
        if (currentTotal > MaximumCharacterLevel && proposedTotal <= currentTotal)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Normal Character progression currently supports a maximum total level of {MaximumCharacterLevel}. Level 21+ progression is reserved for the planned epic-level rules update.");
    }
}
