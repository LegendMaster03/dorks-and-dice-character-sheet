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

    /// <summary>
    /// Delegated read-only planning for a separately verified manifestation.
    /// The Rules Core host must independently resolve an acquisition and
    /// transition authority provider; no provider means no commit candidate.
    /// </summary>
    Task<JsonNode> PlanVerifiedManifestationAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Verified manifestation planning is unsupported by this gateway."));

    /// <summary>
    /// Authenticated server-owned primary character effective-state evaluation.
    /// Rejects all supernatural entitlements until independently verified.
    /// </summary>
    Task<JsonNode> EvaluatePrimaryStateBaselineAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Primary-state evaluation is unsupported by this gateway."));

    /// <summary>
    /// Stateless origin construction preview. This does not authorize a DM ruling
    /// or commit character-owned selections to persistent storage.
    /// </summary>
    Task<JsonNode> PreviewNativeOriginAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native origin preview is unsupported by this gateway."));
    /// <summary>
    /// Server-held, Site-scoped DM-reviewed mixed Origin evaluation.
    /// It computes a result but does not persist or certify the owner's state.
    /// </summary>
    Task<JsonNode> EvaluateApprovedMixedOriginAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Approved mixed Origin evaluation is unsupported by this gateway."));

    Task<JsonNode> GetNativeHarvestingCatalogAsync(
        string fingerprint, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native harvesting is not supported by this Rules Core gateway."));
    Task<JsonNode> ResolveNativeHarvestingTableAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native harvesting table resolution is unsupported."));
    Task<JsonNode> EvaluateNativeHarvestingOutcomeAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native harvesting outcome evaluation is unsupported."));
    Task<JsonNode> ResolveNativeManufacturingAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native manufacturing is not supported by this Rules Core gateway."));
    Task<JsonNode> ResolveNativeStartingEquipmentAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native starting equipment is unsupported by this gateway."));
    Task<JsonNode> ResolveNativeResourceAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native resource resolution is not supported by this Rules Core gateway."));
}
