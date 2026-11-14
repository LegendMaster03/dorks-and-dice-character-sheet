namespace CharacterSheet.Domain.Characters;

/// <summary>
/// The legacy Species/Subspecies builder may not change the underlying
/// configuration once an authoritative Origin has been committed.
/// This is not a denial of an authorized future Origin or DM override workflow.
/// </summary>
public sealed class CharacterOriginManagedException : InvalidOperationException
{
    public CharacterOriginManagedException() : base(
        "The character's Species and Ancestry are managed by the authorized Origin profile.")
    {
    }
}
