using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// The parent's last *initial, direct, first-party* construction can be
/// checked against the current native catalog. This is not verification of
/// biological parentage, nested ancestry, independent entitlements or form
/// inheritance. Verified profile data stays server-private; no grants or
/// reusable ruling credentials leave through the browser review endpoint.
/// </summary>
public enum CharacterOriginParentNativeReviewStatus
{
    Ready,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    CharacterSheetNotInitialized,
    ParentOriginMissing,
    StoredProfileUnverifiable,
    UnsupportedParentHistory,
    OriginSourceDraftUnavailable,
    NativeRulesUnavailable,
    NativeRulesUnresolved,
    NativeProfileChanged,
    ConcurrentOriginChange
}

public sealed record CharacterOriginParentNativeReview(
    Guid ParentCharacterId,
    string RulesetId,
    long StoredRevision,
    string LatestNativeFingerprint,
    string VerifiedProposalId);

/// <summary>
/// Request-local witness for the exact reviewed first-party parent source.
/// Its values remain server-private and are compared again by a multi-source
/// inspection after all independent native requests finish.
/// </summary>
public sealed record CharacterOriginParentInitialEvidence(
    string OriginFingerprint, DateTimeOffset OriginUpdatedAt,
    long SourceDraftRevision, string DraftProposalJson, string? DraftFingerprint,
    DateTimeOffset DraftUpdatedAt, string ReceiptEventId,
    DateTimeOffset ReceiptCreatedAt);

public sealed record CharacterOriginParentNativeReviewResult(
    CharacterOriginParentNativeReviewStatus Status,
    CharacterOriginParentNativeReview? Review = null,
    // Server-private intermediary. The HTTP endpoint deliberately sends only
    // Review, never grants, vision effects or the native preview body.
    [property: JsonIgnore] JsonElement? EvaluatedIntrinsics = null,
    [property: JsonIgnore] JsonElement? EvaluatedVision = null,
    // Cloned from a fresh native evaluation only after exact correspondence
    // with the committed direct Origin and a second persisted-state check.
    // Not an authorization to inherit any part of the parent's profile.
    [property: JsonIgnore] JsonElement? VerifiedInitialProfile = null,
    // Internal evidence for multi-source freshness checks. Not lineage or
    // heritability proof, never included in the endpoint response.
    [property: JsonIgnore] CharacterOriginParentInitialEvidence? PersistenceEvidence = null);

public sealed class CharacterOriginParentNativeReviewService(
    CharacterOriginParentSnapshotService snapshots,
    ICharacterAuthorizedOriginStore origins,
    ICharacterOriginDraftStore drafts,
    INativeRulesCoreGateway native)
{
    public async Task<CharacterOriginParentNativeReviewResult> ReviewAsync(
        Guid characterId, Guid parentCharacterId,
        CancellationToken cancellationToken = default)
    {
        // The snapshot service checks *both* Site identities before its first
        // read of parent persistence. A matching parent ID in the draft alone
        // is never authentication or a biological relationship.
        var inspected = await snapshots.InspectAsync(
            characterId, parentCharacterId, cancellationToken);
        if (inspected.Status != CharacterOriginParentSnapshotStatus.Ready)
            return new(Map(inspected.Status));

        var initial = inspected.Snapshot!;
        var saved = await origins.GetAsync(parentCharacterId, cancellationToken);
        if (saved is null || !SameSnapshot(saved, initial))
            return new(CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange);

        // This increment recognizes *only* initial first-party construction.
        // Later reconstruction, rites and active transformations must be
        // revalidated through separate native contracts and event evidence.
        if (saved.Revision != 1 || saved.SourceDraftRevision is not long revision ||
            revision < 1)
            return new(CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory);

        var receipts = await origins.GetEventsAsync(parentCharacterId, cancellationToken);
        if (receipts.Count != 1 ||
            receipts[0].CharacterId != parentCharacterId ||
            receipts[0].CommittedRevision != 1 ||
            receipts[0].AuthorizationKind != "first-party" ||
            receipts[0].AuthorizationEvidenceId != "native-catalog:" + saved.Fingerprint)
            return new(CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory);

        var draft = await drafts.GetAsync(parentCharacterId, cancellationToken);
        if (draft is null || draft.Revision != revision ||
            draft.RulesetId != saved.RulesetId)
            return new(CharacterOriginParentNativeReviewStatus.OriginSourceDraftUnavailable);

        try
        {
            using var storedProfile = JsonDocument.Parse(saved.ProfileJson);
            var baseline = storedProfile.RootElement;
            if (!baseline.TryGetProperty("underlying", out var underlying) ||
                !baseline.TryGetProperty("current", out var current) ||
                !JsonElement.DeepEquals(underlying, current))
                return new(CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory);

            using var proposal = JsonDocument.Parse(draft.ProposalJson);
            if (!CharacterOriginInitialActivationService.TryInitialProposal(
                    proposal.RootElement, out var proposalId,
                    out var ruleId, out var methodId, out var speciesId,
                    out var ancestryId) ||
                receipts[0].EventId != "construction:" + proposalId ||
                receipts[0].MethodRuleId != ruleId ||
                receipts[0].MethodId != methodId)
                return new(CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory);

            JsonNode latest;
            try
            {
                // The historical fingerprint is an audit input, not a ruleset
                // permission gate or a demand to load an obsolete catalog.
                latest = await native.PreviewNativeOriginAsync(
                    JsonSerializer.SerializeToElement(new
                    {
                        rulesetId = draft.RulesetId,
                        fingerprint = draft.Fingerprint,
                        proposal = proposal.RootElement
                    }), cancellationToken);
            }
            catch (RulesCoreGatewayException)
            {
                return new(CharacterOriginParentNativeReviewStatus.NativeRulesUnavailable);
            }

            using var response = JsonDocument.Parse(latest.ToJsonString());
            if (!CharacterOriginInitialActivationService.TryAuthorizedNativeResult(
                    response.RootElement, saved.RulesetId,
                    proposalId!, ruleId!, methodId!, speciesId, ancestryId,
                    out var latestFingerprint, out var latestProfile))
                return new(CharacterOriginParentNativeReviewStatus.NativeRulesUnresolved);

            using var fresh = JsonDocument.Parse(latestProfile!);
            if (!JsonElement.DeepEquals(storedProfile.RootElement, fresh.RootElement))
                return new(CharacterOriginParentNativeReviewStatus.NativeProfileChanged);

            // A Rite could commit while the native request is pending. Neither
            // the prior revision nor its parent grants are reusable evidence.
            var after = await origins.GetAsync(parentCharacterId, cancellationToken);
            var afterDraft = await drafts.GetAsync(parentCharacterId, cancellationToken);
            var afterReceipts = await origins.GetEventsAsync(parentCharacterId, cancellationToken);
            if (after is null || !SameSnapshot(after, initial) ||
                // Revision metadata alone cannot detect an in-place rewrite.
                // Reject content changes while Rules Core is evaluating.
                !string.Equals(after.ProfileJson, saved.ProfileJson,
                    StringComparison.Ordinal) ||
                after.SourceDraftRevision != saved.SourceDraftRevision ||
                afterDraft is null || afterDraft.Revision != revision ||
                afterDraft.RulesetId != draft.RulesetId ||
                afterDraft.Fingerprint != draft.Fingerprint ||
                afterDraft.UpdatedAt != draft.UpdatedAt ||
                !string.Equals(afterDraft.ProposalJson, draft.ProposalJson,
                    StringComparison.Ordinal) ||
                afterReceipts.Count != 1 ||
                !SameFirstPartyReceipt(afterReceipts[0], receipts[0]))
                return new(CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange);

            var intrinsics = response.RootElement.TryGetProperty("intrinsics",
                out var verifiedIntrinsics) &&
                verifiedIntrinsics.ValueKind == JsonValueKind.Object
                ? verifiedIntrinsics.Clone() : (JsonElement?)null;
            // This is an interpretation of the same *fresh* full-profile
            // evaluation, not a separate browser-provided sense entitlement.
            var vision = response.RootElement.TryGetProperty("vision",
                out var evaluatedVision) &&
                evaluatedVision.ValueKind == JsonValueKind.Object
                ? evaluatedVision.Clone() : (JsonElement?)null;
            // Request-local native material, never a second stored effective
            // state or an HTTP response. Underlying == Current here only
            // because the checked parent is initial direct first-party.
            var verifiedProfile = fresh.RootElement.Clone();
            var evidence = new CharacterOriginParentInitialEvidence(
                saved.Fingerprint, saved.UpdatedAt, revision, draft.ProposalJson,
                draft.Fingerprint, draft.UpdatedAt,
                receipts[0].EventId, receipts[0].CreatedAt);
            return new(CharacterOriginParentNativeReviewStatus.Ready,
                new(parentCharacterId, saved.RulesetId, saved.Revision,
                    latestFingerprint!, proposalId!), intrinsics, vision,
                verifiedProfile, evidence);
        }
        catch (JsonException)
        {
            return new(CharacterOriginParentNativeReviewStatus.StoredProfileUnverifiable);
        }
    }

    private static bool SameFirstPartyReceipt(
        global::CharacterSheet.Domain.Characters.CharacterOriginEventReceipt current,
        global::CharacterSheet.Domain.Characters.CharacterOriginEventReceipt expected) =>
        current.CharacterId == expected.CharacterId &&
        current.EventId == expected.EventId &&
        current.CommittedRevision == expected.CommittedRevision &&
        current.MethodRuleId == expected.MethodRuleId &&
        current.MethodId == expected.MethodId &&
        current.AuthorizationKind == expected.AuthorizationKind &&
        current.AuthorizationEvidenceId == expected.AuthorizationEvidenceId &&
        current.CreatedAt == expected.CreatedAt &&
        current.ApprovalActorUserId is null &&
        current.ApprovalCampaignId is null &&
        current.ApprovedDecisionsJson is null;

    private static bool SameSnapshot(
        global::CharacterSheet.Domain.Characters.CharacterAuthorizedOriginState saved,
        CharacterOriginParentSnapshot prior) =>
        saved.CharacterId == prior.ParentCharacterId &&
        saved.RulesetId == prior.RulesetId &&
        saved.Revision == prior.Revision &&
        saved.Fingerprint == prior.Fingerprint &&
        saved.UpdatedAt == prior.SnapshotUpdatedAt;

    private static CharacterOriginParentNativeReviewStatus Map(
        CharacterOriginParentSnapshotStatus status) => status switch
        {
            CharacterOriginParentSnapshotStatus.Unauthenticated =>
                CharacterOriginParentNativeReviewStatus.Unauthenticated,
            CharacterOriginParentSnapshotStatus.NotFoundOrNotOwned =>
                CharacterOriginParentNativeReviewStatus.NotFoundOrNotOwned,
            CharacterOriginParentSnapshotStatus.ProjectionUnavailable =>
                CharacterOriginParentNativeReviewStatus.ProjectionUnavailable,
            CharacterOriginParentSnapshotStatus.CharacterSheetNotInitialized =>
                CharacterOriginParentNativeReviewStatus.CharacterSheetNotInitialized,
            CharacterOriginParentSnapshotStatus.ParentOriginMissing =>
                CharacterOriginParentNativeReviewStatus.ParentOriginMissing,
            _ => CharacterOriginParentNativeReviewStatus.StoredProfileUnverifiable
        };
}
