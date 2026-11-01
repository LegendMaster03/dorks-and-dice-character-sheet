using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    public async Task NativeClassAdvancementRequiresHitPointGainAndPersistsApprovedLevel()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Advancing Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);

        using (var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id }))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var plan = await preview.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
            Assert.NotNull(plan);
            Assert.Equal("class.warlock", plan.ClassConceptKey);
            Assert.Equal("Warlock", plan.ClassDisplayName);
            Assert.Equal(1, plan.CurrentClassLevel);
            Assert.Equal(2, plan.TargetClassLevel);
            Assert.False(plan.CanApply);
            Assert.Contains(plan.BlockingConflicts, conflict =>
                conflict.ConflictKey == "hit-point-gain");
        }

        using (var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 }))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var plan = await preview.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
            Assert.NotNull(plan);
            Assert.True(plan.CanApply);
            Assert.Equal("ready", plan.Status);
        }

        using var apply = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/apply",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 });
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        var applied = await apply.Content.ReadFromJsonAsync<ApplyView>();
        Assert.NotNull(applied);
        Assert.Equal("applied", applied.Plan.Status);
        var updated = Assert.Single(applied.Build.ProgressionEntries);
        Assert.Equal(entry.Id, updated.Id);
        Assert.Equal("class.warlock", updated.RuleConceptKey);
        Assert.Equal(2, updated.Level);
        Assert.True(factory.Native.EligibilityCalls >= 3);
        Assert.All(factory.Native.RequestedLevels, level => Assert.Equal(1, level));
    }

    [Fact]
    public async Task HitPointGainOutsideNativeHitDieCannotBeApplied()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);
        using var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 9 });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
        Assert.NotNull(plan);
        Assert.False(plan.CanApply);
        Assert.Contains(plan.BlockingConflicts, conflict => conflict.ConflictKey == "hit-die");
        using var apply = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/apply",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, apply.StatusCode);
    }

    [Fact]
    public async Task RequiredNativePatronOrPactChoiceBlocksAdvancement()
    {
        using var factory = new Factory { Native = new FakeNativeGateway {
            RequiredChoices = ["advancement.class.warlock.patron",
                "advancement.class.warlock.pact"]
        } };
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);
        using var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
        Assert.NotNull(plan);
        Assert.False(plan.CanApply);
        Assert.Contains(plan.BlockingConflicts, c =>
            c.ConflictKey == "advancement.class.warlock.patron");
        Assert.Contains(plan.BlockingConflicts, c =>
            c.ConflictKey == "advancement.class.warlock.pact");
        Assert.Contains(plan.RequiredChoices, c =>
            c.Kind == "patron" && c.ChoiceKey == "native-option.class.warlock.patron"
            && c.Options.Any(o => o.Value == "patron.warlock.fiend"));
        Assert.Contains(plan.RequiredChoices, c =>
            c.Kind == "pact" && c.ChoiceKey == "native-option.class.warlock.pact"
            && c.Options.Any(o => o.Value == "pact.warlock.blade"));
    }

    [Fact]
    public async Task NativeEligibilityUnavailableDoesNotAdvanceOrPersistChanges()
    {
        using var factory = new Factory { Native = new FakeNativeGateway { IsEligible = false } };
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);

        using (var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 }))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            var plan = await preview.Content.ReadFromJsonAsync<CharacterAdvancementPlanView>();
            Assert.NotNull(plan);
            Assert.False(plan.CanApply);
            Assert.Contains(plan.BlockingConflicts, conflict =>
                conflict.ConflictKey == "class-level");
        }
        using var rejected = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/apply",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var read = await factory.SendHostedAsync(
            HttpMethod.Get, $"/api/characters/{id:D}/build");
        var build = await read.Content.ReadFromJsonAsync<CharacterBuildView>();
        Assert.NotNull(build);
        Assert.Equal(1, Assert.Single(build.ProgressionEntries).Level);
    }

    [Fact]
    public async Task UnsupportedSubclassAndRetiredClassIdentityFailClosed()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);

        using var unsupported = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new {
                classAdvancementEntryId = entry.Id,
                subclassConceptKey = "subclass.warlock.demo"
            });
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);

        using var legacy = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classConceptKey = "class:warlock" });
        Assert.Equal(HttpStatusCode.BadRequest, legacy.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedOrMissingCharacterCannotInvokeNativeEligibility()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        using var response = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classConceptKey = "class.warlock" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Native.EligibilityCalls);
    }

    [Fact]
    public async Task NativeChoiceStateReachesBothProjectionAndEligibility()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Warlock Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);
        using (var saved = await factory.SendHostedAsync(
            HttpMethod.Put, $"/api/characters/{id:D}/state/rules-inputs",
            new { kind = "choice", key = "native-option.class.warlock.patron",
                textValue = "patron.warlock.fiend" }))
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var preview = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/advancement/preview",
            new { classAdvancementEntryId = entry.Id, hitDieValue = 5 });
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(["patron.warlock.fiend"], factory.Native.LastProjectedOptions);
        Assert.Contains("patron.warlock.fiend", factory.Native.RequestedOptionCandidateIds);
    }

    [Fact]
    public async Task NativeResourceSpendAndRestUsePersistedCharacterState()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Resource Tester", "Active", null, []));
        await InitializeClassAsync(factory, id);

        using (var input = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/state/rules-inputs",
            new { kind = "resource", key = "resource.pact-magic", integerValue = 2 }))
            Assert.Equal(HttpStatusCode.OK, input.StatusCode);

        using (var spend = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/resources/resolve",
            new { resourceId = "resource.pact-magic", spend = 1 }))
        {
            Assert.Equal(HttpStatusCode.OK, spend.StatusCode);
            using var parsed = JsonDocument.Parse(await spend.Content.ReadAsStringAsync());
            Assert.Equal(1, parsed.RootElement.GetProperty("resolution")
                .GetProperty("current").GetInt32());
        }
        using (var restore = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/resources/resolve",
            new { resourceId = "resource.pact-magic", rest = "short-rest" }))
        {
            Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
            using var parsed = JsonDocument.Parse(await restore.Content.ReadAsStringAsync());
            Assert.Equal(2, parsed.RootElement.GetProperty("resolution")
                .GetProperty("current").GetInt32());
        }
        using (var invalid = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/resources/resolve",
            new { resourceId = "resource.pact-magic", spend = 1, rest = "long-rest" }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var read = await factory.SendHostedAsync(
            HttpMethod.Get, $"/api/characters/{id:D}/state");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var state = await read.Content.ReadFromJsonAsync<CharacterStateView>();
        Assert.NotNull(state);
        Assert.Equal(2, Assert.Single(state.RulesInputs!,
            value => value.Key == "resource.pact-magic").IntegerValue);
        Assert.Equal(2, factory.Native.ResourceActionCalls);
    }

    [Fact]
    public async Task NativeResourceActionsRejectMissingCurrentOrUnsupportedRest()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Resource Tester", "Active", null, []));
        await InitializeClassAsync(factory, id);

        using (var unconfigured = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/resources/resolve",
            new { resourceId = "resource.pact-magic", spend = 1 }))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unconfigured.StatusCode);
        Assert.Equal(0, factory.Native.ResourceActionCalls);
    }

    [Fact]
    public async Task NativeStartingEquipmentPersistsValidatedGrantsAndDoesNotDuplicateOnReplay()
    {
        using var factory = new Factory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Equipment Tester", "Active", null, []));
        var entry = await InitializeClassAsync(factory, id);

        // An unresolved Core response is never mistaken for zero starting items.
        factory.Native.EquipmentStatus = "needs-choices";
        using (var preview = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/state/inventory/starting-equipment/preview"))
        {
            Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
            using var payload = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
            Assert.Equal("needs-choices",
                payload.RootElement.GetProperty("status").GetString());
            Assert.Empty(payload.RootElement.GetProperty("grants").EnumerateArray());
            Assert.Single(payload.RootElement.GetProperty("choices").EnumerateArray());
        }
        using (var incomplete = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/inventory/starting-equipment"))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, incomplete.StatusCode);
        Assert.Equal(2, factory.Native.EquipmentCalls);

        var choiceKey = "starting-equipment." + entry.Id.ToString("D");
        using (var choice = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/state/rules-inputs",
            new { kind = "choice", key = choiceKey, textValue = "gold" }))
            Assert.Equal(HttpStatusCode.OK, choice.StatusCode);
        factory.Native.EquipmentStatus = "resolved";
        using (var selectedPreview = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/state/inventory/starting-equipment/preview"))
        {
            Assert.Equal(HttpStatusCode.OK, selectedPreview.StatusCode);
            using var payload = JsonDocument.Parse(await selectedPreview.Content.ReadAsStringAsync());
            Assert.Equal("resolved", payload.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, payload.RootElement.GetProperty("grants")[0]
                .GetProperty("quantity").GetInt32());
        }
        Assert.Equal("gold", factory.Native.LastEquipmentRequest
            .GetProperty("selectedPackages").GetProperty(choiceKey).GetString());
        using (var materialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/inventory/starting-equipment"))
            Assert.Equal(HttpStatusCode.OK, materialize.StatusCode);
        using (var repeat = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/inventory/starting-equipment"))
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        using var read = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/state");
        var state = await read.Content.ReadFromJsonAsync<CharacterStateView>();
        Assert.NotNull(state);
        var item = Assert.Single(state.InventoryItemOccurrences);
        Assert.Equal("item.demo.knife", item.RuleConceptKey);
        Assert.Equal(2, item.Quantity);
        Assert.Contains(factory.Native.LastEquipmentRequest.GetProperty("advancements")
            .EnumerateArray().ToArray(), row =>
                row.GetProperty("occurrenceId").GetString() == entry.Id.ToString("D"));
        Assert.Equal(5, factory.Native.EquipmentCalls);
    }

    [Fact]
    public async Task NativeStartingEquipmentRejectsInvalidGrantBatchWithoutPersistence()
    {
        using var factory = new Factory { Native = new FakeNativeGateway {
            EquipmentStatus = "resolved", InjectInvalidEquipmentGrant = true
        } };
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Equipment Tester", "Active", null, []));
        await InitializeClassAsync(factory, id);
        using var response = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/state/inventory/starting-equipment");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var read = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/state");
        var state = await read.Content.ReadFromJsonAsync<CharacterStateView>();
        Assert.Empty(state!.InventoryItemOccurrences);
    }

    private static async Task<CharacterAdvancementEntryView> InitializeClassAsync(
        Factory factory, Guid id)
    {
        using (var start = await factory.SendHostedAsync(
            HttpMethod.Post, $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, start.StatusCode);

        using var save = await factory.SendHostedAsync(
            HttpMethod.Put, $"/api/characters/{id:D}/build/starting-class",
            new { conceptKey = "class.warlock" });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        var build = await save.Content.ReadFromJsonAsync<CharacterBuildView>();
        return Assert.Single(build!.ProgressionEntries);
    }

    private static ToolHostAuthenticationContext Context(params ToolHostCharacterContext[] characters) =>
        new(1, "character-sheet", "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Owner"), [], [], characters);

    private sealed record ApplyView(CharacterAdvancementPlanView Plan, CharacterBuildView Build);

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();
        public ToolHostAuthenticationContext Context { get; set; } = Context();
        public FakeNativeGateway Native { get; init; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:CharacterSheet", _database.ConnectionString);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IToolHostAuthenticationClient>();
                services.AddSingleton<IToolHostAuthenticationClient>(new AuthenticationClient(this));
                services.RemoveAll<INativeRulesCoreGateway>();
                services.AddSingleton<INativeRulesCoreGateway>(Native);
            });
        }

        public async Task<HttpResponseMessage> SendHostedAsync(
            HttpMethod method, string path, object? body = null)
        {
            using var client = CreateClient();
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add(ToolHostAuthenticationHeaders.Ticket, "test-ticket");
            request.Headers.Add(ToolHostAuthenticationHeaders.IntrospectionPath,
                DorksAndDiceToolHostAuthenticationClient.ExpectedIntrospectionPath);
            if (body is not null) request.Content = JsonContent.Create(body);
            return await client.SendAsync(request);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _database.Dispose();
        }

        private sealed class AuthenticationClient(Factory factory) : IToolHostAuthenticationClient
        {
            public Task<ToolHostAuthenticationContext?> RedeemAsync(
                string ticket, string path, CancellationToken cancellationToken = default) =>
                Task.FromResult<ToolHostAuthenticationContext?>(factory.Context);
        }
    }

    private sealed class FakeNativeGateway : INativeRulesCoreGateway
    {
        private static readonly string Fingerprint = new('a', 64);
        public bool IsEligible { get; init; } = true;
        public IReadOnlyList<string> RequiredChoices { get; init; } = [];
        public int EligibilityCalls { get; private set; }
        public int ResourceActionCalls { get; private set; }
        public int EquipmentCalls { get; private set; }
        public string EquipmentStatus { get; set; } = "unavailable";
        public bool InjectInvalidEquipmentGrant { get; init; }
        public JsonElement LastEquipmentRequest { get; private set; }
        public List<int> RequestedLevels { get; } = [];
        public List<string> RequestedOptionCandidateIds { get; } = [];
        public string[] LastProjectedOptions { get; private set; } = [];

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken token) =>
            Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint,
                capabilities = new { advancementEligibility = "supported-subset",
                    resourceResolution = true, startingEquipmentResolution = "authored-grants-only" }
            })!);

        public Task<JsonNode> SearchNativeOptionsAsync(
            string fingerprint, string category, string? query, int limit, int offset,
            CancellationToken token)
        {
            var options = category switch {
                "class" => new[] { new { id = "class.warlock", kind = "class", name = "Warlock" } },
                "patron" => new[] { new { id = "patron.warlock.fiend", kind = "patron", name = "The Fiend" } },
                "pact" => new[] { new { id = "pact.warlock.blade", kind = "pact", name = "Pact of the Blade" } },
                "invocation" => new[] { new { id = "invocation.eldritch-sight", kind = "invocation", name = "Eldritch Sight" } },
                _ => throw new InvalidOperationException("Unsupported test option category.")
            };
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                rulesetId = "first-party-001", fingerprint = Fingerprint, options
            })!);
        }

        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken token)
        {
            EligibilityCalls++;
            var current = request.GetProperty("character")
                .GetProperty("advancements").EnumerateArray().Single();
            Assert.Equal("class.warlock", current.GetProperty("classId").GetString());
            RequestedLevels.Add(current.GetProperty("level").GetInt32());
            var candidate = request.GetProperty("candidateId").GetString()!;
            if (candidate != "class.warlock") RequestedOptionCandidateIds.Add(candidate);
            var die = request.TryGetProperty("hitDieValue", out var gain)
                && gain.ValueKind == JsonValueKind.Number ? gain.GetInt32() : (int?)null;
            var withinDie = die is null or >= 1 and <= 8;
            var accepted = IsEligible && withinDie;
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint,
                candidateId = candidate,
                candidateKind = candidate == "class.warlock" ? "class"
                    : candidate.StartsWith("patron.", StringComparison.Ordinal) ? "patron"
                    : candidate.StartsWith("pact.", StringComparison.Ordinal) ? "pact" : "invocation",
                status = accepted ? "eligible" : "ineligible",
                eligible = accepted,
                requirements = new[] {
                    new {
                        key = "class-level",
                        status = IsEligible ? "satisfied" : "failed",
                        detail = "Native class progression prerequisite"
                    },
                    new {
                        key = "hit-die",
                        status = withinDie ? "satisfied" : "failed",
                        detail = "Hit point gain must fit a d8."
                    }
                }
            })!);
        }

        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken token)
        {
            Assert.Equal("first-party-001", request.GetProperty("rulesetId").GetString());
            LastProjectedOptions = request.GetProperty("selectedOptions")
                .EnumerateArray().Select(value => value.GetString()!).ToArray();
            var resources = request.TryGetProperty("currentResources", out var currents)
                && currents.TryGetProperty("resource.pact-magic", out var value)
                ? new[] { new { id = "resource.pact-magic", maximum = 2,
                    current = value.GetInt32(), recovery = "short-or-long-rest" } }
                : [];
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint, status = "partial",
                values = new Dictionary<string, object>(),
                featureIds = Array.Empty<string>(),
                resources,
                requiredChoices = RequiredChoices,
                issues = new[] { new { code = "defense-input-required" } }
            })!);
        }
        public Task<JsonNode> ResolveNativeStartingEquipmentAsync(
            JsonElement request, CancellationToken token)
        {
            EquipmentCalls++;
            LastEquipmentRequest = request.Clone();
            var grants = EquipmentStatus == "resolved" ? new[] {
                new { grantKey = "native.grant.test-item",
                    kind = InjectInvalidEquipmentGrant ? "unsupported" : "item",
                    targetKey = "item.demo.knife", displayName = "Demo Knife", quantity = 2 }
            } : [];
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint, status = EquipmentStatus,
                grants,
                choices = new[] { new {
                    choiceKey = "starting-equipment." +
                        request.GetProperty("advancements")[0]
                            .GetProperty("occurrenceId").GetString(),
                    options = new[] { new { id = "gold", name = "Starting Gold" },
                        new { id = "kit", name = "Adventurer Kit" } }
                } },
                issues = Array.Empty<object>()
            })!);
        }

        public Task<JsonNode> ResolveNativeResourceAsync(
            JsonElement request, CancellationToken cancellationToken)
        {
            ResourceActionCalls++;
            Assert.Equal("resource.pact-magic", request.GetProperty("resourceId").GetString());
            var current = request.GetProperty("current").GetInt32();
            var spend = request.GetProperty("spend");
            var rest = request.GetProperty("rest");
            var next = spend.ValueKind == JsonValueKind.Number
                ? current - spend.GetInt32()
                : rest.GetInt32() == 0 || rest.GetInt32() == 1
                    ? 2 : current;
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                status = "resolved", resourceId = "resource.pact-magic",
                current = next, maximum = 2
            })!);
        }
    }
}
