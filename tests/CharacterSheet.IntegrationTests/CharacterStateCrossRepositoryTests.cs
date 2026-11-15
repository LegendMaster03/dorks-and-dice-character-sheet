#if CHARACTER_STATE_INCREMENT4
extern alias CoreHost;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CoreApplication = CoreHost::RulesCore.Host.RulesCoreApplication;
using CoreTicketValidator = CoreHost::RulesCore.Host.CharacterSheetMechanicsTicketValidator;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Real delegated Rules Core HTTP plus Character Sheet PostgreSQL and owner
/// authorization. These tests do not install or grant playable lycanthropy.
/// </summary>
public sealed class CharacterStateCrossRepositoryTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Guid Actor = Guid.Parse("e977c3b5-6c50-4405-97c3-9b17f3b99e99");
    private const string Ticket = "character-state-increment4-test-ticket";

    private sealed class SiteTicketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/tool-host/registrations/rules-core/api/introspect",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(Ticket, request.Headers.Authorization?.Parameter);
            var payload = JsonSerializer.Serialize(new
            {
                contractVersion = 1, toolKey = "rules-core",
                siteMode = "dorks-and-dice",
                user = new { id = Actor.ToString("D") },
                delegatedFromToolKey = "character-sheet",
                privateTunnelSourceToolKey = (string?)null,
                campaigns = Array.Empty<object>()
            }, Wire);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CoreHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public static async Task<CoreHost> StartAsync()
        {
            var root = Environment.GetEnvironmentVariable("RULES_CORE_CONTENT_ROOT");
            if (root is null || !Directory.Exists(Path.Combine(root, "first-party-001", "native")))
                throw new InvalidOperationException("RULES_CORE_CONTENT_ROOT is missing the real native ruleset.");

            var app = CoreApplication.Create([], builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RulesCore:ApiSurface"] = "PublicOnly",
                    ["Rulesets:ContentRoot"] = root,
                    ["Rulesets:DefaultRuleset"] = "first-party-001",
                    ["ToolHost:BaseUrl"] = "https://site.test"
                });
            }, services =>
            {
                services.AddHttpClient<CoreTicketValidator>()
                    .ConfigurePrimaryHttpMessageHandler(() => new SiteTicketHandler());
            });
            await app.StartAsync();
            var server = app.Services.GetRequiredService<IServer>();
            var url = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new CoreHost(app, new HttpClient { BaseAddress = new Uri(url) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }

    private sealed class OwnerAccess(Guid ownedId, bool authorize = true)
        : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken ct = default) =>
            Task.FromResult(authorize && characterId == ownedId
                ? SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                    ownedId, "Cross-repository fixture",
                    SiteCharacterLifecycleState.Active, null, []))
                : new SiteCharacterAccessResult(SiteCharacterAccessStatus.NotFoundOrNotOwned));
    }

    private sealed class RealGateway(HttpClient http) : INativeRulesCoreGateway
    {
        public int ProjectionRequests { get; private set; }
        public async Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct)
        {
            using var request = Authorized(HttpMethod.Get, "/api/mechanics/v1/ruleset");
            using var response = await http.SendAsync(request, ct);
            return await Parse(response, ct);
        }

        public async Task<JsonNode> EvaluatePrimaryStateBaselineAsync(
            JsonElement request, CancellationToken ct)
        {
            ProjectionRequests++;
            using var message = Authorized(HttpMethod.Post,
                "/api/mechanics/v1/characters/effective-state-baseline");
            message.Content = JsonContent.Create(request, options: Wire);
            using var response = await http.SendAsync(message, ct);
            return await Parse(response, ct);
        }

        public async Task<HttpResponseMessage> SendUnverifiedAsync(
            JsonElement request, CancellationToken ct = default)
        {
            using var message = Authorized(HttpMethod.Post,
                "/api/mechanics/v1/characters/effective-state-baseline");
            message.Content = JsonContent.Create(request, options: Wire);
            return await http.SendAsync(message, ct);
        }

        private static HttpRequestMessage Authorized(HttpMethod method, string path)
        {
            var message = new HttpRequestMessage(method, path);
            message.Headers.Add("X-Dorks-Tool-Auth-Ticket", Ticket);
            message.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                "/tool-host/registrations/rules-core/api/introspect");
            return message;
        }

        private static async Task<JsonNode> Parse(
            HttpResponseMessage response, CancellationToken ct)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new RulesCoreGatewayException(
                    $"Real Core HTTP {(int)response.StatusCode}: {payload}");
            return JsonNode.Parse(payload) ??
                throw new RulesCoreGatewayException("Real Core returned empty JSON.");
        }

        public Task<JsonNode> SearchNativeOptionsAsync(string fingerprint,
            string category, string? query, int limit, int offset, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(JsonElement request,
            CancellationToken ct) => throw new NotSupportedException();
    }

    private static CharacterEffectiveStateService Reader(CharacterSheetDbContext db,
        Guid characterId, RealGateway gateway, bool authorize = true)
    {
        var site = new OwnerAccess(characterId, authorize);
        var build = new PostgresCharacterBuildStore(db);
        return new CharacterEffectiveStateService(
            new CharacterBuildService(site, build, TimeProvider.System),
            new CharacterStateService(site, new PostgresCharacterStateStore(db),
                TimeProvider.System),
            new CharacterOriginStateService(site, build,
                new PostgresCharacterAuthorizedOriginStore(db)),
            new PostgresCharacterSupernaturalStateStore(db), gateway);
    }

    [Fact]
    public async Task OwnedPrimaryCharacterUsesRealNativeHttpAndPostgresWithoutMutatingState()
    {
        await using var core = await CoreHost.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, score) in new[]
        {
            ("strength", 14), ("dexterity", 12), ("constitution", 11),
            ("intelligence", 10), ("wisdom", 13), ("charisma", 9)
        })
            await build.SetBaseAbilityScoreInputAsync(id, key, score, now);

        var gateway = new RealGateway(core.Client);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = await store.GetAuthoritativeInputsFingerprintAsync(id);
        var result = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("partial", result.View!.Status);
        Assert.False(result.View.EffectiveMechanicsReady);
        Assert.Equal("@primary", result.View.EffectiveSheetId);
        Assert.NotNull(result.View.NativeProjection);
        Assert.Equal(14, result.View.NativeProjection!["baseMechanics"]?["values"]?
            ["ability.strength.score"]?["value"]?.GetValue<int>());
        Assert.Equal(1, gateway.ProjectionRequests);
        Assert.Null(await store.GetAsync(id));
        Assert.Empty(await store.GetEventsAsync(id));
        Assert.Equal(digest, await store.GetAuthoritativeInputsFingerprintAsync(id));

        var denied = await Reader(db, id, gateway, authorize: false).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.NotFoundOrNotOwned, denied.Status);
        Assert.Equal(1, gateway.ProjectionRequests);
    }

    [Fact]
    public async Task ForgedFormAndSavedUnverifiedOwnerNeverBecomePlayable()
    {
        await using var core = await CoreHost.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var gateway = new RealGateway(core.Client);
        var catalog = await gateway.GetNativeRulesetAsync(CancellationToken.None);
        var fingerprint = catalog["fingerprint"]!.GetValue<string>();

        var fakeRequest = JsonSerializer.SerializeToElement(new
        {
            state = new
            {
                characterId = id.ToString("D"),
                primaryMechanics = new
                {
                    rulesetId = "first-party-001", fingerprint,
                    abilityScores = new Dictionary<string, int>(),
                    advancements = Array.Empty<object>()
                },
                alternateSheets = new[]
                {
                    new
                    {
                        id = "wolf", parentId = "@primary",
                        definitionId = "forged.wolf",
                        definitionSource = new { kind = 0, id = "forged-source", version = "v1" },
                        acquisitionEvidenceId = "claimed:acquired"
                    }
                },
                persistentInfluences = Array.Empty<object>()
            },
            ledger = new
            {
                characterId = id.ToString("D"), revision = 0,
                instances = Array.Empty<object>(),
                resources = Array.Empty<object>(),
                processedEventIds = Array.Empty<string>()
            }
        }, Wire);
        using (var forbidden = await gateway.SendUnverifiedAsync(fakeRequest))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, forbidden.StatusCode);
            Assert.Contains("supernatural_authority_unavailable",
                await forbidden.Content.ReadAsStringAsync());
        }

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = JsonSerializer.Serialize(new
        {
            characterId = id.ToString("D"), revision = 0,
            instances = Array.Empty<object>(),
            resources = Array.Empty<object>(),
            processedEventIds = Array.Empty<string>()
        });
        // A controlled test writer installs source evidence which no real
        // authority has verified; read must not elevate it to a form.
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                """[{"id":"wolf","parentId":"@primary"}]""",
                "[]", ledger, 0, 0, 0, digest,
                "fixture:unverified", "test:unverified", false,
                DateTimeOffset.UtcNow))).Status);
        var result = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
        Assert.Equal("unresolved", result.View!.Status);
        Assert.Null(result.View.NativeProjection);
        Assert.False(result.View.EffectiveMechanicsReady);
        Assert.Equal(0, gateway.ProjectionRequests);
    }
}
#endif
