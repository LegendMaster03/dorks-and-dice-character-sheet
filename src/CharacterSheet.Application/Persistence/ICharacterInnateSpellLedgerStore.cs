using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// Prepared ONLY by a trusted application evaluator after authenticating the
/// Site character owner, active character state, committed Origin revision,
/// native spell projection and the exact proposed ledger transition.
/// This record and the store do not themselves authenticate those claims.
/// Do not bind this command directly from an HTTP request.
/// </summary>
public sealed record VerifiedInnateSpellLedgerPersistenceCommand(
    Guid CharacterId, string RulesetId, string Fingerprint,
    string OriginProfileId, string ProposalId, long ExpectedOriginRevision,
    long? ExpectedLedgerRevision, string LedgerJson,
    string EventId, Guid ActorUserId, string AuthorizationEvidenceId,
    string Operation, DateTimeOffset RecordedAt,
    string? AppliedDecisionIdsJson = null);

public enum InnateSpellLedgerStoreStatus
{
    Applied,
    SheetNotInitialized,
    AuthorizedOriginMissing,
    OriginRevisionConflict,
    OriginIdentityConflict,
    LedgerAlreadyInitialized,
    LedgerMissing,
    LedgerRevisionConflict,
    DuplicateEvent
}

public sealed record InnateSpellLedgerStoreResult(
    InnateSpellLedgerStoreStatus Status, long? Revision = null);

/// <summary>
/// Durable, character-owned CAS boundary. The caller must verify all gameplay,
/// account and event authorization before constructing the persistence command.
/// Implementations must atomically commit snapshot plus immutable event receipt.
/// </summary>
public interface ICharacterInnateSpellLedgerStore
{
    Task<CharacterInnateSpellLedgerState?> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterInnateSpellLedgerReceipt>> GetEventsAsync(
        Guid characterId, CancellationToken cancellationToken = default);

    Task<InnateSpellLedgerStoreResult> CommitVerifiedAsync(
        VerifiedInnateSpellLedgerPersistenceCommand command,
        CancellationToken cancellationToken = default);
}
