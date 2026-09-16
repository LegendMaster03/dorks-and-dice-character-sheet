using CharacterSheet.Application.Hosting;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Web;

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

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseMiddleware<HostedToolAuthenticationMiddleware>();
app.UseStaticFiles();

app.MapHealthChecks("/health");

app.MapGet("/ready", () => Results.Ok(new
{
    status = "ready",
    persistence = "deferred",
    characterAuthorization = "site-contract-extension-required"
}));

app.MapGet("/api", () => Results.Ok(new
{
    service = "Dorks & Dice Character Sheet",
    version = "0.1-dev",
    status = "foundation"
}));

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

public partial class Program;
