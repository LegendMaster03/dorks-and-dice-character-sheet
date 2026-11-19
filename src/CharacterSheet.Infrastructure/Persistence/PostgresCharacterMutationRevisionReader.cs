using CharacterSheet.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Reads the committed character aggregate revision independently of an EF
/// tracked root. It is a pre-evaluation token, not a database lock: the ledger
/// commit obtains a root FOR UPDATE lock and validates this exact value.
/// </summary>
public sealed class PostgresCharacterMutationRevisionReader(
    CharacterSheetDbContext db) : ICharacterMutationRevisionReader
{
    public Task<long?> GetRevisionAsync(Guid characterId,
        CancellationToken cancellationToken = default) =>
        db.CharacterSheets.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .Select(x => (long?)x.MutationRevision)
            .SingleOrDefaultAsync(cancellationToken);
}
