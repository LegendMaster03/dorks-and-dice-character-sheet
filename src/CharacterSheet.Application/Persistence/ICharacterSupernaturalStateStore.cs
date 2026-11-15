using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

public enum CharacterSupernaturalWriteStatus
{
    Applied, SheetNotInitialized, RevisionConflict, LedgerRevisionConflict,
    DuplicateEvent, AuthoritativeInputsChanged
}

/// <summary>
/// A trusted application-service output AFTER actual Site authorization, native
/// source verification and a successful Rules Core resolution/transition plan.
/// Never bind this command from HTTP or treat its evidence strings as proof.
/// The persistence store enforces atomicity, not gameplay authorization.
/// </summary>
public sealed record VerifiedSupernaturalStateWrite(
    Guid CharacterId, string RulesetId, string Fingerprint,
    string AlternateSheetsJson, string PersistentInfluencesJson, string LedgerJson,
    long ExpectedRevision, long ExpectedLedgerRevision, long NextLedgerRevision,
    string ExpectedDependencyFingerprint, string EventId,
    string AuthorizationEvidenceId, bool IsTransition, DateTimeOffset RecordedAt);

public sealed record CharacterSupernaturalWriteResult(
    CharacterSupernaturalWriteStatus Status, long? Revision = null,
    long? LedgerRevision = null);

/// <summary>
/// Internal-only durable ownership boundary. No unprivileged request route may
/// call CommitVerifiedAsync. A read digest is advisory until repeated under
/// PostgreSQL serializable isolation in the commit transaction.
/// </summary>
public interface ICharacterSupernaturalStateStore
{
    Task<CharacterSupernaturalState?> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterSupernaturalEventReceipt>> GetEventsAsync(
        Guid characterId, CancellationToken cancellationToken = default);

    Task<string?> GetAuthoritativeInputsFingerprintAsync(
        Guid characterId, CancellationToken cancellationToken = default);

    Task<CharacterSupernaturalWriteResult> CommitVerifiedAsync(
        VerifiedSupernaturalStateWrite command, CancellationToken cancellationToken = default);
}
