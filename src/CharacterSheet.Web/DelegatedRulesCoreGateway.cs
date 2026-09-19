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
    private const int MaximumRuleResolutionCalls = 28;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
        IReadOnlyCollection<string> conceptKeys,
        CancellationToken cancellationToken = default)
    {
        var requested = conceptKeys
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var resolved = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);

        foreach (var conceptKey in requested.Take(MaximumRuleResolutionCalls))
        {
            var rule = await SendOptionalRuleJsonAsync<RulesCoreResolvedRuleSummaryView>(
                HttpMethod.Get,
                $"/api/rules/{Uri.EscapeDataString(conceptKey)}",
                cancellationToken);
            if (rule is null)
            {
                continue;
            }

            if (!string.Equals(rule.ConceptKey, conceptKey, StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Rules Core resolved concept {RequestedConceptKey} as a different stable concept key; the advancement reference remains unavailable.",
                    conceptKey);
                continue;
            }

            resolved[conceptKey] = rule;
        }

        if (requested.Length > MaximumRuleResolutionCalls)
        {
            logger.LogWarning(
                "Rules Core advancement resolution reached the delegated request budget; additional references remain unavailable.");
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
        where T : class
    {
        var value = await SendJsonCoreAsync<T>(
            method,
            targetPath,
            content,
            allowUnavailableRule: false,
            cancellationToken);
        return value ?? throw new RulesCoreGatewayException("Rules Core returned an empty response.");
    }

    private Task<T?> SendOptionalRuleJsonAsync<T>(
        HttpMethod method,
        string targetPath,
        CancellationToken cancellationToken)
        where T : class =>
        SendJsonCoreAsync<T>(
            method,
            targetPath,
            content: null,
            allowUnavailableRule: true,
            cancellationToken);

    private async Task<T?> SendJsonCoreAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        bool allowUnavailableRule,
        CancellationToken cancellationToken)
        where T : class
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
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Delegated Rules Core request failed before a response was received.");
            throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Delegated Rules Core request timed out.");
            throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
        }

        using (response)
        {
            if (allowUnavailableRule
                && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

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

            try
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                return value ?? throw new RulesCoreGatewayException(
                    "Rules Core returned an empty response.");
            }
            catch (Exception exception) when (exception is JsonException
                or NotSupportedException
                or HttpRequestException)
            {
                logger.LogWarning(exception, "Rules Core returned an unreadable projection response.");
                throw new RulesCoreGatewayException("Rules Core returned an invalid response.", exception);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Rules Core response reading timed out.");
                throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
            }
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
