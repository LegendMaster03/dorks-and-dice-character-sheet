using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Web;

public sealed class DelegatedRulesCoreGateway(
    HttpClient httpClient,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DelegatedRulesCoreGateway> logger)
    : IRulesCoreAdvancementGateway, INativeRulesCoreGateway
{
    private const string TargetSlug = "rules-core";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<JsonNode> GetNativeRulesetAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Get, "/api/mechanics/v1/ruleset",
            content: null, cancellationToken);

    public Task<JsonNode> SearchNativeOptionsAsync(
        string fingerprint, string category, string? query, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var path = "/api/mechanics/v1/catalog/options?fingerprint=" +
            Uri.EscapeDataString(fingerprint) + "&category=" + Uri.EscapeDataString(category) +
            "&q=" + Uri.EscapeDataString(query ?? "") +
            "&limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "&offset=" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return SendJsonAsync<JsonNode>(HttpMethod.Get, path, content: null, cancellationToken);
    }

    public Task<JsonNode> EvaluateNativeAdvancementAsync(
        JsonElement request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Post, "/api/mechanics/v1/advancements/eligibility",
            JsonContent.Create(request, options: JsonOptions), cancellationToken);

    public Task<JsonNode> ProjectNativeCharacterAsync(
        JsonElement request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Post, "/api/mechanics/v1/characters/project",
            JsonContent.Create(request, options: JsonOptions), cancellationToken);

    public Task<JsonNode> ResolveNativeManufacturingAsync(
        JsonElement request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Post, "/api/mechanics/v1/crafting/manufacturing/resolve",
            JsonContent.Create(request, options: JsonOptions), cancellationToken);

    public Task<JsonNode> ResolveNativeStartingEquipmentAsync(
        JsonElement request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Post, "/api/mechanics/v1/characters/starting-equipment",
            JsonContent.Create(request, options: JsonOptions), cancellationToken);

    public Task<JsonNode> ResolveNativeResourceAsync(
        JsonElement request, CancellationToken cancellationToken = default) =>
        SendJsonAsync<JsonNode>(HttpMethod.Post, "/api/mechanics/v1/resources/resolve",
            JsonContent.Create(request, options: JsonOptions), cancellationToken);

    // These contract members are retained for callers awaiting a native
    // equivalent. Do not send requests to the removed application API.
    public Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
        IReadOnlyCollection<string> conceptKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conceptKeys);
        // A missing native Species/Subspecies relationship is not proof of
        // compatibility. Return no identities, so dependent choices fail closed.
        IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> empty =
            new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);
        return Task.FromResult(empty);
    }

    public Task<RulesCoreCharacterSupportProjectionView> ProjectGlobalCharacterSupportAsync(
        RulesCoreCharacterSupportProjectionRequest request, CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterSupportProjectionView>("recovery procedure catalog");

    public Task<RulesCoreCharacterRecoveryResolutionView> ResolveGlobalCharacterRecoveryAsync(
        string procedureKey, RulesCoreCharacterRecoveryResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterRecoveryResolutionView>("recovery procedures");

    public Task<RulesCoreCharacterRulesProjectionView> ResolveGlobalCharacterMechanicsAsync(
        RulesCoreCharacterRulesProjectionRequest request, CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterRulesProjectionView>("retired character mechanics projection");

    public Task<RulesCoreCharacterRulesProjectionView> ResolveCampaignCharacterMechanicsAsync(
        Guid campaignId, RulesCoreCharacterRulesProjectionRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterRulesProjectionView>("campaign mechanics projection");

    public Task<RulesCoreCharacterAdvancementEligibilityView> ResolveGlobalCharacterAdvancementEligibilityAsync(
        RulesCoreCharacterAdvancementEligibilityRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterAdvancementEligibilityView>("retired advancement eligibility");

    public Task<RulesCoreCharacterAdvancementEligibilityView> ResolveCampaignCharacterAdvancementEligibilityAsync(
        Guid campaignId, RulesCoreCharacterAdvancementEligibilityRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCharacterAdvancementEligibilityView>("campaign advancement eligibility");

    public Task<RulesCoreCraftingCheckResolutionView> ResolveGlobalManufacturingAsync(
        RulesCoreManufacturingResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCraftingCheckResolutionView>("manufacturing");

    public Task<RulesCoreCraftingCheckResolutionView> ResolveCampaignManufacturingAsync(
        Guid campaignId, RulesCoreManufacturingResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCraftingCheckResolutionView>("campaign manufacturing");

    public Task<RulesCoreCraftingCheckResolutionView> ResolveGlobalEnchantingAsync(
        RulesCoreEnchantingResolutionRequest request, CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCraftingCheckResolutionView>("enchanting");

    public Task<RulesCoreCraftingCheckResolutionView> ResolveCampaignEnchantingAsync(
        Guid campaignId, RulesCoreEnchantingResolutionRequest request,
        CancellationToken cancellationToken = default) =>
        UnsupportedAsync<RulesCoreCraftingCheckResolutionView>("campaign enchanting");

    private static Task<T> UnsupportedAsync<T>(string operation) =>
        Task.FromException<T>(new RulesCoreGatewayException(
            $"Rules Core has no native {operation} contract. The retired API is not a fallback."));

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
            cancellationToken);
        return value ?? throw new RulesCoreGatewayException("Rules Core returned an empty response.");
    }

    private async Task<T?> SendJsonCoreAsync<T>(
        HttpMethod method,
        string targetPath,
        HttpContent? content,
        CancellationToken cancellationToken)
        where T : class
    {
        var timingContext = httpContextAccessor.HttpContext;
        if (timingContext is not null)
        {
            CharacterSheetServerTiming.EnsureRequestTiming(timingContext);
        }

        var timingOperation = TimingOperationFor(targetPath);
        var dependencyTimer = Stopwatch.StartNew();
        var timingOutcome = "failed";

        try
        {
            string capability;
            string delegationPrefix;
            try
            {
                (capability, delegationPrefix) = GetDelegation();
            }
            catch (RulesCoreGatewayException)
            {
                timingOutcome = "delegation-unavailable";
                throw;
            }

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
                timingOutcome = "transport-failed";
                logger.LogWarning(exception, "Delegated Rules Core request failed before a response was received.");
                throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                timingOutcome = "transport-failed";
                logger.LogWarning(exception, "Delegated Rules Core request timed out.");
                throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
            }

            using (response)
            {
                CharacterSheetServerTiming.AppendDownstreamRulesCoreMetrics(timingContext, response);

                if (!response.IsSuccessStatusCode)
                {
                    timingOutcome = $"http-{(int)response.StatusCode}";
                    logger.LogWarning(
                        "Delegated Rules Core request {Method} {Path} returned status {StatusCode}.",
                        method,
                        targetPath,
                        (int)response.StatusCode);
                    var message = response.StatusCode == HttpStatusCode.BadRequest
                        ? "Rules Core rejected a mechanics evaluation request."
                        : "Rules Core is unavailable for this projection.";
                    throw new RulesCoreGatewayException(message, statusCode: (int)response.StatusCode);
                }

                try
                {
                    var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
                    if (value is null)
                    {
                        timingOutcome = "invalid-response";
                        throw new RulesCoreGatewayException("Rules Core returned an empty response.");
                    }

                    timingOutcome = "ok";
                    return value;
                }
                catch (Exception exception) when (exception is JsonException
                    or NotSupportedException
                    or HttpRequestException)
                {
                    timingOutcome = "invalid-response";
                    logger.LogWarning(exception, "Rules Core returned an unreadable projection response.");
                    throw new RulesCoreGatewayException("Rules Core returned an invalid response.", exception);
                }
                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
                {
                    timingOutcome = "transport-failed";
                    logger.LogWarning(exception, "Rules Core response reading timed out.");
                    throw new RulesCoreGatewayException("Rules Core transport is unavailable.", exception);
                }
            }
        }
        finally
        {
            dependencyTimer.Stop();
            CharacterSheetServerTiming.AppendDuration(
                timingContext,
                CharacterSheetServerTiming.RulesCoreDependencyMetricName,
                dependencyTimer.Elapsed.TotalMilliseconds,
                $"{timingOperation}:{timingOutcome}");
        }
    }

    private static string TimingOperationFor(string targetPath)
    {
        if (targetPath.EndsWith("/characters/project", StringComparison.Ordinal))
            return "native-character-projection";
        if (targetPath.EndsWith("/resources/resolve", StringComparison.Ordinal))
            return "native-resource-resolution";
        if (targetPath.EndsWith("/characters/starting-equipment", StringComparison.Ordinal))
            return "native-starting-equipment";
        if (targetPath.EndsWith("/crafting/manufacturing/resolve", StringComparison.Ordinal))
            return "native-manufacturing";
        if (targetPath.EndsWith("/advancements/eligibility", StringComparison.Ordinal))
            return "native-advancement-eligibility";
        if (targetPath.Contains("/catalog/options", StringComparison.Ordinal))
            return "native-option-search";
        return "native-ruleset";
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
