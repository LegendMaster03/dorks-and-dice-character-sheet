namespace CharacterSheet.Application.Characters;

/// <summary>
/// Only choices in the native-option namespace participate in native eligibility.
/// Other Character-owned choices (such as starting equipment) retain their own
/// validation and do not become class features. Malformed native slots fail closed.
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
            // Sheet-owned choices must not invalidate native class mechanics.
            // The bare namespace name remains an invalid native-option slot.
            if (!choice.Key.StartsWith(SlotPrefix, StringComparison.Ordinal))
            {
                if (choice.Key == "native-option") unmapped = true;
                continue;
            }
            var value = choice.TextValue;
            if (choice.Key.Length <= SlotPrefix.Length ||
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
