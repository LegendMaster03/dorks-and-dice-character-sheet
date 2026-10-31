using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Web;

/// <summary>
/// Server-only, explicitly named native mechanics operations. A browser never
/// receives Site delegation credentials or calls the Rules Core target directly.
/// </summary>
public interface INativeRulesCoreGateway
{
    Task<JsonNode> GetNativeRulesetAsync(CancellationToken cancellationToken);
    Task<JsonNode> SearchNativeOptionsAsync(
        string fingerprint, string category, string? query, int limit, int offset,
        CancellationToken cancellationToken);
    Task<JsonNode> EvaluateNativeAdvancementAsync(
        JsonElement request, CancellationToken cancellationToken);
    Task<JsonNode> ProjectNativeCharacterAsync(
        JsonElement request, CancellationToken cancellationToken);
}

public static class NativeRulesCoreEndpoints
{
    public static void MapNativeRulesCoreEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/native-rules");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            invocation.HttpContext.Response.Headers.CacheControl = "no-store";
            // The user session must be a redeemed Site-issued Character Sheet
            // ticket; the ordinary delegation capability stays server-only.
            if (HostedToolAuthenticationMiddleware.GetAuthenticationContext(
                    invocation.HttpContext) is null)
                return Results.Unauthorized();
            return await next(invocation);
        });

        group.MapGet("/ruleset", (INativeRulesCoreGateway gateway,
            CancellationToken cancellationToken) =>
            Forward(() => gateway.GetNativeRulesetAsync(cancellationToken)));

        group.MapGet("/options", (string fingerprint, string category, string? q,
            int? limit, int? offset, INativeRulesCoreGateway gateway,
            CancellationToken cancellationToken) =>
            Forward(() => gateway.SearchNativeOptionsAsync(
                fingerprint, category, q, limit ?? 50, offset ?? 0, cancellationToken)));

        group.MapPost("/advancements/eligibility", (JsonElement request,
            INativeRulesCoreGateway gateway, CancellationToken cancellationToken) =>
            Forward(() => gateway.EvaluateNativeAdvancementAsync(request, cancellationToken)));

        group.MapPost("/characters/project", (JsonElement request,
            INativeRulesCoreGateway gateway, CancellationToken cancellationToken) =>
            Forward(() => gateway.ProjectNativeCharacterAsync(request, cancellationToken)));
    }

    private static async Task<IResult> Forward(Func<Task<JsonNode>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (RulesCoreGatewayException error)
        {
            var status = error.StatusCode is 400 or 401 or 403 or 409
                ? error.StatusCode.Value : StatusCodes.Status503ServiceUnavailable;
            // Do not leak target paths, Site credentials or private diagnostics.
            return Results.Json(new { error = status == 409
                ? "ruleset_version_mismatch"
                : status == 400 ? "invalid_native_request"
                : status is 401 or 403 ? "native_mechanics_access_denied"
                : "native_mechanics_unavailable" }, statusCode: status);
        }
    }
}
