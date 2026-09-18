using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Web;

public sealed class DelegatedRulesCoreGateway(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DelegatedRulesCoreGateway> logger)
    : IRulesCoreGateway
{
    private const string TargetSlug = "rules-core";
    private const int RulePageSize = 200;
    private const int MaximumRuleCatalogCalls = 28;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
        IReadOnlyCollection<RulesCoreRuleReference> references,
        CancellationToken cancellationToken = default)
    {
        var unresolved = references
            .Where(value => !string.IsNullOrWhiteSpace(value.ConceptKey)
                && !string.IsNullOrWhiteSpace(value.EntityType))
            .Distinct()
            .GroupBy(value => value.EntityType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(value => value.ConceptKey).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var resolved = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);
        var calls = 0;

        foreach (var group in unresolved.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var offset = 0;
            while (group.Value.Count > 0 && calls < MaximumRuleCatalogCalls)
            {
                var path = $"/api/rules?entityType={Uri.EscapeDataString(group.Key)}&limit={RulePageSize}&offset={offset}";
                var page = await SendJsonAsync<RulesCoreResolvedRulesCatalogView>(
                    HttpMethod.Get,
                    path,
                    content: null,
                    cancellationToken);
                calls++;

                foreach (var rule in page.Rules)
                {
                    if (group.Value.Remove(rule.ConceptKey))
                    {
                        resolved[rule.ConceptKey] = rule;
                    }
                }

                offset += page.Rules.Count;
                if (page.Rules.Count == 0 || offset >= page.TotalCount)
                {
                    break;
                }
            }
        }

        if (unresolved.Values.Any(value => value.Count > 0) && calls >= MaximumRuleCatalogCalls)
        {
            logger.LogWarning(
                "Rules Core advancement reference resolution reached the delegated request budget; unresolved references remain unavailable.");
        }

        return resolved;
    }

    public Task<RulesCoreMechanicsCatalogView> GetGlobalMechanicsAsync(
        CancellationToken cancellationToken = default) =>
        SendJsonAsync<RulesCoreMechanicsCatalogView>(
            HttpMethod.Get,
            "/api/rules/mechanics",
            content: null,
            cancellationToken);

    public Task<RulesCoreMechanicsBatchEvaluationView> EvaluateGlobalMechanicsAsync(
        RulesCoreMechanicsBatchEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SendJsonAsync<RulesCoreMechanicsBatchEvaluationView>(
            HttpMethod.Post,
            "/api/rules/mechanics/evaluate",
            JsonContent.Create(request, options: JsonOptions),
            cancellationToken);
    }

    private async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var (capability, delegationPrefix) = GetDelegation();
        using var request = new HttpRequestMessage(method, $"{delegationPrefix}{targetPath}")
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", capability);
        request.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Delegated Rules Core request failed before a response was received.");
            throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Delegated Rules Core request {Method} {Path} returned status {StatusCode}.",
                    method,
                    targetPath,
                    (int)response.StatusCode);
                var message = response.StatusCode == HttpStatusCode.BadRequest
                    ? "Rules Core rejected a mechanics evaluation request."
                    : "Rules Core is unavailable for this projection.";
                throw new RulesCoreGatewayException(message);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new RulesCoreGatewayException(
                "Rules Core returned an empty response.");
        }
    }

    private (string Capability, string DelegationPrefix) GetDelegation()
    {
        if (httpClient.BaseAddress is null)
        {
            throw new RulesCoreGatewayException(
                "ToolHost:BaseUrl is not configured for Rules Core delegation.");
        }

        var httpContext = httpContextAccessor.HttpContext;
        var authenticationContext = httpContext is null
            ? null
            : HostedToolAuthenticationMiddleware.GetAuthenticationContext(httpContext);
        if (authenticationContext is null
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationCapability)
            || string.IsNullOrWhiteSpace(authenticationContext.DelegationPath))
        {
            throw new RulesCoreGatewayException(
                "The Site did not issue a Character Sheet Rules Core delegation capability.");
        }

        var prefix = authenticationContext.DelegationPath.Replace(
            "{targetSlug}",
            TargetSlug,
            StringComparison.Ordinal);
        return (authenticationContext.DelegationCapability, prefix);
    }
}
