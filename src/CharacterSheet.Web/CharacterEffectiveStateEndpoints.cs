using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

/// <summary>Read-only owner-gated state evaluation; never accepts authoritative client records.</summary>
public static class CharacterEffectiveStateEndpoints
{
    public static void MapCharacterEffectiveStateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/state/effective", async (
            Guid characterId, CharacterEffectiveStateService service,
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
                _ => Results.Json(new { error = "Verified character state unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        });
    }
}
