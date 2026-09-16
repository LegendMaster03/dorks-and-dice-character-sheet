namespace CharacterSheet.Application.Hosting;

public static class ToolHostAuthenticationHeaders
{
    public const string Ticket = "X-Dorks-Tool-Auth-Ticket";
    public const string IntrospectionPath = "X-Dorks-Tool-Auth-Introspection-Path";
}

public sealed record ToolHostUserContext(string Id, string DisplayName);

public sealed record ToolHostCampaignContext(Guid Id, string Name, string Role);

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
    IReadOnlyList<ToolHostCharacterContext>? Characters = null);

public interface IToolHostAuthenticationClient
{
    Task<ToolHostAuthenticationContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default);
}
