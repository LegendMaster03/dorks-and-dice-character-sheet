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

        app.MapPut("/api/characters/{characterId:guid}/state/health", async (
            Guid characterId,
            CharacterHealthRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
            ToApiResult(
                await service.SetCurrentHitPointsAsync(
                    characterId,
                    request.CurrentHitPoints,
                    cancellationToken),
                mutating: true));

        app.MapPut("/api/characters/{characterId:guid}/state/death-saves", async (
            Guid characterId,
            CharacterDeathSavesRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.SetDeathSavesAsync(
                        characterId,
                        request.Successes,
                        request.Failures,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPut("/api/characters/{characterId:guid}/state/rules-inputs", async (
            Guid characterId,
            CharacterRulesInputStateRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.SetRulesInputAsync(
                        characterId,
                        request.Kind,
                        request.Key,
                        request.IntegerValue,
                        request.BooleanValue,
                        request.TextValue,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/characters/{characterId:guid}/state/rules-inputs/{kind}", async (
            Guid characterId,
            string kind,
            string key,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.RemoveRulesInputAsync(
                        characterId,
                        kind,
                        key,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPut("/api/characters/{characterId:guid}/state/hit-point-gains/{advancementOccurrenceId:guid}/{classLevel:int}", async (
            Guid characterId,
            Guid advancementOccurrenceId,
            int classLevel,
            CharacterHitPointGainRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.SetHitPointGainAsync(
                        characterId,
                        advancementOccurrenceId,
                        classLevel,
                        request.HitDieValue,
                        cancellationToken),
                    mutating: true);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/characters/{characterId:guid}/state/hit-point-gains/{advancementOccurrenceId:guid}/{classLevel:int}", async (
            Guid characterId,
            Guid advancementOccurrenceId,
            int classLevel,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.RemoveHitPointGainAsync(
                        characterId,
                        advancementOccurrenceId,
                        classLevel,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

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

        app.MapPut("/api/characters/{characterId:guid}/state/inventory/{occurrenceId:guid}", async (
            Guid characterId,
            Guid occurrenceId,
            CharacterInventoryItemOccurrenceStateRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.UpdateInventoryItemOccurrenceAsync(
                        characterId,
                        occurrenceId,
                        request.Quantity,
                        request.IsCarried,
                        request.IsEquipped,
                        request.IsAttuned,
                        request.ContainerOccurrenceId,
                        cancellationToken),
                    mutating: true);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                or InvalidOperationException)
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
        app.MapPost("/api/characters/{characterId:guid}/state/conditions", async (
            Guid characterId,
            CharacterConditionCreateRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.AddConditionAsync(
                        characterId,
                        request.ConceptKey,
                        request.CustomName,
                        request.Level,
                        request.CounterCurrent,
                        request.CounterMaximum,
                        request.Duration,
                        request.Notes,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapPut("/api/characters/{characterId:guid}/state/conditions/{conditionId:guid}", async (
            Guid characterId,
            Guid conditionId,
            CharacterConditionUpdateRequest request,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.UpdateConditionAsync(
                        characterId,
                        conditionId,
                        request.CustomName,
                        request.Level,
                        request.CounterCurrent,
                        request.CounterMaximum,
                        request.Duration,
                        request.Notes,
                        cancellationToken),
                    mutating: true);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        app.MapDelete("/api/characters/{characterId:guid}/state/conditions/{conditionId:guid}", async (
            Guid characterId,
            Guid conditionId,
            CharacterStateService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return ToApiResult(
                    await service.RemoveConditionAsync(
                        characterId,
                        conditionId,
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

public sealed record CharacterRulesInputStateRequest(
    string Kind,
    string Key,
    int? IntegerValue,
    bool? BooleanValue,
    string? TextValue);

public sealed record CharacterHitPointGainRequest(int HitDieValue);

public sealed record CharacterInventoryItemOccurrenceRequest(string ConceptKey);

public sealed record CharacterInventoryItemOccurrenceStateRequest(
    int Quantity,
    bool IsCarried,
    bool IsEquipped,
    bool IsAttuned,
    Guid? ContainerOccurrenceId);

public sealed record CharacterNoteRequest(string Content);

public sealed record CharacterHealthRequest(int? CurrentHitPoints);

public sealed record CharacterDeathSavesRequest(int Successes, int Failures);

public sealed record CharacterConditionCreateRequest(
    string? ConceptKey,
    string? CustomName,
    int? Level,
    int? CounterCurrent,
    int? CounterMaximum,
    string? Duration,
    string? Notes);

public sealed record CharacterConditionUpdateRequest(
    string? CustomName,
    int? Level,
    int? CounterCurrent,
    int? CounterMaximum,
    string? Duration,
    string? Notes);
