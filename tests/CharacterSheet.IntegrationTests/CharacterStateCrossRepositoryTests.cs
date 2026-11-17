#if CHARACTER_STATE_INCREMENT4
extern alias CoreHost;
using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;
using CharacterSheet.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RulesCore.Mechanics;
using CoreAuthorityProvider = CoreHost::RulesCore.Host.INativeManifestationHostAuthorityProvider;
using CoreReadAuthorityProvider = CoreHost::RulesCore.Host.INativeManifestationHostReadAuthorityProvider;
using CoreOriginAuthorityProvider = CoreHost::RulesCore.Host.INativeOriginHostUnderlyingAuthorityProvider;
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

        public static async Task<CoreHost> StartAsync(
            CoreAuthorityProvider? verifiedSourceProvider = null,
            CoreReadAuthorityProvider? verifiedReadProvider = null,
            CoreOriginAuthorityProvider? verifiedOriginProvider = null)
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
                if (verifiedSourceProvider is not null)
                    services.AddSingleton(verifiedSourceProvider);
                if (verifiedReadProvider is not null)
                    services.AddSingleton(verifiedReadProvider);
                if (verifiedOriginProvider is not null)
                    services.AddSingleton(verifiedOriginProvider);
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

    /// <summary>
    /// An intentionally test-only independent verifier. The submitted profile
    /// and revision must equal the currently committed PostgreSQL Origin,
    /// and the separate source receipt must still be intact. No Site or DM
    /// entitlement is inferred from a serialized profile or delegated ticket.
    /// </summary>
    private sealed class PostgresSyntheticOriginAuthority(
        DbContextOptions<CharacterSheetDbContext> options, Guid characterId,
        string fingerprint, string authorizedSourceProfile) : CoreOriginAuthorityProvider
    {
        public async Task<bool> VerifyAsync(Guid actorId, Guid id, long revision,
            NativeOriginProfile profile, CancellationToken cancellationToken)
        {
            if (actorId != Actor || id != characterId || revision < 1)
                return false;
            await using var db = new CharacterSheetDbContext(options);
            var saved = await db.AuthorizedOrigins.AsNoTracking()
                .SingleOrDefaultAsync(x => x.CharacterId == id, cancellationToken);
            if (saved is null || saved.Revision != revision ||
                saved.RulesetId != "first-party-001" ||
                saved.Fingerprint != fingerprint)
                return false;
            using var committed = JsonDocument.Parse(saved.ProfileJson);
            using var sourced = JsonDocument.Parse(authorizedSourceProfile);
            var claimed = JsonSerializer.SerializeToElement(profile, Wire);
            if (!JsonElement.DeepEquals(committed.RootElement, claimed) ||
                !JsonElement.DeepEquals(committed.RootElement, sourced.RootElement))
                return false;

            var events = await db.OriginEventReceipts.AsNoTracking()
                .Where(x => x.CharacterId == id)
                .OrderBy(x => x.CommittedRevision).ToArrayAsync(cancellationToken);
            return events.Length == revision &&
                events.Select(x => x.CommittedRevision)
                    .SequenceEqual(Enumerable.Range(1, events.Length).Select(n => (long)n)) &&
                events.All(x => x.AuthorizationKind == "first-party" &&
                    x.AuthorizationEvidenceId == "fixture:origin-source" &&
                    x.MethodRuleId == "rule.creature-origination" &&
                    x.MethodId == "biological");
        }
    }

    private sealed class OwnerAccess(Guid ownedId, bool authorize = true,
        SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active)
        : ISiteCharacterAccessGateway, ISiteFreshCharacterAccessGateway
    {
        public bool Allow { get; set; } = authorize;
        public bool FreshAllow { get; set; } = authorize;
        public IReadOnlyCollection<Guid>? OverrideFreshCampaignIds { get; set; }

        public async Task<SiteCharacterAccessResult> GetFreshAuthorizedCharacterAsync(
            Guid characterId, CancellationToken ct = default)
        {
            if (!FreshAllow)
                return new(SiteCharacterAccessStatus.NotFoundOrNotOwned);
            var cached = await GetAuthorizedCharacterAsync(characterId, ct);
            return cached.Character is { } owned && OverrideFreshCampaignIds is not null
                ? cached with { Character = owned with
                    { CampaignIds = OverrideFreshCampaignIds } }
                : cached;
        }
        public SiteCharacterLifecycleState CurrentLifecycle { get; set; } = lifecycle;
        public IReadOnlyCollection<Guid> CampaignIds { get; set; } = [];

        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken ct = default) =>
            Task.FromResult(Allow && characterId == ownedId
                ? SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                    ownedId, "Cross-repository fixture",
                    CurrentLifecycle, CurrentLifecycle == SiteCharacterLifecycleState.Archived
                        ? DateTimeOffset.UtcNow : null, CampaignIds))
                : new SiteCharacterAccessResult(SiteCharacterAccessStatus.NotFoundOrNotOwned));
    }

    private sealed class RealGateway(HttpClient http) : INativeRulesCoreGateway
    {
        public int ProjectionRequests { get; private set; }
        public int VerifiedReadRequests { get; private set; }

        /// <summary>
        /// Test-only corrupted transport responses. The real host still
        /// evaluates and authenticates the unchanged persisted ledger first.
        /// Character Sheet must reject a subsequently substituted selection.
        /// </summary>
        public bool ForgePrimarySelection { get; set; }
        public bool ForgeWolfSelection { get; set; }

        // Simulates an independent Site authorization change at the exact
        // boundary after a successful, real Rules Core HTTP plan.
        public Action? AfterVerifiedPlan { get; set; }
        public Action? AfterVerifiedOriginRead { get; set; }

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

        public async Task<JsonNode> EvaluateVerifiedManifestationStateAsync(
            JsonElement request, CancellationToken ct)
        {
            VerifiedReadRequests++;
            using var message = Authorized(HttpMethod.Post,
                "/api/mechanics/v1/characters/effective-state");
            message.Content = JsonContent.Create(request, options: Wire);
            using var response = await http.SendAsync(message, ct);
            var result = await Parse(response, ct);
            if (ForgePrimarySelection)
            {
                result["projection"]!["effectiveCharacter"]!["effectiveSheetId"] = "@primary";
                result["projection"]!["effectiveCharacter"]!["manifestationInstanceId"] = null;
            }
            if (ForgeWolfSelection)
            {
                result["projection"]!["effectiveCharacter"]!["effectiveSheetId"] = "wolf";
                result["projection"]!["effectiveCharacter"]!["manifestationInstanceId"] =
                    "manifest.wolf.1";
            }
            return result;
        }

        public async Task<JsonNode> VerifyNativeOriginUnderlyingAsync(
            JsonElement request, CancellationToken ct)
        {
            using var message = Authorized(HttpMethod.Post,
                "/api/mechanics/v1/origins/underlying/verify");
            message.Content = JsonContent.Create(request, options: Wire);
            using var response = await http.SendAsync(message, ct);
            var result = await Parse(response, ct);
            AfterVerifiedOriginRead?.Invoke();
            return result;
        }

        public async Task<JsonNode> PlanVerifiedManifestationAsync(
            JsonElement request, CancellationToken ct)
        {
            using var response = await SendManifestationPlanAsync(request, ct);
            var plan = await Parse(response, ct);
            AfterVerifiedPlan?.Invoke();
            return plan;
        }

        public async Task<HttpResponseMessage> SendManifestationPlanAsync(
            JsonElement request, CancellationToken ct = default)
        {
            using var message = Authorized(HttpMethod.Post,
                "/api/mechanics/v1/characters/manifestations/plan");
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
            {
                string? code = null;
                try { code = JsonNode.Parse(payload)?["error"]?.GetValue<string>(); }
                catch (JsonException) { }
                throw new RulesCoreGatewayException(
                    $"Real Core HTTP {(int)response.StatusCode}: {payload}",
                    statusCode: (int)response.StatusCode, errorCode: code);
            }
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
            new PostgresCharacterSupernaturalStateStore(db), gateway, site);
    }


    /// <summary>
    /// Adversarial test seam: revokes external Site authority at the final
    /// Character Sheet write boundary, after all earlier fresh Site reads.
    /// This is intentionally not a production store or a fencing solution.
    /// </summary>
    private sealed class RevokeSiteAtCommitStore(
        ICharacterSupernaturalStateStore inner, Action revoke)
        : ICharacterSupernaturalStateStore
    {
        public Task<CharacterSupernaturalState?> GetAsync(
            Guid characterId, CancellationToken ct = default) =>
            inner.GetAsync(characterId, ct);

        public Task<IReadOnlyList<CharacterSupernaturalEventReceipt>> GetEventsAsync(
            Guid characterId, CancellationToken ct = default) =>
            inner.GetEventsAsync(characterId, ct);

        public Task<string?> GetAuthoritativeInputsFingerprintAsync(
            Guid characterId, CancellationToken ct = default) =>
            inner.GetAuthoritativeInputsFingerprintAsync(characterId, ct);

        public Task<CharacterSupernaturalWriteResult> CommitVerifiedAsync(
            VerifiedSupernaturalStateWrite command, CancellationToken ct = default)
        {
            revoke();
            return inner.CommitVerifiedAsync(command, ct);
        }
    }

    private static CharacterManifestationTransitionService TransitionRunner(
        CharacterSheetDbContext db, ISiteCharacterAccessGateway site,
        ICharacterSupernaturalStateStore store, RealGateway gateway,
        ITrustedCharacterManifestationTransitionSource source)
    {
        var buildStore = new PostgresCharacterBuildStore(db);
        return new CharacterManifestationTransitionService(site,
            (ISiteFreshCharacterAccessGateway)site, store, gateway, source,
            new CharacterBuildService(site, buildStore, TimeProvider.System),
            new CharacterStateService(site, new PostgresCharacterStateStore(db),
                TimeProvider.System),
            new CharacterOriginStateService(site, buildStore,
                new PostgresCharacterAuthorizedOriginStore(db)));
    }

    [Fact]
    public async Task LatestOwnedOriginUnderlyingRequiresCurrentSourceEvidenceThroughRealCore()
    {
        // Read-only bridging of the independently committed Origin to the
        // actual current-catalog Core validator. No supernatural entitlement
        // and no new Origin construction procedure is created here.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var publicCore = await CoreHost.StartAsync();
        using var catalogRequest = new HttpRequestMessage(
            HttpMethod.Get, "/api/mechanics/v1/ruleset");
        catalogRequest.Headers.Add("X-Dorks-Tool-Auth-Ticket", Ticket);
        catalogRequest.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
            "/tool-host/registrations/rules-core/api/introspect");
        using var catalogResponse = await publicCore.Client.SendAsync(catalogRequest);
        Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
        var metadata = JsonNode.Parse(await catalogResponse.Content.ReadAsStringAsync())!;
        var fingerprint = metadata["fingerprint"]!.GetValue<string>();
        var underlying = new NativeOriginCurrentConfiguration("species.human", null, []);
        var profile = new NativeOriginProfile("origin:source-verified", underlying,
            underlying, [], [],
            new NativeOriginDecisionBasis(NativeOriginAdjudication.Supported));
        var sourceJson = JsonSerializer.Serialize(profile, Wire);

        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(id);
            setup.AuthorizedOrigins.Add(new CharacterAuthorizedOriginState(
                id, "first-party-001", fingerprint, sourceJson, null,
                DateTimeOffset.UtcNow));
            setup.OriginEventReceipts.Add(new CharacterOriginEventReceipt(
                id, "construction:source-verified", 1,
                "rule.creature-origination", "biological", "first-party",
                "fixture:origin-source", DateTimeOffset.UtcNow));
            await setup.SaveChangesAsync();
        }

        var verifier = new PostgresSyntheticOriginAuthority(
            database.CreateOptions(), id, fingerprint, sourceJson);
        await using var core = await CoreHost.StartAsync(
            verifiedOriginProvider: verifier);

        async Task<(HttpStatusCode Status, JsonNode Body)> Review(
            NativeOriginProfile submitted, long revision = 1)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                "/api/mechanics/v1/origins/underlying/verify");
            request.Headers.Add("X-Dorks-Tool-Auth-Ticket", Ticket);
            request.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                "/tool-host/registrations/rules-core/api/introspect");
            request.Content = JsonContent.Create(new
            {
                characterId = id,
                originRevision = revision,
                rulesetId = "first-party-001",
                fingerprint,
                profile = submitted
            }, options: Wire);
            using var result = await core.Client.SendAsync(request);
            return (result.StatusCode,
                JsonNode.Parse(await result.Content.ReadAsStringAsync())!);
        }

        var original = await Review(profile);
        Assert.Equal(HttpStatusCode.OK, original.Status);
        Assert.Equal("partial", original.Body["status"]!.GetValue<string>());
        Assert.False(original.Body["effectiveMechanicsReady"]!.GetValue<bool>());
        Assert.Equal("species.human",
            original.Body["underlying"]!["speciesId"]!.GetValue<string>());

        var staleRevision = await Review(profile, revision: 2);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, staleRevision.Status);
        Assert.Equal("origin_authority_unverified",
            staleRevision.Body["error"]!.GetValue<string>());

        var forged = await Review(profile with
        {
            Underlying = new NativeOriginCurrentConfiguration("species.elf", null, [])
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, forged.Status);

        // A changed persisted profile at the original revision must not turn
        // an independently verified source into an alternative grant.
        await using (var tamper = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var changed = JsonSerializer.Serialize(profile with
            {
                Underlying = new NativeOriginCurrentConfiguration(
                    "species.elf", null, [])
            }, Wire);
            await tamper.AuthorizedOrigins.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ProfileJson, changed));
            var denied = await Review(profile);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.Status);
            await tamper.AuthorizedOrigins.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.ProfileJson, sourceJson));
        }

        // A matching stored profile is still insufficient when its separate
        // source receipt is altered.
        await using (var evidence = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await evidence.OriginEventReceipts.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    x => x.AuthorizationEvidenceId, "fixture:forged-source"));
            var denied = await Review(profile);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, denied.Status);
            await evidence.OriginEventReceipts.Where(x => x.CharacterId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    x => x.AuthorizationEvidenceId, "fixture:origin-source"));
        }

        var restored = await Review(profile);
        Assert.Equal(HttpStatusCode.OK, restored.Status);
        Assert.True(JsonNode.DeepEquals(original.Body, restored.Body));
        // Exercise Character Sheet's production read coordinator against the
        // independently sourced real Core HTTP endpoint, not only direct
        // test-client requests.
        await using (var owned = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var site = new OwnerAccess(id);
            var build = new PostgresCharacterBuildStore(owned);
            var verified = new CharacterVerifiedOriginUnderlyingService(
                new CharacterOriginStateService(site, build,
                    new PostgresCharacterAuthorizedOriginStore(owned)),
                new PostgresCharacterSupernaturalStateStore(owned),
                new RealGateway(core.Client), site);
            var partial = await verified.GetAsync(id);
            Assert.Equal(CharacterVerifiedOriginUnderlyingStatus.VerifiedPartial,
                partial.Status);
            Assert.Equal(1, partial.OriginRevision);
            Assert.Equal("species.human",
                partial.Underlying!["speciesId"]!.GetValue<string>());
            Assert.Equal("species.human",
                partial.Current!["speciesId"]!.GetValue<string>());
        }
        await using (var revoked = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var site = new OwnerAccess(id);
            var build = new PostgresCharacterBuildStore(revoked);
            var realGateway = new RealGateway(core.Client)
            {
                // Simulate a live Site revocation that the original
                // cached introspection snapshot still permits.
                AfterVerifiedOriginRead = () => site.FreshAllow = false
            };
            var verified = new CharacterVerifiedOriginUnderlyingService(
                new CharacterOriginStateService(site, build,
                    new PostgresCharacterAuthorizedOriginStore(revoked)),
                new PostgresCharacterSupernaturalStateStore(revoked),
                realGateway, site);
            Assert.Equal(
                CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                (await verified.GetAsync(id)).Status);
        }
        await using (var unauthorized = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var site = new OwnerAccess(id, authorize: false);
            var build = new PostgresCharacterBuildStore(unauthorized);
            var verified = new CharacterVerifiedOriginUnderlyingService(
                new CharacterOriginStateService(site, build,
                    new PostgresCharacterAuthorizedOriginStore(unauthorized)),
                new PostgresCharacterSupernaturalStateStore(unauthorized),
                new RealGateway(core.Client), site);
            Assert.Equal(
                CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                (await verified.GetAsync(id)).Status);
        }
        await using var finalDb = new CharacterSheetDbContext(database.CreateOptions());
        Assert.Empty(await finalDb.SupernaturalStates.ToArrayAsync());
        Assert.Empty(await finalDb.SupernaturalEventReceipts.ToArrayAsync());
        Assert.Equal(1, await finalDb.OriginEventReceipts.CountAsync(x => x.CharacterId == id));
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

        // Even the exact delegated character-sheet ticket must not make an
        // unverified SourceOccurrence and claimed receipt an executable plan.
        var deniedPlanRequest = JsonSerializer.SerializeToElement(new
        {
            state = fakeRequest.GetProperty("state"),
            ledger = fakeRequest.GetProperty("ledger"),
            transition = new
            {
                characterId = id.ToString("D"), expectedRevision = 0,
                eventId = "forged:activate", steps = new[]
                {
                    Enter(id, "wolf", "manifest.forged", "forged:activate")
                }
            }
        }, Wire);
        using (var deniedPlan = await gateway.SendManifestationPlanAsync(deniedPlanRequest))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, deniedPlan.StatusCode);
            Assert.Contains("supernatural_authority_unavailable",
                await deniedPlan.Content.ReadAsStringAsync());
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

    private static NativeOriginSourceReference SyntheticSource() =>
        new(NativeOriginSourceKind.Native, "synthetic.werewolf", "test-v1");

    private static NativeOriginSourceOccurrence Occurrence(string path) =>
        new(SyntheticSource(), path);

    private static NativeOriginDecisionBasis SyntheticRule() =>
        new(NativeOriginAdjudication.Supported, NativeRuleReference: "rule.synthetic.forms");

    private static NativeSheetEarnedProgression Earned(string id) =>
        new("progress." + id, "progress.definition." + id,
            Occurrence("progress." + id), "receipt.progress." + id,
            [new NativeSheetEarnedGrant("grant." + id, "grant.definition." + id,
                Occurrence("grants." + id), "receipt.grant." + id)]);

    private static NativePersistentCharacterSheet[] TestOwnedForms() =>
    [
        new("werewolf", "@primary", "form.werewolf", SyntheticSource(),
            "receipt.acquire.werewolf",
            Progression: [Earned("shared")],
            GrantApplicability: [new("grant.shared",
                NativeOriginGrantApplicabilityState.Applicable, SyntheticRule())]),
        new("wolf", "werewolf", "form.wolf", SyntheticSource(),
            "receipt.acquire.wolf",
            Overrides: new(AbilityScores: new(false,
                new Dictionary<string, int> { ["strength"] = 17 })),
            Progression: [Earned("wolf")],
            GrantApplicability: [new("grant.wolf",
                NativeOriginGrantApplicabilityState.Applicable, SyntheticRule())]),
        new("hybrid", "werewolf", "form.hybrid", SyntheticSource(),
            "receipt.acquire.hybrid",
            Overrides: new(AbilityScores: new(false,
                new Dictionary<string, int> { ["dexterity"] = 16 })))
    ];

    private static NativeCharacterState TestCharacter(Guid id, string fingerprint,
        NativePersistentCharacterSheet[] forms) =>
        new(id.ToString("D"), new NativeCharacterRequest("first-party-001",
            fingerprint, new Dictionary<string, int>
            {
                ["strength"] = 10, ["dexterity"] = 12,
                ["constitution"] = 13, ["intelligence"] = 11,
                ["wisdom"] = 12, ["charisma"] = 10
            }, []), forms, []);

    private static NativeManifestationInstance TestInstance(Guid id,
        string form, string instanceId, string eventId) =>
        new(id.ToString("D"), instanceId, form, "form." + form,
            Occurrence("forms." + form), null, eventId,
            "receipt.activate." + eventId, SyntheticRule(),
            NativeManifestationLifecycle.Active,
            NativeManifestationContribution.Contributing,
            new Dictionary<string, int>(), ["rule.synthetic.revert"],
            [], eventId);

    private static NativeManifestationTransitionStep Enter(Guid id,
        string form, string instanceId, string eventId) =>
        new(NativeManifestationTransitionAction.Activate, instanceId,
            Occurrence("actions.activate"), SyntheticRule(),
            "receipt.transition." + eventId,
            TestInstance(id, form, instanceId, eventId));

    private static NativeManifestationTransitionStep End(
        string instanceId, string eventId) =>
        new(NativeManifestationTransitionAction.End, instanceId,
            Occurrence("actions.end"), SyntheticRule(),
            "receipt.transition." + eventId,
            EndingConditionId: "rule.synthetic.revert");

    private static NativeManifestationTransitionStep UnlistedEnd(
        string instanceId, string eventId) =>
        End(instanceId, eventId) with
        {
            EndingConditionId = "rule.synthetic.unlisted-ending"
        };

    private static NativeManifestationTransitionStep Contribution(
        NativeManifestationTransitionAction action, string instanceId,
        string eventId) =>
        new(action, instanceId, Occurrence("actions.contribution"),
            SyntheticRule(), "receipt.transition." + eventId);


    private static NativeManifestationResourceState ControlledWolfResource() =>
        new("resource.wolf.1", "resource.definition.wolf",
            Occurrence("resources.wolf"), "receipt.resource.wolf.1",
            NativeManifestationResourceOwner.Manifestation, "manifest.wolf.1",
            3, 5);

    private static NativeManifestationResourcePolicy ControlledWolfResourcePolicy() =>
        new("resource.wolf.1", NativeManifestationResourceMode.Separate);

    // Exact test-only acquisition grants for all three non-activation owner
    // categories. This is not a registered production resource policy.
    private static NativeManifestationResourceState[] AcquiredOwnerPools() =>
    [
        new("resource.primary.1", "resource.definition.primary",
            Occurrence("resources.primary"), "receipt.resource.primary.1",
            NativeManifestationResourceOwner.Primary, "@primary", 4, 7),
        new("resource.sheet.1", "resource.definition.sheet",
            Occurrence("resources.sheet"), "receipt.resource.sheet.1",
            NativeManifestationResourceOwner.Sheet, "werewolf", 2, 5),
        new("resource.influence.1", "resource.definition.influence",
            Occurrence("resources.influence"), "receipt.resource.influence.1",
            NativeManifestationResourceOwner.Influence,
            "influence.synthetic.pool", 1, 3)
    ];

    private static NativeCharacterPersistentInfluence PassiveResourceInfluence() =>
        new("influence.synthetic.pool", "definition.influence.synthetic.pool",
            SyntheticSource(), "receipt.acquire.influence.synthetic.pool",
            "receipt.expression.influence.synthetic.pool",
            new Dictionary<string, string>(), []);

    private static NativeManifestationResourcePolicy[] FourOwnerPolicies() =>
    [
        new("resource.primary.1", NativeManifestationResourceMode.Inherit),
        new("resource.sheet.1", NativeManifestationResourceMode.Inherit),
        new("resource.influence.1", NativeManifestationResourceMode.Inherit),
        ControlledWolfResourcePolicy()
    ];

    private static NativeManifestationTransitionStep EnterWithResource(
        Guid characterId, string eventId) =>
        new(NativeManifestationTransitionAction.Activate, "manifest.wolf.1",
            Occurrence("actions.activate"), SyntheticRule(),
            "receipt.transition." + eventId,
            TestInstance(characterId, "wolf", "manifest.wolf.1", eventId) with
            {
                ResourcePolicies = [ControlledWolfResourcePolicy()]
            },
            [ControlledWolfResource()]);

    private static NativeManifestationTransitionStep EnterWithFourOwnerResources(
        Guid characterId, string eventId) =>
        new(NativeManifestationTransitionAction.Activate, "manifest.wolf.1",
            Occurrence("actions.activate"), SyntheticRule(),
            "receipt.transition." + eventId,
            TestInstance(characterId, "wolf", "manifest.wolf.1", eventId) with
            {
                ResourcePolicies = FourOwnerPolicies()
            },
            [ControlledWolfResource()]);

    private static NativeManifestationInteractionDecision AuthorizedOverlap() =>
        new("interaction.synthetic.overlap",
            NativeManifestationInteractionKind.Replace,
            ["manifest.wolf.1", "manifest.hybrid.1"], "manifest.hybrid.1",
            Occurrence("interactions.replace"), SyntheticRule(),
            "receipt.interaction.overlap");

    private static NativeManifestationInteractionDecision UnapprovedOverlap() =>
        AuthorizedOverlap() with { AuthorizationEvidenceId = "receipt.interaction.forged" };

    // Synthetic mechanics-only interaction: the two contributing forms replace
    // disjoint ability-score slots, rather than selecting one full form.
    private static NativeManifestationInteractionDecision AuthorizedCoexist() =>
        new("interaction.synthetic.coexist",
            NativeManifestationInteractionKind.Coexist,
            ["manifest.wolf.1", "manifest.hybrid.1"], null,
            Occurrence("interactions.coexist"), SyntheticRule(),
            "receipt.interaction.coexist");



    private sealed class PostgresSyntheticAuthorityProvider(
        DbContextOptions<CharacterSheetDbContext> databaseOptions,
        Guid characterId) : CoreAuthorityProvider, CoreReadAuthorityProvider
    {
        // Read authorization is not implied by a delegated Site ticket.
        // Reuse the same independent PostgreSQL source and primary row checks;
        // no transition action is trusted or executed by this read.
        Task<INativeCharacterManifestationAuthority?> CoreReadAuthorityProvider.VerifyAsync(
            Guid actor, NativeCharacterState state, NativeManifestationLedger current,
            CancellationToken ct) =>
            VerifyAsync(actor, state, current,
                new NativeManifestationTransitionRequest(
                    state.CharacterId, current.Revision, "fixture:read", []), ct);

        public async Task<INativeCharacterManifestationAuthority?> VerifyAsync(
            Guid actor, NativeCharacterState state, NativeManifestationLedger current,
            NativeManifestationTransitionRequest transition, CancellationToken ct)
        {
            if (actor != Actor || state.CharacterId != characterId.ToString("D") ||
                current.CharacterId != state.CharacterId ||
                transition.CharacterId != state.CharacterId)
                return null;

            await using var db = new CharacterSheetDbContext(databaseOptions);
            var store = new PostgresCharacterSupernaturalStateStore(db);
            var owner = await store.GetAsync(characterId, ct);
            if (owner is null || owner.LedgerRevision != current.Revision ||
                owner.Fingerprint != state.PrimaryMechanics.Fingerprint ||
                owner.RulesetId != state.PrimaryMechanics.RulesetId ||
                !SameJson(owner.AlternateSheetsJson, JsonSerializer.Serialize(
                    state.AlternateSheets, Wire)) ||
                !SameJson(owner.PersistentInfluencesJson, JsonSerializer.Serialize(
                    state.PersistentInfluences ?? [], Wire)) ||
                !SameJson(owner.LedgerJson, JsonSerializer.Serialize(current, Wire)))
                return null;

            var receipts = await store.GetEventsAsync(characterId, ct);
            if (!receipts.Any(x => x.EventId == "fixture:acquire-werewolf" &&
                x.AuthorizationEvidenceId == "fixture:verified-source"))
                return null;
            var scores = await db.BaseAbilityScoreInputs.AsNoTracking()
                .Where(x => x.CharacterId == characterId)
                .ToDictionaryAsync(x => x.AbilityKey, x => x.Score, ct);
            if (scores.Count != state.PrimaryMechanics.AbilityScores.Count ||
                scores.Any(x => !state.PrimaryMechanics.AbilityScores.TryGetValue(
                    x.Key, out var score) || x.Value != score))
                return null;

            var classRows = await db.CharacterAdvancementEntries.AsNoTracking()
                .Where(x => x.CharacterId == characterId &&
                    x.Kind == CharacterAdvancementKind.Class).ToArrayAsync(ct);
            if (classRows.Length != state.PrimaryMechanics.Advancements.Count ||
                state.PrimaryMechanics.Advancements.Any(x =>
                    !classRows.Any(savedClass =>
                        savedClass.Id.ToString("D") == x.OccurrenceId &&
                        savedClass.RuleConceptKey == x.ClassId &&
                        savedClass.Level == x.Level)))
                return null;

            return new SyntheticAuthority(state, current, transition,
                receipts.Select(x => x.EventId).ToHashSet(StringComparer.Ordinal));
        }

        private static bool SameJson(string a, string b)
        {
            using var left = JsonDocument.Parse(a);
            using var right = JsonDocument.Parse(b);
            return JsonElement.DeepEquals(left.RootElement, right.RootElement);
        }
    }

    /// <summary>
    /// A strictly synthetic, test-owned provider. No production registration,
    /// catalog entitlement or DM source is inferred from the fixture receipts.
    /// </summary>
    private sealed class SyntheticAuthority(
        NativeCharacterState state,
        NativeManifestationLedger committed,
        NativeManifestationTransitionRequest submitted,
        IReadOnlySet<string> recordedEvents) : INativeCharacterManifestationAuthority
    {
        private bool Character(string id) => id == state.CharacterId;
        private static bool Source(NativeOriginSourceReference source) =>
            source == SyntheticSource();
        private static bool Award(NativeOriginSourceOccurrence occurrence) =>
            Source(occurrence.Source);
        private static bool Basis(NativeOriginDecisionBasis basis) =>
            basis == SyntheticRule();

        public bool AuthorizesPrimary(string id, NativeCharacterRequest primary) =>
            Character(id) && ReferenceEquals(primary, state.PrimaryMechanics);
        public bool AuthorizesSheet(string id, NativePersistentCharacterSheet sheet) =>
            Character(id) &&
            state.AlternateSheets.Contains(sheet) &&
            Source(sheet.DefinitionSource) &&
            sheet.AcquisitionEvidenceId == "receipt.acquire." + sheet.Id;
        public bool AuthorizesProgression(string id, string owner,
            NativeSheetEarnedProgression progression) =>
            Character(id) && Award(progression.SourceOccurrence) &&
            progression.Id.StartsWith("progress.", StringComparison.Ordinal) &&
            progression.AcquisitionEvidenceId == "receipt.progress." +
                progression.Id["progress.".Length..];
        public bool AuthorizesGrant(string id, string owner,
            NativeSheetEarnedProgression progression, NativeSheetEarnedGrant grant) =>
            AuthorizesProgression(id, owner, progression) &&
            Award(grant.SourceOccurrence) &&
            grant.AcquisitionEvidenceId == "receipt." + grant.Id;
        public bool AuthorizesApplicability(string id, string sheet,
            NativeSheetGrantApplicability decision) =>
            Character(id) && Basis(decision.Basis);
        public bool AuthorizesLedger(string id, NativeManifestationLedger ledger) =>
            Character(id) && ReferenceEquals(ledger, committed);
        public bool AuthorizesInstance(string id, NativeManifestationInstance instance,
            NativePersistentCharacterSheet sheet) =>
            Character(id) && instance.CharacterId == id &&
            instance.SheetId == sheet.Id && Award(instance.DefinitionOccurrence) &&
            instance.ActivationEvidenceId == "receipt.activate." + instance.ActivationEventId &&
            (recordedEvents.Contains(instance.ActivationEventId) ||
                instance.ActivationEventId == submitted.EventId) &&
            Basis(instance.ActivationBasis);
        public bool AuthorizesTransition(string id, NativeManifestationLedger current,
            NativeManifestationTransitionRequest request,
            NativeManifestationTransitionStep step) =>
            Character(id) && ReferenceEquals(current, committed) &&
            ReferenceEquals(request, submitted) &&
            request.ExpectedRevision == current.Revision &&
            Award(step.RuleOccurrence) && Basis(step.Basis) &&
            step.AuthorizationEvidenceId == "receipt.transition." + request.EventId;
        public bool AuthorizesUnlistedEnding(string id, NativeManifestationLedger current,
            NativeManifestationInstance instance,
            NativeManifestationTransitionStep step) => false;
        // The source-verified synthetic fixture permits only these exact
        // acquisition pools and scoped policies; arbitrary IDs fail closed.
        public bool AuthorizesResource(string id, NativeManifestationResourceState resource) =>
            Character(id) && (resource == ControlledWolfResource() ||
                AcquiredOwnerPools().Contains(resource));
        public bool AuthorizesResourcePolicy(string id, NativeManifestationInstance instance,
            NativeManifestationResourcePolicy policy) =>
            Character(id) && instance.Id == "manifest.wolf.1" &&
            (instance.ResourcePolicies.SequenceEqual([ControlledWolfResourcePolicy()]) ||
             instance.ResourcePolicies.SequenceEqual(FourOwnerPolicies())) &&
            instance.ResourcePolicies.Contains(policy);
        public bool AuthorizesInteraction(string id,
            NativeManifestationInteractionDecision decision) =>
            Character(id) && decision.Id == "interaction.synthetic.overlap" &&
            decision.Kind == NativeManifestationInteractionKind.Replace &&
            decision.InstanceIds.SequenceEqual(
                new[] { "manifest.wolf.1", "manifest.hybrid.1" },
                StringComparer.Ordinal) &&
            decision.SelectedInstanceId == "manifest.hybrid.1" &&
            decision.DefinitionOccurrence == Occurrence("interactions.replace") &&
            decision.Basis == SyntheticRule() &&
            decision.AuthorizationEvidenceId == "receipt.interaction.overlap" ||
            Character(id) && decision.Id == "interaction.synthetic.coexist" &&
            decision.Kind == NativeManifestationInteractionKind.Coexist &&
            decision.InstanceIds.SequenceEqual(
                new[] { "manifest.wolf.1", "manifest.hybrid.1" },
                StringComparer.Ordinal) &&
            decision.SelectedInstanceId is null &&
            decision.DefinitionOccurrence == Occurrence("interactions.coexist") &&
            decision.Basis == SyntheticRule() &&
            decision.AuthorizationEvidenceId == "receipt.interaction.coexist";
        public bool AuthorizesInfluence(string id, NativeCharacterPersistentInfluence influence) =>
            Character(id) && JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(influence, Wire),
                JsonSerializer.SerializeToElement(PassiveResourceInfluence(), Wire));
        public bool AuthorizesExpression(string id, NativeCharacterPersistentInfluence influence) =>
            Character(id) && JsonElement.DeepEquals(
                JsonSerializer.SerializeToElement(influence, Wire),
                JsonSerializer.SerializeToElement(PassiveResourceInfluence(), Wire));
        public bool AuthorizesEffect(string id, string influenceId,
            NativeCharacterInfluenceEffect effect) => false;
        public bool AuthorizesInfluenceProgression(string id, string influenceId,
            NativeSheetEarnedProgression progression) => false;
        public bool AuthorizesInfluenceGrant(string id, string influenceId,
            NativeSheetEarnedProgression progression, NativeSheetEarnedGrant grant) => false;
        public bool AuthorizesInfluenceApplicability(string id, string influenceId,
            NativeInfluenceGrantApplicability decision) => false;
    }


    /// <summary>
    /// Server-side, synthetic action policy. It does not accept source JSON
    /// from the caller and is not registered in Character Sheet production.
    /// The separate Core host authority provider checks the persisted record.
    /// </summary>
    private sealed class SyntheticTrustedSource(NativeCharacterState state)
        : ITrustedCharacterManifestationTransitionSource
    {
        public NativeCharacterState Current { get; set; } = state;
        public int Calls { get; private set; }

        public Task<TrustedManifestationTransitionMaterial?> PrepareAsync(
            Guid characterId, SiteCharacterProjection authorizedCharacter,
            CharacterSheet.Domain.Characters.CharacterSupernaturalState existing,
            string intentId, string eventId, CancellationToken ct)
        {
            Calls++;
            if (authorizedCharacter.CharacterId != characterId ||
                !authorizedCharacter.AllowsOrdinaryEditingByLifecycle ||
                Current.CharacterId != characterId.ToString("D") ||
                intentId != eventId)
                return Task.FromResult<TrustedManifestationTransitionMaterial?>(null);

            NativeManifestationTransitionStep[] steps = intentId switch
            {
                "enter:wolf" => [Enter(characterId, "wolf",
                    "manifest.wolf.1", eventId)],
                "enter:resource-wolf" => [EnterWithResource(characterId, eventId)],
                "enter:four-owner-wolf" => [EnterWithFourOwnerResources(characterId, eventId)],
                "revert:wolf" => [End("manifest.wolf.1", eventId)],
                "revert:resource-wolf" => [End("manifest.wolf.1", eventId)],
                "end:unlisted" => [UnlistedEnd("manifest.wolf.1", eventId)],
                "suppress:wolf" => [Contribution(
                    NativeManifestationTransitionAction.Suppress,
                    "manifest.wolf.1", eventId)],
                "restore:wolf" => [Contribution(
                    NativeManifestationTransitionAction.Restore,
                    "manifest.wolf.1", eventId)],
                "reenter:wolf" => [Enter(characterId, "wolf",
                    "manifest.wolf.2", eventId)],
                "overlap:unresolved" or "overlap:unapproved" or "overlap:authorized" =>
                [
                    Enter(characterId, "wolf", "manifest.wolf.1", eventId),
                    Enter(characterId, "hybrid", "manifest.hybrid.1", eventId)
                ],
                "coexist:authorized" =>
                [
                    Enter(characterId, "wolf", "manifest.wolf.1", eventId) with
                    {
                        NewInstance = TestInstance(characterId, "wolf",
                            "manifest.wolf.1", eventId) with
                        {
                            ContributionSlots = [new(
                                NativeInfluenceCharacteristic.AbilityScore, "strength")]
                        }
                    },
                    Enter(characterId, "hybrid", "manifest.hybrid.1", eventId) with
                    {
                        NewInstance = TestInstance(characterId, "hybrid",
                            "manifest.hybrid.1", eventId) with
                        {
                            ContributionSlots = [new(
                                NativeInfluenceCharacteristic.AbilityScore, "dexterity")]
                        }
                    }
                ],
                "revert:coexist" =>
                [
                    End("manifest.wolf.1", eventId),
                    End("manifest.hybrid.1", eventId)
                ],
                "switch:hybrid" =>
                [
                    End("manifest.wolf.2", eventId),
                    Enter(characterId, "hybrid", "manifest.hybrid.1", eventId)
                ],
                "switch:invalid" =>
                [
                    End("manifest.wolf.2", eventId),
                    // The first step is legitimate, but this target was never
                    // acquired. The entire proposed switch must be discarded.
                    Enter(characterId, "not-acquired", "manifest.invalid.1", eventId)
                ],
                _ => []
            };
            if (steps.Length == 0)
                return Task.FromResult<TrustedManifestationTransitionMaterial?>(null);
            var replacement = intentId switch
            {
                "overlap:authorized" =>
                    new[] { AuthorizedOverlap() },
                "coexist:authorized" => new[] { AuthorizedCoexist() },
                "overlap:unapproved" =>
                    new[] { UnapprovedOverlap() },
                _ => null
            };
            var transition = new NativeManifestationTransitionRequest(
                characterId.ToString("D"), existing.LedgerRevision, eventId, steps,
                replacement);
            return Task.FromResult<TrustedManifestationTransitionMaterial?>(
                new(JsonSerializer.SerializeToElement(Current.PrimaryMechanics, Wire),
                    JsonSerializer.SerializeToElement(transition, Wire),
                    "fixture:verified-source"));
        }
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("archive")]
    [InlineData("campaign")]
    public async Task SiteRevocationAtCommitBoundaryIsNotFencedByEarlierFreshReads(
        string revocation)
    {
        // This is a deliberate characterization of the unresolved F1 race.
        // An observed successful commit after revocation is NOT a passing
        // production-authorization guarantee or acceptance for scenarios 26/27.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var forms = TestOwnedForms();
        var state = TestCharacter(id, fingerprint, forms);
        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var pair in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(
                id, pair.Key, pair.Value, DateTimeOffset.UtcNow);
        var updatedBuild = await build.SetStartingClassAsync(
            id, "class.barbarian", DateTimeOffset.UtcNow);
        var classEntry = Assert.Single(updatedBuild!.AdvancementEntries,
            x => x.Kind == CharacterAdvancementKind.Class);
        state = state with
        {
            PrimaryMechanics = state.PrimaryMechanics with
            {
                Advancements =
                [
                    new NativeAdvancement("class.barbarian", 1,
                        classEntry.Id.ToString("D"))
                ]
            }
        };
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var before = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(forms, Wire), "[]",
                JsonSerializer.Serialize(
                    new NativeManifestationLedger(id.ToString("D"), 0, [], [], []), Wire),
                0, 0, 0, before, "fixture:acquire-werewolf",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);

        var owner = new OwnerAccess(id);
        var revokeCalls = 0;
        var fenced = new RevokeSiteAtCommitStore(store, () =>
        {
            revokeCalls++;
            switch (revocation)
            {
                case "ownership": owner.FreshAllow = false; break;
                case "archive":
                    owner.CurrentLifecycle = SiteCharacterLifecycleState.Archived;
                    break;
                case "campaign":
                    owner.OverrideFreshCampaignIds = [Guid.NewGuid()];
                    break;
                default: throw new InvalidOperationException("Unknown revocation");
            }
        });
        var service = TransitionRunner(
            db, owner, fenced, gateway, new SyntheticTrustedSource(state));
        var result = await service.ApplyAsync(
            id, 1, 0, "enter:wolf", "enter:wolf");

        Assert.Equal(1, revokeCalls);
        // Both independent Site checks happened before this revocation; no
        // Site-owned lease or revision is checked inside the PostgreSQL CAS.
        Assert.Equal(CharacterManifestationApplyStatus.Applied, result.Status);
        var committed = (await store.GetAsync(id))!;
        Assert.Equal(2, committed.Revision);
        Assert.Equal(1, committed.LedgerRevision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
        var newlyLive = await owner.GetFreshAuthorizedCharacterAsync(id);
        if (revocation == "ownership")
            Assert.Equal(SiteCharacterAccessStatus.NotFoundOrNotOwned, newlyLive.Status);
        else if (revocation == "archive")
            Assert.False(newlyLive.Character!.AllowsOrdinaryEditingByLifecycle);
        else
            Assert.NotEmpty(newlyLive.Character!.CampaignIds);

        // Do not mistake this expected race reproduction for a resolved
        // authorization property; it requires an approved cross-service fence.
    }

    [Fact]
    public async Task StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();

        var forms = TestOwnedForms();
        var state = TestCharacter(id, fingerprint, forms);
        var ledger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        string storedForms = JsonSerializer.Serialize(forms, Wire);

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var pair in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(id, pair.Key, pair.Value, DateTimeOffset.UtcNow);

        var updatedBuild = await build.SetStartingClassAsync(
            id, "class.barbarian", DateTimeOffset.UtcNow);
        var barbarian = Assert.Single(updatedBuild!.AdvancementEntries,
            x => x.Kind == CharacterAdvancementKind.Class);
        state = state with
        {
            PrimaryMechanics = state.PrimaryMechanics with
            {
                Advancements =
                [
                    new NativeAdvancement("class.barbarian", 1,
                        barbarian.Id.ToString("D"))
                ]
            }
        };

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var inputDigest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint, storedForms, "[]",
                JsonSerializer.Serialize(ledger, Wire), 0, 0, 0, inputDigest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var owner = new OwnerAccess(id);
        var service = TransitionRunner(db, owner, store, gateway, source);

        async Task<NativeManifestationEffectiveCharacter> Execute(string eventId)
        {
            var revision = (await store.GetAsync(id))!.Revision;
            var result = await service.ApplyAsync(id, revision, ledger.Revision,
                eventId, eventId);
            Assert.Equal(CharacterManifestationApplyStatus.Applied, result.Status);
            Assert.Equal(revision + 1, result.StateRevision);
            Assert.Equal(ledger.Revision + 1, result.LedgerRevision);
            Assert.NotNull(result.EffectivePreview);
            var effective = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
                result.EffectivePreview!.ToJsonString(), Wire)!;
            ledger = JsonSerializer.Deserialize<NativeManifestationLedger>(
                (await store.GetAsync(id))!.LedgerJson, Wire)!;
            return effective;
        }


        // Even a policy that the host normally trusts may not rewrite primary
        // ability scores. Character Sheet reconstructs them from its own rows.
        var forgedScores = state.PrimaryMechanics.AbilityScores
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        forgedScores["strength"] = 99;
        source.Current = state with
        {
            PrimaryMechanics = state.PrimaryMechanics with
            {
                AbilityScores = forgedScores
            }
        };
        var forged = await service.ApplyAsync(id, 1, 0,
            "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized, forged.Status);
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Single(await store.GetEventsAsync(id));
        source.Current = state;

        // A valid source-verified Core plan is not sufficient if the Site
        // revokes owner access, archives the character, or changes campaign
        // scope before PostgreSQL accepts the event.
        gateway.AfterVerifiedPlan = () => owner.CurrentLifecycle =
            SiteCharacterLifecycleState.Archived;
        Assert.Equal(CharacterManifestationApplyStatus.ArchivedReadOnly,
            (await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        owner.CurrentLifecycle = SiteCharacterLifecycleState.Active;

        gateway.AfterVerifiedPlan = () => owner.Allow = false;
        Assert.Equal(CharacterManifestationApplyStatus.NotFoundOrNotOwned,
            (await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        owner.Allow = true;

        gateway.AfterVerifiedPlan = () => owner.CampaignIds = [Guid.NewGuid()];
        Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized,
            (await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        owner.CampaignIds = [];

        // Cached Site introspection remains authorized. Only the separately
        // reloaded live Site access notices this owner revocation.
        gateway.AfterVerifiedPlan = () => owner.FreshAllow = false;
        Assert.Equal(CharacterManifestationApplyStatus.NotFoundOrNotOwned,
            (await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        owner.FreshAllow = true;

        // Same failure mode for a campaign association revoked at Site but
        // still present in the original introspected Tool Host snapshot.
        gateway.AfterVerifiedPlan = () =>
            owner.OverrideFreshCampaignIds = [Guid.NewGuid()];
        Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized,
            (await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        owner.OverrideFreshCampaignIds = null;
        gateway.AfterVerifiedPlan = null;

        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Equal(0, (await store.GetAsync(id))!.LedgerRevision);
        Assert.Single(await store.GetEventsAsync(id));

        var wolf = await Execute("enter:wolf");
        Assert.Equal("wolf", wolf.EffectiveSheetId);
        Assert.Equal(17, wolf.EffectiveMechanics!.AbilityScores["strength"]);
        Assert.Contains(wolf.SheetGrants, x => x.Entitlement.Id == "grant.shared");
        Assert.Contains(wolf.SheetGrants, x => x.Entitlement.Id == "grant.wolf");
        // The same persisted activation is now returned through the real
        // authenticated Core HTTP read endpoint, guarded by PostgreSQL
        // authority revalidation rather than the prior transition preview.
        var effectiveRead = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, effectiveRead.Status);
        Assert.Equal("partial", effectiveRead.View!.Status);
        Assert.Equal("wolf", effectiveRead.View.EffectiveSheetId);
        Assert.Equal("manifest.wolf.1", effectiveRead.View.ActiveManifestationId);
        Assert.NotNull(effectiveRead.View.NativeProjection);
        Assert.False(effectiveRead.View.EffectiveMechanicsReady);
        Assert.DoesNotContain("supernatural-acquisition-authority-unavailable",
            effectiveRead.View.Unresolved);
        Assert.Equal(1, gateway.VerifiedReadRequests);

        gateway.ForgePrimarySelection = true;
        var falsifiedPrimary = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal("unresolved", falsifiedPrimary.View!.Status);
        Assert.Null(falsifiedPrimary.View.NativeProjection);
        Assert.Contains("native-state-response-unverified",
            falsifiedPrimary.View.Unresolved);
        Assert.Null(falsifiedPrimary.View.ActiveManifestationId);
        gateway.ForgePrimarySelection = false;


        // Primary advancement belongs to the original character and must
        // project dynamically even while Wolf remains the active sheet.
        // No owned supernatural progression or activation row is rewritten.
        var wolfOwnershipBeforeAdvancement = (await store.GetAsync(id))!;
        await build.SetBaseAbilityScoreInputAsync(id, "constitution", 15,
            DateTimeOffset.UtcNow.AddMinutes(1));
        await build.SetAdvancementLevelAsync(id, barbarian.Id, 2,
            DateTimeOffset.UtcNow.AddMinutes(1));
        var latestScores = state.PrimaryMechanics.AbilityScores
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        latestScores["constitution"] = 15;
        state = state with
        {
            PrimaryMechanics = state.PrimaryMechanics with
            {
                AbilityScores = latestScores,
                Advancements =
                [
                    new NativeAdvancement("class.barbarian", 2,
                        barbarian.Id.ToString("D"))
                ]
            }
        };
        source.Current = state;
        var advancedWolfRead = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal("partial", advancedWolfRead.View!.Status);
        Assert.Equal("wolf", advancedWolfRead.View.EffectiveSheetId);
        Assert.Equal("manifest.wolf.1", advancedWolfRead.View.ActiveManifestationId);
        Assert.NotNull(advancedWolfRead.View.NativeProjection);
        Assert.Equal(15, advancedWolfRead.View.NativeProjection!["effectiveCharacter"]?
            ["effectiveMechanics"]?["abilityScores"]?["constitution"]?.GetValue<int>());
        Assert.Equal(2, advancedWolfRead.View.NativeProjection!["effectiveCharacter"]?
            ["effectiveMechanics"]?["advancements"]?[0]?["level"]?.GetValue<int>());
        var wolfOwnershipAfterAdvancement = (await store.GetAsync(id))!;
        Assert.Equal(wolfOwnershipBeforeAdvancement.Revision, wolfOwnershipAfterAdvancement.Revision);
        Assert.Equal(wolfOwnershipBeforeAdvancement.LedgerRevision,
            wolfOwnershipAfterAdvancement.LedgerRevision);
        Assert.Equal(wolfOwnershipBeforeAdvancement.AlternateSheetsJson,
            wolfOwnershipAfterAdvancement.AlternateSheetsJson);
        Assert.Equal(wolfOwnershipBeforeAdvancement.LedgerJson,
            wolfOwnershipAfterAdvancement.LedgerJson);
        Assert.Equal("enter:wolf",
            (await store.GetEventsAsync(id))[1].EventId);

        // The same current Class/Origin inputs govern reversion. No snapshot
        // from the activation event is restored over the owner's progression.
        var reverted = await Execute("revert:wolf");
        Assert.Equal(15, reverted.EffectiveMechanics!.AbilityScores["constitution"]);
        Assert.Equal(2, Assert.Single(reverted.EffectiveMechanics.Advancements).Level);
        Assert.Equal("@primary", reverted.EffectiveSheetId);
        Assert.Equal(10, reverted.EffectiveMechanics!.AbilityScores["strength"]);
        var normalRead = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal("partial", normalRead.View!.Status);
        Assert.Equal("@primary", normalRead.View.EffectiveSheetId);
        Assert.Null(normalRead.View.ActiveManifestationId);


        var reentered = await Execute("reenter:wolf");
        Assert.Equal("wolf", reentered.EffectiveSheetId);
        Assert.Contains(reentered.SheetGrants, x => x.Entitlement.Id == "grant.wolf");

        // A two-step End+Activate must not publish the first End when the
        // destination sheet fails Rules Core validation.
        var beforeFailedSwitch = (await store.GetAsync(id))!;
        var eventsBeforeFailedSwitch = (await store.GetEventsAsync(id)).Count;
        var rejectedSwitch = await service.ApplyAsync(id,
            beforeFailedSwitch.Revision, beforeFailedSwitch.LedgerRevision,
            "switch:invalid", "switch:invalid");
        Assert.Equal(CharacterManifestationApplyStatus.PlanUnresolved,
            rejectedSwitch.Status);
        var afterFailedSwitch = (await store.GetAsync(id))!;
        Assert.Equal(beforeFailedSwitch.Revision, afterFailedSwitch.Revision);
        Assert.Equal(beforeFailedSwitch.LedgerRevision, afterFailedSwitch.LedgerRevision);
        Assert.Equal(beforeFailedSwitch.LedgerJson, afterFailedSwitch.LedgerJson);
        Assert.Equal(eventsBeforeFailedSwitch, (await store.GetEventsAsync(id)).Count);

        var hybrid = await Execute("switch:hybrid");
        Assert.Equal("hybrid", hybrid.EffectiveSheetId);
        Assert.Equal(16, hybrid.EffectiveMechanics!.AbilityScores["dexterity"]);
        Assert.Equal(2, Assert.Single(hybrid.EffectiveMechanics.Advancements).Level);
        Assert.DoesNotContain(hybrid.SheetGrants, x => x.Entitlement.Id == "grant.wolf");
        Assert.Contains(hybrid.SheetGrants, x => x.Entitlement.Id == "grant.shared");

        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var durable = new PostgresCharacterSupernaturalStateStore(reloaded);
        var saved = (await durable.GetAsync(id))!;
        Assert.Equal(5, saved.Revision);
        Assert.Equal(4, saved.LedgerRevision);
        Assert.Contains("progress.wolf", saved.AlternateSheetsJson);
        var committedEvents = await durable.GetEventsAsync(id);
        Assert.Equal(5, committedEvents.Count);
        var switchReceipt = Assert.Single(committedEvents,
            x => x.EventId == "switch:hybrid");
        Assert.Equal("transition", switchReceipt.Kind);
        Assert.Equal(saved.Revision, switchReceipt.CommittedRevision);
        Assert.Equal(saved.LedgerRevision, switchReceipt.LedgerRevision);
        using (var orderedAudit = JsonDocument.Parse(switchReceipt.TransitionRequestJson!))
        {
            var request = orderedAudit.RootElement;
            Assert.Equal(id.ToString("D"),
                request.GetProperty("characterId").GetString());
            Assert.Equal("switch:hybrid",
                request.GetProperty("eventId").GetString());
            Assert.Equal(3, request.GetProperty("expectedRevision").GetInt64());
            var steps = request.GetProperty("steps").EnumerateArray().ToArray();
            Assert.Equal(2, steps.Length);
            Assert.Equal(1, steps[0].GetProperty("action").GetInt32());
            Assert.Equal("manifest.wolf.2",
                steps[0].GetProperty("targetInstanceId").GetString());
            Assert.Equal(0, steps[1].GetProperty("action").GetInt32());
            Assert.Equal("manifest.hybrid.1",
                steps[1].GetProperty("targetInstanceId").GetString());
            Assert.Equal("receipt.transition.switch:hybrid",
                steps[1].GetProperty("authorizationEvidenceId").GetString());
            Assert.True(steps[1].TryGetProperty("newInstance", out _));
        }
        using var journal = JsonDocument.Parse(saved.LedgerJson);
        Assert.Equal(3, journal.RootElement.GetProperty("instances").GetArrayLength());
        Assert.Equal(4, journal.RootElement.GetProperty("processedEventIds").GetArrayLength());
        // Fresh Context simulates service reinitialization. The host verifier
        // reads the current persisted ledger, not the original service's state.
        var restartedRead = await Reader(reloaded, id, gateway).GetAsync(id);
        Assert.Equal("partial", restartedRead.View!.Status);
        Assert.Equal("hybrid", restartedRead.View.EffectiveSheetId);
        Assert.Equal("manifest.hybrid.1", restartedRead.View.ActiveManifestationId);
        Assert.NotNull(restartedRead.View.NativeProjection);
        // Acceptance #29: after a fresh application/DbContext scope, the
        // independently verified Core read must reproduce the complete
        // committed effective character, not only its selected sheet ID.
        // This compares inherited mechanics and source-owned progression,
        // grants, effects, resources and provenance without reactivating.
        var reloadedEffective = restartedRead.View.NativeProjection!["effectiveCharacter"];
        Assert.NotNull(reloadedEffective);
        Assert.True(JsonNode.DeepEquals(
            JsonSerializer.SerializeToNode(hybrid, Wire), reloadedEffective),
            "Reloaded effective mechanics and provenance must match the committed plan.");

        // Five denied post-plan Site revocations (including live-only
        // revocation and campaign drift) invoked the verified source; none
        // wrote an event or consumed the current ledger revision.
        Assert.Equal(11, source.Calls);

        // A replay through the application boundary can not append an
        // already-committed event or silently reconstruct a prior snapshot.
        // Reconstruct the Character Sheet application service and PostgreSQL
        // store in a fresh scope. The prior activation is recovered from the
        // database, never from the former service instance's memory.
        var resumedService = TransitionRunner(reloaded,
            new OwnerAccess(id), durable, gateway, source);
        var replay = await resumedService.ApplyAsync(id, saved.Revision, saved.LedgerRevision,
            "enter:wolf", "enter:wolf");
        Assert.NotEqual(CharacterManifestationApplyStatus.Applied, replay.Status);
        Assert.Equal(5, (await durable.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task CanonicalBackgroundAndHitPointsBlockUnverifiedFormTransition()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(
                id, key, score, DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                JsonSerializer.Serialize(ledger, Wire), 0, 0, 0, digest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var service = TransitionRunner(db, new OwnerAccess(id), store, gateway, source);

        async Task AssertRefused()
        {
            var saved = (await store.GetAsync(id))!;
            var beforeReceipts = (await store.GetEventsAsync(id)).Count;
            var result = await service.ApplyAsync(id, saved.Revision,
                saved.LedgerRevision, "enter:wolf", "enter:wolf");
            Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized, result.Status);
            Assert.Equal(saved.LedgerJson, (await store.GetAsync(id))!.LedgerJson);
            Assert.Equal(saved.Revision, (await store.GetAsync(id))!.Revision);
            Assert.Equal(beforeReceipts, (await store.GetEventsAsync(id)).Count);
        }

        await build.SetFoundationalSelectionAsync(id,
            CharacterFoundationalSelectionCategory.Background,
            "background.synthetic", DateTimeOffset.UtcNow);
        await AssertRefused();

        // Removing an unverified Background does not authorize HP changes:
        // these are independently owned canonical Character Sheet values.
        await build.ClearFoundationalSelectionAsync(id,
            CharacterFoundationalSelectionCategory.Background, DateTimeOffset.UtcNow);
        var routine = new PostgresCharacterStateStore(db);
        await routine.SetCurrentHitPointsAsync(id, 7, DateTimeOffset.UtcNow);
        await AssertRefused();

        await routine.SetCurrentHitPointsAsync(id, null, DateTimeOffset.UtcNow);
        await routine.SetDeathSavesAsync(id, 1, 0, DateTimeOffset.UtcNow);
        await AssertRefused();

        await routine.SetDeathSavesAsync(id, 0, 0, DateTimeOffset.UtcNow);
        var inventoryOwner = await routine.AddInventoryItemOccurrenceAsync(
            id, "item.synthetic.unverified", DateTimeOffset.UtcNow);
        var item = Assert.Single(inventoryOwner!.InventoryItemOccurrences);
        Assert.False(item.IsEquipped);
        await AssertRefused();
        await routine.RemoveInventoryItemOccurrenceAsync(
            id, item.Id, DateTimeOffset.UtcNow);

        await routine.SetRulesInputAsync(id,
            CharacterRulesInputKind.Training, "skill.athletics",
            null, null, null, DateTimeOffset.UtcNow);
        await AssertRefused();
        await routine.RemoveRulesInputAsync(id,
            CharacterRulesInputKind.Training, "skill.athletics", DateTimeOffset.UtcNow);

        await routine.SetRulesInputAsync(id,
            CharacterRulesInputKind.Choice, "starting-equipment.choice",
            null, null, "custom-kit", DateTimeOffset.UtcNow);
        await AssertRefused();
        await routine.RemoveRulesInputAsync(id,
            CharacterRulesInputKind.Choice, "starting-equipment.choice", DateTimeOffset.UtcNow);

        await routine.SetRulesInputAsync(id,
            CharacterRulesInputKind.Resource, "resource.synthetic",
            3, null, null, DateTimeOffset.UtcNow);
        await AssertRefused();
        await routine.RemoveRulesInputAsync(id,
            CharacterRulesInputKind.Resource, "resource.synthetic", DateTimeOffset.UtcNow);

        var beforeCurrency = await store.GetAuthoritativeInputsFingerprintAsync(id);
        await routine.SetCurrencyBalanceAsync(id, "gold", 25, DateTimeOffset.UtcNow);
        Assert.NotEqual(beforeCurrency,
            await store.GetAuthoritativeInputsFingerprintAsync(id));
        await AssertRefused();
        await routine.RemoveCurrencyBalanceAsync(id, "gold", DateTimeOffset.UtcNow);

        // Once these individually owned unsupported facts are absent,
        // the existing test-only source-free primary subset may be planned.
        var allowed = await service.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, allowed.Status);
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
        Assert.Equal(2, (await store.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());
        var ledger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var score in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(id, score.Key, score.Value,
                DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                JsonSerializer.Serialize(ledger, Wire),
                0, 0, 0, digest, "fixture:acquire-werewolf",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);
        var source = new SyntheticTrustedSource(state);
        var service = TransitionRunner(db, new OwnerAccess(id), store, gateway, source);

        async Task<NativeManifestationEffectiveCharacter> Execute(string eventId)
        {
            var current = (await store.GetAsync(id))!;
            var applied = await service.ApplyAsync(id, current.Revision,
                current.LedgerRevision, eventId, eventId);
            Assert.Equal(CharacterManifestationApplyStatus.Applied, applied.Status);
            Assert.NotNull(applied.EffectivePreview);
            return JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
                applied.EffectivePreview!.ToJsonString(), Wire)!;
        }

        var wolf = await Execute("enter:resource-wolf");
        Assert.Equal("wolf", wolf.EffectiveSheetId);
        var pool = Assert.Single(wolf.Resources);
        Assert.Equal("resource.wolf.1", pool.Id);
        Assert.Equal(NativeManifestationResourceOwner.Manifestation, pool.OwnerKind);
        Assert.Equal("manifest.wolf.1", pool.OwnerId);
        Assert.Equal(NativeManifestationResourceMode.Separate, pool.Policy);
        Assert.Equal(3, pool.Current);
        Assert.Equal(5, pool.RecordedMaximum);
        Assert.Equal(5, pool.EffectiveMaximum);

        var beforeRead = (await store.GetAsync(id))!;
        var verified = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal("partial", verified.View!.Status);
        Assert.Equal("wolf", verified.View.EffectiveSheetId);
        Assert.Equal(3, verified.View.NativeProjection!["effectiveCharacter"]?
            ["resources"]?[0]?["current"]?.GetValue<int>());
        Assert.Equal(beforeRead.Revision, (await store.GetAsync(id))!.Revision);
        Assert.Equal(beforeRead.LedgerJson, (await store.GetAsync(id))!.LedgerJson);

        var reverted = await Execute("revert:resource-wolf");
        Assert.Equal("@primary", reverted.EffectiveSheetId);
        Assert.Empty(reverted.Resources);
        var next = await Execute("reenter:wolf");
        Assert.Equal("wolf", next.EffectiveSheetId);
        Assert.Empty(next.Resources);

        await using var fresh = new CharacterSheetDbContext(database.CreateOptions());
        var recovered = new PostgresCharacterSupernaturalStateStore(fresh);
        var saved = (await recovered.GetAsync(id))!;
        Assert.Equal(4, saved.Revision);
        Assert.Equal(3, saved.LedgerRevision);
        using var document = JsonDocument.Parse(saved.LedgerJson);
        var resource = Assert.Single(document.RootElement.GetProperty("resources")
            .EnumerateArray());
        Assert.Equal("manifest.wolf.1",
            resource.GetProperty("ownerId").GetString());
        Assert.Equal(3, resource.GetProperty("current").GetInt32());
        Assert.Equal(5, resource.GetProperty("maximum").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("instances")
            .GetArrayLength());
        var eventReceipts = await recovered.GetEventsAsync(id);
        Assert.Equal(4, eventReceipts.Count);
        var activated = Assert.Single(eventReceipts,
            entry => entry.EventId == "enter:resource-wolf");
        using var audit = JsonDocument.Parse(activated.TransitionRequestJson!);
        Assert.Equal("resource.wolf.1",
            audit.RootElement.GetProperty("steps")[0]
                .GetProperty("newResources")[0].GetProperty("id").GetString());

        var unchanged = (await recovered.GetAsync(id))!;
        var denied = await TransitionRunner(fresh, new OwnerAccess(id), recovered,
            gateway, source).ApplyAsync(id, unchanged.Revision,
            unchanged.LedgerRevision, "enter:resource-wolf", "enter:resource-wolf");
        Assert.NotEqual(CharacterManifestationApplyStatus.Applied, denied.Status);
        Assert.Equal(unchanged.Revision, (await recovered.GetAsync(id))!.Revision);
        Assert.Equal(4, (await recovered.GetEventsAsync(id)).Count);
    }


    [Fact]
    public async Task RealCharacterSheetProcessRestartPreservesVerifiedActiveFormAndAudit()
    {
        // This test starts the actual CharacterSheet.Web executable twice.
        // Site introspection and delegation are independently authenticated
        // disposable fixtures, not production bypass endpoints.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var verifier = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(verifier, verifier);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());
        string ledgerBeforeRestart = "";
        string sheetsBeforeRestart = "";
        string influencesBeforeRestart = "";
        (string EventId, long Revision, string EvidenceId, string? TransitionRequest)[]
            receiptsBeforeRestart = [];

        await using (var setup = new CharacterSheetDbContext(database.CreateOptions()))
        {
            await setup.Database.MigrateAsync();
            await new PostgresCharacterSheetStore(setup).GetOrCreateAsync(id);
            var build = new PostgresCharacterBuildStore(setup);
            foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
                await build.SetBaseAbilityScoreInputAsync(id, key, score, DateTimeOffset.UtcNow);
            var owned = new PostgresCharacterSupernaturalStateStore(setup);
            var digest = (await owned.GetAuthoritativeInputsFingerprintAsync(id))!;
            var initial = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
                (await owned.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                    id, "first-party-001", fingerprint,
                    JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                    JsonSerializer.Serialize(initial, Wire),
                    0, 0, 0, digest, "fixture:acquire-werewolf",
                    "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);
            var transition = await TransitionRunner(setup, new OwnerAccess(id),
                owned, gateway, new SyntheticTrustedSource(state)).ApplyAsync(
                    id, 1, 0, "enter:resource-wolf", "enter:resource-wolf");
            Assert.Equal(CharacterManifestationApplyStatus.Applied, transition.Status);
            var committed = (await owned.GetAsync(id))!;
            ledgerBeforeRestart = committed.LedgerJson;
            sheetsBeforeRestart = committed.AlternateSheetsJson;
            influencesBeforeRestart = committed.PersistentInfluencesJson;
            receiptsBeforeRestart = (await owned.GetEventsAsync(id))
                .Select(x => (x.EventId, x.CommittedRevision,
                    x.AuthorizationEvidenceId, x.TransitionRequestJson)).ToArray();
        }

        // Host-only Site fixture: a caller must redeem a concrete ticket,
        // and the Rules Core proxy separately requires Site delegation.
        const string sheetTicket = "test:character-sheet-restart";
        const string capability = "test:rules-core-delegation";
        var authentications = 0;
        var siteBuilder = WebApplication.CreateBuilder();
        siteBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var site = siteBuilder.Build();
        site.MapPost("/tool-host/character-sheet/api/introspect",
            (HttpContext context) =>
            {
                if (context.Request.Headers.Authorization.ToString() !=
                    "Bearer " + sheetTicket)
                    return Results.Unauthorized();
                System.Threading.Interlocked.Increment(ref authentications);
                context.Response.Headers["X-Dorks-Tool-Delegation-Capability"] =
                    capability;
                context.Response.Headers["X-Dorks-Tool-Delegation-Path"] =
                    "/tool-host/character-sheet/api/delegate/{targetSlug}/upstream";
                return Results.Json(new
                {
                    contractVersion = 1, toolSlug = "character-sheet",
                    siteMode = "dorks-and-dice",
                    user = new { id = Actor.ToString("D"), displayName = "Test owner" },
                    globalRoles = Array.Empty<string>(), campaigns = Array.Empty<object>(),
                    characters = new[]
                    {
                        new
                        {
                            id, name = "Restart test character", status = "Active",
                            archivedAt = (DateTimeOffset?)null,
                            campaignIds = Array.Empty<Guid>()
                        }
                    }
                });
            });
        // The real Character Sheet process now independently refreshes Site
        // ownership around protected reads. The restart fixture must model
        // that backend-only capability rather than returning a cached ticket
        // projection in its place.
        var freshOwnerQueries = 0;
        site.MapPost(
            "/tool-host/registrations/character-sheet/api/characters/{characterId:guid}/fresh-access",
            (HttpContext context, Guid characterId) =>
            {
                if (context.Request.Headers.Authorization.ToString() !=
                    "Bearer " + capability)
                    return Results.Unauthorized();
                if (characterId != id)
                    return Results.NotFound();
                Interlocked.Increment(ref freshOwnerQueries);
                return Results.Json(new
                {
                    contractVersion = 1,
                    sourceToolKey = "character-sheet",
                    actorUserId = Actor,
                    characterId = id,
                    characterName = "Restart test character",
                    status = "Active",
                    archivedAt = (DateTimeOffset?)null,
                    campaignIds = Array.Empty<Guid>(),
                    allowsOrdinaryEditing = true
                });
            });
        site.MapMethods(
            "/tool-host/character-sheet/api/delegate/rules-core/upstream/{**target}",
            ["GET", "POST"],
            async (HttpContext context, string target) =>
            {
                if (context.Request.Headers.Authorization.ToString() !=
                    "Bearer " + capability)
                {
                    context.Response.StatusCode = 401;
                    return;
                }
                using var upstream = new HttpRequestMessage(
                    new HttpMethod(context.Request.Method), "/" + target);
                upstream.Headers.Add("X-Dorks-Tool-Auth-Ticket", Ticket);
                upstream.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                    "/tool-host/registrations/rules-core/api/introspect");
                if (context.Request.Method == "POST")
                {
                    upstream.Content = new StreamContent(context.Request.Body);
                    upstream.Content.Headers.ContentType =
                        new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                }
                using var returned = await core.Client.SendAsync(
                    upstream, context.RequestAborted);
                context.Response.StatusCode = (int)returned.StatusCode;
                context.Response.ContentType = "application/json";
                await returned.Content.CopyToAsync(context.Response.Body);
            });
        await site.StartAsync();
        var siteUrl = site.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        // Run from the build output of the actual web project, which includes
        // its executable runtime configuration and dependencies.
        var webDll = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../src/CharacterSheet.Web/bin/Release/net10.0/CharacterSheet.Web.dll"));
        Assert.True(File.Exists(webDll), $"Missing real Character Sheet host: {webDll}");
        var freePort = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        freePort.Start();
        var port = ((IPEndPoint)freePort.LocalEndpoint).Port;
        freePort.Stop();
        var hostUrl = $"http://127.0.0.1:{port}";
        using var client = new HttpClient { BaseAddress = new Uri(hostUrl) };

        async Task<Process> StartHost()
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(webDll)!
            };
            start.ArgumentList.Add(webDll);
            start.Environment["ASPNETCORE_URLS"] = hostUrl;
            start.Environment["ConnectionStrings__CharacterSheet"] =
                database.ConnectionString;
            start.Environment["ToolHost__BaseUrl"] = siteUrl;
            var process = Process.Start(start)!;
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            for (var attempt = 0; attempt < 80; attempt++)
            {
                if (process.HasExited)
                    throw new InvalidOperationException(
                        $"Character Sheet host exited early: {process.ExitCode}");
                try
                {
                    using var ready = await client.GetAsync("/ready");
                    if (ready.IsSuccessStatusCode) return process;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(150);
            }
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Real Character Sheet host did not become ready.");
        }

        async Task<JsonNode> AuthorizedFormRead()
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"/api/characters/{id:D}/state/effective");
            request.Headers.Add("X-Dorks-Tool-Auth-Ticket", sheetTicket);
            request.Headers.Add("X-Dorks-Tool-Auth-Introspection-Path",
                "/tool-host/character-sheet/api/introspect");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        }

        var before = 0;
        JsonNode firstProjection;
        using (var first = await StartHost())
        {
            try
            {
                firstProjection = await AuthorizedFormRead();
                Assert.Equal("wolf",
                    firstProjection["effectiveSheetId"]?.GetValue<string>());
                Assert.Equal("manifest.wolf.1",
                    firstProjection["activeManifestationId"]?.GetValue<string>());
                before = authentications;
                Assert.True(before > 0);
            }
            finally
            {
                if (!first.HasExited) first.Kill(entireProcessTree: true);
                await first.WaitForExitAsync();
            }
        }

        using (var restarted = await StartHost())
        {
            try
            {
                var restored = await AuthorizedFormRead();
                Assert.True(authentications > before,
                    "The restarted process must redeem Site authorization again.");
                Assert.True(JsonNode.DeepEquals(firstProjection, restored));
            }
            finally
            {
                if (!restarted.HasExited) restarted.Kill(entireProcessTree: true);
                await restarted.WaitForExitAsync();
            }
        }

        Assert.True(freshOwnerQueries >= 4,
            "Both process reads must independently query fresh Site authority twice.");

        await using var inspected = new CharacterSheetDbContext(database.CreateOptions());
        var persisted = new PostgresCharacterSupernaturalStateStore(inspected);
        var saved = (await persisted.GetAsync(id))!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(1, saved.LedgerRevision);
        Assert.Equal(ledgerBeforeRestart, saved.LedgerJson);
        Assert.Equal(sheetsBeforeRestart, saved.AlternateSheetsJson);
        Assert.Equal(influencesBeforeRestart, saved.PersistentInfluencesJson);
        // Progression belongs to the acquired persistent sheet, and must not
        // be reconstructed from an activation snapshot after process restart.
        var retainedSheets = JsonSerializer.Deserialize<NativePersistentCharacterSheet[]>(
            saved.AlternateSheetsJson, Wire)!;
        Assert.Contains(retainedSheets, x => x.Id == "werewolf" &&
            x.Progression.Any(p => p.Id == "progress.shared"));
        Assert.Contains(retainedSheets, x => x.Id == "wolf" &&
            x.Progression.Any(p => p.Id == "progress.wolf"));
        var receipts = await persisted.GetEventsAsync(id);
        Assert.Equal(receiptsBeforeRestart,
            receipts.Select(x => (x.EventId, x.CommittedRevision,
                x.AuthorizationEvidenceId, x.TransitionRequestJson)).ToArray());
        Assert.Equal(2, receipts.Count);
        Assert.Equal("fixture:acquire-werewolf", receipts[0].EventId);
        Assert.Equal("enter:resource-wolf", receipts[1].EventId);
        using var ledger = JsonDocument.Parse(saved.LedgerJson);
        Assert.Equal(1, ledger.RootElement.GetProperty("processedEventIds")
            .GetArrayLength());
        Assert.Equal("enter:resource-wolf",
            ledger.RootElement.GetProperty("processedEventIds")[0].GetString());
        Assert.Equal("manifest.wolf.1",
            ledger.RootElement.GetProperty("instances")[0]
                .GetProperty("id").GetString());
        Assert.Equal("resource.wolf.1",
            ledger.RootElement.GetProperty("resources")[0]
                .GetProperty("id").GetString());
    }

    [Fact]
    public async Task FourResourceOwnersRemainDistinctThroughTransitionReadAndReload()
    {
        // Source evidence is deliberately test-only; the real Core HTTP host
        // verifies all four owners against the persisted PostgreSQL snapshot.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var verifier = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(verifier, verifier);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms()) with
        {
            PersistentInfluences = [PassiveResourceInfluence()]
        };
        var emptyLedger = new NativeManifestationLedger(
            id.ToString("D"), 0, [], AcquiredOwnerPools(), []);

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(id, key, score, DateTimeOffset.UtcNow);
        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire),
                JsonSerializer.Serialize(state.PersistentInfluences, Wire),
                JsonSerializer.Serialize(emptyLedger, Wire),
                0, 0, 0, digest, "fixture:acquire-werewolf",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var transition = await TransitionRunner(db, new OwnerAccess(id),
            store, gateway, source).ApplyAsync(id, 1, 0,
                "enter:four-owner-wolf", "enter:four-owner-wolf");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, transition.Status);
        var preview = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
            transition.EffectivePreview!.ToJsonString(), Wire)!;
        Assert.Equal("wolf", preview.EffectiveSheetId);
        Assert.Equal(4, preview.Resources.Count);
        var expected = new[]
        {
            ("resource.primary.1", NativeManifestationResourceOwner.Primary, "@primary", 4, 7),
            ("resource.sheet.1", NativeManifestationResourceOwner.Sheet, "werewolf", 2, 5),
            ("resource.influence.1", NativeManifestationResourceOwner.Influence,
                "influence.synthetic.pool", 1, 3),
            ("resource.wolf.1", NativeManifestationResourceOwner.Manifestation,
                "manifest.wolf.1", 3, 5)
        };
        foreach (var (poolId, ownerKind, ownerId, current, maximum) in expected)
        {
            var pool = Assert.Single(preview.Resources, x => x.Id == poolId);
            Assert.Equal(ownerKind, pool.OwnerKind);
            Assert.Equal(ownerId, pool.OwnerId);
            Assert.Equal(current, pool.Current);
            Assert.Equal(maximum, pool.RecordedMaximum);
            Assert.Equal(maximum, pool.EffectiveMaximum);
            Assert.Equal(poolId == "resource.wolf.1"
                ? NativeManifestationResourceMode.Separate
                : NativeManifestationResourceMode.Inherit, pool.Policy);
        }

        // Fresh persistence context, independently verified real Core read;
        // compare full resource identity, mechanics and source provenance.
        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(reloaded);
        var saved = (await fresh.GetAsync(id))!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(1, saved.LedgerRevision);
        var read = await Reader(reloaded, id, gateway).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
        Assert.Equal("partial", read.View!.Status);
        Assert.Equal("wolf", read.View.EffectiveSheetId);
        Assert.True(JsonNode.DeepEquals(transition.EffectivePreview,
            read.View.NativeProjection!["effectiveCharacter"]));
        using (var journal = JsonDocument.Parse(saved.LedgerJson))
        {
            var persisted = journal.RootElement.GetProperty("resources")
                .EnumerateArray().ToArray();
            Assert.Equal(4, persisted.Length);
            foreach (var (poolId, ownerKind, ownerId, current, maximum) in expected)
            {
                var pool = Assert.Single(persisted,
                    x => x.GetProperty("id").GetString() == poolId);
                Assert.Equal((int)ownerKind, pool.GetProperty("ownerKind").GetInt32());
                Assert.Equal(ownerId, pool.GetProperty("ownerId").GetString());
                Assert.Equal(current, pool.GetProperty("current").GetInt32());
                Assert.Equal(maximum, pool.GetProperty("maximum").GetInt32());
            }
        }

        // An authenticated delegate cannot substitute altered ledger
        // resource claims for the PostgreSQL-owned snapshot. Exercise each
        // provenance dimension separately through real Core HTTP.
        var persistedLedger = JsonSerializer.Deserialize<NativeManifestationLedger>(
            saved.LedgerJson, Wire)!;
        JsonElement ReadBody(NativeManifestationLedger ledger) =>
            JsonSerializer.SerializeToElement(new { state, ledger }, Wire);
        var accepted = await gateway.EvaluateVerifiedManifestationStateAsync(
            ReadBody(persistedLedger), CancellationToken.None);
        Assert.Equal("partial", accepted["status"]?.GetValue<string>());

        var resourceToAttack = Assert.Single(persistedLedger.Resources,
            resource => resource.Id == "resource.primary.1");
        var forgedResources = new[]
        {
            resourceToAttack with { DefinitionId = "resource.forged" },
            resourceToAttack with { SourceOccurrence = Occurrence("resource.forged") },
            resourceToAttack with { EvidenceId = "receipt.forged" },
            resourceToAttack with { OwnerKind = NativeManifestationResourceOwner.Sheet },
            resourceToAttack with { OwnerId = "wolf" }
        };
        foreach (var forgedResource in forgedResources)
        {
            var forgedLedger = persistedLedger with
            {
                Resources = persistedLedger.Resources.Select(resource =>
                    resource.Id == resourceToAttack.Id ? forgedResource : resource)
                    .ToArray()
            };
            var denial = await Assert.ThrowsAsync<RulesCoreGatewayException>(() =>
                gateway.EvaluateVerifiedManifestationStateAsync(
                    ReadBody(forgedLedger), CancellationToken.None));
            Assert.Equal(422, denial.StatusCode);
            Assert.Equal("supernatural_authority_unverified", denial.ErrorCode);
        }
        Assert.Equal(saved.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);

        // Corrupt the *persisted* owner ledger, not only a caller's HTTP
        // representation. The test-only verifier must independently reject
        // mutated resource provenance without deriving authority from the
        // rewritten row, then recover after restoring exact saved JSON.
        foreach (var (field, replacement) in new (string, JsonNode)[]
        {
            ("definitionId", JsonValue.Create("resource.forged")!),
            ("evidenceId", JsonValue.Create("receipt.forged")!),
            ("sourceOccurrence", JsonSerializer.SerializeToNode(
                Occurrence("resource.forged"), Wire)!),
            ("ownerKind", JsonValue.Create((int)NativeManifestationResourceOwner.Sheet)!),
            ("ownerId", JsonValue.Create("wolf")!)
        })
        {
            var damaged = JsonNode.Parse(saved.LedgerJson)!;
            var resource = damaged["resources"]!.AsArray().Single(x =>
                x!["id"]!.GetValue<string>() == "resource.primary.1")!;
            resource[field] = replacement.DeepClone();
            try
            {
                await reloaded.SupernaturalStates
                    .Where(x => x.CharacterId == id)
                    .ExecuteUpdateAsync(change => change.SetProperty(
                        x => x.LedgerJson, damaged.ToJsonString()));
                await using var adversarialReadContext =
                    new CharacterSheetDbContext(database.CreateOptions());
                var refused = await Reader(adversarialReadContext,
                    id, new RealGateway(core.Client)).GetAsync(id);
                Assert.Equal(CharacterEffectiveStateStatus.Ready, refused.Status);
                Assert.NotEqual("wolf", refused.View!.EffectiveSheetId);
                Assert.Contains("native-state-response-unverified",
                    refused.View.Unresolved);
            }
            finally
            {
                await reloaded.SupernaturalStates
                    .Where(x => x.CharacterId == id)
                    .ExecuteUpdateAsync(change => change.SetProperty(
                        x => x.LedgerJson, saved.LedgerJson));
            }
        }
        Assert.Equal(saved.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);

        // A matching ledger row is not enough if the independently recorded
        // acquisition or transition receipts have been rewritten in storage.
        var committedReceiptAudit = (await fresh.GetEventsAsync(id))
            .Select(x => (x.EventId, x.CommittedRevision, x.AuthorizationEvidenceId,
                x.TransitionRequestJson)).ToArray();
        try
        {
            await reloaded.SupernaturalEventReceipts
                .Where(x => x.CharacterId == id &&
                    x.EventId == "fixture:acquire-werewolf")
                .ExecuteUpdateAsync(change => change.SetProperty(
                    x => x.AuthorizationEvidenceId, "receipt.forged"));
            await using var alteredEvidenceContext =
                new CharacterSheetDbContext(database.CreateOptions());
            var alteredEvidence = await Reader(alteredEvidenceContext, id,
                new RealGateway(core.Client)).GetAsync(id);
            Assert.Equal(CharacterEffectiveStateStatus.Ready, alteredEvidence.Status);
            Assert.NotEqual("wolf", alteredEvidence.View!.EffectiveSheetId);
            Assert.Contains("verified-manifestation-read-unavailable",
                alteredEvidence.View.Unresolved);
        }
        finally
        {
            await reloaded.SupernaturalEventReceipts
                .Where(x => x.CharacterId == id &&
                    x.EventId == "fixture:acquire-werewolf")
                .ExecuteUpdateAsync(change => change.SetProperty(
                    x => x.AuthorizationEvidenceId, "fixture:verified-source"));
        }

        try
        {
            await reloaded.SupernaturalEventReceipts
                .Where(x => x.CharacterId == id &&
                    x.EventId == "enter:four-owner-wolf")
                .ExecuteUpdateAsync(change => change.SetProperty(
                    x => x.EventId, "receipt.forged.transition"));
            await using var alteredTransitionContext =
                new CharacterSheetDbContext(database.CreateOptions());
            var alteredTransition = await Reader(alteredTransitionContext, id,
                new RealGateway(core.Client)).GetAsync(id);
            Assert.Equal(CharacterEffectiveStateStatus.Ready, alteredTransition.Status);
            Assert.NotEqual("wolf", alteredTransition.View!.EffectiveSheetId);
        }
        finally
        {
            await reloaded.SupernaturalEventReceipts
                .Where(x => x.CharacterId == id &&
                    x.EventId == "receipt.forged.transition")
                .ExecuteUpdateAsync(change => change.SetProperty(
                    x => x.EventId, "enter:four-owner-wolf"));
        }
        Assert.Equal(committedReceiptAudit,
            (await fresh.GetEventsAsync(id))
                .Select(x => (x.EventId, x.CommittedRevision,
                    x.AuthorizationEvidenceId, x.TransitionRequestJson)).ToArray());

        // Concurrent verified reads traverse the actual Rules Core HTTP host
        // through independent PostgreSQL contexts. Their projections are not
        // authority to change ownership, balances, revisions, or receipts.
        var receiptsBeforeReads = (await fresh.GetEventsAsync(id))
            .Select(x => (x.EventId, x.CommittedRevision, x.AuthorizationEvidenceId,
                x.TransitionRequestJson)).ToArray();
        async Task<CharacterEffectiveStateResult> IndependentRead()
        {
            await using var independentDb = new CharacterSheetDbContext(
                database.CreateOptions());
            return await Reader(independentDb, id, new RealGateway(core.Client))
                .GetAsync(id);
        }
        var concurrent = await Task.WhenAll(
            IndependentRead(), IndependentRead(), IndependentRead());
        foreach (var result in concurrent)
        {
            Assert.Equal(CharacterEffectiveStateStatus.Ready, result.Status);
            Assert.Equal("wolf", result.View!.EffectiveSheetId);
            Assert.True(JsonNode.DeepEquals(transition.EffectivePreview,
                result.View.NativeProjection!["effectiveCharacter"]));
        }
        var afterReads = (await fresh.GetAsync(id))!;
        Assert.Equal(saved.Revision, afterReads.Revision);
        Assert.Equal(saved.LedgerRevision, afterReads.LedgerRevision);
        Assert.Equal(saved.LedgerJson, afterReads.LedgerJson);
        Assert.Equal(receiptsBeforeReads,
            (await fresh.GetEventsAsync(id))
                .Select(x => (x.EventId, x.CommittedRevision, x.AuthorizationEvidenceId,
                    x.TransitionRequestJson)).ToArray());

        // Ending the instance hides its private resource, not its durable
        // pool, and must not grant Sheet/Influence resources to primary.
        var reverted = await TransitionRunner(reloaded, new OwnerAccess(id),
            fresh, gateway, source).ApplyAsync(id, saved.Revision,
                saved.LedgerRevision, "revert:resource-wolf", "revert:resource-wolf");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, reverted.Status);
        var primary = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
            reverted.EffectivePreview!.ToJsonString(), Wire)!;
        Assert.Equal("@primary", primary.EffectiveSheetId);
        Assert.Equal("resource.primary.1", Assert.Single(primary.Resources).Id);
        var after = (await fresh.GetAsync(id))!;
        using var finalLedger = JsonDocument.Parse(after.LedgerJson);
        Assert.Equal(4, finalLedger.RootElement.GetProperty("resources").GetArrayLength());
        using var priorLedger = JsonDocument.Parse(saved.LedgerJson);
        // Compare the complete persisted record, including provenance and
        // source occurrence; visible resource counts are insufficient.
        Assert.True(JsonElement.DeepEquals(
            priorLedger.RootElement.GetProperty("resources"),
            finalLedger.RootElement.GetProperty("resources")));
        Assert.Equal(3, (await fresh.GetEventsAsync(id)).Count);
    }

    [Fact]
    public async Task SuppressionAndRestorationPreserveResourceOwnershipWithoutNewActivation()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(
                id, key, score, DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var emptyLedger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                JsonSerializer.Serialize(emptyLedger, Wire),
                0, 0, 0, digest, "fixture:acquire-werewolf",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var service = TransitionRunner(db, new OwnerAccess(id), store, gateway, source);

        async Task<(NativeManifestationEffectiveCharacter View,
            CharacterSheet.Domain.Characters.CharacterSupernaturalState Snapshot)>
            Execute(string eventId, bool shouldApply = true)
        {
            var before = (await store.GetAsync(id))!;
            var response = await service.ApplyAsync(id, before.Revision,
                before.LedgerRevision, eventId, eventId);
            if (!shouldApply)
            {
                Assert.NotEqual(CharacterManifestationApplyStatus.Applied, response.Status);
                var unchanged = (await store.GetAsync(id))!;
                Assert.Equal(before.Revision, unchanged.Revision);
                Assert.Equal(before.LedgerJson, unchanged.LedgerJson);
                return (null!, unchanged);
            }

            Assert.Equal(CharacterManifestationApplyStatus.Applied, response.Status);
            Assert.NotNull(response.EffectivePreview);
            var current = (await store.GetAsync(id))!;
            return (JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
                response.EffectivePreview!.ToJsonString(), Wire)!, current);
        }

        var activated = await Execute("enter:resource-wolf");
        Assert.Equal("wolf", activated.View.EffectiveSheetId);
        Assert.Single(activated.View.Resources);
        Assert.Equal(3, activated.View.Resources[0].Current);
        Assert.Equal(1, activated.Snapshot.LedgerRevision);

        // Suppress changes contribution but does not end or recreate the
        // activation. Resource balances and owner identity remain durable.
        var suppressed = await Execute("suppress:wolf");
        Assert.Equal("@primary", suppressed.View.EffectiveSheetId);
        Assert.Empty(suppressed.View.Resources);
        using (var journal = JsonDocument.Parse(suppressed.Snapshot.LedgerJson))
        {
            var instance = Assert.Single(journal.RootElement
                .GetProperty("instances").EnumerateArray());
            Assert.Equal(0, instance.GetProperty("lifecycle").GetInt32());
            Assert.Equal(1, instance.GetProperty("contribution").GetInt32());
            Assert.Equal("suppress:wolf",
                instance.GetProperty("lastEventId").GetString());
            var pool = Assert.Single(journal.RootElement
                .GetProperty("resources").EnumerateArray());
            Assert.Equal("manifest.wolf.1",
                pool.GetProperty("ownerId").GetString());
            Assert.Equal(3, pool.GetProperty("current").GetInt32());
        }

        await Execute("suppress:wolf", shouldApply: false);
        var restored = await Execute("restore:wolf");
        Assert.Equal("wolf", restored.View.EffectiveSheetId);
        Assert.Equal("manifest.wolf.1", restored.View.ManifestationInstanceId);
        Assert.Equal("manifest.wolf.1", Assert.Single(restored.View.Resources).OwnerId);
        Assert.Equal(3, Assert.Single(restored.View.Resources).Current);
        Assert.Equal(5, Assert.Single(restored.View.Resources).EffectiveMaximum);
        await Execute("restore:wolf", shouldApply: false);

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(reload);
        var committed = (await fresh.GetAsync(id))!;
        Assert.Equal(4, committed.Revision);
        Assert.Equal(3, committed.LedgerRevision);
        var events = await fresh.GetEventsAsync(id);
        Assert.Equal(new[]
        {
            "fixture:acquire-werewolf", "enter:resource-wolf",
            "suppress:wolf", "restore:wolf"
        }, events.Select(x => x.EventId));
        foreach (var item in events.Skip(1))
        {
            using var audit = JsonDocument.Parse(item.TransitionRequestJson!);
            Assert.Equal(item.EventId,
                audit.RootElement.GetProperty("eventId").GetString());
        }

        var verified = await Reader(reload, id, gateway).GetAsync(id);
        Assert.Equal("partial", verified.View!.Status);
        Assert.Equal("wolf", verified.View.EffectiveSheetId);
        Assert.Equal("manifest.wolf.1", verified.View.ActiveManifestationId);
        Assert.Equal(3, verified.View.NativeProjection!["effectiveCharacter"]?
            ["resources"]?[0]?["current"]?.GetValue<int>());

        // The recorded ending condition, not a Suppress/Restore event,
        // permanently ends this activation. Its pool stays stored, not
        // transferable to a later activation.
        var endedService = TransitionRunner(reload,
            new OwnerAccess(id), fresh, gateway, source);
        var beforeEnding = (await fresh.GetAsync(id))!;
        var refusedEnd = await endedService.ApplyAsync(id, beforeEnding.Revision,
            beforeEnding.LedgerRevision, "end:unlisted", "end:unlisted");
        Assert.Equal(CharacterManifestationApplyStatus.PlanUnresolved,
            refusedEnd.Status);
        Assert.Equal(beforeEnding.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);
        Assert.Equal(4, (await fresh.GetEventsAsync(id)).Count);

        var ending = await endedService.ApplyAsync(id, beforeEnding.Revision,
            beforeEnding.LedgerRevision, "revert:resource-wolf",
            "revert:resource-wolf");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, ending.Status);
        var ended = (await fresh.GetAsync(id))!;
        Assert.Equal(5, ended.Revision);
        Assert.Equal(4, ended.LedgerRevision);

        var forbidden = await endedService.ApplyAsync(id, ended.Revision,
            ended.LedgerRevision, "restore:wolf", "restore:wolf");
        Assert.NotEqual(CharacterManifestationApplyStatus.Applied,
            forbidden.Status);
        Assert.Equal(5, (await fresh.GetEventsAsync(id)).Count);
        Assert.Equal(ended.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);
    }

    [Fact]
    public async Task OverlappingManifestationsRequireIndependentInteractionAuthorityAndAtomicSelection()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(
                id, key, score, DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var empty = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                JsonSerializer.Serialize(empty, Wire), 0, 0, 0, digest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var service = TransitionRunner(db, new OwnerAccess(id), store, gateway, source);
        var prior = (await store.GetAsync(id))!;
        var priorEvents = await store.GetEventsAsync(id);

        // Core must not infer which of two concurrently contributing forms
        // wins merely from creation order or from their own species rules.
        var ambiguous = await service.ApplyAsync(id, prior.Revision,
            prior.LedgerRevision, "overlap:unresolved", "overlap:unresolved");
        Assert.Equal(CharacterManifestationApplyStatus.PlanUnresolved,
            ambiguous.Status);
        var deniedRuling = await service.ApplyAsync(id, prior.Revision,
            prior.LedgerRevision, "overlap:unapproved", "overlap:unapproved");
        Assert.Equal(CharacterManifestationApplyStatus.PlanUnresolved,
            deniedRuling.Status);
        var unchanged = (await store.GetAsync(id))!;
        Assert.Equal(prior.Revision, unchanged.Revision);
        Assert.Equal(prior.LedgerJson, unchanged.LedgerJson);
        Assert.Equal(priorEvents.Count, (await store.GetEventsAsync(id)).Count);

        // An exact test-only host source authorizes the replacement
        // interaction, selecting Hybrid while preserving both distinct
        // acquired manifestations and their individual activation receipts.
        var allowed = await service.ApplyAsync(id, prior.Revision,
            prior.LedgerRevision, "overlap:authorized", "overlap:authorized");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, allowed.Status);
        Assert.NotNull(allowed.EffectivePreview);
        var selected = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
            allowed.EffectivePreview!.ToJsonString(), Wire)!;
        Assert.Equal("hybrid", selected.EffectiveSheetId);
        Assert.Equal("manifest.hybrid.1", selected.ManifestationInstanceId);
        Assert.Equal(16, selected.EffectiveMechanics!.AbilityScores["dexterity"]);

        await using var reload = new CharacterSheetDbContext(database.CreateOptions());
        var fresh = new PostgresCharacterSupernaturalStateStore(reload);
        var saved = (await fresh.GetAsync(id))!;
        Assert.Equal(2, saved.Revision);
        Assert.Equal(1, saved.LedgerRevision);
        using (var journal = JsonDocument.Parse(saved.LedgerJson))
        {
            var instances = journal.RootElement.GetProperty("instances")
                .EnumerateArray().ToArray();
            Assert.Equal(2, instances.Length);
            Assert.All(instances, entry =>
            {
                Assert.Equal(0, entry.GetProperty("lifecycle").GetInt32());
                Assert.Equal(0, entry.GetProperty("contribution").GetInt32());
            });
            var choice = Assert.Single(journal.RootElement.GetProperty("interactions")
                .EnumerateArray());
            Assert.Equal("manifest.hybrid.1",
                choice.GetProperty("selectedInstanceId").GetString());
            Assert.Equal("receipt.interaction.overlap",
                choice.GetProperty("authorizationEvidenceId").GetString());
        }
        var receipts = await fresh.GetEventsAsync(id);
        Assert.Equal(2, receipts.Count);
        using (var audit = JsonDocument.Parse(receipts[1].TransitionRequestJson!))
        {
            var request = audit.RootElement;
            Assert.Equal("overlap:authorized",
                request.GetProperty("eventId").GetString());
            Assert.Equal(2, request.GetProperty("steps").GetArrayLength());
            Assert.Equal("manifest.wolf.1",
                request.GetProperty("steps")[0]
                    .GetProperty("targetInstanceId").GetString());
            Assert.Equal("manifest.hybrid.1",
                request.GetProperty("steps")[1]
                    .GetProperty("targetInstanceId").GetString());
            var interaction = Assert.Single(request.GetProperty("replacementInteractions")
                .EnumerateArray());
            Assert.Equal("receipt.interaction.overlap",
                interaction.GetProperty("authorizationEvidenceId").GetString());
        }
        var read = await Reader(reload, id, gateway).GetAsync(id);
        Assert.Equal(CharacterEffectiveStateStatus.Ready, read.Status);
        Assert.Equal("partial", read.View!.Status);
        Assert.Equal("hybrid", read.View.EffectiveSheetId);
        Assert.Equal("manifest.hybrid.1", read.View.ActiveManifestationId);

        // Both form instances are active and contributing. A modified
        // transport response selecting Wolf contradicts the single recorded
        // Replace decision that selects Hybrid and must be withheld.
        gateway.ForgeWolfSelection = true;
        var falsifiedSelection = await Reader(reload, id, gateway).GetAsync(id);
        Assert.Equal("unresolved", falsifiedSelection.View!.Status);
        Assert.Null(falsifiedSelection.View.NativeProjection);
        Assert.Contains("native-state-response-unverified",
            falsifiedSelection.View.Unresolved);
        gateway.ForgeWolfSelection = false;

        var replay = await TransitionRunner(reload, new OwnerAccess(id),
            fresh, gateway, source).ApplyAsync(id, saved.Revision,
                saved.LedgerRevision, "overlap:authorized", "overlap:authorized");
        Assert.NotEqual(CharacterManifestationApplyStatus.Applied, replay.Status);
        Assert.Equal(2, (await fresh.GetEventsAsync(id)).Count);
        Assert.Equal(saved.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);
    }


    [Fact]
    public async Task CoexistingPartialFormsPersistAndProjectBothSlotsThroughRealCoreHttp()
    {
        // Rules Core owns transformation composition. Character Sheet stores
        // only the verified transition and independently matches the returned
        // composite contributor identities to its persisted ledger.
        await using var database = await PostgresTestDatabase.CreateAsync();
        var characterId = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(
            database.CreateOptions(), characterId);
        await using var core = await CoreHost.StartAsync(provider, provider);
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(characterId, fingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(characterId);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var (key, score) in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(
                characterId, key, score, DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(characterId))!;
        var empty = new NativeManifestationLedger(
            characterId.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                characterId, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire), "[]",
                JsonSerializer.Serialize(empty, Wire), 0, 0, 0, digest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        var source = new SyntheticTrustedSource(state);
        var transition = TransitionRunner(
            db, new OwnerAccess(characterId), store, gateway, source);
        var applied = await transition.ApplyAsync(
            characterId, 1, 0, "coexist:authorized", "coexist:authorized");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, applied.Status);
        Assert.Equal(2, applied.StateRevision);
        Assert.Equal(1, applied.LedgerRevision);

        var effective = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
            applied.EffectivePreview!.ToJsonString(), Wire)!;
        Assert.Equal("partial", effective.Status);
        Assert.Equal("@composite", effective.EffectiveSheetId);
        Assert.Equal(17, effective.EffectiveMechanics!.AbilityScores["strength"]);
        Assert.Equal(16, effective.EffectiveMechanics.AbilityScores["dexterity"]);
        Assert.Equal(new[] { "manifest.hybrid.1", "manifest.wolf.1" },
            effective.ContributingManifestationInstanceIds);

        await using (var reload = new CharacterSheetDbContext(database.CreateOptions()))
        {
            var saved = (await new PostgresCharacterSupernaturalStateStore(reload)
                .GetAsync(characterId))!;
            Assert.Equal(1, saved.LedgerRevision);
            using var json = JsonDocument.Parse(saved.LedgerJson);
            Assert.Equal(2, json.RootElement.GetProperty("instances").GetArrayLength());
            Assert.All(json.RootElement.GetProperty("instances").EnumerateArray(),
                item => Assert.Single(item.GetProperty("contributionSlots").EnumerateArray()));
            Assert.Single(json.RootElement.GetProperty("interactions").EnumerateArray());
            var reader = await Reader(reload, characterId, gateway).GetAsync(characterId);
            Assert.Equal(CharacterEffectiveStateStatus.Ready, reader.Status);
            Assert.Equal("partial", reader.View!.Status);
            Assert.Equal("@composite", reader.View.EffectiveSheetId);
            Assert.Null(reader.View.ActiveManifestationId);
            Assert.Equal(new[] { "manifest.hybrid.1", "manifest.wolf.1" },
                reader.View.ActiveManifestationIds);
            Assert.NotNull(reader.View.NativeProjection);
            Assert.False(reader.View.EffectiveMechanicsReady);
        }

        // Ending both source-owned manifestations is one atomic event.
        var reverted = await transition.ApplyAsync(
            characterId, 2, 1, "revert:coexist", "revert:coexist");
        Assert.Equal(CharacterManifestationApplyStatus.Applied, reverted.Status);
        Assert.Equal(3, reverted.StateRevision);
        Assert.Equal(2, reverted.LedgerRevision);
        var restored = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
            reverted.EffectivePreview!.ToJsonString(), Wire)!;
        Assert.Equal("@primary", restored.EffectiveSheetId);
        Assert.Null(restored.ManifestationInstanceId);
        Assert.Equal(10, restored.EffectiveMechanics!.AbilityScores["strength"]);
        Assert.Equal(12, restored.EffectiveMechanics.AbilityScores["dexterity"]);

        var events = await store.GetEventsAsync(characterId);
        Assert.Equal(3, events.Count);
        Assert.Equal("coexist:authorized", events[1].EventId);
        Assert.Equal("revert:coexist", events[2].EventId);
    }

    [Fact]
    public async Task AbsentHostAuthorityStaleRevisionAndArchivedActorNeverCommitTransition()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        // No production supernatural authority provider is installed.
        await using var core = await CoreHost.StartAsync();
        var gateway = new RealGateway(core.Client);
        var fingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var state = TestCharacter(id, fingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var pair in state.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(id, pair.Key, pair.Value, DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint,
                JsonSerializer.Serialize(state.AlternateSheets, Wire),
                "[]", JsonSerializer.Serialize(ledger, Wire), 0, 0, 0, digest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        var actions = new SyntheticTrustedSource(state);
        var active = TransitionRunner(db,
            new OwnerAccess(id), store, gateway, actions);

        var missingAuthority = await active.ApplyAsync(id, 1, 0,
            "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized,
            missingAuthority.Status);
        // Even saved and structurally correct supernatural state is not
        // playable through the read endpoint without an independent provider.
        var unverifiedRead = await Reader(db, id, gateway).GetAsync(id);
        Assert.Equal("unresolved", unverifiedRead.View!.Status);
        Assert.Equal("@primary", unverifiedRead.View.EffectiveSheetId);
        Assert.Null(unverifiedRead.View.NativeProjection);
        Assert.Contains("supernatural-acquisition-authority-unavailable",
            unverifiedRead.View.Unresolved);
        Assert.Equal(1, gateway.VerifiedReadRequests);

        var unsupported = await active.ApplyAsync(id, 1, 0,
            "invented:unapproved-form", "invented:unapproved-form");
        Assert.Equal(CharacterManifestationApplyStatus.SourceUnauthorized,
            unsupported.Status);
        Assert.Equal(2, actions.Calls);

        var staleState = await active.ApplyAsync(id, 2, 0,
            "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.StateRevisionConflict,
            staleState.Status);
        var staleLedger = await active.ApplyAsync(id, 1, 1,
            "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.LedgerRevisionConflict,
            staleLedger.Status);
        Assert.Equal(2, actions.Calls);

        var archived = TransitionRunner(db,
            new OwnerAccess(id, lifecycle: SiteCharacterLifecycleState.Archived),
            store, gateway, actions);
        Assert.Equal(CharacterManifestationApplyStatus.ArchivedReadOnly,
            (await archived.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        var notOwned = TransitionRunner(db,
            new OwnerAccess(id, authorize: false), store, gateway, actions);
        Assert.Equal(CharacterManifestationApplyStatus.NotFoundOrNotOwned,
            (await notOwned.ApplyAsync(id, 1, 0, "enter:wolf", "enter:wolf")).Status);
        Assert.Equal(2, actions.Calls);
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Equal(0, (await store.GetAsync(id))!.LedgerRevision);
        Assert.Single(await store.GetEventsAsync(id));
    }

    [Fact]
    public async Task NativeCatalogChangeRejectsPreviouslyAcquiredStateBeforeAnyTransitionCommit()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        await using var core = await CoreHost.StartAsync();
        var gateway = new RealGateway(core.Client);
        var activeFingerprint = (await gateway.GetNativeRulesetAsync(CancellationToken.None))
            ["fingerprint"]!.GetValue<string>();
        var oldFingerprint = new string('b', 64);
        Assert.NotEqual(activeFingerprint, oldFingerprint);
        var oldState = TestCharacter(id, oldFingerprint, TestOwnedForms());

        await using var db = new CharacterSheetDbContext(database.CreateOptions());
        await db.Database.MigrateAsync();
        await new PostgresCharacterSheetStore(db).GetOrCreateAsync(id);
        var build = new PostgresCharacterBuildStore(db);
        foreach (var pair in oldState.PrimaryMechanics.AbilityScores)
            await build.SetBaseAbilityScoreInputAsync(id, pair.Key, pair.Value,
                DateTimeOffset.UtcNow);

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var digest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        var ledger = new NativeManifestationLedger(id.ToString("D"), 0, [], [], []);
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", oldFingerprint,
                JsonSerializer.Serialize(oldState.AlternateSheets, Wire),
                "[]", JsonSerializer.Serialize(ledger, Wire),
                0, 0, 0, digest, "fixture:acquire-werewolf",
                "fixture:verified-source", false, DateTimeOffset.UtcNow))).Status);

        // Core compares pinned catalog identity before consulting a source
        // authority. It must not plan against the newer definitions.
        var actions = new SyntheticTrustedSource(oldState);
        var service = TransitionRunner(db,
            new OwnerAccess(id), store, gateway, actions);
        var refused = await service.ApplyAsync(id, 1, 0,
            "enter:wolf", "enter:wolf");
        Assert.Equal(CharacterManifestationApplyStatus.NativeContentChanged, refused.Status);
        Assert.Equal(1, actions.Calls);
        var persisted = (await store.GetAsync(id))!;
        Assert.Equal(1, persisted.Revision);
        Assert.Equal(0, persisted.LedgerRevision);
        Assert.Single(await store.GetEventsAsync(id));
        using var savedLedger = JsonDocument.Parse(persisted.LedgerJson);
        Assert.Empty(savedLedger.RootElement.GetProperty("processedEventIds").EnumerateArray());
    }
}
#endif
