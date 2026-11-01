namespace CharacterSheet.Application.Characters;

/// <summary>
/// Native options occupy ordinary Character-owned choice slots. Unknown choices
/// are preserved in state but cannot silently authorize a native advancement.
/// Slot keys distinguish multiple Invocations without duplicating rule IDs.
/// </summary>
public static class NativeCharacterOptionSelections
{
    public const string SlotPrefix = "native-option.";

    public sealed record Result(IReadOnlyList<string> Ids, bool HasUnmappedChoices);

    public static Result FromState(CharacterStateView? state)
    {
        var ids = new List<string>();
        var slots = new HashSet<string>(StringComparer.Ordinal);
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var unmapped = false;

        foreach (var choice in state?.RulesInputs ?? [])
        {
            if (choice.Kind != CharacterRulesInputKinds.Choice) continue;
            var value = choice.TextValue;
            if (!choice.Key.StartsWith(SlotPrefix, StringComparison.Ordinal) ||
                choice.Key.Length <= SlotPrefix.Length ||
                choice.Key.Length > 160 ||
                string.IsNullOrWhiteSpace(value) ||
                value.Length > 160 || value != value.Trim() ||
                !slots.Add(choice.Key) ||
                !distinct.Add(value))
            {
                unmapped = true;
                continue;
            }
            ids.Add(value);
        }
        return new(ids.ToArray(), unmapped);
    }
}
