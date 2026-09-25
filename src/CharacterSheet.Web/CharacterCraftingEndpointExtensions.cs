using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

public static class CharacterCraftingEndpointExtensions
{
    public static void MapCharacterCraftingEndpoints(this WebApplication app)
    {
        app.MapPost("/api/characters/{characterId:guid}/crafting/manufacturing/resolve", async (
            Guid characterId,
            CharacterManufacturingRequest request,
            CharacterCraftingService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(await service.ResolveManufacturingAsync(
                    characterId,
                    request,
                    cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPost("/api/characters/{characterId:guid}/crafting/enchanting/resolve", async (
            Guid characterId,
            CharacterEnchantingRequest request,
            CharacterCraftingService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(await service.ResolveEnchantingAsync(
                    characterId,
                    request,
                    cancellationToken));
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }

    private static IResult ToApiResult(CharacterCraftingResult result) =>
        result.Status switch
        {
            CharacterCraftingAccessStatus.Ready when result.Resolution is not null =>
                Results.Ok(result.Resolution),
            CharacterCraftingAccessStatus.NotFoundOrNotOwned =>
                Results.NotFound(new { error = "Character unavailable." }),
            CharacterCraftingAccessStatus.Unauthenticated =>
                Results.Unauthorized(),
            CharacterCraftingAccessStatus.SheetNotInitialized =>
                Results.NotFound(new { error = "Digital Character Sheet is not initialized." }),
            CharacterCraftingAccessStatus.ProjectionUnavailable =>
                Results.Json(
                    new { error = result.Message ?? "Rules Core crafting resolution is unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
        };
}
