using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Durable compare-and-swap and receipt persistence for previously verified
/// native Origin spell ledger transitions. This is NOT a native rules evaluator
/// or an account/DM authorizer. The application must revalidate the exact
/// Rules Core transition and authenticate the actor before calling it.
/// </summary>
public sealed class PostgresCharacterInnateSpellLedgerStore(CharacterSheetDbContext db)
    : ICharacterInnateSpellLedgerStore
{
    public Task<CharacterInnateSpellLedgerState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default) =>
        db.InnateSpellLedgers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);

    public async Task<IReadOnlyList<CharacterInnateSpellLedgerReceipt>> GetEventsAsync(
        Guid characterId, CancellationToken cancellationToken = default) =>
        await db.InnateSpellLedgerReceipts.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .OrderBy(x => x.CommittedRevision)
            .ToArrayAsync(cancellationToken);

    public async Task<InnateSpellLedgerStoreResult> CommitVerifiedAsync(
        VerifiedInnateSpellLedgerPersistenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CharacterId == Guid.Empty || command.ExpectedOriginRevision < 1 ||
            command.ExpectedLedgerRevision is < 0 or long.MaxValue)
            throw new ArgumentException("Expected Origin/ledger revision is invalid.", nameof(command));

        // Bootstrap revision is zero and may occur only when no ledger row
        // exists. The next ledger must be exactly one revision newer.
        var nextRevision = command.ExpectedLedgerRevision is { } expected
            ? expected + 1 : 0;
        var shape = new CharacterInnateSpellLedgerState(
            command.CharacterId, command.RulesetId, command.Fingerprint,
            command.OriginProfileId, command.ProposalId,
            command.ExpectedOriginRevision, nextRevision,
            command.LedgerJson, command.RecordedAt);
        _ = new CharacterInnateSpellLedgerReceipt(command.CharacterId,
            command.EventId, nextRevision, command.ExpectedOriginRevision,
            command.ActorUserId, command.AuthorizationEvidenceId,
            command.Operation, command.RecordedAt, command.AppliedDecisionIdsJson);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Preserve existing lock order: character root first, then committed
        // Origin, then the free-cast ledger. A competing Origin revision change
        // cannot race this ledger update after the Origin lock.
        var character = await db.CharacterSheets.FromSqlInterpolated(
            $"""SELECT * FROM character_sheet_roots WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (character is null)
            return new(InnateSpellLedgerStoreStatus.SheetNotInitialized);

        var authorizedOrigin = await db.AuthorizedOrigins.FromSqlInterpolated(
            $"""SELECT * FROM character_authorized_origins WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (authorizedOrigin is null)
            return new(InnateSpellLedgerStoreStatus.AuthorizedOriginMissing);
        if (authorizedOrigin.Revision != command.ExpectedOriginRevision)
            return new(InnateSpellLedgerStoreStatus.OriginRevisionConflict);
        if (authorizedOrigin.RulesetId != command.RulesetId ||
            authorizedOrigin.Fingerprint != command.Fingerprint ||
            CharacterInnateSpellLedgerState.ReadProfileId(
                authorizedOrigin.ProfileJson) != command.OriginProfileId)
            return new(InnateSpellLedgerStoreStatus.OriginIdentityConflict);

        var current = await db.InnateSpellLedgers.FromSqlInterpolated(
            $"""SELECT * FROM character_innate_spell_ledgers WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (await db.InnateSpellLedgerReceipts.AsNoTracking().AnyAsync(
            x => x.CharacterId == command.CharacterId &&
                x.EventId == command.EventId, cancellationToken))
            return new(InnateSpellLedgerStoreStatus.DuplicateEvent, current?.LedgerRevision);

        if (current is null && command.ExpectedLedgerRevision is not null)
            return new(InnateSpellLedgerStoreStatus.LedgerMissing);
        if (current is not null && command.ExpectedLedgerRevision is null)
            return new(InnateSpellLedgerStoreStatus.LedgerAlreadyInitialized,
                current.LedgerRevision);
        if (current is not null &&
            current.LedgerRevision != command.ExpectedLedgerRevision)
            return new(InnateSpellLedgerStoreStatus.LedgerRevisionConflict,
                current.LedgerRevision);
        if (current is not null &&
            (current.RulesetId != command.RulesetId ||
             current.Fingerprint != command.Fingerprint ||
             current.OriginProfileId != command.OriginProfileId ||
             current.ProposalId != command.ProposalId ||
             current.OriginRevision != command.ExpectedOriginRevision))
            return new(InnateSpellLedgerStoreStatus.OriginIdentityConflict,
                current.LedgerRevision);

        if (current is null)
        {
            db.InnateSpellLedgers.Add(shape);
        }
        else
        {
            var count = await db.InnateSpellLedgers
                .Where(x => x.CharacterId == command.CharacterId &&
                    x.LedgerRevision == command.ExpectedLedgerRevision &&
                    x.OriginRevision == command.ExpectedOriginRevision)
                .ExecuteUpdateAsync(setter => setter
                    .SetProperty(x => x.LedgerJson, shape.LedgerJson)
                    .SetProperty(x => x.LedgerRevision, nextRevision)
                    .SetProperty(x => x.UpdatedAt, command.RecordedAt),
                    cancellationToken);
            if (count != 1)
                return new(InnateSpellLedgerStoreStatus.LedgerRevisionConflict);
        }

        db.InnateSpellLedgerReceipts.Add(new CharacterInnateSpellLedgerReceipt(
            command.CharacterId, command.EventId, nextRevision,
            command.ExpectedOriginRevision, command.ActorUserId,
            command.AuthorizationEvidenceId, command.Operation,
            command.RecordedAt, command.AppliedDecisionIdsJson));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return new(InnateSpellLedgerStoreStatus.Applied, nextRevision);
        }
        catch (DbUpdateException error) when (
            error.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Unique character/revision and character/event constraints are
            // database-level replay protection even under racing requests.
            db.ChangeTracker.Clear();
            return new(InnateSpellLedgerStoreStatus.LedgerRevisionConflict);
        }
    }
}
