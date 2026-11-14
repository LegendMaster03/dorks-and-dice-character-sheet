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
        var verifiedShape = new CharacterAuthorizedOriginState(command.CharacterId, command.RulesetId,
            command.Fingerprint, command.ProfileJson,
            command.ExpectedDraftRevision, command.RecordedAt,
            command.EvaluatedProjectionJson);
        _ = new CharacterOriginEventReceipt(command.CharacterId, command.EventId,
            command.ExpectedOriginRevision + 1, command.MethodRuleId, command.MethodId,
            command.AuthorizationKind, command.AuthorizationEvidenceId, command.RecordedAt,
            command.ApprovalActorUserId, command.ApprovalCampaignId,
            command.ApprovedDecisionsJson);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Acquire the canonical character lock before any draft, snapshot, or
        // event locks. Species/Subspecies legacy writes take this same lock.
        // This also prevents a new initial Origin from being installed during
        // a concurrent legacy foundational-selection mutation.
        var lockedCharacter = await db.CharacterSheets
            .FromSqlInterpolated(
                $"""SELECT * FROM character_sheet_roots WHERE "CharacterId" = {command.CharacterId} FOR UPDATE""")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (lockedCharacter is null)
            return new(AuthorizedOriginStoreStatus.SheetNotInitialized);

        if (await db.Set<CharacterFoundationalRuleSelection>().AsNoTracking()
            .AnyAsync(selection =>
                selection.CharacterId == command.CharacterId &&
                (selection.Category == CharacterFoundationalSelectionCategory.Species ||
                 selection.Category == CharacterFoundationalSelectionCategory.Subspecies ||
                 selection.Category == CharacterFoundationalSelectionCategory.RaceSpecies),
                cancellationToken))
            return new(AuthorizedOriginStoreStatus.LegacyOriginSelectionConflict);

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
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (await db.OriginEventReceipts.AsNoTracking().AnyAsync(
            x => x.CharacterId == command.CharacterId && x.EventId == command.EventId,
            cancellationToken))
            return new(AuthorizedOriginStoreStatus.DuplicateEvent, state?.Revision);

        if (state is null && command.ExpectedOriginRevision != 0 ||
            state is not null && state.Revision != command.ExpectedOriginRevision)
            return new(AuthorizedOriginStoreStatus.OriginRevisionConflict, state?.Revision);

        var nextRevision = command.ExpectedOriginRevision + 1;
        if (state is null)
        {
            if (!await db.CharacterSheets.AsNoTracking().AnyAsync(
                x => x.CharacterId == command.CharacterId, cancellationToken))
                return new(AuthorizedOriginStoreStatus.SheetNotInitialized);
            // New origin and first receipt are inserted together. An external
            // writer racing this insert will lose to the PK/unique constraint.
            db.AuthorizedOrigins.Add(verifiedShape);
        }
        else
        {
            // ExecuteUpdate avoids previously tracked instances in a reused
            // request context overwriting an origin committed by another writer.
            // The revision predicate remains mandatory even after FOR UPDATE.
            var affected = await db.AuthorizedOrigins
                .Where(x => x.CharacterId == command.CharacterId &&
                    x.Revision == command.ExpectedOriginRevision)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.RulesetId, verifiedShape.RulesetId)
                    .SetProperty(x => x.Fingerprint, verifiedShape.Fingerprint)
                    .SetProperty(x => x.ProfileJson, verifiedShape.ProfileJson)
                    .SetProperty(x => x.EvaluatedProjectionJson, verifiedShape.EvaluatedProjectionJson)
                    .SetProperty(x => x.SourceDraftRevision, verifiedShape.SourceDraftRevision)
                    .SetProperty(x => x.Revision, nextRevision)
                    .SetProperty(x => x.UpdatedAt, command.RecordedAt),
                    cancellationToken);
            if (affected != 1)
                return new(AuthorizedOriginStoreStatus.OriginRevisionConflict);
        }

        db.OriginEventReceipts.Add(new CharacterOriginEventReceipt(
            command.CharacterId, command.EventId, nextRevision,
            command.MethodRuleId, command.MethodId, command.AuthorizationKind,
            command.AuthorizationEvidenceId, command.RecordedAt,
            command.ApprovalActorUserId, command.ApprovalCampaignId,
            command.ApprovedDecisionsJson));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(AuthorizedOriginStoreStatus.Applied, nextRevision);
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
