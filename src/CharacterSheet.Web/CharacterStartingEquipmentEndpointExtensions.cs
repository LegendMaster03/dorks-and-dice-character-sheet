using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

public static class CharacterStartingEquipmentEndpointExtensions
{
    public static void MapCharacterStartingEquipmentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/characters/{characterId:guid}/state/inventory/starting-equipment", async (
            Guid characterId,
            CharacterStartingEquipmentService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.MaterializeInventoryAsync(
                        characterId,
                        cancellationToken));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }

    private static IResult ToApiResult(CharacterStateResult result) => result.Status switch
    {
        CharacterStateAccessStatus.Ready => Results.Ok(result.View),
        CharacterStateAccessStatus.NotFoundOrNotOwned => Results.NotFound(new
        {
            error = "Character unavailable."
        }),
        CharacterStateAccessStatus.ProjectionUnavailable => Results.Json(new
        {
            error = "Rules Core or Site Character projection is unavailable for this request."
        }, statusCode: StatusCodes.Status503ServiceUnavailable),
        CharacterStateAccessStatus.Unauthenticated => Results.Unauthorized(),
        CharacterStateAccessStatus.SheetNotInitialized => Results.NotFound(new
        {
            error = "Digital Character Sheet is not initialized."
        }),
        CharacterStateAccessStatus.ArchivedReadOnly => Results.Conflict(new
        {
            error = "Archived Characters are read-only. Restore the Character through the Site before adding starting equipment."
        }),
        CharacterStateAccessStatus.EntryNotFound => Results.NotFound(new
        {
            error = "Character state entry unavailable."
        }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}
