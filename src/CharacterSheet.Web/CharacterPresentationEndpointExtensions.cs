using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

public static class CharacterPresentationEndpointExtensions
{
    public static void MapCharacterPresentationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/presentation", async (
            Guid characterId,
            CharacterPresentationService service,
            ILogger<CharacterPresentationService> logger,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(characterId, cancellationToken);
            foreach (var diagnostic in result.IntegrationDiagnostics ?? [])
            {
                logger.LogWarning(
                    "Character presentation integration degradation for {CharacterId}: {Diagnostic}",
                    characterId,
                    diagnostic);
            }

            return result.Status switch
            {
                CharacterPresentationAccessStatus.Ready => Results.Ok(result.View),
                CharacterPresentationAccessStatus.NotFoundOrNotOwned => Results.NotFound(new
                {
                    error = "Character unavailable."
                }),
                CharacterPresentationAccessStatus.ProjectionUnavailable => Results.Json(new
                {
                    error = "Site Character authorization is unavailable for this request."
                }, statusCode: StatusCodes.Status503ServiceUnavailable),
                CharacterPresentationAccessStatus.Unauthenticated => Results.Unauthorized(),
                CharacterPresentationAccessStatus.SheetNotInitialized => Results.NotFound(new
                {
                    error = "Digital Character Sheet is not initialized."
                }),
                _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
            };
        });
    }
}
