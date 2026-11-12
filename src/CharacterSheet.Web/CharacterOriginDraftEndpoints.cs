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
        app.MapGet("/api/characters/{characterId:guid}/origin/draft", async (
            Guid characterId, CharacterOriginDraftService service,
            CancellationToken cancellationToken) =>
            Respond(await service.GetAsync(characterId, cancellationToken)));

        app.MapPut("/api/characters/{characterId:guid}/origin/draft", async (
            Guid characterId, SaveRequest request, CharacterOriginDraftService service,
            CancellationToken cancellationToken) =>
            Respond(await service.SaveAsync(characterId, request.RulesetId,
                request.Fingerprint, request.Proposal, request.ExpectedRevision,
                cancellationToken)));
    }

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
