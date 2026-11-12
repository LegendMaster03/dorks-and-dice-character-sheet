#if PILLAR2_PHASE5_ORIGIN
extern alias CoreHost;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
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
using RulesCore.Content;
using CoreApplication = CoreHost::RulesCore.Host.RulesCoreApplication;
using CoreTicketValidator = CoreHost::RulesCore.Host.CharacterSheetMechanicsTicketValidator;

namespace CharacterSheet.IntegrationTests;

/// <summary>
/// Real cross-repository Rules Core Host HTTP evaluation plus actual PostgreSQL
/// Character Sheet transactions. Site actor/DM scope is supplied by a test
/// identity provider; Site's real ticket projection is verified separately in
/// its own integration tests. No mocked Authorized mechanics result is used.
/// </summary>
public sealed class MixedOriginCrossRepositoryPersistenceTests
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);
    private static readonly Guid Actor = Guid.Parse("b989ec77-e740-4412-87ab-fcd5c1049bcb");
    private static readonly Guid Campaign = Guid.Parse("55c09072-f61d-4c90-9d62-69e24398a881");
    private const string HumanElf = """
        {
          "id":"human-elf","intent":1,
          "method":{"ruleId":"rule.creature-origination","methodId":"biological"},
          "contributors":[
            {"id":"parent-one","role":{"kind":0},
             "source":{"kind":0,"id":"species.human"}},
            {"id":"parent-two","role":{"kind":0},
             "source":{"kind":0,"id":"species.elf"}}
          ],
          "candidates":[],"manifestations":[],
          "selectedSpeciesId":null,"selectedAncestryId":null
        }
        """;

    [Fact]
    public async Task SiteScopedHumanElfApprovalEvaluatesPersistsAndReloadsRealNativeGrants()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var characterId = Guid.NewGuid();
        var native = new HttpNativeGateway(core.Client);
        var fingerprint = core.App.Services.GetRequiredService<NativeCatalogStore>()
            .Snapshot!.Fingerprint;

        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await db.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
            var draft = await new PostgresCharacterOriginDraftStore(db).SaveAsync(
                characterId, "first-party-001", fingerprint,
                HumanElf, 0, DateTimeOffset.UtcNow);
            Assert.NotNull(draft);
            Assert.Equal(1, draft.Revision);

            var selected = await FiveNativeHumanElfOffers(native, fingerprint);
            var service = Service(db, native, allow: true);
            var denied = await Service(db, native, allow: false).ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.DmScopeDenied,
                denied.Status);
            Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
                .GetAsync(characterId));

            var stale = await service.ApplyAsync(
                characterId, Campaign, 0, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
                stale.Status);

            var forged = await service.ApplyAsync(
                characterId, Campaign, 1, ["made-up-candidate"]);
            Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
                forged.Status);
            Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
                .GetAsync(characterId));

            var applied = await service.ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.Applied, applied.Status);
            Assert.Equal(1, applied.Revision);

            var duplicate = await service.ApplyAsync(
                characterId, Campaign, 1, selected);
            Assert.Equal(CharacterOriginMixedActivationStatus.OriginAlreadyCommitted,
                duplicate.Status);
        }

        // A fresh DB context must recover all five actual native grants, the
        // construction event, actor, campaign and exact DM ruling outcomes.
        await using (var db = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var origins = new PostgresCharacterAuthorizedOriginStore(db);
            var saved = await origins.GetAsync(characterId);
            Assert.NotNull(saved);
            Assert.Equal(1, saved.Revision);
            Assert.Equal(fingerprint, saved.Fingerprint);
            using var profile = JsonDocument.Parse(saved.ProfileJson);
            Assert.Equal("custom.human-elf",
                profile.RootElement.GetProperty("underlying")
                    .GetProperty("speciesId").GetString());
            var grants = profile.RootElement.GetProperty("underlying")
                .GetProperty("inherentGrants").EnumerateArray().ToArray();
            Assert.Equal(5, grants.Length);
            Assert.Contains(grants, grant =>
                grant.GetProperty("identity").GetProperty("intrinsicSlotId")
                    .GetString() == "creatureType");
            Assert.Contains(grants, grant =>
                grant.GetProperty("identity").GetProperty("intrinsicSlotId")
                    .GetString() == "walkingSpeedFeet");
            var receipts = await origins.GetEventsAsync(characterId);
            var receipt = Assert.Single(receipts);
            Assert.Equal("construction:human-elf", receipt.EventId);
            Assert.Equal("campaign-dm", receipt.AuthorizationKind);
            Assert.Equal(Actor, receipt.ApprovalActorUserId);
            Assert.Equal(Campaign, receipt.ApprovalCampaignId);
            Assert.StartsWith("origin-dm:", receipt.AuthorizationEvidenceId);
            using var approved = JsonDocument.Parse(receipt.ApprovedDecisionsJson!);
            var decisions = approved.RootElement.EnumerateArray().ToArray();
            Assert.Equal(6, decisions.Length);
            Assert.All(decisions, decision =>
            {
                Assert.Equal(Actor.ToString("D"), decision.GetProperty("authority")
                    .GetProperty("actorId").GetString());
                Assert.Equal(Campaign.ToString("D"), decision.GetProperty("scope")
                    .GetProperty("id").GetString());
                Assert.Equal(receipt.AuthorizationEvidenceId,
                    decision.GetProperty("authority")
                        .GetProperty("authorizationEvidenceId").GetString());
            });
        }
    }

    [Fact]
    public async Task ChangedNativeCatalogOrMissingSelectionDoesNotCommitAProfile()
    {
        await using var core = await RealCore.StartAsync();
        await using var database = await PostgresTestDatabase.CreateAsync();
        var native = new HttpNativeGateway(core.Client);
        var characterId = Guid.NewGuid();
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        await new PostgresCharacterOriginDraftStore(db).SaveAsync(
            characterId, "first-party-001", "previous-native-fingerprint",
            HumanElf, 0, DateTimeOffset.UtcNow);
        var selected = await FiveNativeHumanElfOffers(native,
            core.App.Services.GetRequiredService<NativeCatalogStore>().Snapshot!.Fingerprint);
        var service = Service(db, native, allow: true);
        Assert.Equal(CharacterOriginMixedActivationStatus.NativeContentChanged,
            (await service.ApplyAsync(characterId, Campaign, 1, selected)).Status);
        Assert.Equal(CharacterOriginMixedActivationStatus.InvalidSelection,
            (await service.ApplyAsync(characterId, Campaign, 1, [])).Status);
        Assert.Null(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetAsync(characterId));
        Assert.Empty(await new PostgresCharacterAuthorizedOriginStore(db)
            .GetEventsAsync(characterId));
    }

    private static CharacterOriginMixedActivationService Service(
        CharacterSheetDbContext db, INativeRulesCoreGateway native, bool allow) =>
        new(new TestDmScope(allow),
            new PostgresCharacterBuildStore(db),
            new PostgresCharacterOriginDraftStore(db),
            new PostgresCharacterAuthorizedOriginStore(db),
            native, TimeProvider.System);

    private sealed class TestDmScope(bool allow) : ISiteOriginDmAccessGateway
    {
        public Task<SiteOriginDmAccessResult> CheckAsync(
            Guid campaignId, Guid characterId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(allow && campaignId == Campaign
                ? new SiteOriginDmAccessResult(SiteOriginDmAccessStatus.Authorized,
                    Actor, campaignId, characterId)
                : new SiteOriginDmAccessResult(SiteOriginDmAccessStatus.NotAuthorized));
    }

    private static async Task<string[]> FiveNativeHumanElfOffers(
        INativeRulesCoreGateway native, string fingerprint)
    {
        using var document = JsonDocument.Parse(HumanElf);
        var preview = await native.PreviewNativeOriginAsync(
            JsonSerializer.SerializeToElement(new
            {
                rulesetId = "first-party-001",
                fingerprint,
                proposal = document.RootElement
            }, Wire), CancellationToken.None);
        var candidates = preview["result"]?["evaluatedProposal"]?["candidates"]?
            .AsArray() ?? throw new InvalidOperationException("No real native Origin candidates.");
        var paths = new (string Species, string Path)[]
        {
            ("species.human", "/creatureType"),
            ("species.elf", "/size"),
            ("species.human", "/walkingSpeedFeet"),
            ("species.elf", "/traits/0"),
            ("species.human", "/traits/2")
        };
        return paths.Select(choice =>
            candidates.Single(candidate =>
                candidate?["identity"]?["occurrence"]?["source"]?["id"]?
                    .GetValue<string>() == choice.Species &&
                candidate?["identity"]?["occurrence"]?["occurrencePath"]?
                    .GetValue<string>() == choice.Path)!["id"]!.GetValue<string>())
            .ToArray();
    }

    private sealed class HttpNativeGateway(HttpClient http) : INativeRulesCoreGateway
    {
        public Task<JsonNode> PreviewNativeOriginAsync(
            JsonElement request, CancellationToken cancellationToken) =>
            SendAsync("/api/mechanics/v1/origins/preview", request, cancellationToken);

        public Task<JsonNode> EvaluateApprovedMixedOriginAsync(
            JsonElement request, CancellationToken cancellationToken) =>
            SendAsync("/api/mechanics/v1/origins/evaluate-approved-mixed", request,
                cancellationToken);

        private async Task<JsonNode> SendAsync(
            string path, JsonElement request, CancellationToken cancellationToken)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(request, options: Wire)
            };
            message.Headers.Add("X-Dorks-Tool-Auth-Ticket", "site-ticket-for-character-sheet");
            message.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                "/tool-host/registrations/rules-core/api/introspect");
            using var response = await http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new RulesCoreGatewayException(
                    $"Real Rules Core returned {(int)response.StatusCode} at {path}: {body}");
            return JsonNode.Parse(body) ??
                throw new RulesCoreGatewayException("Real Rules Core returned no JSON.");
        }

        public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<JsonNode> SearchNativeOptionsAsync(
            string fingerprint, string category, string? query, int limit, int offset,
            CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> EvaluateNativeAdvancementAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
        public Task<JsonNode> ProjectNativeCharacterAsync(
            JsonElement request, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class SiteTicketHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/tool-host/registrations/rules-core/api/introspect",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("site-ticket-for-character-sheet",
                request.Headers.Authorization?.Parameter);
            var body = JsonSerializer.Serialize(new
            {
                contractVersion = 1,
                toolKey = "rules-core",
                siteMode = "dorks-and-dice",
                user = new { id = Actor.ToString("D") },
                delegatedFromToolKey = "character-sheet",
                privateTunnelSourceToolKey = (string?)null,
                campaigns = new[] { new { id = Campaign, name = "Real-core-test", role = "DM" } }
            }, Wire);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RealCore(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public HttpClient Client { get; } = client;

        public static async Task<RealCore> StartAsync()
        {
            var root = Environment.GetEnvironmentVariable("RULES_CORE_CONTENT_ROOT");
            if (string.IsNullOrWhiteSpace(root) ||
                !Directory.Exists(Path.Combine(root, "first-party-001", "native")))
                throw new InvalidOperationException(
                    "RULES_CORE_CONTENT_ROOT must contain the real Phase 5 native catalog.");
            var app = CoreApplication.Create([], builder =>
            {
                builder.WebHost.UseUrls("http://127.0.0.1:0");
                builder.Configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
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
            return new RealCore(app, new HttpClient { BaseAddress = new Uri(url) });
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.DisposeAsync();
        }
    }
}
#endif
