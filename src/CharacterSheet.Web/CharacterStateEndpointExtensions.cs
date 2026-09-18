using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

public static class CharacterStateEndpointExtensions
{
    public static void MapCharacterStateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/state", async (
            Guid characterId,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
            ToApiResult(
                await service.GetAsync(characterId, cancellationToken),
                mutating: false));

        app.MapPost("/api/characters/{characterId:guid}/state/inventory", async (
            Guid characterId,
            CharacterInventoryItemOccurrenceRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.AddInventoryItemOccurrenceAsync(
                        characterId,
                        request.ConceptKey,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/characters/{characterId:guid}/state/inventory/{occurrenceId:guid}", async (
            Guid characterId,
            Guid occurrenceId,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.RemoveInventoryItemOccurrenceAsync(
                        characterId,
                        occurrenceId,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/characters/{characterId:guid}/state/notes", async (
            Guid characterId,
            CharacterNoteRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.AddNoteAsync(
                        characterId,
                        request.Content,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPut("/api/characters/{characterId:guid}/state/notes/{noteId:guid}", async (
            Guid characterId,
            Guid noteId,
            CharacterNoteRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.UpdateNoteAsync(
                        characterId,
                        noteId,
                        request.Content,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/characters/{characterId:guid}/state/notes/{noteId:guid}", async (
            Guid characterId,
            Guid noteId,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.RemoveNoteAsync(
                        characterId,
                        noteId,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }

    private static IResult ToApiResult(CharacterStateResult result, bool mutating) => result.Status switch
    {
        CharacterStateAccessStatus.Ready => Results.Ok(result.View),
        CharacterStateAccessStatus.NotFoundOrNotOwned => Results.NotFound(new
        {
            error = "Character unavailable."
        }),
        CharacterStateAccessStatus.ProjectionUnavailable => Results.Json(new
        {
            error = "Site Character authorization is unavailable for this request."
        }, statusCode: StatusCodes.Status503ServiceUnavailable),
        CharacterStateAccessStatus.Unauthenticated => Results.Unauthorized(),
        CharacterStateAccessStatus.SheetNotInitialized => Results.NotFound(new
        {
            error = "Digital Character Sheet is not initialized."
        }),
        CharacterStateAccessStatus.ArchivedReadOnly when mutating => Results.Conflict(new
        {
            error = "Archived Characters are read-only. Restore the Character through the Site before editing Character state."
        }),
        CharacterStateAccessStatus.EntryNotFound => Results.NotFound(new
        {
            error = "Character state entry unavailable."
        }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}

public sealed record CharacterInventoryItemOccurrenceRequest(string ConceptKey);

public sealed record CharacterNoteRequest(string Content);
