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
    /// Authenticated read of persistent manifestations. Rules Core requires a
    /// separate independently verifying host source provider. Without it, a
    /// stored form or an echoed receipt must never become playable mechanics.
    /// </summary>
    Task<JsonNode> EvaluateVerifiedManifestationStateAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Verified manifestation reads are unsupported by this gateway."));

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
    /// <summary>
    /// Delegated, read-only Origin Underlying verification. The Rules Core host
    /// must independently reload the exact saved profile, revision and source
    /// evidence. A Site ticket or a supplied profile alone is not authorization.
    /// </summary>
    Task<JsonNode> VerifyNativeOriginUnderlyingAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Independent native Origin verification is unsupported by this gateway."));

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

    /// <summary>
    /// Delegated native calculation for direct Origin free-cast bootstrap.
    /// PreviewOnly is mandatory: the Character Sheet app must first verify
    /// committed Origin and owner-held character progression, then commit
    /// via its separate atomic character-owned store.
    /// </summary>
    Task<JsonNode> PreviewDirectInnateSpellLedgerBootstrapAsync(
        JsonElement request, CancellationToken cancellationToken) =>
        Task.FromException<JsonNode>(new RulesCoreGatewayException(
            "Native direct-Origin innate spell ledger bootstrap is unsupported."));

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
