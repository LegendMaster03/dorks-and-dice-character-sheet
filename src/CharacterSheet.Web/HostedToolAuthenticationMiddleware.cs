using System.Security.Claims;
using CharacterSheet.Application.Hosting;

namespace CharacterSheet.Web;

public sealed class HostedToolAuthenticationMiddleware(RequestDelegate next)
{
    private const string ContextItemKey = "CharacterSheet.ToolHostAuthenticationContext";

    public async Task InvokeAsync(
        HttpContext httpContext,
        IToolHostAuthenticationClient authenticationClient)
    {
        var tickets = httpContext.Request.Headers[ToolHostAuthenticationHeaders.Ticket];
        var introspectionPaths = httpContext.Request.Headers[ToolHostAuthenticationHeaders.IntrospectionPath];
        var hasTicketHeader = tickets.Count > 0;
        var hasIntrospectionHeader = introspectionPaths.Count > 0;

        if (!hasTicketHeader && !hasIntrospectionHeader)
        {
            await next(httpContext);
            return;
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        if (!hasTicketHeader
            || !hasIntrospectionHeader
            || tickets.Count != 1
            || introspectionPaths.Count != 1
            || string.IsNullOrWhiteSpace(tickets[0])
            || string.IsNullOrWhiteSpace(introspectionPaths[0]))
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        ToolHostAuthenticationContext? authenticationContext;
        try
        {
            authenticationContext = await authenticationClient.RedeemAsync(
                tickets[0]!,
                introspectionPaths[0]!,
                httpContext.RequestAborted);
        }
        catch (InvalidOperationException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        catch (InvalidDataException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        catch (ArgumentException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }
        catch (HttpRequestException)
        {
            httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
            return;
        }
        catch (OperationCanceledException) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            return;
        }

        if (authenticationContext is null)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        httpContext.Items[ContextItemKey] = authenticationContext;
        httpContext.User = BuildPrincipal(authenticationContext);
        await next(httpContext);
    }

    public static ToolHostAuthenticationContext? GetAuthenticationContext(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ContextItemKey, out var value)
            ? value as ToolHostAuthenticationContext
            : null;

    private static ClaimsPrincipal BuildPrincipal(ToolHostAuthenticationContext context)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, context.User.Id),
            new(ClaimTypes.Name, context.User.DisplayName),
            new("dorks-and-dice:site-mode", context.SiteMode)
        };

        claims.AddRange(context.GlobalRoles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(context.Campaigns.Select(campaign =>
            new Claim("dorks-and-dice:campaign-role", $"{campaign.Id:D}:{campaign.Role}")));

        return new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType: "DorksAndDiceToolHost",
            nameType: ClaimTypes.Name,
            roleType: ClaimTypes.Role));
    }
}
