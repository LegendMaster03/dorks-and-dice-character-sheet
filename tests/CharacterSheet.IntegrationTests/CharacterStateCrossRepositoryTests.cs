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
using RulesCore.Mechanics;
using CoreAuthorityProvider = CoreHost::RulesCore.Host.INativeManifestationHostAuthorityProvider;
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
            CoreAuthorityProvider? verifiedSourceProvider = null)
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

        public async Task<JsonNode> PlanVerifiedManifestationAsync(
            JsonElement request, CancellationToken ct)
        {
            using var response = await SendManifestationPlanAsync(request, ct);
            return await Parse(response, ct);
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

        // Even the exact delegated character-sheet ticket must not make an
        // unverified SourceOccurrence and claimed receipt an executable plan.
        var deniedPlanRequest = JsonSerializer.SerializeToElement(new
        {
            state = fakeRequest.GetProperty("state"),
            ledger = fakeRequest.GetProperty("ledger"),
            transition = new
            {
                characterId = id.ToString("D"), expectedRevision = 0,
                eventId = "forged:activate", steps = Array.Empty<object>()
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

    private sealed class PostgresSyntheticAuthorityProvider(
        DbContextOptions<CharacterSheetDbContext> databaseOptions,
        Guid characterId) : CoreAuthorityProvider
    {
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
        public bool AuthorizesResource(string id, NativeManifestationResourceState resource) => false;
        public bool AuthorizesResourcePolicy(string id, NativeManifestationInstance instance,
            NativeManifestationResourcePolicy policy) => false;
        public bool AuthorizesInteraction(string id, NativeManifestationInteractionDecision decision) => false;
        public bool AuthorizesInfluence(string id, NativeCharacterPersistentInfluence influence) => false;
        public bool AuthorizesExpression(string id, NativeCharacterPersistentInfluence influence) => false;
        public bool AuthorizesEffect(string id, string influenceId,
            NativeCharacterInfluenceEffect effect) => false;
        public bool AuthorizesInfluenceProgression(string id, string influenceId,
            NativeSheetEarnedProgression progression) => false;
        public bool AuthorizesInfluenceGrant(string id, string influenceId,
            NativeSheetEarnedProgression progression, NativeSheetEarnedGrant grant) => false;
        public bool AuthorizesInfluenceApplicability(string id, string influenceId,
            NativeInfluenceGrantApplicability decision) => false;
    }

    [Fact]
    public async Task StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var id = Guid.NewGuid();
        var provider = new PostgresSyntheticAuthorityProvider(database.CreateOptions(), id);
        await using var core = await CoreHost.StartAsync(provider);
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

        var store = new PostgresCharacterSupernaturalStateStore(db);
        var inputDigest = (await store.GetAuthoritativeInputsFingerprintAsync(id))!;
        Assert.Equal(CharacterSupernaturalWriteStatus.Applied,
            (await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint, storedForms, "[]",
                JsonSerializer.Serialize(ledger, Wire), 0, 0, 0, inputDigest,
                "fixture:acquire-werewolf", "fixture:verified-source",
                false, DateTimeOffset.UtcNow))).Status);

        async Task<NativeManifestationEffectiveCharacter> Execute(
            string eventId, params NativeManifestationTransitionStep[] steps)
        {
            var request = new NativeManifestationTransitionRequest(
                id.ToString("D"), ledger.Revision, eventId, steps);
            var payload = JsonSerializer.SerializeToElement(new
            {
                state, ledger, transition = request
            }, Wire);
            var answer = await gateway.PlanVerifiedManifestationAsync(
                payload, CancellationToken.None);
            Assert.Equal("planned", answer["status"]!.GetValue<string>());
            Assert.False(answer["committed"]!.GetValue<bool>());
            Assert.False(answer["effectiveMechanicsReady"]!.GetValue<bool>());
            var plan = answer["plan"] ?? throw new InvalidOperationException(
                "Core omitted planned state.");
            Assert.Equal("planned", plan["status"]!.GetValue<string>());
            var proposed = JsonSerializer.Deserialize<NativeManifestationLedger>(
                plan["proposed"]!.ToJsonString(), Wire)!;
            var effective = JsonSerializer.Deserialize<NativeManifestationEffectiveCharacter>(
                plan["proposedEffective"]!.ToJsonString(), Wire)!;
            Assert.Equal(ledger.Revision + 1, proposed.Revision);
            var revision = (await store.GetAsync(id))!.Revision;
            var committed = await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                id, "first-party-001", fingerprint, storedForms, "[]",
                JsonSerializer.Serialize(proposed, Wire), revision,
                ledger.Revision, proposed.Revision, inputDigest,
                eventId, "fixture:verified-source", true, DateTimeOffset.UtcNow));
            Assert.Equal(CharacterSupernaturalWriteStatus.Applied, committed.Status);
            ledger = proposed;
            return effective;
        }

        var wolf = await Execute("enter:wolf",
            Enter(id, "wolf", "manifest.wolf.1", "enter:wolf"));
        Assert.Equal("wolf", wolf.EffectiveSheetId);
        Assert.Equal(17, wolf.EffectiveMechanics!.AbilityScores["strength"]);
        Assert.Contains(wolf.SheetGrants, x => x.Entitlement.Id == "grant.shared");
        Assert.Contains(wolf.SheetGrants, x => x.Entitlement.Id == "grant.wolf");

        var reverted = await Execute("revert:wolf",
            End("manifest.wolf.1", "revert:wolf"));
        Assert.Equal("@primary", reverted.EffectiveSheetId);
        Assert.Equal(10, reverted.EffectiveMechanics!.AbilityScores["strength"]);

        var reentered = await Execute("reenter:wolf",
            Enter(id, "wolf", "manifest.wolf.2", "reenter:wolf"));
        Assert.Equal("wolf", reentered.EffectiveSheetId);
        Assert.Contains(reentered.SheetGrants, x => x.Entitlement.Id == "grant.wolf");

        var hybrid = await Execute("switch:hybrid",
            End("manifest.wolf.2", "switch:hybrid"),
            Enter(id, "hybrid", "manifest.hybrid.1", "switch:hybrid"));
        Assert.Equal("hybrid", hybrid.EffectiveSheetId);
        Assert.Equal(16, hybrid.EffectiveMechanics!.AbilityScores["dexterity"]);
        Assert.DoesNotContain(hybrid.SheetGrants, x => x.Entitlement.Id == "grant.wolf");
        Assert.Contains(hybrid.SheetGrants, x => x.Entitlement.Id == "grant.shared");

        await using var reloaded = new CharacterSheetDbContext(database.CreateOptions());
        var durable = new PostgresCharacterSupernaturalStateStore(reloaded);
        var saved = (await durable.GetAsync(id))!;
        Assert.Equal(5, saved.Revision);
        Assert.Equal(4, saved.LedgerRevision);
        Assert.Contains("progress.wolf", saved.AlternateSheetsJson);
        Assert.Equal(5, (await durable.GetEventsAsync(id)).Count);
        using var journal = JsonDocument.Parse(saved.LedgerJson);
        Assert.Equal(3, journal.RootElement.GetProperty("instances").GetArrayLength());
        Assert.Equal(4, journal.RootElement.GetProperty("processedEventIds").GetArrayLength());
    }
}
#endif
