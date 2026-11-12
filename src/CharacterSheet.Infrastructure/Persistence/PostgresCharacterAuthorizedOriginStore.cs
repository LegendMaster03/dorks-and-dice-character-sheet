using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// A narrow PostgreSQL compare-and-swap boundary, not a rules authorizer.
/// The initiating application service must first verify the complete Origin
/// result, authenticated ownership, campaign permissions and event evidence.
/// </summary>
public sealed class PostgresCharacterAuthorizedOriginStore(CharacterSheetDbContext db)
    : ICharacterAuthorizedOriginStore
{
    public Task<CharacterAuthorizedOriginState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default) =>
        db.AuthorizedOrigins.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CharacterId == characterId, cancellationToken);

    public async Task<IReadOnlyList<CharacterOriginEventReceipt>> GetEventsAsync(
        Guid characterId, CancellationToken cancellationToken = default) =>
        await db.OriginEventReceipts.AsNoTracking()
            .Where(x => x.CharacterId == characterId)
            .OrderBy(x => x.CommittedRevision)
            .ToArrayAsync(cancellationToken);

    public async Task<AuthorizedOriginStoreResult> CommitVerifiedAsync(
        VerifiedOriginPersistenceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CharacterId == Guid.Empty ||
            command.ExpectedOriginRevision < 0 ||
            command.ExpectedOriginRevision == long.MaxValue ||
            command.ExpectedDraftRevision is < 1)
            throw new ArgumentException("Invalid expected Origin or draft revision.", nameof(command));

        // Validate all input before beginning the database transaction. This
        // does not confer authority; callers must have verified the result.
        _ = new CharacterAuthorizedOriginState(command.CharacterId, command.RulesetId,
            command.Fingerprint, command.ProfileJson,
            command.ExpectedDraftRevision, command.RecordedAt);
        _ = new CharacterOriginEventReceipt(command.CharacterId, command.EventId,
            command.ExpectedOriginRevision + 1, command.MethodRuleId, command.MethodId,
            command.AuthorizationKind, command.AuthorizationEvidenceId, command.RecordedAt);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (command.ExpectedDraftRevision is long draftRevision)
        {
            // Lock the exact draft row until commit. A concurrent change to the
            // proposal cannot silently turn an older review into an active Origin.
            var draft = await db.CharacterOriginDrafts
                .FromSqlInterpolated($"""
                    SELECT * FROM character_origin_drafts
                    WHERE "CharacterId" = {command.CharacterId} FOR UPDATE
                    """)
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (draft is null || draft.Revision != draftRevision ||
                draft.RulesetId != command.RulesetId)
                return new(AuthorizedOriginStoreStatus.DraftRevisionConflict);
        }

        // Serialize revisions under a row lock, then append one immutable event.
        var state = await db.AuthorizedOrigins
            .FromSqlInterpolated($"""
                SELECT * FROM character_authorized_origins
                WHERE "CharacterId" = {command.CharacterId} FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);

        if (await db.OriginEventReceipts.AsNoTracking().AnyAsync(
            x => x.CharacterId == command.CharacterId && x.EventId == command.EventId,
            cancellationToken))
            return new(AuthorizedOriginStoreStatus.DuplicateEvent, state?.Revision);

        if (state is null && command.ExpectedOriginRevision != 0 ||
            state is not null && state.Revision != command.ExpectedOriginRevision)
            return new(AuthorizedOriginStoreStatus.OriginRevisionConflict, state?.Revision);

        if (state is null)
        {
            if (!await db.CharacterSheets.AsNoTracking().AnyAsync(
                x => x.CharacterId == command.CharacterId, cancellationToken))
                return new(AuthorizedOriginStoreStatus.SheetNotInitialized);
            state = new CharacterAuthorizedOriginState(
                command.CharacterId, command.RulesetId, command.Fingerprint,
                command.ProfileJson, command.ExpectedDraftRevision, command.RecordedAt);
            db.AuthorizedOrigins.Add(state);
        }
        else
        {
            state.Replace(command.RulesetId, command.Fingerprint, command.ProfileJson,
                command.ExpectedDraftRevision, command.RecordedAt);
        }

        db.OriginEventReceipts.Add(new CharacterOriginEventReceipt(
            command.CharacterId, command.EventId, state.Revision,
            command.MethodRuleId, command.MethodId, command.AuthorizationKind,
            command.AuthorizationEvidenceId, command.RecordedAt));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AuthorizedOriginStoreStatus.Applied, state.Revision);
        }
        catch (DbUpdateException error) when (
            error.InnerException is PostgresException postgres &&
            postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // A concurrent first commit can win without a preexisting row lock.
            // The losing transaction is disposed without publishing either row.
            db.ChangeTracker.Clear();
            return new(AuthorizedOriginStoreStatus.OriginRevisionConflict);
        }
    }
}
