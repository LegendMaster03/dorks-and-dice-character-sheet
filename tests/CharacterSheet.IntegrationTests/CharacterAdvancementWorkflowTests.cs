using System.Net;
using System.Net.Http.Json;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CharacterSheet.IntegrationTests;

public sealed class CharacterAdvancementWorkflowTests
{
    [Fact]
    public async Task SubclassAcquisitionRequiresExplicitEligibleAdvancementChoiceAndPersistsParentOccurrence()
    {
        using var factory = new AdvancementWorkflowFactory();
        var characterId = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(
            characterId,
            "Advancing Character",
            "Active",
            null,
            []));

        using (var initialize = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/sheet"))
        {
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        }

        CharacterBuildView startingBuild;
        using (var startingClass = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/starting-class",
                   new { conceptKey = "class:fighter" }))
        {
            Assert.Equal(HttpStatusCode.OK, startingClass.StatusCode);
            startingBuild = (await startingClass.Content.ReadFromJsonAsync<CharacterBuildView>())!;
        }
        var classEntry = Assert.Single(startingBuild.ProgressionEntries);

        using (var setLevel = await factory.SendHostedAsync(
                   HttpMethod.Put,
                   $"/api/characters/{characterId:D}/build/advancements/{classEntry.Id:D}/level",
                   new { level = 2 }))
        {
            Assert.Equal(HttpStatusCode.OK, setLevel.StatusCode);
        }

        using (var noExplicitSubclass = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/advancement/preview",
                   new { classAdvancementEntryId = classEntry.Id }))
        {
            Assert.Equal(HttpStatusCode.OK, noExplicitSubclass.StatusCode);
            var plan = await noExplicitSubclass.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
            Assert.NotNull(plan);
            Assert.False(plan.CanApply);
            Assert.Equal("choices-required", plan.Status);
            var required = Assert.Single(plan.RequiredChoices);
            Assert.Equal("subclass", required.Kind);
            Assert.Equal("choice-required", required.State);
            Assert.Null(required.SelectedValue);
        }

        using (var selectedSubclass = await factory.SendHostedAsync(
                   HttpMethod.Post,
                   $"/api/characters/{characterId:D}/advancement/preview",
                   new
                   {
                       classAdvancementEntryId = classEntry.Id,
                       subclassConceptKey = "subclass:champion"
                   }))
        {
            Assert.Equal(HttpStatusCode.OK, selectedSubclass.StatusCode);
            var plan = await selectedSubclass.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
            Assert.NotNull(plan);
            Assert.True(plan.CanApply);
            Assert.Equal("ready", plan.Status);
            Assert.Equal("subclass:champion", plan.SubclassConceptKey);
            Assert.Equal("Champion", plan.SubclassDisplayName);
            Assert.NotNull(plan.SubclassEligibility);
            Assert.True(plan.SubclassEligibility!.Eligible);
            Assert.Contains(plan.Changes, value =>
                value.Kind == "subclass-acquired"
                && value.Label.Contains("Champion", StringComparison.Ordinal));
            Assert.Contains(plan.Changes, value =>
                value.Kind == "feature"
                && value.Label == "Improved Critical");
        }

        Assert.NotNull(factory.RulesCore.LastEligibilityRequest);
        Assert.Equal("subclass:champion", factory.RulesCore.LastEligibilityRequest!.CandidateConceptKey);
        Assert.Equal(
            classEntry.Id.ToString("D"),
            factory.RulesCore.LastEligibilityRequest.ParentAdvancementOccurrenceKey);

        using var apply = await factory.SendHostedAsync(
            HttpMethod.Post,
            $"/api/characters/{characterId:D}/advancement/apply",
            new
            {
                classAdvancementEntryId = classEntry.Id,
                subclassConceptKey = "subclass:champion"
            });
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var result = await apply.Content.ReadFromJsonAsync<AdvancementApplyResponse>();
        Assert.NotNull(result);
        Assert.Equal("applied", result.Plan.Status);

        var fighter = Assert.Single(result.Build.ProgressionEntries, value => value.Kind == "class");
        Assert.Equal(3, fighter.Level);
        var champion = Assert.Single(result.Build.ProgressionEntries, value => value.Kind == "subclass");
        Assert.Equal("subclass:champion", champion.RuleConceptKey);
        Assert.Equal(fighter.Id, champion.ParentAdvancementEntryId);
        Assert.Null(champion.Level);
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

    private sealed record AdvancementApplyResponse(
        CharacterAdvancementPlanView Plan,
        CharacterBuildView Build);

    private sealed class AdvancementWorkflowFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase database = PostgresTestDatabase.Create();

        public ToolHostAuthenticationContext Context { get; set; } = Context();
        public FakeRulesCoreGateway RulesCore { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CharacterSheet", database.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToolHostAuthenticationClient>();
                services.AddSingleton<IToolHostAuthenticationClient>(new MutableAuthenticationClient(this));
                services.RemoveAll<IRulesCoreGateway>();
                services.AddSingleton<IRulesCoreGateway>(RulesCore);
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
                database.Dispose();
            }
        }

        private sealed class MutableAuthenticationClient(AdvancementWorkflowFactory factory)
            : IToolHostAuthenticationClient
        {
            public Task<ToolHostAuthenticationContext?> RedeemAsync(
                string ticket,
                string introspectionPath,
                CancellationToken cancellationToken = default) =>
                Task.FromResult<ToolHostAuthenticationContext?>(factory.Context);
        }
    }

    private sealed class FakeRulesCoreGateway : IRulesCoreAdvancementGateway
    {
        private const string ClassKey = "class:fighter";
        private const string SubclassKey = "subclass:champion";

        public RulesCoreCharacterAdvancementEligibilityRequest? LastEligibilityRequest { get; private set; }

        public Task<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>> ResolveGlobalRulesAsync(
            IReadOnlyCollection<string> conceptKeys,
            CancellationToken cancellationToken = default)
        {
            var rules = new Dictionary<string, RulesCoreResolvedRuleSummaryView>(StringComparer.Ordinal);
            foreach (var conceptKey in conceptKeys)
            {
                if (conceptKey == ClassKey)
                {
                    rules[conceptKey] = Rule(ClassKey, "class", "Fighter");
                }
                else if (conceptKey == SubclassKey)
                {
                    rules[conceptKey] = Rule(
                        SubclassKey,
                        "subclass",
                        "Champion",
                        [new RulesCoreResolvedRuleRelationshipView(
                            "parent-class",
                            Guid.NewGuid(),
                            ClassKey,
                            "class",
                            "Fighter")]);
                }
            }
            return Task.FromResult<IReadOnlyDictionary<string, RulesCoreResolvedRuleSummaryView>>(rules);
        }

        public Task<RulesCoreCharacterRulesProjectionView> ResolveGlobalCharacterMechanicsAsync(
            RulesCoreCharacterRulesProjectionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Project(request));

        public Task<RulesCoreCharacterAdvancementEligibilityView> ResolveGlobalCharacterAdvancementEligibilityAsync(
            RulesCoreCharacterAdvancementEligibilityRequest request,
            CancellationToken cancellationToken = default)
        {
            LastEligibilityRequest = request;
            var classFact = Assert.Single(request.Character.Advancements!, value => value.ConceptKey == ClassKey);
            return Task.FromResult(new RulesCoreCharacterAdvancementEligibilityView(
                "global",
                null,
                SubclassKey,
                "Champion",
                "subclass",
                "eligible",
                true,
                new RulesCoreCharacterAdvancementParentRequirementView(
                    ClassKey,
                    "Fighter",
                    3,
                    classFact.Level,
                    classFact.Level >= 3,
                    "resolved",
                    "Fighter level 3 is sufficient."),
                null,
                []));
        }

        public Task<RulesCoreCharacterRulesProjectionView> ResolveCampaignCharacterMechanicsAsync(
            Guid campaignId,
            RulesCoreCharacterRulesProjectionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCharacterAdvancementEligibilityView> ResolveCampaignCharacterAdvancementEligibilityAsync(
            Guid campaignId,
            RulesCoreCharacterAdvancementEligibilityRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCharacterSupportProjectionView> ProjectGlobalCharacterSupportAsync(
            RulesCoreCharacterSupportProjectionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCharacterRecoveryResolutionView> ResolveGlobalCharacterRecoveryAsync(
            string procedureKey,
            RulesCoreCharacterRecoveryResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCraftingCheckResolutionView> ResolveGlobalManufacturingAsync(
            RulesCoreManufacturingResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCraftingCheckResolutionView> ResolveCampaignManufacturingAsync(
            Guid campaignId,
            RulesCoreManufacturingResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCraftingCheckResolutionView> ResolveGlobalEnchantingAsync(
            RulesCoreEnchantingResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RulesCoreCraftingCheckResolutionView> ResolveCampaignEnchantingAsync(
            Guid campaignId,
            RulesCoreEnchantingResolutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static RulesCoreCharacterRulesProjectionView Project(
            RulesCoreCharacterRulesProjectionRequest request)
        {
            var classFact = request.Advancements?.SingleOrDefault(value => value.ConceptKey == ClassKey);
            var classLevel = classFact?.Level ?? 0;
            var occurrence = classFact?.OccurrenceKey ?? "missing-occurrence";
            var choiceKey = $"advancement.{ClassKey}.{occurrence}.subclass";
            var structuralSubclass = request.Advancements?.Any(value =>
                value.ConceptKey == SubclassKey
                && value.ParentConceptKey == ClassKey
                && value.ParentOccurrenceKey == occurrence) == true;
            var suppliedChoice = request.Choices?.LastOrDefault(value =>
                value.ChoiceKey == choiceKey)?.Value;
            var explicitChoice = string.Equals(
                suppliedChoice,
                SubclassKey,
                StringComparison.OrdinalIgnoreCase);

            var choices = classLevel >= 3
                ? new[]
                {
                    new RulesCoreCharacterChoiceView(
                        choiceKey,
                        $"choice-group.{choiceKey}",
                        "Fighter Subclass",
                        "subclass",
                        "resolved",
                        [new RulesCoreCharacterChoiceOptionView(SubclassKey, "Champion", SubclassKey)],
                        // Deliberately simulate a stale persisted choice even when this advancement
                        // request did not explicitly select the Subclass.
                        structuralSubclass || explicitChoice ? SubclassKey : SubclassKey,
                        ClassKey,
                        Provenance())
                }
                : [];

            var features = structuralSubclass
                ? new[]
                {
                    new RulesCoreCharacterFeatureView(
                        "feature.champion.improved-critical",
                        "Improved Critical",
                        "class-feature",
                        "resolved",
                        SubclassKey,
                        [],
                        Provenance(),
                        OccurrenceKey: request.Advancements!
                            .Single(value => value.ConceptKey == SubclassKey).OccurrenceKey,
                        GrantingSourceKind: "subclass",
                        AcquisitionLevel: 3)
                }
                : [];

            return new RulesCoreCharacterRulesProjectionView(
                "global",
                null,
                1,
                DateTimeOffset.UtcNow,
                Mechanics: [],
                Capabilities: [],
                Grants: [],
                Effects: [],
                Movement: [],
                Qualifications: [],
                Actions: [],
                Features: features,
                Resources: [],
                Spellcasting: [],
                Procedures: [],
                Choices: choices,
                Prerequisites: [],
                Conflicts: [],
                Equipment: []);
        }

        private static RulesCoreResolvedRuleSummaryView Rule(
            string conceptKey,
            string entityType,
            string displayName,
            IReadOnlyList<RulesCoreResolvedRuleRelationshipView>? relationships = null) =>
            new(
                conceptKey,
                entityType,
                displayName,
                1,
                displayName,
                "TEST",
                "test-package",
                "Test Package",
                "unified",
                "Unified",
                Relationships: relationships);

        private static RulesCoreCharacterMechanicProvenanceView Provenance() =>
            new([], [], []);
    }
}
