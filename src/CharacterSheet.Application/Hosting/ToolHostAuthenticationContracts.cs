namespace CharacterSheet.Application.Hosting;

// Describes the tool-host authentication data received from the Site. A Character Sheet ticket is scoped to this tool and is not a reusable Rules Core credential.

public static class ToolHostAuthenticationHeaders
{
    public const string Ticket = "X-Dorks-Tool-Auth-Ticket";
    public const string IntrospectionPath = "X-Dorks-Tool-Auth-Introspection-Path";
    public const string DelegationCapability = "X-Dorks-Tool-Delegation-Capability";
    public const string DelegationPath = "X-Dorks-Tool-Delegation-Path";
}

public sealed record ToolHostUserContext(string Id, string DisplayName);

public sealed record ToolHostCampaignContext(Guid Id, string Name, string Role);

/// <summary>
/// Site-verified target pairs for active campaign DMs. These do not confer
/// character ownership or themselves authorize any specific Origin ruling.
/// </summary>
public sealed record ToolHostDmCharacterScope(Guid CampaignId, Guid CharacterId);

public sealed record ToolHostCharacterContext(
    Guid Id,
    string Name,
    string Status,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<Guid> CampaignIds);

public sealed record ToolHostAuthenticationContext(
    int ContractVersion,
    string ToolSlug,
    string SiteMode,
    ToolHostUserContext User,
    IReadOnlyList<string> GlobalRoles,
    IReadOnlyList<ToolHostCampaignContext> Campaigns,
    IReadOnlyList<ToolHostCharacterContext>? Characters = null,
    IReadOnlyList<ToolHostDmCharacterScope>? DmCharacterScopes = null)
{
    public string? DelegationCapability { get; init; }
    public string? DelegationPath { get; init; }
}

public interface IToolHostAuthenticationClient
{
    Task<ToolHostAuthenticationContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default);
}
