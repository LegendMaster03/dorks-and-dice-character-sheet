using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Hosting;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Infrastructure.Hosting;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Native-only hosted presentation acceptance. Character Sheet has no released
/// legacy character population and must not call retired Rules Core APIs.
/// </summary>
public sealed class CharacterPresentationWorkflowTests
{
    // Deliberately minimal, but structurally valid first-party direct Orc
    // record. Tests verify that server-owned committed grant occurrences,
    // not browser Species IDs, are the only intrinsic input.
    private const string OrcOriginProfile = """
        {
          "id":"origin:direct-orc","proposalId":"direct-orc",
          "underlying":{
            "speciesId":"species.orc","ancestryId":null,
            "profileId":"origin:direct-orc",
            "inherentGrants":[
              {"id":"grant:type","effectiveValue":"humanoid","identity":{
                "kind":0,"intrinsicSlotId":"creatureType",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/creatureType"}}},
              {"id":"grant:size","effectiveValue":"medium","identity":{
                "kind":0,"intrinsicSlotId":"size",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/size"}}},
              {"id":"grant:walk","effectiveValue":30,"identity":{
                "kind":0,"intrinsicSlotId":"walkingSpeedFeet",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/walkingSpeedFeet"}}}
            ]
          },
          "current":{
            "speciesId":"species.orc","ancestryId":null,
            "profileId":"origin:direct-orc",
            "inherentGrants":[
              {"id":"grant:type","effectiveValue":"humanoid","identity":{
                "kind":0,"intrinsicSlotId":"creatureType",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/creatureType"}}},
              {"id":"grant:size","effectiveValue":"medium","identity":{
                "kind":0,"intrinsicSlotId":"size",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/size"}}},
              {"id":"grant:walk","effectiveValue":30,"identity":{
                "kind":0,"intrinsicSlotId":"walkingSpeedFeet",
                "occurrence":{"source":{"kind":0,"id":"species.orc"},
                "occurrencePath":"/walkingSpeedFeet"}}}
            ]
          },
          "independentGrants":[],
          "history":[{
            "id":"construction:direct-orc",
            "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
            "stage":2,"contributorIds":[],"replacesConfiguration":false,
            "resultingProfileId":"origin:direct-orc"
          }],
          "adjudication":{"adjudication":0,
            "nativeRuleReference":"rule.creature-origination"}
        }
        """;

    [Fact]
    public async Task EmptyCharacterIsUnconfiguredWithoutCallingRulesCore()
    {
        using var factory = new PresentationFactory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using var initialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet");
        Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unconfigured", json.GetProperty("mechanicsStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("mechanics").ValueKind);
        Assert.Equal(0, factory.Native.Calls);
    }

    [Fact]
    public async Task CommittedOriginKeepsNativeCalculationsPartialUntilItsEffectsAreIntegrated()
    {
        using var factory = new PresentationFactory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using (var initialized = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, initialized.StatusCode);
        using (var ability = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/ability-scores/strength", new { score = 15 }))
            Assert.Equal(HttpStatusCode.OK, ability.StatusCode);
        using (var startingClass = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/starting-class",
            new { conceptKey = "class.warlock" }))
            Assert.Equal(HttpStatusCode.OK, startingClass.StatusCode);

        await factory.CommitOriginForPresentationTestAsync(id);
        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("partial", document.GetProperty("mechanicsStatus").GetString());
        Assert.Contains("Species/Ancestry",
            document.GetProperty("mechanicsMessage").GetString()!, StringComparison.Ordinal);
        var mechanics = document.GetProperty("mechanics");
        // Null-valued optional mechanics fields are omitted by the public
        // presentation serializer rather than emitted as JSON null.
        Assert.False(mechanics.TryGetProperty("abilityValues", out _));
        Assert.False(mechanics.TryGetProperty("savingThrows", out _));
        var walk = Assert.Single(mechanics.GetProperty("movement").EnumerateArray());
        Assert.Equal("movement.walk", walk.GetProperty("key").GetString());
        Assert.Equal(30, walk.GetProperty("effectiveValue").GetInt32());
        var metadata = mechanics.GetProperty("characterMetadata").EnumerateArray().ToArray();
        Assert.Contains(metadata, item => item.GetProperty("key").GetString() ==
            "origin.base-size" && item.GetProperty("effectiveValue").GetString() == "medium");
        Assert.Contains(metadata, item => item.GetProperty("key").GetString() ==
            "origin.base-creature-type" &&
            item.GetProperty("effectiveValue").GetString() == "humanoid");
        Assert.Equal(30, factory.Native.LastBaseWalkSpeed);
        Assert.Equal(1, factory.Native.OriginPreviewCalls);
        Assert.Equal(15, factory.Native.LastAbilityScore);
        Assert.Equal("class.warlock", factory.Native.LastClassId);
    }

    [Fact]
    public async Task SavedAbilityIsReadThroughNativeProjectorWithoutLegacyMechanicsCalls()
    {
        using var factory = new PresentationFactory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using (var setAbility = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/ability-scores/strength", new { score = 15 }))
            Assert.Equal(HttpStatusCode.OK, setAbility.StatusCode);

        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("partial", json.GetProperty("mechanicsStatus").GetString());
        var strength = Assert.Single(json.GetProperty("mechanics")
            .GetProperty("abilityValues").EnumerateArray());
        Assert.Equal("strength", strength.GetProperty("key").GetString());
        Assert.Equal(15, strength.GetProperty("effectiveValue").GetInt32());
        Assert.Equal(4, factory.Native.Calls); // Ruleset, Skill, Competency, projection.
        Assert.Equal(15, factory.Native.LastAbilityScore);
        Assert.Null(factory.Native.LastClassId);
    }

    [Fact]
    public async Task NativeClassSelectionKeepsNativeIdAndRendersVerifiedLabel()
    {
        using var factory = new PresentationFactory();
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using (var setClass = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/starting-class", new { conceptKey = "class.warlock" }))
            Assert.Equal(HttpStatusCode.OK, setClass.StatusCode);

        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var occurrence = Assert.Single(json.GetProperty("advancement")
            .GetProperty("occurrences").EnumerateArray());
        Assert.Equal("class.warlock", occurrence.GetProperty("conceptKey").GetString());
        Assert.Equal("Warlock", occurrence.GetProperty("displayName").GetString());
        Assert.Equal("class.warlock", factory.Native.LastClassId);
    }

    [Fact]
    public async Task UnknownNativeClassDoesNotGenerateClassBenefitsOrRewriteSelection()
    {
        using var factory = new PresentationFactory { Native = new NativeGateway { HasWarlock = false } };
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using (var setClass = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/starting-class", new { conceptKey = "class.warlock" }))
            Assert.Equal(HttpStatusCode.OK, setClass.StatusCode);

        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unavailable", json.GetProperty("mechanicsStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("mechanics").ValueKind);
        Assert.Equal("class.warlock", json.GetProperty("advancement")
            .GetProperty("occurrences")[0].GetProperty("conceptKey").GetString());
        Assert.Null(factory.Native.LastClassId);
    }

    [Fact]
    public async Task RulesCoreOutageProducesVisibleUnavailableState()
    {
        using var factory = new PresentationFactory { Native = new NativeGateway { Throw = true } };
        var id = Guid.NewGuid();
        factory.Context = Context(new ToolHostCharacterContext(id, "Tester", "Active", null, []));
        using (var initialize = await factory.SendHostedAsync(HttpMethod.Post,
            $"/api/characters/{id:D}/sheet"))
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);
        using (var setAbility = await factory.SendHostedAsync(HttpMethod.Put,
            $"/api/characters/{id:D}/build/ability-scores/strength", new { score = 15 }))
            Assert.Equal(HttpStatusCode.OK, setAbility.StatusCode);

        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("unavailable", json.GetProperty("mechanicsStatus").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("mechanics").ValueKind);
    }

    [Fact]
    public async Task UnauthorizedCharactersAreNotExposedThroughNativeProjection()
    {
        using var factory = new PresentationFactory();
        var id = Guid.NewGuid();
        using var response = await factory.SendHostedAsync(HttpMethod.Get,
            $"/api/characters/{id:D}/presentation");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, factory.Native.Calls);
    }

    private static ToolHostAuthenticationContext Context(params ToolHostCharacterContext[] characters) =>
        new(1, "character-sheet", "dorks-and-dice",
            new ToolHostUserContext(Guid.NewGuid().ToString("D"), "Owner"), [], [], characters);

    private sealed class PresentationFactory : WebApplicationFactory<Program>
    {
        private readonly PostgresTestDatabase _database = PostgresTestDatabase.Create();
        public ToolHostAuthenticationContext Context { get; set; } = Context();
        public NativeGateway Native { get; init; } = new();

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

        public async Task CommitOriginForPresentationTestAsync(Guid characterId)
        {
            await using var db = new CharacterSheetDbContext(_database.CreateOptions());
            await db.Database.MigrateAsync();
            var draft = await new PostgresCharacterOriginDraftStore(db)
                .SaveAsync(characterId, "first-party-001", "native-fixture",
                    """
                    {"id":"direct-orc","intent":0,
                     "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
                     "contributors":[],"candidates":[],"manifestations":[],
                     "selectedSpeciesId":"species.orc"}
                    """, 0, DateTimeOffset.UtcNow);
            Assert.Equal(1, draft!.Revision);
            // A controlled internal test command bypasses live native
            // adjudication. This tests presentation behavior, not entitlement.
            var committed = await new PostgresCharacterAuthorizedOriginStore(db)
                .CommitVerifiedAsync(new VerifiedOriginPersistenceCommand(
                    characterId, "first-party-001", "native-fixture",
                    OrcOriginProfile,
                    0, 1, "construction:direct-orc",
                    "rule.creature-origination", "biological",
                    "first-party", "native-catalog:native-fixture",
                    DateTimeOffset.UtcNow));
            Assert.Equal(AuthorizedOriginStoreStatus.Applied, committed.Status);
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

        private sealed class AuthenticationClient(PresentationFactory factory)
            : IToolHostAuthenticationClient
        {
            public Task<ToolHostAuthenticationContext?> RedeemAsync(
                string ticket, string path, CancellationToken cancellationToken = default) =>
                Task.FromResult<ToolHostAuthenticationContext?>(factory.Context);
        }
    }

    private sealed class NativeGateway : INativeRulesCoreGateway
    {
        private static readonly string Fingerprint = new('a', 64);
        public bool HasWarlock { get; init; } = true;
        public bool Throw { get; init; }
        public int Calls { get; private set; }
        public int? LastAbilityScore { get; private set; }
        public string? LastClassId { get; private set; }
        public int? LastBaseWalkSpeed { get; private set; }
        public int OriginPreviewCalls { get; private set; }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw) throw new RulesCoreGatewayException("Fixture ruleset unavailable.");
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint, capabilities = new { characterProjection = true }
            })!);
        }

        public Task<JsonNode> SearchNativeOptionsAsync(
            string fingerprint, string category, string? query, int limit, int offset,
            CancellationToken cancellationToken)
        {
            Calls++;
            var options = category switch
            {
                "class" => HasWarlock ? new[] { new {
                    id = "class.warlock", name = "Warlock", kind = "class" } } : [],
                "skill" or "competency" => [],
                _ => throw new InvalidOperationException("Unexpected native catalog category.")
            };
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                rulesetId = "first-party-001", fingerprint = Fingerprint, options
            })!);
        }

        public Task<JsonNode> PreviewNativeOriginAsync(
            JsonElement request, CancellationToken cancellationToken)
        {
            OriginPreviewCalls++;
            if (Throw) throw new RulesCoreGatewayException("Fixture native Origin unavailable.");
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1,
                rulesetId = "first-party-001",
                fingerprint = Fingerprint,
                previewOnly = true,
                result = new {
                    status = 4,
                    issues = Array.Empty<object>(),
                    profile = JsonNode.Parse(OrcOriginProfile)
                },
                intrinsics = new {
                    status = "resolved", previewOnly = true,
                    unresolvedSlots = Array.Empty<string>(),
                    creatureType = new {
                        value = "humanoid", grantId = "grant:type",
                        sourceId = "species.orc", occurrencePath = "/creatureType"
                    },
                    size = new {
                        value = "medium", grantId = "grant:size",
                        sourceId = "species.orc", occurrencePath = "/size"
                    },
                    walkingSpeedFeet = new {
                        value = 30, grantId = "grant:walk",
                        sourceId = "species.orc", occurrencePath = "/walkingSpeedFeet"
                    }
                }
            })!);
        }

        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken cancellationToken)
        {
            Calls++;
            var scores = request.GetProperty("abilityScores");
            LastAbilityScore = scores.TryGetProperty("strength", out var strength)
                ? strength.GetInt32() : null;
            var advancements = request.GetProperty("advancements").EnumerateArray().ToArray();
            LastClassId = advancements.Length == 0
                ? null : advancements[0].GetProperty("classId").GetString();
            LastBaseWalkSpeed = request.TryGetProperty("baseWalkSpeed", out var walk) &&
                walk.ValueKind == JsonValueKind.Number ? walk.GetInt32() : null;
            var values = new Dictionary<string, object>();
            if (LastBaseWalkSpeed is int baseWalk)
                values["movement.walk"] = new { status = "resolved", value = baseWalk };
            if (LastAbilityScore is int score)
            {
                values["ability.strength.score"] = new { status = "resolved", value = score };
                values["ability.strength.modifier"] = new {
                    status = "resolved", value = (score - 10) / 2 };
            }
            return Task.FromResult(JsonSerializer.SerializeToNode(new {
                contractVersion = 1, rulesetId = "first-party-001",
                fingerprint = Fingerprint, status = "partial", values,
                issues = new[] { new { code = "unresolved-input" } },
                requiredChoices = Array.Empty<string>()
            })!);
        }

        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by native presentation.");
    }
}
