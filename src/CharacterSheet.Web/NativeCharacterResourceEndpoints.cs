using CharacterSheet.Application.Characters;
using Microsoft.AspNetCore.Mvc;

namespace CharacterSheet.Web;

public static class NativeCharacterResourceEndpoints
{
    public static void MapNativeCharacterResourceEndpoints(this WebApplication app)
    {
        app.MapPost("/api/characters/{characterId:guid}/state/resources/resolve", async (
            Guid characterId,
            NativeCharacterResourceActionRequest request,
            [FromServices] NativeCharacterResourceService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.ResolveAsync(characterId, request, cancellationToken);
                return result.Status switch
                {
                    NativeCharacterResourceActionStatus.Applied => Results.Ok(new {
                        resolution = result.Resolution, state = result.State
                    }),
                    NativeCharacterResourceActionStatus.NotFoundOrNotOwned =>
                        Results.NotFound(new { error = "Character unavailable." }),
                    NativeCharacterResourceActionStatus.Unauthenticated => Results.Unauthorized(),
                    NativeCharacterResourceActionStatus.SheetNotInitialized =>
                        Results.NotFound(new { error = "Character Sheet is not initialized." }),
                    NativeCharacterResourceActionStatus.ArchivedReadOnly =>
                        Results.Conflict(new { error = "Archived Characters are read-only." }),
                    NativeCharacterResourceActionStatus.Conflict =>
                        Results.Conflict(new { error = result.Message ?? "Character resource state changed." }),
                    NativeCharacterResourceActionStatus.Unsupported =>
                        Results.Json(new {
                            error = result.Message ?? "This native resource action is unsupported."
                        }, statusCode: StatusCodes.Status422UnprocessableEntity),
                    _ => Results.Json(new {
                        error = "Native resource resolution is currently unavailable."
                    }, statusCode: StatusCodes.Status503ServiceUnavailable)
                };
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }
}
