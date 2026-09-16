namespace CharacterSheet.Application.Lifecycle;

public static class ToolLifecycleHeaders
{
    public const string Ticket = "X-Dorks-Tool-Lifecycle-Ticket";
    public const string IntrospectionPath = "X-Dorks-Tool-Lifecycle-Introspection-Path";
}

public static class ToolLifecycleEventTypes
{
    public const string CharacterDeleted = "character.deleted";
    public const string CampaignDeleted = "campaign.deleted";

    public static bool IsSupported(string eventType) =>
        string.Equals(eventType, CharacterDeleted, StringComparison.Ordinal)
        || string.Equals(eventType, CampaignDeleted, StringComparison.Ordinal);
}

public sealed record ToolLifecycleContext(
    int ContractVersion,
    string ToolSlug,
    Guid EventId,
    string EventType,
    Guid SubjectId,
    DateTimeOffset OccurredAt);

public interface IToolLifecycleIntrospectionClient
{
    Task<ToolLifecycleContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default);
}

public enum LifecycleProcessingStatus
{
    Processed,
    AlreadyProcessed,
    Unsupported
}

public interface ICharacterSheetLifecycleProcessor
{
    Task<LifecycleProcessingStatus> ProcessAsync(
        ToolLifecycleContext lifecycleEvent,
        CancellationToken cancellationToken = default);
}
