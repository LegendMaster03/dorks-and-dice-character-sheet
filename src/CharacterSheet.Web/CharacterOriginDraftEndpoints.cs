using System.Text.Json;
using CharacterSheet.Application.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.Web;

/// <summary>
/// Character-scoped Origin draft operations. A saved proposal cannot publish
/// authoritative grants, invoke Rites, or approve a DM decision.
/// </summary>
public static class CharacterOriginDraftEndpoints
{
    public sealed record SaveRequest(string RulesetId, string? Fingerprint,
        JsonElement Proposal, long ExpectedRevision);

    public sealed record ActivateRequest(long ExpectedDraftRevision);
    public sealed record ActivateApprovedMixedRequest(
        Guid CampaignId, long ExpectedDraftRevision,
        IReadOnlyList<string> CandidateIds);

    public sealed record ReviewParentSourcesRequest(
        long ExpectedDraftRevision,
        IReadOnlyList<CharacterOriginParentSourceBinding> Bindings);


    public static void MapCharacterOriginDraftEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/characters/{characterId:guid}/origin");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(invocation);
        });

        group.MapGet("/draft", async (
            Guid characterId, CharacterOriginDraftService service,
            CancellationToken cancellationToken) =>
            Respond(await service.GetAsync(characterId, cancellationToken)));

        // Read-only view of last committed server-held Origin. It is not a
        // current effective character mechanics projection; no authorization
        // receipt secret is returned in the public response.
        // Only initial first-party construction is currently executable.
        // Rites, form changes and DM outcomes need a separately verified flow.
        group.MapPost("/activate-native", async (
            Guid characterId, ActivateRequest request,
            CharacterOriginInitialActivationService service,
            CancellationToken cancellationToken) =>
            Respond(await service.ApplyAsync(characterId,
                request.ExpectedDraftRevision, cancellationToken)));

        // Read only the owner-submitted mixed Origin proposal and current
        // native candidate offer summaries. DM/target scope is checked first;
        // this does not confer general permission to read another user's sheet.
        group.MapGet("/mixed-review", async (
            Guid characterId, Guid campaignId, CharacterOriginMixedReviewService service,
            CancellationToken cancellationToken) =>
            Respond(await service.ReviewAsync(characterId, campaignId, cancellationToken)));

        // The caller must be an active campaign DM for this exact character.
        // The Site-issued scope is checked before any cross-owner draft read.
        // Only this narrow table-authorized Human–Elf initial construction is
        // currently eligible; this route never trusts a browser profile.
        group.MapPost("/activate-mixed-approved", async (
            Guid characterId, ActivateApprovedMixedRequest request,
            CharacterOriginMixedActivationService service,
            ILogger<CharacterOriginMixedActivationService> logger,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Respond(await service.ApplyAsync(characterId,
                    request.CampaignId, request.ExpectedDraftRevision,
                    request.CandidateIds, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                // A canceled HTTP operation is neither a database outage nor
                // a failed gameplay decision. Preserve normal cancellation.
                throw;
            }
            catch (Exception error) when (IsTransientPersistenceFailure(error))
            {
                logger.LogError(error,
                    "Mixed Origin activation persistence unavailable for character {CharacterId}",
                    characterId);
                return Error(503, "origin_persistence_unavailable");
            }
            catch (Exception error)
            {
                // Keep the existing structured error envelope, but never
                // expose PostgreSQL exception messages or SQL to clients.
                logger.LogError(error,
                    "Unexpected mixed Origin activation failure for character {CharacterId}",
                    characterId);
                return Error(500, "origin_activation_failed");
            }
        });

        // Inspect another owner-visible character's last stored Origin identity.
        // This read never returns inheritable grants or authorizes that the
        // proposed character is a biological parent of the target.
        group.MapGet("/parent-snapshot/{parentCharacterId:guid}", async (
            Guid characterId, Guid parentCharacterId,
            CharacterOriginParentSnapshotService service,
            CancellationToken cancellationToken) =>
            Respond(await service.InspectAsync(
                characterId, parentCharacterId, cancellationToken)));

        // Server-side native freshness assessment of an owner-visible
        // parent's committed *direct first-party* initial Origin only.
        // No mechanic grant or mixed-parent relationship is authorized.
        group.MapGet("/parent-native-review/{parentCharacterId:guid}", async (
            Guid characterId, Guid parentCharacterId,
            CharacterOriginParentNativeReviewService service,
            CancellationToken cancellationToken) =>
            Respond(await service.ReviewAsync(
                characterId, parentCharacterId, cancellationToken)));

        // Read-only owner review. Browser-provided IDs are proposed source
        // alignments, not verified parentage or DM-approved inheritance.
        // In-process native profiles never leave Character Sheet.
        group.MapPost("/parent-source-alignment-review", async (
            Guid characterId, ReviewParentSourcesRequest request,
            CharacterOriginParentSourceAlignmentService service,
            CancellationToken cancellationToken) =>
            Respond(await service.ReviewAsync(characterId,
                request.ExpectedDraftRevision, request.Bindings, cancellationToken)));

        group.MapGet("/state", async (
            Guid characterId, CharacterOriginStateService service,
            CancellationToken cancellationToken) =>
            Respond(await service.GetAsync(characterId, cancellationToken)));

        group.MapPut("/draft", async (
            Guid characterId, SaveRequest request, CharacterOriginDraftService service,
            CancellationToken cancellationToken) =>
            Respond(await service.SaveAsync(characterId, request.RulesetId,
                request.Fingerprint, request.Proposal, request.ExpectedRevision,
                cancellationToken)));
    }

    private static IResult Respond(CharacterOriginMixedReviewResult result) =>
        result.Status switch
        {
            CharacterOriginMixedReviewStatus.Ready =>
                Results.Ok(new
                {
                    status = "ready",
                    draftRevision = result.DraftRevision,
                    rulesetId = result.RulesetId,
                    fingerprint = result.Fingerprint,
                    proposalId = result.ProposalId,
                    candidates = result.Candidates,
                    previewOnly = true,
                    // Selecting a current candidate is not an approved grant.
                    // Only the separate Site-scoped activation POST may commit.
                    activeGrants = false
                }),
            CharacterOriginMixedReviewStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterOriginMixedReviewStatus.NotAuthorized =>
                Results.NotFound(),
            CharacterOriginMixedReviewStatus.SiteProjectionUnavailable =>
                Error(503, "dm_authorization_projection_unavailable"),
            CharacterOriginMixedReviewStatus.SheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginMixedReviewStatus.OriginAlreadyCommitted =>
                Error(409, "origin_already_committed"),
            CharacterOriginMixedReviewStatus.DraftMissing =>
                Error(409, "origin_draft_missing"),
            CharacterOriginMixedReviewStatus.InvalidDraft =>
                Error(422, "unsupported_mixed_origin_draft"),
            CharacterOriginMixedReviewStatus.NativeSnapshotUnpinned =>
                Error(409, "native_origin_requires_reviewed_catalog"),
            CharacterOriginMixedReviewStatus.NativeContentChanged =>
                Error(409, "native_origin_content_changed_since_dm_review"),
            _ => Error(503, "native_origin_verification_unavailable")
        };

    private static IResult Respond(CharacterOriginMixedActivationResult result) =>
        result.Status switch
        {
            CharacterOriginMixedActivationStatus.Applied =>
                Results.Ok(new
                {
                    status = "applied",
                    revision = result.Revision,
                    effectiveMechanicsReady = false
                }),
            CharacterOriginMixedActivationStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterOriginMixedActivationStatus.DmScopeDenied =>
                Results.NotFound(),
            CharacterOriginMixedActivationStatus.SiteProjectionUnavailable =>
                Error(503, "dm_authorization_projection_unavailable"),
            CharacterOriginMixedActivationStatus.InvalidSelection =>
                Error(400, "invalid_mixed_origin_selection"),
            CharacterOriginMixedActivationStatus.DraftMissing =>
                Error(409, "origin_draft_missing"),
            CharacterOriginMixedActivationStatus.DraftRevisionConflict =>
                Error(409, "origin_draft_revision_conflict"),
            CharacterOriginMixedActivationStatus.OriginAlreadyCommitted =>
                Error(409, "origin_already_committed"),
            CharacterOriginMixedActivationStatus.SheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginMixedActivationStatus.LegacySelectionConflict =>
                Error(409, "legacy_origin_selection_requires_reconciliation"),
            CharacterOriginMixedActivationStatus.NativeSnapshotUnpinned =>
                Error(409, "native_origin_requires_reviewed_catalog"),
            CharacterOriginMixedActivationStatus.NativeContentChanged =>
                Error(409, "native_origin_content_changed_since_dm_review"),
            CharacterOriginMixedActivationStatus.NotAuthorizedByNativeRules =>
                Error(422, "mixed_origin_not_authorized"),
            _ => Error(503, "native_origin_verification_unavailable")
        };

    private static IResult Respond(CharacterOriginActivationResult result) =>
        result.Status switch
        {
            CharacterOriginActivationStatus.Applied =>
                Results.Ok(new { status = "applied", revision = result.Revision,
                    // The effective cross-pillar consumer projection is still
                    // under construction. A stored Origin is not a fully
                    // evaluated, playable character.
                    effectiveMechanicsReady = false }),
            CharacterOriginActivationStatus.NotFoundOrNotOwned => Results.NotFound(),
            CharacterOriginActivationStatus.Unauthenticated => Results.Unauthorized(),
            CharacterOriginActivationStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            CharacterOriginActivationStatus.SheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginActivationStatus.ArchivedReadOnly =>
                Error(403, "character_archived_read_only"),
            CharacterOriginActivationStatus.DraftMissing =>
                Error(409, "origin_draft_missing"),
            CharacterOriginActivationStatus.DraftRevisionConflict =>
                Error(409, "origin_draft_revision_conflict"),
            CharacterOriginActivationStatus.OriginAlreadyCommitted =>
                Error(409, "origin_already_committed"),
            CharacterOriginActivationStatus.LegacySelectionConflict =>
                Error(409, "legacy_origin_selection_requires_reconciliation"),
            CharacterOriginActivationStatus.NotAuthorizedByNativeRules =>
                Error(422, "native_origin_not_authorized"),
            _ => Error(503, "native_origin_verification_unavailable")
        };

    private static IResult Respond(CharacterOriginParentSourceAlignmentResult result) =>
        result.Status switch
        {
            CharacterOriginParentSourceAlignmentStatus.Ready =>
                Results.Ok(new
                {
                    status = "ready",
                    draftRevision = result.DraftRevision,
                    matches = result.Matches,
                    inspectionOnly = true,
                    relationshipVerified = false,
                    inheritanceAuthorized = false,
                    activeGrants = false,
                    effectiveMechanicsReady = false
                }),
            CharacterOriginParentSourceAlignmentStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned =>
                Results.NotFound(),
            CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            CharacterOriginParentSourceAlignmentStatus.SheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginParentSourceAlignmentStatus.DraftMissing =>
                Error(409, "origin_draft_missing"),
            CharacterOriginParentSourceAlignmentStatus.DraftRevisionConflict =>
                Error(409, "origin_draft_revision_conflict"),
            CharacterOriginParentSourceAlignmentStatus.AlreadyCommitted =>
                Error(409, "origin_already_committed"),
            CharacterOriginParentSourceAlignmentStatus.UnsupportedDraft =>
                Error(422, "unsupported_parent_source_draft"),
            CharacterOriginParentSourceAlignmentStatus.InvalidBindings =>
                Error(400, "invalid_parent_source_bindings"),
            CharacterOriginParentSourceAlignmentStatus.ParentNativeCatalogMismatch =>
                Error(409, "parent_origin_native_catalog_mismatch"),
            CharacterOriginParentSourceAlignmentStatus.ParentSourceMismatch =>
                Error(422, "parent_native_source_mismatch"),
            CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange =>
                Error(409, "parent_source_changed_during_review"),
            _ => Error(409, "parent_origin_not_natively_verified")
        };

    private static IResult Respond(CharacterOriginParentNativeReviewResult result) =>
        result.Status switch
        {
            CharacterOriginParentNativeReviewStatus.Ready =>
                Results.Ok(new
                {
                    status = "ready",
                    review = result.Review,
                    inspectedOriginMatchesLatestNativeCatalog = true,
                    inspectionOnly = true,
                    inheritanceAuthorized = false,
                    effectiveMechanicsReady = false
                }),
            CharacterOriginParentNativeReviewStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterOriginParentNativeReviewStatus.NotFoundOrNotOwned =>
                Results.NotFound(),
            CharacterOriginParentNativeReviewStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            CharacterOriginParentNativeReviewStatus.CharacterSheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginParentNativeReviewStatus.ParentOriginMissing =>
                Error(409, "parent_origin_not_committed"),
            CharacterOriginParentNativeReviewStatus.StoredProfileUnverifiable =>
                Error(409, "parent_origin_stored_profile_unverifiable"),
            CharacterOriginParentNativeReviewStatus.UnsupportedParentHistory =>
                Error(422, "parent_origin_history_requires_reconciliation"),
            CharacterOriginParentNativeReviewStatus.OriginSourceDraftUnavailable =>
                Error(409, "parent_origin_source_draft_unavailable"),
            CharacterOriginParentNativeReviewStatus.NativeProfileChanged =>
                Error(409, "parent_origin_native_profile_changed"),
            CharacterOriginParentNativeReviewStatus.ConcurrentOriginChange =>
                Error(409, "parent_origin_changed_during_review"),
            CharacterOriginParentNativeReviewStatus.NativeRulesUnresolved =>
                Error(422, "parent_origin_native_rules_unresolved"),
            _ => Error(503, "native_origin_verification_unavailable")
        };

    private static IResult Respond(CharacterOriginParentSnapshotResult result) =>
        result.Status switch
        {
            CharacterOriginParentSnapshotStatus.Ready =>
                Results.Ok(new
                {
                    status = "ready",
                    snapshot = result.Snapshot,
                    inspectionOnly = true,
                    inheritanceAuthorized = false,
                    // No native review or independently earned grants have
                    // been loaded for the proposed parent relationship.
                    effectiveMechanicsReady = false
                }),
            CharacterOriginParentSnapshotStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterOriginParentSnapshotStatus.NotFoundOrNotOwned =>
                Results.NotFound(),
            CharacterOriginParentSnapshotStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            CharacterOriginParentSnapshotStatus.CharacterSheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginParentSnapshotStatus.ParentOriginMissing =>
                Error(409, "parent_origin_not_committed"),
            _ => Error(409, "parent_origin_stored_profile_unverifiable")
        };

    private static IResult Respond(CharacterOriginReadResult result) =>
        result.Status switch
        {
            CharacterOriginReadStatus.Ready => Results.Ok(new
            {
                status = "ready",
                savedState = result.SavedState is null ? null : new
                {
                    characterId = result.SavedState.CharacterId,
                    rulesetId = result.SavedState.RulesetId,
                    fingerprint = result.SavedState.Fingerprint,
                    profile = JsonSerializer.Deserialize<JsonElement>(result.SavedState.ProfileJson),
                    revision = result.SavedState.Revision,
                    sourceDraftRevision = result.SavedState.SourceDraftRevision,
                    updatedAt = result.SavedState.UpdatedAt
                },
                events = result.CompletedEvents?.Select(receipt => new
                {
                    eventId = receipt.EventId,
                    committedRevision = receipt.CommittedRevision,
                    methodRuleId = receipt.MethodRuleId,
                    methodId = receipt.MethodId,
                    createdAt = receipt.CreatedAt
                }).ToArray(),
                readOnly = result.ReadOnly,
                // A saved projection is not a revalidated, usable grant list.
                effectiveMechanicsReady = false
            }),
            CharacterOriginReadStatus.NotFoundOrNotOwned => Results.NotFound(),
            CharacterOriginReadStatus.Unauthenticated => Results.Unauthorized(),
            CharacterOriginReadStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            _ => Error(409, "character_sheet_not_initialized")
        };

    private static IResult Respond(CharacterOriginDraftResult result) =>
        result.Status switch
        {
            CharacterOriginDraftStatus.Ready => Results.Ok(new
            {
                status = "ready", draft = result.Draft,
                preview = result.Preview, activeGrants = false
            }),
            CharacterOriginDraftStatus.NotFoundOrNotOwned => Results.NotFound(),
            CharacterOriginDraftStatus.Unauthenticated => Results.Unauthorized(),
            CharacterOriginDraftStatus.ProjectionUnavailable =>
                Error(503, "character_authorization_unavailable"),
            CharacterOriginDraftStatus.SheetNotInitialized =>
                Error(409, "character_sheet_not_initialized"),
            CharacterOriginDraftStatus.ArchivedReadOnly =>
                Error(403, "character_archived_read_only"),
            CharacterOriginDraftStatus.RevisionConflict =>
                Error(409, "origin_draft_revision_conflict"),
            CharacterOriginDraftStatus.OriginAlreadyCommitted =>
                Error(409, "origin_already_committed"),
            CharacterOriginDraftStatus.InvalidRequest =>
                Error(400, "invalid_origin_draft"),
            _ => Error(503, "origin_preview_unavailable")
        };

    /// <summary>
    /// Only database-provider-confirmed transient failures become HTTP 503.
    /// A constraint violation, schema defect, or programming error is not
    /// automatically retryable; transaction cleanup remains with the store.
    /// </summary>
    private static bool IsTransientPersistenceFailure(Exception error) =>
        // Npgsql's flag covers connectivity timeouts and socket failures.
        // Explicit SQLSTATEs also cover server shutdown/restart conditions,
        // which a trigger-based integration fixture can report without
        // actually terminating the disposable PostgreSQL instance.
        error is PostgresException postgres &&
            (postgres.IsTransient ||
             postgres.SqlState.StartsWith("08", StringComparison.Ordinal) ||
             postgres.SqlState is "57P01" or "57P02" or "57P03" or
                 "53300" or "53400") ||
        error is NpgsqlException databaseError && databaseError.IsTransient ||
        error is DbUpdateException { InnerException: { } inner } &&
            IsTransientPersistenceFailure(inner);

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = code }, statusCode: status);
}
