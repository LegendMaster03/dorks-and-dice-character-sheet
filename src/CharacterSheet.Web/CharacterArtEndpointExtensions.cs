using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

public static class CharacterArtEndpointExtensions
{
    public static void MapCharacterArtEndpoints(this WebApplication app)
    {
        app.MapGet("/api/characters/{characterId:guid}/state/art", async (
            Guid characterId,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
            ToApiResult(await service.ListAsync(characterId, cancellationToken), mutating: false));

        app.MapPost("/api/characters/{characterId:guid}/state/art", async (
            Guid characterId,
            HttpRequest request,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "Character art upload must use multipart form data." });
            var form = await request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file");
            if (file is null) return Results.BadRequest(new { error = "Character art upload requires a file field." });
            try
            {
                if (file.Length <= 0 || file.Length > CharacterArtService.MaxUploadBytes)
                {
                    return Results.BadRequest(new { error = $"Character art must be between 1 byte and {CharacterArtService.MaxUploadBytes / (1024 * 1024)} MiB." });
                }
                await using var input = file.OpenReadStream();
                return ToApiResult(
                    await service.UploadAsync(characterId, file.FileName, file.ContentType, input, cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapGet("/api/characters/{characterId:guid}/state/art/{assetId:guid}/content", async (
            Guid characterId,
            Guid assetId,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.OpenAsync(characterId, assetId, cancellationToken);
            if (result.Status == CharacterArtAccessStatus.Ready && result.Content is not null)
                return Results.Stream(result.Content, result.ContentType ?? "application/octet-stream", enableRangeProcessing: true);
            return ToContentError(result);
        });

        app.MapPut("/api/characters/{characterId:guid}/state/art/{assetId:guid}/portrait", async (
            Guid characterId,
            Guid assetId,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
            ToApiResult(await service.SetPortraitAsync(characterId, assetId, cancellationToken), mutating: true));

        app.MapDelete("/api/characters/{characterId:guid}/state/art/portrait", async (
            Guid characterId,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
            ToApiResult(await service.ClearPortraitAsync(characterId, cancellationToken), mutating: true));

        app.MapDelete("/api/characters/{characterId:guid}/state/art/{assetId:guid}", async (
            Guid characterId,
            Guid assetId,
            CharacterArtService service,
            CancellationToken cancellationToken) =>
            ToApiResult(await service.DeleteAsync(characterId, assetId, cancellationToken), mutating: true));
    }

    private static IResult ToApiResult(CharacterArtResult result, bool mutating) => result.Status switch
    {
        CharacterArtAccessStatus.Ready => Results.Ok(new { assets = result.Assets ?? Array.Empty<CharacterArtAssetView>() }),
        CharacterArtAccessStatus.NotFoundOrNotOwned => Results.NotFound(new { error = "Character unavailable." }),
        CharacterArtAccessStatus.ProjectionUnavailable => Results.Json(new { error = "Site Character authorization is unavailable for this request." }, statusCode: 503),
        CharacterArtAccessStatus.Unauthenticated => Results.Unauthorized(),
        CharacterArtAccessStatus.SheetNotInitialized => Results.NotFound(new { error = "Digital Character Sheet is not initialized." }),
        CharacterArtAccessStatus.ArchivedReadOnly when mutating => Results.Conflict(new { error = "Archived Characters are read-only." }),
        CharacterArtAccessStatus.EntryNotFound => Results.NotFound(new { error = "Character art asset unavailable." }),
        _ => Results.StatusCode(500)
    };

    private static IResult ToContentError(CharacterArtContentResult result) => result.Status switch
    {
        CharacterArtAccessStatus.NotFoundOrNotOwned or CharacterArtAccessStatus.EntryNotFound or CharacterArtAccessStatus.SheetNotInitialized =>
            Results.NotFound(new { error = "Character art asset unavailable." }),
        CharacterArtAccessStatus.Unauthenticated => Results.Unauthorized(),
        CharacterArtAccessStatus.ProjectionUnavailable => Results.StatusCode(503),
        _ => Results.StatusCode(500)
    };
}
