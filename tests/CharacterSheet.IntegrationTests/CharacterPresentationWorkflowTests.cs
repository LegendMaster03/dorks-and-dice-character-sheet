using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterPresentationWorkflowTests
{
    [Fact]
    public async Task ActiveAndArchivedOwnedCharactersCanReadPresentationWithoutArbitraryCampaignSelection()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Projected Hero", "Active", null, [campaignId]));
        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post, $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }
        using (var feat = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/build/feats",
                   new { conceptKey = "feat:alert" }))
        {
            Assert.Equal(HttpStatusCode.OK, feat.StatusCode);
        }
        factory.Gateway.ResolvedRules["feat:alert"] = Rule("feat:alert", "feat", "Alert");

        using (var response = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/presentation"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("Alert", json.RootElement.GetProperty("advancement")
                .GetProperty("occurrences")[0].GetProperty("displayName").GetString());
            Assert.True(json.RootElement.TryGetProperty("mechanics", out _));
        }
        Assert.True(factory.Gateway.GlobalMechanicsRequested);
        Assert.False(factory.Gateway.CampaignMechanicsRequested);

        factory.Context = Context(new ToolHostCharacterContext(
            characterId,
            "Projected Hero",
            "Archived",
            DateTimeOffset.UtcNow,
            []));
        using var archived = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
    }

    [Fact]
    public async Task RecoverySupportUsesResolvedCharacterCapabilitiesAndProjectsProcedures()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Recovery", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        factory.Gateway.ProjectionCapabilities =
        [
            new RulesCoreCharacterCapabilityView(
                "recovery.short-rest",
                "Short Rest",
                [],
                new RulesCoreCharacterMechanicProvenanceView([], [], []))
        ];
        factory.Gateway.SupportProjection = new RulesCoreCharacterSupportProjectionView(
            "global",
            null,
            1,
            DateTimeOffset.UtcNow,
            [
                new RulesCoreCharacterRecoveryProcedureView(
                    "recovery.short-rest",
                    "Catch Breath",
                    "short-rest",
                    true,
                    "applicable",
                    new RulesCoreMechanicApplicabilityView(
                        "capability",
                        true,
                        ["recovery.short-rest"],
                        null),
                    [],
                    [],
                    [],
                    [],
                    new RulesCoreCharacterRecoveryRuntimeRequirementsView(
                        true,
                        false,
                        false,
                        false,
                        false),
                    [])
            ]);

        using var response = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var recovery = Assert.Single(json.RootElement.GetProperty("mechanics")
            .GetProperty("recoveryProcedures").EnumerateArray().ToArray());
        Assert.Equal("recovery.short-rest", recovery.GetProperty("procedureKey").GetString());
        Assert.Equal("Catch Breath", recovery.GetProperty("displayName").GetString());
        Assert.Equal("short-rest", recovery.GetProperty("presentationRole").GetString());
        Assert.Equal(
            ["recovery.short-rest"],
            factory.Gateway.LastSupportCapabilityKeys);
    }

    [Fact]
    public async Task RecoveryEndpointAppliesResolvedCharacterOwnedConsequences()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Recovery", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var health = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/state/health",
            new { currentHitPoints = 10 });
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var resource = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/state/rules-inputs",
            new
            {
                kind = "resource",
                key = "resource.focus",
                integerValue = 5,
                booleanValue = (bool?)null,
                textValue = (string?)null
            });
        Assert.Equal(HttpStatusCode.OK, resource.StatusCode);

        factory.Gateway.RecoveryResolution = new RulesCoreCharacterRecoveryResolutionView(
            "global",
            null,
            1,
            DateTimeOffset.UtcNow,
            "recovery.fixture",
            "Fixture Recovery",
            "short-rest",
            "resolved",
            [],
            [],
            [],
            [],
            [
                new RulesCoreCharacterRecoveryEffectView(
                    "heal", "resource", "hit-points", "adjust", 4, null, null),
                new RulesCoreCharacterRecoveryEffectView(
                    "spend", "resource", "resource.focus", "expend", 2, null, null),
                new RulesCoreCharacterRecoveryEffectView(
                    "reset-failures", "resource", "resource.death-save.failures", "set", 0, null, null)
            ],
            []);

        using var response = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/state/recovery/recovery.fixture",
            new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("resolved", json.RootElement.GetProperty("resolution").GetProperty("status").GetString());
        var state = json.RootElement.GetProperty("state");
        Assert.Equal(14, state.GetProperty("currentHitPoints").GetInt32());
        var focus = Assert.Single(
            state.GetProperty("rulesInputs").EnumerateArray()
                .Where(value => value.GetProperty("kind").GetString() == "resource"
                    && value.GetProperty("key").GetString() == "resource.focus"));
        Assert.Equal(3, focus.GetProperty("integerValue").GetInt32());
    }

    [Fact]
    public async Task UnsupportedRecoveryConsequenceDoesNotPartiallyMutateCharacterState()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Recovery", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var health = await factory.SendHostedAsync(
            HttpMethod.Put,
            $"/api/characters/{characterId:D}/state/health",
            new { currentHitPoints = 10 });
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        factory.Gateway.RecoveryResolution = new RulesCoreCharacterRecoveryResolutionView(
            "global",
            null,
            1,
            DateTimeOffset.UtcNow,
            "recovery.fixture",
            "Fixture Recovery",
            null,
            "resolved",
            [],
            [],
            [],
            [],
            [
                new RulesCoreCharacterRecoveryEffectView(
                    "heal", "resource", "hit-points", "adjust", 4, null, null),
                new RulesCoreCharacterRecoveryEffectView(
                    "unsupported", "condition", "condition.prone", "set", 0, null, null)
            ],
            []);

        using var response = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/state/recovery/recovery.fixture",
            new { });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var stateResponse = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/state");
        Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
        using var stateJson = JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
        Assert.Equal(10, stateJson.RootElement.GetProperty("currentHitPoints").GetInt32());
    }

    [Fact]
    public async Task RecoveryContinuationReturnsPendingRulesCoreStateWithoutMutation()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Recovery", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        factory.Gateway.RecoveryResolution = new RulesCoreCharacterRecoveryResolutionView(
            "global",
            null,
            1,
            DateTimeOffset.UtcNow,
            "recovery.fixture",
            "Fixture Recovery",
            null,
            "choice-required",
            [],
            [],
            [new RulesCoreCharacterRecoveryChoiceView(
                "resource",
                "Choose resource",
                true,
                [new RulesCoreCharacterRecoveryChoiceOptionView(
                    "focus", "Focus", "resource.focus")])],
            [],
            [],
            []);

        using var response = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/state/recovery/recovery.fixture",
            new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("choice-required", json.RootElement.GetProperty("resolution").GetProperty("status").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("resolution").GetProperty("pendingChoices").GetArrayLength());
    }

    [Fact]
    public async Task UninitializedAndUnauthorizedCharactersRemainUnavailable()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Basic", "Active", null, []));

        using (var uninitialized = await factory.SendHostedAsync(
                   HttpMethod.Get,
                   $"/api/characters/{characterId:D}/presentation"))
        {
            Assert.Equal(HttpStatusCode.NotFound, uninitialized.StatusCode);
        }

        factory.Context = Context();
        using var unauthorized = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/presentation");
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
    }

    [Fact]
    public async Task RulesCoreFailurePreservesCharacterOwnedAdvancementAndReturnsNullMechanics()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Degraded", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using var feat = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/build/feats",
            new { conceptKey = "feat:unavailable" });
        Assert.Equal(HttpStatusCode.OK, feat.StatusCode);

        factory.Gateway.Throw = true;
        using var response = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var occurrence = json.RootElement.GetProperty("advancement").GetProperty("occurrences")[0];
        Assert.Equal("feat:unavailable", occurrence.GetProperty("conceptKey").GetString());
        Assert.Equal("Unavailable rule reference", occurrence.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("mechanics").ValueKind);
    }

    [Fact]
    public async Task PartialMechanicsReturnsUnconfiguredCompetencyWithRealProjectionShape()
    {
        using var factory = new PresentationFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId, "Skills", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        factory.Gateway.Catalog = Catalog(Competency());
        using var response = await factory.SendHostedAsync(
            HttpMethod.Get,
            $"/api/characters/{characterId:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var competency = json.RootElement.GetProperty("mechanics")
            .GetProperty("competencies")
            .GetProperty("entries")[0];
        Assert.Equal("skill.hide", competency.GetProperty("key").GetString());
        Assert.Equal("-", competency.GetProperty("effectiveValue").GetString());
        Assert.True(competency.GetProperty("supportsRanks").GetBoolean());
        Assert.True(competency.GetProperty("supportsClassSkillState").GetBoolean());
        Assert.False(competency.GetProperty("supportsTrainingState").GetBoolean());
        Assert.False(competency.TryGetProperty("ranks", out _));
        Assert.False(competency.TryGetProperty("training", out _));
        Assert.False(competency.TryGetProperty("classSkill", out _));
        Assert.False(json.RootElement.GetProperty("mechanics").TryGetProperty("abilityValues", out _));
    }

    private static ToolHostAuthenticationContext Context(params ToolHostCharacterContext[] characters) =>
        new(
            1,
            "character-sheet",
            "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Owner"),
            [],
            [],
            characters);

    private static RulesCoreResolvedRuleSummaryView Rule(string key, string type, string name) =>
        new(key, type, name, 1, name, "SRD", "fixture", "Fixture", "5e", "5e");

    private static RulesCoreMechanicsCatalogView Catalog(params RulesCoreMechanicView[] mechanics) =>
        new("global", null, 1, DateTimeOffset.UtcNow, mechanics);

    private static RulesCoreMechanicView Competency() =>
        new(
            "competency.skill.hide",
            "competency",
            "Hide",
            "skill.hide",
            true,
            new RulesCoreMechanicApplicabilityView("always", true, [], null),
            "competency-profile",
            false,
            [],
            [],
            [],
            null,
            new RulesCoreCompetencyDefinitionView(
                "skill", null, null, "dexterity", true, true, false, false, true, null, []),
            [],
            []);

    private sealed class PresentationFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();

        public ToolHostAuthenticationContext Context { get; set; } = Context();
        public FakeRulesCoreGateway Gateway { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CharacterSheet", _database.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToolHostAuthenticationClient>();
                services.AddSingleton<IToolHostAuthenticationClient>(new MutableAuthenticationClient(this));
                services.RemoveAll<IRulesCoreGateway>();
                services.AddSingleton<IRulesCoreGateway>(Gateway);
            });
        }

        public async Task<HttpResponseMessage> SendHostedAsync(
            HttpMethod method,
            string path,
            object? body = null)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ToolHostAuthenticationHeaders.Ticket, "test-ticket");
            request.Headers.Add(
                ToolHostAuthenticationHeaders.IntrospectionPath,
                DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            return await client.SendAsync(request);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _database.Dispose();
            }
        }

        private sealed class MutableAuthenticationClient(PresentationFactory factory)
            : IToolHostAuthenticationClient
        {
            public Task<ToolHostAuthenticationContext?> RedeemAsync(
                string ticket,
                string introspectionPath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<ToolHostAuthenticationContext?>(factory.Context);
        }
    }

    private sealed class FakeRulesCoreGateway : IRulesCoreGateway
    {
        public bool Throw { get; set; }
        public bool GlobalMechanicsRequested { get; private set; }
        public bool GlobalCharacterProjectionRequested { get; private set; }
        public Guid? CharacterProjectionCampaignId { get; private set; }
        public bool CampaignMechanicsRequested => CharacterProjectionCampaignId is not null;
        public Dictionary<string, RulesCoreResolvedRuleSummaryView> ResolvedRules { get; } =
            new(StringComparer.Ordinal);
        public RulesCoreMechanicsCatalogView Catalog { get; set; } =
            new("global", null, 1, DateTimeOffset.UtcNow, []);
        public IReadOnlyList<RulesCoreCharacterCapabilityView> ProjectionCapabilities { get; set; } = [];
        public RulesCoreCharacterSupportProjectionView SupportProjection { get; set; } =
            new("global", null, 1, DateTimeOffset.UtcNow, []);
        public RulesCoreCharacterRecoveryResolutionView? RecoveryResolution { get; set; }
        public IReadOnlyList<string>? LastSupportCapabilityKeys { get; private set; }
        public RulesCoreCharacterRecoveryResolutionRequest? LastRecoveryRequest { get; private set; }

        public Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
            IReadOnlyCollection<string> conceptKeys,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new RulesCoreGatewayException("test outage");
            IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView> result = ResolvedRules
                .Where(value => conceptKeys.Contains(value.Key, StringComparer.Ordinal))
                .ToDictionary(StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public Task<RulesCoreMechanicsCatalogView> GetGlobalMechanicsAsync(
            CancellationToken cancellationToken = default)
        {
            GlobalMechanicsRequested = true;
            if (Throw) throw new RulesCoreGatewayException("test outage");
            return Task.FromResult(Catalog);
        }

        public Task<RulesCoreMechanicsBatchEvaluationView> EvaluateGlobalMechanicsAsync(
            RulesCoreMechanicsBatchEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new RulesCoreGatewayException("test outage");
            return Task.FromResult(new RulesCoreMechanicsBatchEvaluationView(
                "global", null, 1, DateTimeOffset.UtcNow, []));
        }

        public Task<RulesCoreCharacterSupportProjectionView> ProjectGlobalCharacterSupportAsync(
            RulesCoreCharacterSupportProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new RulesCoreGatewayException("test outage");
            LastSupportCapabilityKeys = request.CapabilityKeys;
            return Task.FromResult(SupportProjection);
        }

        public Task<RulesCoreCharacterRecoveryResolutionView> ResolveGlobalCharacterRecoveryAsync(
            string procedureKey,
            RulesCoreCharacterRecoveryResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Throw) throw new RulesCoreGatewayException("test outage");
            LastRecoveryRequest = request;
            return Task.FromResult(RecoveryResolution ?? new RulesCoreCharacterRecoveryResolutionView(
                "global",
                null,
                1,
                DateTimeOffset.UtcNow,
                procedureKey,
                procedureKey,
                null,
                "resolved",
                [],
                [],
                [],
                [],
                [],
                []));
        }

        public Task<RulesCoreCharacterRulesProjectionView> ResolveGlobalCharacterMechanicsAsync(
            RulesCoreCharacterRulesProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            GlobalCharacterProjectionRequested = true;
            if (Throw) throw new RulesCoreGatewayException("test outage");
            return Task.FromResult(EmptyProjection("global", null) with
            {
                Capabilities = ProjectionCapabilities
            });
        }

        public Task<RulesCoreCharacterRulesProjectionView> ResolveCampaignCharacterMechanicsAsync(
            Guid campaignId,
            RulesCoreCharacterRulesProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            CharacterProjectionCampaignId = campaignId;
            if (Throw) throw new RulesCoreGatewayException("test outage");
            return Task.FromResult(EmptyProjection("campaign", campaignId));
        }

        private static RulesCoreCharacterRulesProjectionView EmptyProjection(
            string scope,
            Guid? campaignId) =>
            new(
                scope,
                campaignId,
                1,
                DateTimeOffset.UtcNow,
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                [],
                []);

    }
}
