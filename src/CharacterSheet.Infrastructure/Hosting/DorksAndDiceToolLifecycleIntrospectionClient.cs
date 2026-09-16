using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterSheet.Application.Lifecycle;

namespace CharacterSheet.Infrastructure.Hosting;

public sealed class DorksAndDiceToolLifecycleIntrospectionClient(HttpClient httpClient)
    : IToolLifecycleIntrospectionClient
{
    public const string ExpectedToolSlug = "character-sheet";
    public const string ExpectedIntrospectionPath = "/tool-host/character-sheet/api/lifecycle/introspect";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ToolLifecycleContext?> RedeemAsync(
        string ticket,
        string introspectionPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticket))
        {
            throw new ArgumentException("Tool lifecycle ticket can not be blank.", nameof(ticket));
        }

        if (!string.Equals(introspectionPath, ExpectedIntrospectionPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The Tool lifecycle introspection path is not valid for Character Sheet.");
        }

        if (httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException(
                "ToolHost:BaseUrl must be configured before lifecycle introspection can be used.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, ExpectedIntrospectionPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket.Trim());

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var context = await response.Content.ReadFromJsonAsync<ToolLifecycleContext>(
            JsonOptions,
            cancellationToken);
        if (context is null)
        {
            throw new InvalidDataException("Tool lifecycle introspection returned an empty context.");
        }

        ValidateContext(context);
        return context;
    }

    private static void ValidateContext(ToolLifecycleContext context)
    {
        if (context.ContractVersion != 1)
        {
            throw new InvalidDataException(
                $"Unsupported Tool lifecycle contract version '{context.ContractVersion}'.");
        }

        if (!string.Equals(context.ToolSlug, ExpectedToolSlug, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Tool lifecycle context was issued for another Tool.");
        }

        if (context.EventId == Guid.Empty)
        {
            throw new InvalidDataException("Tool lifecycle context does not include an EventId.");
        }

        if (context.SubjectId == Guid.Empty)
        {
            throw new InvalidDataException("Tool lifecycle context does not include a SubjectId.");
        }

        if (!ToolLifecycleEventTypes.IsSupported(context.EventType))
        {
            throw new InvalidDataException(
                $"Unsupported Tool lifecycle event type '{context.EventType}'.");
        }
    }
}
