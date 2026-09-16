using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Infrastructure.Persistence;
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

var characterSheetConnectionString = builder.Configuration.GetConnectionString("CharacterSheet");
if (string.IsNullOrWhiteSpace(characterSheetConnectionString))
{
    characterSheetConnectionString = $"Data Source={Path.Combine(Path.GetTempPath(), "dorks-and-dice-character-sheet-dev.db")}";
}

builder.Services.AddDbContext<CharacterSheetDbContext>(options =>
    options.UseSqlite(characterSheetConnectionString));
builder.Services.AddScoped<ICharacterSheetStore, SqliteCharacterSheetStore>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ISiteCharacterAccessGateway, ToolHostSiteCharacterAccessGateway>();
builder.Services.AddScoped<CharacterSheetBootstrapService>();
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
                persistence = "sqlite-unavailable",
                characterAuthorization = "tool-host-owner-projection"
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
    catch
    {
        return Results.Json(new
        {
            status = "not-ready",
            persistence = "sqlite-unavailable",
            characterAuthorization = "tool-host-owner-projection"
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new
    {
        status = "ready",
        persistence = "sqlite-ready",
        characterAuthorization = "tool-host-owner-projection"
    });
});

app.MapGet("/api", () => Results.Ok(new
{
    service = "Dorks & Dice Character Sheet",
    version = "0.2-dev",
    status = "character-bootstrap"
}));

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

const string standaloneShell = """
<!doctype html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title>Character Sheet</title>
</head>
<body>
    <main id="tool-root"></main>
    <script type="module" src="/app.js"></script>
</body>
</html>
""";

IResult StandaloneShell() => Results.Content(standaloneShell, "text/html; charset=utf-8");

app.MapGet("/", StandaloneShell);
app.MapGet("/new", StandaloneShell);
app.MapGet("/characters/{**route}", StandaloneShell);

app.Run();

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

public partial class Program;
