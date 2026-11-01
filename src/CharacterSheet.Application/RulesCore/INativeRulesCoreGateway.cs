using System.Text.Json;
using System.Text.Json.Nodes;

namespace CharacterSheet.Application.RulesCore;

/// <summary>
/// Named, server-to-server native Rules Core operations. Implementations must use
/// Site-issued delegation and must not expose target credentials to the browser.
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
    Task<JsonNode> ResolveNativeResourceAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native resource resolution is not supported by this Rules Core gateway."));
}
