using System.Text.Json;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.UnitTests;

/// <summary>
/// The evaluator stub represents an independently verified native transition;
/// it is deliberately not a production implementation of Rules Core mechanics.
/// These tests cover the application ownership, actor, stale state and
/// evidence-to-CAS gates independently of the separate PostgreSQL tests.
/// </summary>
public sealed class CharacterInnateSpellLedgerServiceTests
{
    private const string Fingerprint = "catalog-1";
    private const string Profile = """{"id":"human","underlying":{"speciesId":"species.human"}}""";
    private static readonly Guid Character = Guid.Parse("c134e79d-c8e8-48d2-846d-d6c4a372e29a");
    private static readonly Guid Actor = Guid.Parse("9234b6e2-dc82-4a74-9b89-b92ffecedb22");
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private const string CourseHash =
        "7E2F9A1E2BF14579B23C95EE88A7A4E3E1E9FD63F3AA2DA37F96F41351BDB82A";

    private static string Ledger(long revision, int remaining = 1) =>
        JsonSerializer.Serialize(new
        {
            RulesetId = "first-party-001",
            CatalogFingerprint = Fingerprint,
            OriginProfileId = "human",
            ProposalId = "proposal-human",
            CharacterLevel = 3,
            ProjectionFingerprint = CourseHash,
            Revision = revision,
            RemainingFreeCasts = new Dictionary<string, int>
            {
                ["grant:spell"] = remaining
            }
        });

    private sealed class SiteStub(SiteCharacterAccessStatus status =
        SiteCharacterAccessStatus.Authorized, bool archived = false,
        bool otherId = false) : ISiteCharacterAccessGateway
    {
        public int Calls { get; private set; }

        public Task<SiteCharacterAccessResult> GetAuthorizedCharacterAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(status == SiteCharacterAccessStatus.Authorized
                ? SiteCharacterAccessResult.Authorized(new(
                    otherId ? Guid.NewGuid() : id, "Test",
                    archived ? SiteCharacterLifecycleState.Archived :
                        SiteCharacterLifecycleState.Active,
                    archived ? Now : null, []))
                : new SiteCharacterAccessResult(status));
        }
    }

    private sealed class ActorStub(Guid? actor) : ICharacterInnateSpellActorSource
    {
        public Guid? GetActorUserId() => actor;
    }

    private sealed class OriginStub : ICharacterAuthorizedOriginStore
    {
        public int Reads { get; private set; }
        public CharacterAuthorizedOriginState? Saved { get; set; } =
            new(Character, "first-party-001", Fingerprint, Profile, 1, Now);

        public Task<CharacterAuthorizedOriginState?> GetAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Saved);
        }

        public Task<IReadOnlyList<CharacterOriginEventReceipt>> GetEventsAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CharacterOriginEventReceipt>>([]);

        public Task<AuthorizedOriginStoreResult> CommitVerifiedAsync(
            VerifiedOriginPersistenceCommand input,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Origin mutation is prohibited.");
    }

    private sealed class LedgerStub : ICharacterInnateSpellLedgerStore
    {
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public CharacterInnateSpellLedgerState? Saved { get; set; }
        public VerifiedInnateSpellLedgerPersistenceCommand? LastCommand { get; private set; }
        public InnateSpellLedgerStoreStatus CommitStatus { get; set; } =
            InnateSpellLedgerStoreStatus.Applied;

        public Task<CharacterInnateSpellLedgerState?> GetAsync(Guid id,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(Saved);
        }

        public Task<IReadOnlyList<CharacterInnateSpellLedgerReceipt>> GetEventsAsync(
            Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CharacterInnateSpellLedgerReceipt>>([]);

        public Task<InnateSpellLedgerStoreResult> CommitVerifiedAsync(
            VerifiedInnateSpellLedgerPersistenceCommand input,
            CancellationToken cancellationToken = default)
        {
            Writes++;
            LastCommand = input;
            var revision = input.ExpectedLedgerRevision is null
                ? 0 : input.ExpectedLedgerRevision.Value + 1;
            if (CommitStatus == InnateSpellLedgerStoreStatus.Applied)
                Saved = new(input.CharacterId, input.RulesetId,
                    input.Fingerprint, input.OriginProfileId,
                    input.ProposalId, input.ExpectedOriginRevision,
                    revision, input.LedgerJson, input.RecordedAt);
            return Task.FromResult(new InnateSpellLedgerStoreResult(
                CommitStatus, CommitStatus == InnateSpellLedgerStoreStatus.Applied
                    ? revision : null));
        }
    }

    private sealed class NativeStub : ITrustedInnateSpellLedgerEvaluator
    {
        public int Calls { get; private set; }
        public TrustedInnateSpellLedgerEvaluation? LastInput { get; private set; }
        public TrustedInnateSpellLedgerCandidate? Output { get; set; } =
            new(Ledger(0), "human", "proposal-human",
                "native-evaluated:original");

        public Task<TrustedInnateSpellLedgerCandidate?> CalculateAsync(
            TrustedInnateSpellLedgerEvaluation input,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastInput = input;
            return Task.FromResult(Output);
        }
    }

    private static CharacterInnateSpellLedgerService Service(
        ISiteCharacterAccessGateway site, ICharacterInnateSpellActorSource actor,
        OriginStub origins, LedgerStub ledgers, NativeStub native) =>
        new(site, actor, origins, ledgers, native, TimeProvider.System);

    private static CharacterInnateSpellLedgerState SavedLedger(
        long revision = 0, long originRevision = 1) =>
        new(Character, "first-party-001", Fingerprint, "human",
            "proposal-human", originRevision, revision, Ledger(revision), Now);

    [Theory]
    [InlineData(SiteCharacterAccessStatus.Unauthenticated,
        CharacterInnateSpellLedgerStatus.Unauthenticated)]
    [InlineData(SiteCharacterAccessStatus.NotFoundOrNotOwned,
        CharacterInnateSpellLedgerStatus.NotFoundOrNotOwned)]
    [InlineData(SiteCharacterAccessStatus.ProjectionUnavailable,
        CharacterInnateSpellLedgerStatus.ProjectionUnavailable)]
    public async Task DeniedSiteOwnershipShortCircuitsAllSensitiveReadsAndNativeCalls(
        SiteCharacterAccessStatus siteStatus, CharacterInnateSpellLedgerStatus expected)
    {
        var site = new SiteStub(siteStatus);
        var origins = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub();
        var service = Service(site, new ActorStub(Actor), origins, ledgers, native);
        Assert.Equal(expected, (await service.GetAsync(Character)).Status);
        Assert.Equal(expected, (await service.ApplyAsync(
            Character, CharacterInnateSpellLedgerAction.Initialize,
            null, null)).Status);
        Assert.Equal(0, origins.Reads);
        Assert.Equal(0, ledgers.Reads);
        Assert.Equal(0, ledgers.Writes);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task WrongSiteProjectionOrArchivedCharacterDoesNotPermitMutation()
    {
        var origins = new OriginStub();
        var ledgers = new LedgerStub { Saved = SavedLedger() };
        var native = new NativeStub();
        var wrong = Service(new SiteStub(otherId: true),
            new ActorStub(Actor), origins, ledgers, native);
        Assert.Equal(CharacterInnateSpellLedgerStatus.ProjectionUnavailable,
            (await wrong.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.SpendFreeCast,
                "grant:spell", 0)).Status);

        var archived = Service(new SiteStub(archived: true),
            new ActorStub(Actor), origins, ledgers, native);
        Assert.Equal(CharacterInnateSpellLedgerStatus.ArchivedReadOnly,
            (await archived.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.SpendFreeCast,
                "grant:spell", 0)).Status);
        var read = await archived.GetAsync(Character);
        Assert.Equal(CharacterInnateSpellLedgerStatus.Ready, read.Status);
        Assert.NotNull(read.Ledger);
        Assert.Equal(0, native.Calls);
        Assert.Equal(0, ledgers.Writes);
    }

    [Fact]
    public async Task ActorMustComeFromRedeemedSiteSessionBeforeNativeEvaluation()
    {
        var origins = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub();
        foreach (Guid? actor in new Guid?[] { null, Guid.Empty })
        {
            var service = Service(new SiteStub(), new ActorStub(actor),
                origins, ledgers, native);
            Assert.Equal(CharacterInnateSpellLedgerStatus.Unauthenticated,
                (await service.ApplyAsync(Character,
                    CharacterInnateSpellLedgerAction.Initialize,
                    null, null)).Status);
        }
        Assert.Equal(0, origins.Reads);
        Assert.Equal(0, native.Calls);
        Assert.Equal(0, ledgers.Writes);
    }

    [Fact]
    public async Task VerifiedBootstrapUsesServerHeldOriginActorAndNativeOutputOnly()
    {
        var origins = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub();
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origins, ledgers, native);
        var result = await service.ApplyAsync(Character,
            CharacterInnateSpellLedgerAction.Initialize, null, null);
        Assert.Equal(CharacterInnateSpellLedgerStatus.Applied, result.Status);
        Assert.Equal(0, result.Revision);
        Assert.Equal(1, ledgers.Writes);
        Assert.Equal(1, native.Calls);
        Assert.Equal(Character, native.LastInput!.CharacterId);
        Assert.Null(native.LastInput.CurrentLedger);
        Assert.Equal(Actor, native.LastInput.SiteActorId);
        Assert.Same(origins.Saved, native.LastInput.CommittedOrigin);

        var cmd = ledgers.LastCommand!;
        Assert.Equal(Character, cmd.CharacterId);
        Assert.Equal(Actor, cmd.ActorUserId);
        Assert.Equal(1, cmd.ExpectedOriginRevision);
        Assert.Null(cmd.ExpectedLedgerRevision);
        Assert.Equal("native-evaluated:original", cmd.AuthorizationEvidenceId);
        Assert.Equal("Initialize", cmd.Operation);
        Assert.StartsWith("native-innate:", cmd.EventId, StringComparison.Ordinal);
        Assert.Equal("human", cmd.OriginProfileId);
        Assert.Equal("proposal-human", cmd.ProposalId);
        Assert.Equal(Ledger(0), cmd.LedgerJson);
        Assert.Null(cmd.AppliedDecisionIdsJson);
        Assert.NotNull(ledgers.Saved);
    }

    [Fact]
    public async Task MissingNativeVerifierNeverPublishesBalance()
    {
        var origin = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub { Output = null };
        var service = Service(new SiteStub(), new ActorStub(Actor), origin, ledgers, native);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.Initialize, null, null)).Status);
        Assert.Equal(0, ledgers.Writes);
        Assert.Null(ledgers.Saved);
    }

    [Fact]
    public async Task StaleOrChangedCommittedOriginBlocksMutationBeforeNative()
    {
        var origins = new OriginStub();
        var ledgers = new LedgerStub { Saved = SavedLedger(originRevision: 3) };
        var native = new NativeStub();
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origins, ledgers, native);

        Assert.Equal(CharacterInnateSpellLedgerStatus.OriginChanged,
            (await service.GetAsync(Character)).Status);
        Assert.Equal(CharacterInnateSpellLedgerStatus.OriginChanged,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.LongRest, null, 0)).Status);
        Assert.Equal(0, native.Calls);
        ledgers.Saved = SavedLedger();
        Assert.Equal(CharacterInnateSpellLedgerStatus.RevisionConflict,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.LongRest, null, 12)).Status);
        Assert.Equal(0, native.Calls);
        Assert.Equal(CharacterInnateSpellLedgerStatus.LedgerAlreadyInitialized,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.Initialize, null, null)).Status);
        Assert.Equal(0, native.Calls);
        origins.Saved = null;
        Assert.Equal(CharacterInnateSpellLedgerStatus.OriginNotCommitted,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.LongRest, null, 0)).Status);
        Assert.Equal(0, native.Calls);
    }

    [Theory]
    [InlineData(CharacterInnateSpellLedgerAction.Initialize, "grant:x", null)]
    [InlineData(CharacterInnateSpellLedgerAction.Initialize, null, 0)]
    [InlineData(CharacterInnateSpellLedgerAction.SpendFreeCast, null, 0)]
    [InlineData(CharacterInnateSpellLedgerAction.SpendFreeCast, "", 0)]
    [InlineData(CharacterInnateSpellLedgerAction.ShortRest, "grant:x", 0)]
    [InlineData(CharacterInnateSpellLedgerAction.Advance, "grant:x", 0)]
    public async Task MalformedActionOrRevisionCannotReachNative(
        CharacterInnateSpellLedgerAction action, string? grant, int? revision)
    {
        var origin = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub();
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origin, ledgers, native);
        Assert.NotEqual(CharacterInnateSpellLedgerStatus.Applied,
            (await service.ApplyAsync(Character, action, grant, revision)).Status);
        Assert.Equal(0, native.Calls);
        Assert.Equal(0, ledgers.Writes);
    }

    [Fact]
    public async Task MalformedNativeCandidateAndMismatchedProfileCannotCommit()
    {
        var origin = new OriginStub();
        var ledgers = new LedgerStub();
        var native = new NativeStub();
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origin, ledgers, native);
        var bad = new[]
        {
            new TrustedInnateSpellLedgerCandidate(Ledger(0),
                "other-profile", "proposal-human", "native:verification"),
            new TrustedInnateSpellLedgerCandidate(Ledger(0),
                "human", "proposal-human", ""),
            new TrustedInnateSpellLedgerCandidate(Ledger(1),
                "human", "proposal-human", "native:verification"),
            new TrustedInnateSpellLedgerCandidate("""{"foo":"bar"}""",
                "human", "proposal-human", "native:verification"),
            new TrustedInnateSpellLedgerCandidate(Ledger(0),
                "human", "proposal-human", "native:verification",
                ["injected-capacity-decision"])
        };
        foreach (var candidate in bad)
        {
            native.Output = candidate;
            var result = await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.Initialize, null, null);
            Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
                result.Status);
        }
        Assert.Null(ledgers.Saved);
        Assert.Equal(0, ledgers.Writes);
    }

    [Fact]
    public async Task MalformedCommittedProfileCannotAuthorizeReadOrMutation()
    {
        var origins = new OriginStub
        {
            Saved = new(Character, "first-party-001", Fingerprint,
                """{"underlying":{}}""", 1, Now)
        };
        var ledgers = new LedgerStub { Saved = SavedLedger() };
        var native = new NativeStub();
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origins, ledgers, native);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.GetAsync(Character)).Status);
        Assert.Equal(CharacterInnateSpellLedgerStatus.NativeUnresolved,
            (await service.ApplyAsync(Character,
                CharacterInnateSpellLedgerAction.LongRest, null, 0)).Status);
        Assert.Equal(0, native.Calls);
        Assert.Equal(0, ledgers.Writes);
    }

    [Fact]
    public async Task NativeCalculatedSpendCommitsExpectedRevisionWithExactEvidence()
    {
        var origin = new OriginStub();
        var ledgers = new LedgerStub { Saved = SavedLedger() };
        var native = new NativeStub
        {
            Output = new(Ledger(1, 0), "human", "proposal-human",
                "native-calculated:grant:spell")
        };
        var service = Service(new SiteStub(), new ActorStub(Actor),
            origin, ledgers, native);
        var result = await service.ApplyAsync(Character,
            CharacterInnateSpellLedgerAction.SpendFreeCast,
            "grant:spell", 0);
        Assert.Equal(CharacterInnateSpellLedgerStatus.Applied, result.Status);
        Assert.Equal(1, result.Revision);
        Assert.Equal(0, ledgers.LastCommand!.ExpectedLedgerRevision);
        Assert.Equal("SpendFreeCast", ledgers.LastCommand.Operation);
        Assert.Equal("native-calculated:grant:spell",
            ledgers.LastCommand.AuthorizationEvidenceId);
        Assert.Equal(ledgers.Saved?.CharacterId, origin.Saved?.CharacterId);
        Assert.Equal(CharacterInnateSpellLedgerAction.SpendFreeCast,
            native.LastInput!.Action);
        Assert.Equal("grant:spell", native.LastInput.GrantId);
    }

    [Fact]
    public async Task TransactionConflictNeverReturnsAnAppliedResult()
    {
        var origin = new OriginStub();
        var ledgers = new LedgerStub
        {
            Saved = SavedLedger(),
            CommitStatus = InnateSpellLedgerStoreStatus.LedgerRevisionConflict
        };
        var native = new NativeStub
        {
            Output = new(Ledger(1, 0), "human", "proposal-human",
                "native:calculated")
        };
        var result = await Service(new SiteStub(), new ActorStub(Actor),
            origin, ledgers, native).ApplyAsync(
                Character, CharacterInnateSpellLedgerAction.SpendFreeCast,
                "grant:spell", 0);
        Assert.Equal(CharacterInnateSpellLedgerStatus.RevisionConflict,
            result.Status);
        Assert.Equal(1, ledgers.Writes);
        Assert.Equal(0, ledgers.Saved!.LedgerRevision);
    }
}
