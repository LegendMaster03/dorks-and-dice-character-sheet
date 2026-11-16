using System.Text.Json;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Obtains an actor identity from the already-redeemed Site session. The
/// supplied operation may not dictate the authenticated actor or ownership.
/// </summary>
public interface ICharacterInnateSpellActorSource
{
    Guid? GetActorUserId();
}

public enum CharacterInnateSpellLedgerAction
{
    Initialize, SpendFreeCast, ShortRest, LongRest, Advance
}

public enum CharacterInnateSpellLedgerStatus
{
    Ready, Applied, Unauthenticated, NotFoundOrNotOwned,
    ProjectionUnavailable, ArchivedReadOnly, OriginNotCommitted,
    LedgerMissing, LedgerAlreadyInitialized, RevisionConflict,
    OriginChanged, InvalidAction, NativeUnresolved, NativeUnavailable
}

/// <summary>
/// Only a trusted native adapter, backed by current authoritative character
/// progression and re-evaluated Rules Core grants, can return a candidate.
/// This is deliberately NOT a browser-facing or input-echoing contract.
/// Implementations must evaluate the exact requested action against the
/// supplied committed Origin and previous saved ledger snapshot.
/// </summary>
public interface ITrustedInnateSpellLedgerEvaluator
{
    Task<TrustedInnateSpellLedgerCandidate?> CalculateAsync(
        TrustedInnateSpellLedgerEvaluation input,
        CancellationToken cancellationToken);
}

public sealed record TrustedInnateSpellLedgerEvaluation(
    Guid CharacterId, CharacterAuthorizedOriginState CommittedOrigin,
    CharacterInnateSpellLedgerState? CurrentLedger,
    CharacterInnateSpellLedgerAction Action, string? GrantId,
    Guid SiteActorId);

/// <summary>
/// Created by trusted native computation, never deserialized from a client.
/// EvidenceId refers to the verifier's independently established evidence;
/// the application still validates the shape before storage and never grants
/// a new Origin or creates a substitute for the native verifier.
/// </summary>
public sealed record TrustedInnateSpellLedgerCandidate(
    string LedgerJson, string OriginProfileId, string ProposalId,
    string EvidenceId, IReadOnlyList<string>? AppliedCapacityDecisionIds = null);

public sealed record CharacterInnateSpellLedgerResult(
    CharacterInnateSpellLedgerStatus Status,
    CharacterInnateSpellLedgerState? Ledger = null,
    long? Revision = null);

/// <summary>
/// Character Sheet's owner/actor and application-to-persistence gate.
/// State-changing operations are internal-only until an independently
/// implemented and registered native verifier exists. Site authorization
/// precedes all character-owned database reads, and the exact stored
/// revision is rechecked transactionally by the PostgreSQL store.
/// </summary>
public sealed class CharacterInnateSpellLedgerService(
    ISiteCharacterAccessGateway site,
    ICharacterInnateSpellActorSource actorSource,
    ICharacterAuthorizedOriginStore originStore,
    ICharacterInnateSpellLedgerStore ledgerStore,
    ITrustedInnateSpellLedgerEvaluator native,
    TimeProvider clock)
{
    public async Task<CharacterInnateSpellLedgerResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        var access = await CheckSiteAsync(characterId, cancellationToken,
            requiresEditing: false);
        if (access != CharacterInnateSpellLedgerStatus.Ready)
            return new(access);
        if (await originStore.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterInnateSpellLedgerStatus.OriginNotCommitted);
        var ledger = await ledgerStore.GetAsync(characterId, cancellationToken);
        return ledger is null
            ? new(CharacterInnateSpellLedgerStatus.LedgerMissing)
            : new(CharacterInnateSpellLedgerStatus.Ready, ledger,
                ledger.LedgerRevision);
    }

    /// <summary>
    /// No submitted spell entitlement, balance, profile, actor, fingerprint,
    /// receipt or decision may enter this operation. A client-facing route
    /// must separately restrict the exposed actions before calling it.
    /// </summary>
    public async Task<CharacterInnateSpellLedgerResult> ApplyAsync(
        Guid characterId, CharacterInnateSpellLedgerAction action,
        string? grantId, long? expectedLedgerRevision,
        CancellationToken cancellationToken = default)
    {
        var access = await CheckSiteAsync(characterId, cancellationToken,
            requiresEditing: true);
        if (access != CharacterInnateSpellLedgerStatus.Ready)
            return new(access);
        // Read-only Site owner access is insufficient for applying any action
        // without a fresh, nonempty actor from the redeemed Site ticket.
        var actor = actorSource.GetActorUserId();
        if (actor is not Guid siteActor || siteActor == Guid.Empty)
            return new(CharacterInnateSpellLedgerStatus.Unauthenticated);

        // No actions accepted implicitly, no arbitrary grant name, and no
        // cast or rest is silently inferred from a displayed UI state.
        if (!Enum.IsDefined(action) ||
            (action == CharacterInnateSpellLedgerAction.SpendFreeCast
                ? string.IsNullOrWhiteSpace(grantId) || grantId.Length > 500
                : grantId is not null) ||
            expectedLedgerRevision is < 0)
            return new(CharacterInnateSpellLedgerStatus.InvalidAction);

        var origin = await originStore.GetAsync(characterId, cancellationToken);
        if (origin is null)
            return new(CharacterInnateSpellLedgerStatus.OriginNotCommitted);
        var stored = await ledgerStore.GetAsync(characterId, cancellationToken);
        if (action == CharacterInnateSpellLedgerAction.Initialize)
        {
            if (expectedLedgerRevision is not null)
                return new(CharacterInnateSpellLedgerStatus.InvalidAction);
            if (stored is not null)
                return new(CharacterInnateSpellLedgerStatus.LedgerAlreadyInitialized,
                    stored, stored.LedgerRevision);
        }
        else
        {
            if (stored is null)
                return new(CharacterInnateSpellLedgerStatus.LedgerMissing);
            if (expectedLedgerRevision is null ||
                stored.LedgerRevision != expectedLedgerRevision)
                return new(CharacterInnateSpellLedgerStatus.RevisionConflict);
            if (stored.OriginRevision != origin.Revision ||
                stored.RulesetId != origin.RulesetId ||
                stored.Fingerprint != origin.Fingerprint)
                return new(CharacterInnateSpellLedgerStatus.OriginChanged);
        }

        var profileId = CharacterInnateSpellLedgerState.ReadProfileId(origin.ProfileJson);
        if (stored is not null && stored.OriginProfileId != profileId)
            return new(CharacterInnateSpellLedgerStatus.OriginChanged);

        TrustedInnateSpellLedgerCandidate? candidate;
        try
        {
            candidate = await native.CalculateAsync(
                new(characterId, origin, stored, action, grantId, siteActor),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (error is InvalidOperationException or
            NotSupportedException or JsonException)
        {
            return new(CharacterInnateSpellLedgerStatus.NativeUnavailable);
        }
        if (candidate is null)
            return new(CharacterInnateSpellLedgerStatus.NativeUnresolved);
        if (candidate.OriginProfileId != profileId ||
            string.IsNullOrWhiteSpace(candidate.ProposalId) ||
            candidate.ProposalId.Length > 240 ||
            (stored is not null && candidate.ProposalId != stored.ProposalId) ||
            string.IsNullOrWhiteSpace(candidate.EvidenceId) ||
            candidate.EvidenceId.Length > 320)
            return new(CharacterInnateSpellLedgerStatus.NativeUnresolved);

        if (stored is not null && stored.LedgerRevision == long.MaxValue)
            return new(CharacterInnateSpellLedgerStatus.RevisionConflict);
        var nextRevision = stored is null ? 0 : stored.LedgerRevision + 1;
        try
        {
            _ = new CharacterInnateSpellLedgerState(
                characterId, origin.RulesetId, origin.Fingerprint,
                profileId, candidate.ProposalId, origin.Revision,
                nextRevision, candidate.LedgerJson, clock.GetUtcNow());
        }
        catch (ArgumentException)
        {
            return new(CharacterInnateSpellLedgerStatus.NativeUnresolved);
        }

        string? decisionsJson = null;
        if (candidate.AppliedCapacityDecisionIds is { Count: > 0 } ids)
        {
            if (action != CharacterInnateSpellLedgerAction.Advance ||
                ids.Count > 128 ||
                ids.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 240) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                return new(CharacterInnateSpellLedgerStatus.NativeUnresolved);
            decisionsJson = JsonSerializer.Serialize(ids);
        }
        var commit = await ledgerStore.CommitVerifiedAsync(
            new VerifiedInnateSpellLedgerPersistenceCommand(
                characterId, origin.RulesetId, origin.Fingerprint,
                profileId, candidate.ProposalId, origin.Revision,
                expectedLedgerRevision, candidate.LedgerJson,
                "native-innate:" + Guid.NewGuid().ToString("N"), siteActor,
                candidate.EvidenceId, action.ToString(), clock.GetUtcNow(),
                decisionsJson), cancellationToken);
        return commit.Status switch
        {
            InnateSpellLedgerStoreStatus.Applied =>
                new(CharacterInnateSpellLedgerStatus.Applied,
                    Revision: commit.Revision),
            InnateSpellLedgerStoreStatus.LedgerAlreadyInitialized =>
                new(CharacterInnateSpellLedgerStatus.LedgerAlreadyInitialized),
            InnateSpellLedgerStoreStatus.LedgerRevisionConflict or
                InnateSpellLedgerStoreStatus.DuplicateEvent or
                InnateSpellLedgerStoreStatus.LedgerMissing =>
                new(CharacterInnateSpellLedgerStatus.RevisionConflict),
            InnateSpellLedgerStoreStatus.OriginRevisionConflict or
                InnateSpellLedgerStoreStatus.OriginIdentityConflict =>
                new(CharacterInnateSpellLedgerStatus.OriginChanged),
            InnateSpellLedgerStoreStatus.AuthorizedOriginMissing =>
                new(CharacterInnateSpellLedgerStatus.OriginNotCommitted),
            _ => new(CharacterInnateSpellLedgerStatus.NativeUnavailable)
        };
    }

    private async Task<CharacterInnateSpellLedgerStatus> CheckSiteAsync(
        Guid characterId, CancellationToken ct, bool requiresEditing)
    {
        if (characterId == Guid.Empty)
            return CharacterInnateSpellLedgerStatus.NotFoundOrNotOwned;
        var result = await site.GetAuthorizedCharacterAsync(characterId, ct);
        if (result.Status == SiteCharacterAccessStatus.Unauthenticated)
            return CharacterInnateSpellLedgerStatus.Unauthenticated;
        if (result.Status == SiteCharacterAccessStatus.NotFoundOrNotOwned)
            return CharacterInnateSpellLedgerStatus.NotFoundOrNotOwned;
        if (result.Status != SiteCharacterAccessStatus.Authorized ||
            result.Character?.CharacterId != characterId)
            return CharacterInnateSpellLedgerStatus.ProjectionUnavailable;
        return !requiresEditing ||
            result.Character.AllowsOrdinaryEditingByLifecycle
            ? CharacterInnateSpellLedgerStatus.Ready
            : CharacterInnateSpellLedgerStatus.ArchivedReadOnly;
    }
}
