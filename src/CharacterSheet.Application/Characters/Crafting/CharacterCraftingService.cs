using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.RulesCore;

namespace CharacterSheet.Application.Characters;

public enum CharacterCraftingAccessStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    SheetNotInitialized
}

public sealed record CharacterCraftingManualCompetencyRequest(
    string DisplayName,
    int Contribution,
    bool IsQualified);

public sealed record CharacterCraftingCompetencyRequest(
    string? CompetencyKey = null,
    CharacterCraftingManualCompetencyRequest? Manual = null);

public sealed record CharacterManufacturingRequest(
    CharacterCraftingCompetencyRequest Competency,
    Guid? CampaignId = null,
    bool HasQualifiedGuidance = false,
    int? D20Roll = null,
    int OtherModifier = 0,
    int? TargetDc = null,
    string? AbilityKey = null,
    int? ManualAbilityModifier = null);

public sealed record CharacterEnchantingRequest(
    Guid? CampaignId = null,
    string? CreatureType = null,
    CharacterCraftingCompetencyRequest? Competency = null,
    string? SpellcastingKey = null,
    int? D20Roll = null,
    int OtherModifier = 0,
    int? TargetDc = null);

public sealed record CharacterCraftingResult(
    CharacterCraftingAccessStatus Status,
    RulesCoreCraftingCheckResolutionView? Resolution = null,
    string? Message = null);

public sealed class CharacterCraftingService(
    CharacterBuildService buildService,
    CharacterStateService stateService,
    IRulesCoreGateway rulesCoreGateway,
    INativeRulesCoreGateway nativeCore)
{
    public async Task<CharacterCraftingResult> ResolveManufacturingAsync(
        Guid characterId,
        CharacterManufacturingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Competency);

        var context = await BuildCharacterContextAsync(characterId, cancellationToken);
        if (context.Result is not null)
        {
            return context.Result;
        }

        // The approved manufacturing outcome is available in native Core with
        // explicit manual inputs. Full automatic native competency and
        // effective Ability contribution resolution are still incomplete.
        // Do not pass the retired rules projection or silently infer a value.
        if (request.CampaignId is not null)
            return Unsupported("Campaign-effective manufacturing is not available in native Rules Core.");
        if (request.Competency.Manual is null ||
            !string.IsNullOrWhiteSpace(request.Competency.CompetencyKey))
            return Unsupported("Select a manual competency until native competency-backed manufacturing is available.");
        if (request.ManualAbilityModifier is null ||
            !string.IsNullOrWhiteSpace(request.AbilityKey))
            return Unsupported("Enter a manual Ability modifier until native Character Ability contributions are supported.");

        try
        {
            var metadata = await nativeCore.GetNativeRulesetAsync(cancellationToken);
            var rulesetId = metadata["rulesetId"]?.GetValue<string>();
            var fingerprint = metadata["fingerprint"]?.GetValue<string>();
            if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                string.IsNullOrWhiteSpace(rulesetId) ||
                string.IsNullOrWhiteSpace(fingerprint) ||
                metadata["capabilities"]?["manufacturingResolution"]?.GetValue<string>()
                    != "manual-competency-subset")
                return Unsupported("Rules Core does not currently advertise native manufacturing.");

            var nativeRequest = JsonSerializer.SerializeToElement(new {
                rulesetId,
                fingerprint,
                competency = new {
                    displayName = request.Competency.Manual.DisplayName,
                    contribution = request.Competency.Manual.Contribution,
                    isQualified = request.Competency.Manual.IsQualified
                },
                request.HasQualifiedGuidance,
                request.D20Roll,
                request.OtherModifier,
                request.TargetDc,
                request.ManualAbilityModifier
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            var result = await nativeCore.ResolveNativeManufacturingAsync(
                nativeRequest, cancellationToken);
            if (result["contractVersion"]?.GetValue<int>() != 1 ||
                result["rulesetId"]?.GetValue<string>() != rulesetId ||
                result["fingerprint"]?.GetValue<string>() != fingerprint ||
                result["status"]?.GetValue<string>() != "resolved" ||
                result["procedureKey"]?.GetValue<string>() != "manufacturing")
                return Unsupported("Rules Core did not return a valid native manufacturing result.");

            var resolved = JsonSerializer.Deserialize<RulesCoreCraftingCheckResolutionView>(
                result.ToJsonString(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return resolved is null
                ? Unsupported("Native manufacturing returned no check result.")
                : new CharacterCraftingResult(CharacterCraftingAccessStatus.Ready, resolved);
        }
        catch (RulesCoreGatewayException exception)
        {
            return Unsupported(exception.Message);
        }
        catch (JsonException)
        {
            return Unsupported("Rules Core manufacturing returned an invalid response.");
        }
    }

    public async Task<CharacterCraftingResult> ResolveEnchantingAsync(
        Guid characterId,
        CharacterEnchantingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await BuildCharacterContextAsync(characterId, cancellationToken);
        if (context.Result is not null)
        {
            return context.Result;
        }

        var rulesRequest = new RulesCoreEnchantingResolutionRequest(
            context.Projection!,
            request.CreatureType,
            request.Competency is null ? null : MapCompetency(request.Competency),
            request.SpellcastingKey,
            request.D20Roll,
            request.OtherModifier,
            request.TargetDc);

        try
        {
            var resolution = request.CampaignId is Guid campaignId
                ? await rulesCoreGateway.ResolveCampaignEnchantingAsync(
                    campaignId,
                    rulesRequest,
                    cancellationToken)
                : await rulesCoreGateway.ResolveGlobalEnchantingAsync(
                    rulesRequest,
                    cancellationToken);
            return new CharacterCraftingResult(
                CharacterCraftingAccessStatus.Ready,
                resolution);
        }
        catch (RulesCoreGatewayException exception)
        {
            return new CharacterCraftingResult(
                CharacterCraftingAccessStatus.ProjectionUnavailable,
                Message: exception.Message);
        }
    }

    private async Task<(
        RulesCoreCharacterRulesProjectionRequest? Projection,
        CharacterCraftingResult? Result)> BuildCharacterContextAsync(
        Guid characterId,
        CancellationToken cancellationToken)
    {
        var buildResult = await buildService.GetAsync(characterId, cancellationToken);
        if (buildResult.Status != CharacterBuildAccessStatus.Ready || buildResult.View is null)
        {
            return (null, new CharacterCraftingResult(MapBuildStatus(buildResult.Status)));
        }

        var stateResult = await stateService.GetAsync(characterId, cancellationToken);
        if (stateResult.Status != CharacterStateAccessStatus.Ready || stateResult.View is null)
        {
            return (null, new CharacterCraftingResult(MapStateStatus(stateResult.Status)));
        }

        return (
            CharacterRulesProjectionRequestBuilder.Build(
                buildResult.View,
                stateResult.View),
            null);
    }

    private static CharacterCraftingResult Unsupported(string message) =>
        new(CharacterCraftingAccessStatus.ProjectionUnavailable, Message: message);

    private static RulesCoreCraftingCompetencyInput MapCompetency(
        CharacterCraftingCompetencyRequest request) =>
        new(
            request.CompetencyKey,
            request.Manual is null
                ? null
                : new RulesCoreCraftingManualCompetencyInput(
                    request.Manual.DisplayName,
                    request.Manual.Contribution,
                    request.Manual.IsQualified));

    private static CharacterCraftingAccessStatus MapBuildStatus(
        CharacterBuildAccessStatus status) => status switch
    {
        CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterCraftingAccessStatus.NotFoundOrNotOwned,
        CharacterBuildAccessStatus.ProjectionUnavailable => CharacterCraftingAccessStatus.ProjectionUnavailable,
        CharacterBuildAccessStatus.Unauthenticated => CharacterCraftingAccessStatus.Unauthenticated,
        CharacterBuildAccessStatus.SheetNotInitialized => CharacterCraftingAccessStatus.SheetNotInitialized,
        CharacterBuildAccessStatus.ArchivedReadOnly => CharacterCraftingAccessStatus.Ready,
        _ => CharacterCraftingAccessStatus.ProjectionUnavailable
    };

    private static CharacterCraftingAccessStatus MapStateStatus(
        CharacterStateAccessStatus status) => status switch
    {
        CharacterStateAccessStatus.NotFoundOrNotOwned => CharacterCraftingAccessStatus.NotFoundOrNotOwned,
        CharacterStateAccessStatus.ProjectionUnavailable => CharacterCraftingAccessStatus.ProjectionUnavailable,
        CharacterStateAccessStatus.Unauthenticated => CharacterCraftingAccessStatus.Unauthenticated,
        CharacterStateAccessStatus.SheetNotInitialized => CharacterCraftingAccessStatus.SheetNotInitialized,
        CharacterStateAccessStatus.ArchivedReadOnly => CharacterCraftingAccessStatus.Ready,
        _ => CharacterCraftingAccessStatus.ProjectionUnavailable
    };
}
