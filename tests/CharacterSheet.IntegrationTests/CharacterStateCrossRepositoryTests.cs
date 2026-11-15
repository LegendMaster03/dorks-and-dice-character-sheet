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
using CharacterSheet.Domain.Characters;
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
using CoreReadAuthorityProvider = CoreHost::RulesCore.Host.INativeManifestationHostReadAuthorityProvider;
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
            CoreReadAuthorityProvider? verifiedReadProvider = null)
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

    private sealed class OwnerAccess(Guid ownedId, bool authorize = true,
        SiteCharacterLifecycleState lifecycle = SiteCharacterLifecycleState.Active)
        : ISiteCharacterAccessGateway
    {
        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid characterId, CancellationToken ct = default) =>
            Task.FromResult(authorize && characterId == ownedId
                ? SiteCharacterAccessResult.Authorized(new SiteCharacterProjection(
                    ownedId, "Cross-repository fixture",
                    lifecycle, lifecycle == SiteCharacterLifecycleState.Archived
                        ? DateTimeOffset.UtcNow : null, []))
                : new SiteCharacterAccessResult(SiteCharacterAccessStatus.NotFoundOrNotOwned));
    }

    private sealed class RealGateway(HttpClient http) : INativeRulesCoreGateway
    {
        public int ProjectionRequests { get; private set; }
        public int VerifiedReadRequests { get; private set; }
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
            return await Parse(response, ct);
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
            new PostgresCharacterSupernaturalStateStore(db), gateway);
    }


    private static CharacterManifestationTransitionService TransitionRunner(
        CharacterSheetDbContext db, ISiteCharacterAccessGateway site,
        ICharacterSupernaturalStateStore store, RealGateway gateway,
        ITrustedCharacterManifestationTransitionSource source)
    {
        var buildStore = new PostgresCharacterBuildStore(db);
        return new CharacterManifestationTransitionService(site, store, gateway, source,
            new CharacterBuildService(site, buildStore, TimeProvider.System),
            new CharacterStateService(site, new PostgresCharacterStateStore(db),
                TimeProvider.System),
            new CharacterOriginStateService(site, buildStore,
                new PostgresCharacterAuthorizedOriginStore(db)));
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

    private static NativeManifestationInteractionDecision AuthorizedOverlap() =>
        new("interaction.synthetic.overlap",
            NativeManifestationInteractionKind.Replace,
            ["manifest.wolf.1", "manifest.hybrid.1"], "manifest.hybrid.1",
            Occurrence("interactions.replace"), SyntheticRule(),
            "receipt.interaction.overlap");

    private static NativeManifestationInteractionDecision UnapprovedOverlap() =>
        AuthorizedOverlap() with { AuthorizationEvidenceId = "receipt.interaction.forged" };



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
        // One exact test-only acquired pool and its separately proven policy.
        // Other IDs, quantities, owners and effects remain unauthorized.
        public bool AuthorizesResource(string id, NativeManifestationResourceState resource) =>
            Character(id) && resource == ControlledWolfResource();
        public bool AuthorizesResourcePolicy(string id, NativeManifestationInstance instance,
            NativeManifestationResourcePolicy policy) =>
            Character(id) && instance.Id == "manifest.wolf.1" &&
            policy == ControlledWolfResourcePolicy();
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
            decision.AuthorizationEvidenceId == "receipt.interaction.overlap";
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
        var service = TransitionRunner(db,
            new OwnerAccess(id), store, gateway, source);

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

        Assert.Equal(6, source.Calls);

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

        var replay = await TransitionRunner(reload, new OwnerAccess(id),
            fresh, gateway, source).ApplyAsync(id, saved.Revision,
                saved.LedgerRevision, "overlap:authorized", "overlap:authorized");
        Assert.NotEqual(CharacterManifestationApplyStatus.Applied, replay.Status);
        Assert.Equal(2, (await fresh.GetEventsAsync(id)).Count);
        Assert.Equal(saved.LedgerJson, (await fresh.GetAsync(id))!.LedgerJson);
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
