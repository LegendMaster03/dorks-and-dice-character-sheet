using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Persistence;

/// <summary>
/// An input from an application service that independently authenticated owner,
/// campaign scope, current state, Rules Core outcome, entitlement sources and
/// completion evidence. This record does NOT verify any of those claims itself.
/// It must never be bound directly from a client request.
/// </summary>
public sealed record VerifiedOriginPersistenceCommand(
    Guid CharacterId,
    string RulesetId,
    string Fingerprint,
    string ProfileJson,
    long ExpectedOriginRevision,
    long? ExpectedDraftRevision,
    string EventId,
    string MethodRuleId,
    string MethodId,
    string AuthorizationKind,
    string AuthorizationEvidenceId,
    DateTimeOffset RecordedAt,
    Guid? ApprovalActorUserId = null,
    Guid? ApprovalCampaignId = null,
    string? ApprovedDecisionsJson = null,
    string? EvaluatedProjectionJson = null,
    // The exact draft identity read before delegated native evaluation.
    // Production initial/mixed activation must supply both values; optional
    // defaults preserve legacy internal fixture commands, not authorization.
    string? ExpectedDraftProposalJson = null,
    string? ExpectedDraftFingerprint = null);

public enum AuthorizedOriginStoreStatus
{
    Applied,
    OriginRevisionConflict,
    DraftRevisionConflict,
    LegacyOriginSelectionConflict,
    DuplicateEvent,
    SheetNotInitialized
}

public sealed record AuthorizedOriginStoreResult(
    AuthorizedOriginStoreStatus Status, long? Revision = null);

/// <summary>
/// Character-owned durable Origin snapshots and completed-event receipts.
/// No public route may write through this store without trusted re-evaluation.
/// All changes to the snapshot and receipt are one PostgreSQL transaction.
/// </summary>
public interface ICharacterAuthorizedOriginStore
{
    Task<CharacterAuthorizedOriginState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CharacterOriginEventReceipt>> GetEventsAsync(Guid characterId,
        CancellationToken cancellationToken = default);

    Task<AuthorizedOriginStoreResult> CommitVerifiedAsync(
        VerifiedOriginPersistenceCommand command,
        CancellationToken cancellationToken = default);
}
