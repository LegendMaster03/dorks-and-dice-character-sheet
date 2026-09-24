using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Lifecycle;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Infrastructure.Persistence;
using CharacterSheet.Infrastructure.Storage;
using CharacterSheet.Web;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var toolHostBaseUrl = builder.Configuration["ToolHost:BaseUrl"];
Uri? toolHostBaseUri = null;
if (!string.IsNullOrWhiteSpace(toolHostBaseUrl))
{
    if (!Uri.TryCreate(toolHostBaseUrl, UriKind.Absolute, out toolHostBaseUri)
        || (toolHostBaseUri.Scheme != Uri.UriSchemeHttp
            && toolHostBaseUri.Scheme != Uri.UriSchemeHttps))
    {
        throw new InvalidOperationException("ToolHost:BaseUrl must be an absolute HTTP or HTTPS URL.");
    }
}

string? rulesCoreDevelopmentBaseUrl = null;
if (builder.Environment.IsDevelopment())
{
    var configuredRulesCoreDevelopmentBaseUrl = builder.Configuration["RulesCore:DevelopmentBaseUrl"];
    if (!string.IsNullOrWhiteSpace(configuredRulesCoreDevelopmentBaseUrl))
    {
        if (!Uri.TryCreate(configuredRulesCoreDevelopmentBaseUrl, UriKind.Absolute, out var rulesCoreDevelopmentBaseUri)
            || (rulesCoreDevelopmentBaseUri.Scheme != Uri.UriSchemeHttp
                && rulesCoreDevelopmentBaseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "RulesCore:DevelopmentBaseUrl must be an absolute HTTP or HTTPS URL when configured.");
        }

        rulesCoreDevelopmentBaseUrl = rulesCoreDevelopmentBaseUri.ToString().TrimEnd('/');
    }
}

builder.Services
    .AddHttpClient<IToolHostAuthenticationClient, DorksAndDiceToolHostAuthenticationClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(3);
        client.BaseAddress = toolHostBaseUri;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
builder.Services
    .AddHttpClient<IRulesCoreGateway, DelegatedRulesCoreGateway>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.BaseAddress = toolHostBaseUri;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });
builder.Services
    .AddHttpClient<IToolLifecycleIntrospectionClient, DorksAndDiceToolLifecycleIntrospectionClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(3);
        client.BaseAddress = toolHostBaseUri;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false
    });

var characterSheetConnectionString = builder.Configuration.GetConnectionString("CharacterSheet");
if (string.IsNullOrWhiteSpace(characterSheetConnectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:CharacterSheet must be configured with the Character Sheet PostgreSQL connection string.");
}

builder.Services.AddDbContext<CharacterSheetDbContext>(options =>
    options.UseNpgsql(characterSheetConnectionString));
var characterArtStoragePath = builder.Configuration["CharacterArt:StoragePath"];
if (string.IsNullOrWhiteSpace(characterArtStoragePath))
{
    characterArtStoragePath = Path.Combine(Path.GetTempPath(), "dorks-and-dice-character-sheet-art");
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICharacterArtStorage>(new FileSystemCharacterArtStorage(characterArtStoragePath));
builder.Services.AddScoped<ICharacterSheetStore, PostgresCharacterSheetStore>();
builder.Services.AddScoped<ICharacterBuildStore, PostgresCharacterBuildStore>();
builder.Services.AddScoped<ICharacterStateStore, PostgresCharacterStateStore>();
builder.Services.AddScoped<ICharacterArtStore, PostgresCharacterArtStore>();
builder.Services.AddScoped<ICharacterSheetLifecycleProcessor, CharacterSheetLifecycleProcessor>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISiteCharacterAccessGateway, ToolHostSiteCharacterAccessGateway>();
builder.Services.AddScoped<CharacterSheetBootstrapService>();
builder.Services.AddScoped<CharacterBuildService>();
builder.Services.AddScoped<CharacterStateService>();
builder.Services.AddScoped<CharacterArtService>();
builder.Services.AddScoped<CharacterRecoveryService>();
builder.Services.AddScoped<CharacterPresentationService>();
builder.Services.AddHealthChecks();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CharacterSheetDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseMiddleware<HostedToolAuthenticationMiddleware>();
app.UseStaticFiles();

app.MapHealthChecks("/health");

app.MapGet("/ready", async (CharacterSheetDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return Results.Json(new
            {
                status = "not-ready",
                persistence = "postgresql-unavailable",
                characterAuthorization = "tool-host-owner-projection"
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
    catch
    {
        return Results.Json(new
        {
            status = "not-ready",
            persistence = "postgresql-unavailable",
            characterAuthorization = "tool-host-owner-projection"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "ready",
        persistence = "postgresql-ready",
        characterAuthorization = "tool-host-owner-projection"
    });
});

app.MapGet("/api", () => Results.Ok(new
{
    service = "Dorks & Dice Character Sheet",
    version = "0.3-dev",
    status = "rules-core-builder-foundation"
}));

app.MapPost("/api/lifecycle/events", ReceiveLifecycleEventAsync);
app.MapCharacterStateEndpoints();
app.MapCharacterArtEndpoints();
app.MapCharacterPresentationEndpoints();

app.MapGet("/api/characters/{characterId:guid}/sheet", async (
    Guid characterId,
    CharacterSheetBootstrapService service,
    CancellationToken cancellationToken) =>
    ToApiResult(await service.GetAsync(characterId, cancellationToken), initializing: false));

app.MapPost("/api/characters/{characterId:guid}/sheet", async (
    Guid characterId,
    CharacterSheetBootstrapService service,
    CancellationToken cancellationToken) =>
    ToApiResult(await service.InitializeAsync(characterId, cancellationToken), initializing: true));

app.MapGet("/api/characters/{characterId:guid}/build", async (
    Guid characterId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
    ToBuildApiResult(await service.GetAsync(characterId, cancellationToken), mutating: false));

app.MapPut("/api/characters/{characterId:guid}/build/race-species", async (
    Guid characterId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.SetRaceSpeciesAsync(characterId, request.ConceptKey, cancellationToken),
            mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/race-species", async (
    Guid characterId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
    ToBuildApiResult(
        await service.ClearRaceSpeciesAsync(characterId, cancellationToken),
        mutating: true));

app.MapPut("/api/characters/{characterId:guid}/build/background", async (
    Guid characterId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(await service.SetBackgroundAsync(characterId, request.ConceptKey, cancellationToken), mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/background", async (
    Guid characterId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
    ToBuildApiResult(await service.ClearBackgroundAsync(characterId, cancellationToken), mutating: true));

app.MapPut("/api/characters/{characterId:guid}/build/deity", async (
    Guid characterId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(await service.SetDeityAsync(characterId, request.ConceptKey, cancellationToken), mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/deity", async (
    Guid characterId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
    ToBuildApiResult(await service.ClearDeityAsync(characterId, cancellationToken), mutating: true));

app.MapPut("/api/characters/{characterId:guid}/build/ability-scores/{abilityKey}", async (
    Guid characterId,
    string abilityKey,
    BaseAbilityScoreInputRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.SetBaseAbilityScoreInputAsync(
                characterId,
                abilityKey,
                request.Score,
                cancellationToken),
            mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/ability-scores/{abilityKey}", async (
    Guid characterId,
    string abilityKey,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.ClearBaseAbilityScoreInputAsync(
                characterId,
                abilityKey,
                cancellationToken),
            mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/api/characters/{characterId:guid}/build/starting-class", async (
    Guid characterId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.SetStartingClassAsync(characterId, request.ConceptKey, cancellationToken),
            mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/starting-class", async (
    Guid characterId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
    ToBuildApiResult(
        await service.ClearStartingClassAsync(characterId, cancellationToken),
        mutating: true));

app.MapPut("/api/characters/{characterId:guid}/build/classes/{classAdvancementEntryId:guid}/subclass", async (
    Guid characterId,
    Guid classAdvancementEntryId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.SetSubclassAsync(
                characterId,
                classAdvancementEntryId,
                request.ConceptKey,
                cancellationToken),
            mutating: true);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/classes/{classAdvancementEntryId:guid}/subclass", async (
    Guid characterId,
    Guid classAdvancementEntryId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.ClearSubclassAsync(characterId, classAdvancementEntryId, cancellationToken),
            mutating: true);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPut("/api/characters/{characterId:guid}/build/advancements/{advancementEntryId:guid}/level", async (
    Guid characterId,
    Guid advancementEntryId,
    AdvancementLevelRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.SetAdvancementLevelAsync(
                characterId,
                advancementEntryId,
                request.Level,
                cancellationToken),
            mutating: true);
    }
    catch (Exception exception) when (
        exception is ArgumentException
        or InvalidOperationException
        or KeyNotFoundException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/api/characters/{characterId:guid}/build/feats", async (
    Guid characterId,
    RuleConceptSelectionRequest request,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.AddFeatOccurrenceAsync(characterId, request.ConceptKey, cancellationToken),
            mutating: true);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapDelete("/api/characters/{characterId:guid}/build/feats/{featAdvancementEntryId:guid}", async (
    Guid characterId,
    Guid featAdvancementEntryId,
    CharacterBuildService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        return ToBuildApiResult(
            await service.RemoveFeatOccurrenceAsync(
                characterId,
                featAdvancementEntryId,
                cancellationToken),
            mutating: true);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

var standaloneDevelopmentAttributes = app.Environment.IsDevelopment()
    ? " data-standalone-development=\"true\""
        + (rulesCoreDevelopmentBaseUrl is null
            ? string.Empty
            : $" data-rules-core-development-base-url=\"{System.Net.WebUtility.HtmlEncode(rulesCoreDevelopmentBaseUrl)}\"")
    : string.Empty;
var standaloneShell = $$"""
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Character Sheet</title>
</head>
<body>
    <main id="tool-root"{{standaloneDevelopmentAttributes}}></main>
    <script type="module" src="/app.js"></script>
</body>
</html>
""";

IResult StandaloneShell() => Results.Content(standaloneShell, "text/html; charset=utf-8");

app.MapGet("/", StandaloneShell);
app.MapGet("/new", StandaloneShell);
app.MapGet("/characters/{**route}", StandaloneShell);

app.Run();

static async Task<IResult> ReceiveLifecycleEventAsync(
    HttpContext httpContext,
    IToolLifecycleIntrospectionClient introspectionClient,
    ICharacterSheetLifecycleProcessor processor,
    CancellationToken cancellationToken)
{
    httpContext.Response.Headers.CacheControl = "no-store";

    var tickets = httpContext.Request.Headers[ToolLifecycleHeaders.Ticket];
    var introspectionPaths = httpContext.Request.Headers[ToolLifecycleHeaders.IntrospectionPath];
    if (tickets.Count != 1
        || introspectionPaths.Count != 1
        || string.IsNullOrWhiteSpace(tickets[0])
        || string.IsNullOrWhiteSpace(introspectionPaths[0])
        || !string.Equals(
            introspectionPaths[0],
            DorksAndDiceToolLifecycleIntrospectionClient.ExpectedIntrospectionPath,
            StringComparison.Ordinal))
    {
        return Results.Unauthorized();
    }

    ToolLifecycleContext? lifecycleContext;
    try
    {
        lifecycleContext = await introspectionClient.RedeemAsync(
            tickets[0]!,
            introspectionPaths[0]!,
            cancellationToken);
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (InvalidDataException exception)
    {
        return Results.Json(
            new { error = exception.Message },
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }
    catch (ArgumentException)
    {
        return Results.Unauthorized();
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
    catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }

    if (lifecycleContext is null)
    {
        return Results.Unauthorized();
    }

    var processingStatus = await processor.ProcessAsync(lifecycleContext, cancellationToken);
    return processingStatus switch
    {
        LifecycleProcessingStatus.Processed => Results.NoContent(),
        LifecycleProcessingStatus.AlreadyProcessed => Results.NoContent(),
        LifecycleProcessingStatus.Unsupported => Results.Json(
            new { error = $"Unsupported lifecycle event type '{lifecycleContext.EventType}'." },
            statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
    };
}

static IResult ToApiResult(CharacterSheetBootstrapResult result, bool initializing) => result.Status switch
{
    CharacterSheetBootstrapStatus.Ready => Results.Ok(result.View),
    CharacterSheetBootstrapStatus.NotFoundOrNotOwned => Results.NotFound(new
    {
        error = "Character unavailable."
    }),
    CharacterSheetBootstrapStatus.ProjectionUnavailable => Results.Json(new
    {
        error = "Site Character authorization is unavailable for this request."
    }, statusCode: StatusCodes.Status503ServiceUnavailable),
    CharacterSheetBootstrapStatus.Unauthenticated => Results.Unauthorized(),
    CharacterSheetBootstrapStatus.Archived when initializing => Results.Conflict(new
    {
        error = "Archived Characters can not initialize or edit a digital Character Sheet. Restore the Character through the Site first.",
        character = result.View
    }),
    _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
};

static IResult ToBuildApiResult(CharacterBuildResult result, bool mutating) => result.Status switch
{
    CharacterBuildAccessStatus.Ready => Results.Ok(result.View),
    CharacterBuildAccessStatus.NotFoundOrNotOwned => Results.NotFound(new
    {
        error = "Character unavailable."
    }),
    CharacterBuildAccessStatus.ProjectionUnavailable => Results.Json(new
    {
        error = "Site Character authorization is unavailable for this request."
    }, statusCode: StatusCodes.Status503ServiceUnavailable),
    CharacterBuildAccessStatus.Unauthenticated => Results.Unauthorized(),
    CharacterBuildAccessStatus.SheetNotInitialized => Results.NotFound(new
    {
        error = "Digital Character Sheet is not initialized."
    }),
    CharacterBuildAccessStatus.ArchivedReadOnly when mutating => Results.Conflict(new
    {
        error = "Archived Characters are read-only. Restore the Character through the Site before editing its build."
    }),
    _ => Results.StatusCode(StatusCodes.Status500InternalServerError)
};

public sealed record RuleConceptSelectionRequest(string ConceptKey);

public sealed record BaseAbilityScoreInputRequest(int Score);

public sealed record AdvancementLevelRequest(int Level);

public partial class Program;
