using System.Text.Json;
using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

/// <summary>
/// Character-scoped Origin draft operations. A saved proposal cannot publish
/// authoritative grants, invoke Rites, or approve a DM decision.
/// </summary>
public static class CharacterOriginDraftEndpoints
{
    public sealed record SaveRequest(string RulesetId, string? Fingerprint,
        JsonElement Proposal, long ExpectedRevision);

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
            CharacterOriginDraftStatus.InvalidRequest =>
                Error(400, "invalid_origin_draft"),
            _ => Error(503, "origin_preview_unavailable")
        };

    private static IResult Error(int status, string code) =>
        Results.Json(new { error = code }, statusCode: status);
}
