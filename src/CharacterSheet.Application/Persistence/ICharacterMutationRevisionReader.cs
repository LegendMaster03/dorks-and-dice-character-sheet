namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Read the authoritative character aggregate mutation revision. This token
/// covers abilities, class progression, native option selections and other
/// aggregate state changes, including writes whose timestamps do not advance.
/// A native evaluator captures the revision BEFORE reading character mechanics;
/// the ledger store checks it under a locked root row at commit.
/// </summary>
public interface ICharacterMutationRevisionReader
{
    Task<long?> GetRevisionAsync(Guid characterId,
        CancellationToken cancellationToken = default);
}
