using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

/// <summary>
/// Revision-correlated read model for the eventual unified Character Origins UI.
/// No acquisition, grant, entitlement or transformation mutation is exposed.
/// </summary>
public static class CharacterOriginsBackendEndpoints
{
    public static void MapCharacterOriginsBackendEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/origins/overview", async (
            Guid characterId, CharacterOriginsBackendReadService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(characterId, cancellationToken);
            return result.Status switch
            {
                CharacterEffectiveStateStatus.Ready => Results.Ok(result.View),
                CharacterEffectiveStateStatus.Unauthenticated => Results.Unauthorized(),
                CharacterEffectiveStateStatus.NotFoundOrNotOwned =>
                    Results.NotFound(new { error = "Character unavailable." }),
                CharacterEffectiveStateStatus.SheetNotInitialized =>
                    Results.NotFound(new { error = "Digital Character Sheet is not initialized." }),
                _ => Results.Json(
                    new { error = "Consistent Character Origins state unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        });
    }
}
