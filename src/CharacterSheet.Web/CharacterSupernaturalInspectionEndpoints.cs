using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

/// <summary>
/// Owner-only inspection of recorded supernatural ownership and provenance.
/// Never authorizes or performs a transition, even for a dormant sheet.
/// </summary>
public static class CharacterSupernaturalInspectionEndpoints
{
    public static void MapCharacterSupernaturalInspectionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/state/supernatural", async (
            Guid characterId, CharacterSupernaturalInspectionService service,
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
                _ => Results.Json(new { error = "Supernatural state inspection unavailable." },
                    statusCode: StatusCodes.Status503ServiceUnavailable)
            };
        });
    }
}
