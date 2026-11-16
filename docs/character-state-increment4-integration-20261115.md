# Character State Inheritance — Increment 4 integration record

**Date:** 2026-11-15
**Status:** In progress — not a finished production supernatural backend.
**Character Sheet branch:** `feature/character-state-inheritance-increment4-20261114`
**Rules Core branch:** `feature/character-state-mechanics-adapter-increment4-20261114`
**Rules Core review:** [draft PR #158](https://github.com/LegendMaster03/dorks-and-dice-rules-core/pull/158)

## Ownership and installed boundaries

The Site owns authenticated identity, character ownership, campaigns, DM scope and
lifecycle decisions. Character Sheet owns the canonical character, advancement,
inventory, current HP, authorized Origin and PostgreSQL state. Rules Core owns
the inheritance, influence, manifestation and transition mechanics.

This branch adds `character_supernatural_states` (one row per canonical
`CharacterId`) and `character_supernatural_event_receipts` (immutable,
per-character event identity, unique committed revision). An ordinary character
does not need either row. The migration is additive; it changes no existing
Origin, Class or inventory record. JSONB is a versioned *ownership storage
contract*, not a source of playable grants or acquisition evidence.

Current serialized state holds independently owned alternate sheets, locally
owned progression, influences, their grants/evidence, manifestation instances,
per-owner resource quantities, interaction decisions, and processed event IDs.
The event table retains event IDs, state/ledger revisions, event kind,
authority-evidence reference and, for new transitions, the full ordered
source-verified transition request. The additive migration
`20261115020000_AddSupernaturalTransitionRequestAudit` adds nullable
`TransitionRequestJson` (JSONB). New transition commits must include
matching character, prior ledger revision and event identity, with nonempty
ordered steps retaining each action, target, rule occurrence, adjudication
basis, authorization evidence and any replacement interactions. Acquisitions
can not supply a transition audit. Existing receipts remain null rather than
fabricating missing historical steps. **A persisted audit is not itself
cryptographic or independent proof of native/DM authorization:** the trusted
source provider and Rules Core host must verify those facts before writing.

### Transaction boundary

`PostgresCharacterSupernaturalStateStore.CommitVerifiedAsync` accepts only
an internal application-layer command. It validates bounded JSON structure,
ledger character ID and revision, duplicate owned IDs and event identity.
Inside a PostgreSQL serializable transaction it locks the canonical character
root, compares a digest of authoritative Class, Origin, ability, rules, HP,
condition, inventory and other relevant input rows, compares supernatural
state and ledger revisions, validates previous receipts against the committed
ledger, writes the new state, inserts the unique event receipt and commits. PostgreSQL serialization failures are returned as
conflicts, including exceptions wrapped by EF Core.

The ordered transition audit is written inside the same serializable
transaction as the proposed ledger and immutable event receipt. A missing
or mismatched audit rejects the entire write before changing the character.
Old pre-audit event receipts can still load; absent steps can not be inferred
from a processed event ID or a post-transition snapshot. The migration's Down
operation refuses to drop its JSONB audit column once any receipt contains
recorded transition steps. Empty and legacy-only histories can still downgrade;
an audited database needs a separate data-preserving recovery procedure rather
than silent destruction of immutable source evidence.

The PostgreSQL history guard now also binds each ordered step to the
resulting instance and resource changes. An audited Activate must contain
the exact newly committed instance, and every minted manifestation-owned
resource must match a NewResources entry on that activation, including
its owner, source, evidence, quantity and maximum. End, Suppress and
Restore must produce only the matching authorized lifecycle/contribution
delta, with the event recorded as LastEventId. Duplicate step targets,
unexpectedly changed existing instances, unlisted new instances, unlisted
resources and mismatched activation captures fail before commit. The
tests use actual synthetic instance deltas rather than allowing an event
ID to be appended as an otherwise empty transition.

Interaction decisions are also bound to the ordered transition
audit. When the request does not include a replacement interaction set, the
persisted set must remain unchanged. When it does, the proposed ledger must
contain exactly the recorded replacement set. A normal form change does not
implicitly approve a new coexistence, suppression or replacement ruling.
As with the rest of the history guard, this protects the commit boundary
without authorizing a native or DM decision on its own.

The database also checks historical receipt and ledger continuity before
any new acquisition or transition. Existing receipts must have one
contiguous state revision each; transition receipts alone advance the ledger
revision, and their event IDs must exactly match the ordered
`processedEventIds` journal. Corrupted event kind, revision, missing receipt
or reordered history rejects the new command under the canonical character
lock. This remains structural evidence integrity, not independent acquisition
or DM authorization.

The database now additionally enforces historical continuity. A first
acquisition can not fabricate an already-active manifestation. A form
transition must append exactly one processed event, retain previously
acquired sheets and influences, keep activation identity and captures
unchanged, and preserve existing resource quantities. Any new transition
resource must belong to a newly activated manifestation. A separate
acquisition can append ownership/progression, but it can not delete or
rewrite earlier evidence or modify the manifestation journal without
an explicit ledger event. First acquisition can also initialize an
independently source-qualified Primary-, Sheet- or Influence-owned
resource pool, never a Manifestation-owned pool before an activation
exists. Adding pools to an existing character after the first
acquisition still requires a future explicitly revisioned resource
operation; it is not silently implemented as a ledger edit. These are defense-in-depth invariants, not
substitutes for verified gameplay authority.

The application must obtain its digest from the current authoritative rows,
run native planning using the verified values, then commit with exactly that
digest. The DB transition has atomicity, CAS and durable idempotency
constraints. This does **not** establish that the proposed result was
mechanically valid or independently authorized. A production transition
service must prove both before calling this store. Any new material
dependency must be added to the digest before enabling live transitions.

### Delegated transition planning (authority-gated)

Rules Core now includes
`POST /api/mechanics/v1/characters/manifestations/plan`.
This is a read-only host adapter over the existing native transition
planner. The authenticated delegate must supply a current ledger and
transition, but the endpoint requires an independently configured
`INativeManifestationHostAuthorityProvider` to revalidate source
acquisition, event evidence, DM scope, character ownership and revision.
**No production provider is registered.** The endpoint consequently
returns `supernatural_authority_unavailable` rather than accepting a
claimed `DefinitionOccurrence` or fabricated receipt.

A provider may return the Core authority interface only after independently
verifying source records. A successful Core plan is detached, carries
`committed: false`, and is not a database mutation. Character Sheet also has an internal
`CharacterManifestationTransitionService` that checks current Site ownership,
loads the authoritative owned record, delegates source-verified intent
construction, calls this Core planner, rechecks primary input freshness, and
performs the one-event PostgreSQL CAS. Its
`ITrustedCharacterManifestationTransitionSource` policy is deliberately
unregistered in production and no browser mutation endpoint is mapped.
Only the test implementation emits synthetic Wolf/Hybrid transitions.

### Read-only transport

Character Sheet exposes
`GET /api/characters/{characterId}/state/effective`. It retrieves the
Site-authorized Character Build, State, Origin, and current supernatural
ownership snapshot. The shared `CharacterNativePrimaryStateAdapter` constructs
the narrow current Class/ability/native-option subset from authoritative
Character Sheet rows; unsupported Feats, Origin grants, equipment, and HP
policies remain unresolved.

For ordinary characters, Character Sheet delegates to Rules Core
`POST /api/mechanics/v1/characters/effective-state-baseline`. The baseline
rejects all supernatural ownership and events.

For stored supernatural ownership, Character Sheet now delegates to
`POST /api/mechanics/v1/characters/effective-state`. Rules Core uses the
existing `NativeCharacterManifestationProjector`, but only after an
`INativeManifestationHostReadAuthorityProvider` independently authenticates
the actual character snapshot, persisted form acquisition and progression,
source and DM evidence, ruleset and history. **There is no production read
provider registration**. Without it, the Core endpoint rejects the request
and Character Sheet reports an unresolved form, never a fictional default
manifestation.

After Core responds, Character Sheet compares the current primary dependency
fingerprint, the exact native catalog pin, the response contract, the returned
sheet identity and any selected active instance against persisted ownership.
After the first Site-authorized Build access, the application obtains
the authoritative primary dependency digest **before** loading the Build
state used for projection, then reloads Build and reads routine State and
the latest authorized Origin. It compares the same digest after the remote
mechanics request. This explicitly catches updates occurring between the
first Build authorization read and the later State/Origin assembly; a
digest first taken after those reads would not detect that window.

A delegated read also reloads the supernatural record after Core returns.
A concurrent acquisition or form transition may leave all primary Class,
Origin, inventory and HP inputs unchanged; the primary input digest alone
can not detect it. If supernatural ownership or its revision changed during
the remote call, the read is marked `unresolved`, the outdated projection is
withheld, and the snapshot revision is not represented as current. Both
ordinary baseline reads and authorized alternate-form reads enforce this
optimistic freshness check. It does not claim a long-lived database lock.

The result remains `partial` and
`effectiveMechanicsReady: false` even when verified: this endpoint has not
completed form-specific Class, Origin, equipment, HP or resource-policy
entitlement reconciliation. A verified read can surface the currently active
Wolf or Hybrid and its native-projected source provenance. Reversion and
fresh-context reload dynamically resolve the current primary character
instead of restoring an old activation snapshot. Reads do not commit ledger
events or consume resources.

The bounded canonical primary adapter also classifies owned legacy
Species/Subspecies selections, independent Background/Upbringing and other
foundational selections as unresolved sources of form-relevant grants.
Stored inventory is not assumed irrelevant simply because an item is not
equipped or attuned. Nonzero death saves and existing current HP remain
blocked until explicit form-interaction policies exist. These records
continue to appear in read-only partial primary projections; they do not
become usable transformation entitlements.

The Character Sheet host additionally verifies the selected effective
manifestation against **all** persisted active and contributing instances.
It rejects an apparent primary sheet while any activation contributes.
When multiple instances contribute, the selected form must match the
unique persisted suppression/replacement interaction covering exactly
those instances; a selected instance merely existing in the journal is
insufficient. Malformed instance identities, duplicate interaction
participants, or contradictory native response selections fail closed. The
Rules Core source/DM authority provider remains responsible for verifying
the interaction itself.

An archived character may read but does not gain mutation rights.

## Verification

* Character Sheet GitHub `Validate Character Sheet` runs .NET 10 builds,
  real disposable PostgreSQL integration tests, image build and service
  smoke checks.
* Rules Core draft PR #158 runs `Rules Core Phase 3B Public Native API CI`,
  including authenticated HTTP endpoint tests and the existing mechanics
  and source regression suite.
* Character Sheet `CharacterSupernaturalPersistenceTests` exercises
  fresh database reload, absence of synthetic state for ordinary characters,
  duplicate event, revision conflicts, concurrent transitions, stale primary
  input changes and malformed records.
* `CharacterEffectiveStateReadTests` exercises owner gating, archived
  read-only behavior, unmodeled form refusal, invalid native response,
  concurrent input mutation and read-only projection. It also tests
  acquisition during a primary-only evaluation and a second acquisition
  during an independently verified supernatural read; both suppress stale
  projected mechanics while preserving the committed state. A separate
  test mutates a primary ability between the first Site ownership check
  and the authorized Build reload, proving that the primary digest
  brackets the full source assembly, not only the Rules Core call.
* The real Core/PostgreSQL tests verify that an independently owned
  Background selection, current HP, nonzero death saves and an
  unequipped inventory occurrence each block manifestation mutation
  without writing a transition or receipt. Removing those unsupported
  inputs restores the deliberately source-free synthetic test path.
  Unit tests independently cover legacy Species, Background, deity,
  unequipped inventory and nonzero death saves.

* The PostgreSQL history guard tests reject unauthorized interaction
  replacement or removal and confirm that the recorded replacement set,
  when independently verified, remains in the same transition receipt.
  New step-to-ledger tests reject missing, duplicated, forged or changed
  activation resources; altered activation captures; mismatched End and
  Suppress actions; duplicate steps; and an extra committed instance not
  named in the audit. Positive activation and ending retain exact
  ordered evidence after a fresh database context.
* Core host tests cover delegated baseline, missing/wrong ticket,
  content mismatch and forged event ledger refusal.

* The branch-scoped `character-state-increment4-cross-repo.yml`
  runs the actual Core HTTP host and Character Sheet PostgreSQL together.
  It verifies read-only primary evaluation, Site owner enforcement, and
  fail-closed rejection of forged forms and unverified transition planning.
* The cross-repository tests also cover source-verified Suppress and
  Restore operations: one activation switches contribution off and on
  without changing its owner, event of origin, resource quantity or
  activation identity. An unlisted ending condition fails unless its
  source independently authorizes it; an End makes subsequent Restore
  invalid. All operations are stored through real PostgreSQL.
* A separate dual-activation test attempts simultaneously contributing
  Wolf and Hybrid forms. The unadjudicated overlap fails atomically,
  as does a forged interaction. A precisely authorized, test-only
  Replace decision selects Hybrid and preserves both activation records
  plus the exact decision and two ordered steps in one receipt.
  Fresh-context verified reads confirm Hybrid. Tests also tamper with
  Core's returned effective selection: claiming an active alternate form
  is Primary or selecting Wolf contrary to the persisted Replace
  decision must yield `unresolved` and no published native projection.
* The integration test now executes a **test-only authenticated source
  policy** plus independently checking Core host provider, real Core
  manifestation planning, and Character Sheet's internal transactional
  application service. It verifies acquired Werewolf/Wolf/Hybrid ownership,
  Wolf activation, reversion using the latest primary Class level and
  ability scores, reentry,
  atomic switching, preserved grant provenance, reload and event history,
  and denial when the host verifier is absent or the user is archived.
  A verified active-Wolf read after primary Class advancement confirms that
  current primary Level and ability scores inherit dynamically, without
  rewriting persistent Wolf progression or the active manifestation ledger.
  An additional controlled test runs a source-verified manifestation-owned
  resource pool through real Core planning, atomic PostgreSQL activation,
  effective-state read, reversion, and later reentry. It verifies separate
  owner identity, current/recorded/effective maximum, no balance transfer to a
  new activation, unchanged read-only ledger and receipt durability after
  service restart. The resource authority exists only in the synthetic
  fixture; no production resource grant is being inferred.
  A deliberately invalid two-step Wolf switch verifies that a successful
  End step is not persisted when a later Activate is rejected. Character
  Sheet recognizes only bounded, protocol-defined Rules Core error codes.
  Native-content mismatch maps to `NativeContentChanged`, missing or denied
  source authority maps to `SourceUnauthorized`, and an upstream ledger
  conflict maps to `LedgerRevisionConflict`; other deterministic 400/409/422
  rejections remain `PlanUnresolved` rather than transport outages.
  A separate real Core HTTP/PostgreSQL test verifies that a persisted form
  pinned to obsolete native content can not transition, returns
  `NativeContentChanged`, and leaves revision and event history intact.
  This is a synthetic validation slice, not playable first-party rules.
* New PostgreSQL audit tests verify that a source-verified ordered
  transition request survives service/context reload, malformed or mismatched
  step audits leave no partial write, and acquisition cannot impersonate a
  transition event. Additive migration tests cover upgrade, legacy-only
  rollback and rejection of an audit-destroying downgrade. Corruption tests
  change historical receipt kinds and revisions directly in PostgreSQL;
  subsequent writes fail until the original journal history is restored. The real Rules Core integration asserts that the
  Wolf-to-Hybrid switch persists its exact End and Activate steps, source
  occurrence and per-step evidence in one receipt.
* Additional PostgreSQL tests exercise immutable activation receipts,
  reversion history, duplicate/reordered events, attempted progression
  replacement, unauthorized resource mutation and separate acquisition.
* Effective-state concurrency tests run two independent application contexts
  against real PostgreSQL, verifying that ordinary reads neither create state
  nor alter primary inputs and that concurrent reads of stored supernatural
  resources do not change resource quantities, owners, revisions or receipts.
* Fresh-context construction of the transition service verifies that a prior
  manifestation remains stored after the former application service instance
  is replaced; it is not reconstructed from in-memory activation state.
* A shared `CharacterNativePrimaryStateAdapter` now reconstructs supported
  primary Class advancement occurrences, ability scores and native selections
  from Site-authorized Character Sheet Build/State views. Independently owned
  Feats, Subclasses, equipment, Origin, HP and non-native rules inputs remain
  explicitly unresolved, but no longer suppress supported Class progression in
  a read-only primary projection. This is not a substitute for full primary
  entitlement evaluation.
* Internal manifestation transitions independently reload that same primary
  snapshot and compare all source-supplied primary fields before contacting
  Rules Core. Supplied primary levels, scores, choices, resources or duplicate
  JSON property names can not override Character Sheet's own state. Transitions
  with unmatched or presently unsupported primary policies fail closed.
  Synthetic end-to-end tests include a forged primary score rejection;
  isolated unit tests exercise source injection and incomplete mechanics.
* The verified manifestation read test uses real authenticated Rules Core
  HTTP and real Character Sheet PostgreSQL, with an independently checking
  **test-only** host read provider. It verifies active Wolf projection,
  reversion to primary, Hybrid after a fresh database context, retained source
  progression, and denial without a registered verifier. A successful test
  projection is never proof of a production acquisition or DM policy.
* A saved Origin or supernatural record pinned to an older native catalog
  fingerprint is reported as `authorized-origin-native-content-changed` or
  `supernatural-native-content-changed`. Such reads remain unresolved and
  can not elevate stored evidence into current mechanical authority.

These tests do **not** substitute for the requested real end-to-end
authenticated Werewolf/Wolf/Hybrid acquisition and transition tests.

## Required work before Increment 4 can be closed

1. Implement the production `INativeCharacterManifestationAuthority`
   verification or equally strict trusted exchange. Verify acquired source
   occurrences, progression/grants, DM ruling evidence, scope, and revocation
   against authoritative Site and Rule sources, rather than IDs/receipts
   echoed by a browser.
2. Implement authenticated acquisition and transition application services.
   Their requests may carry intent, event ID and expected revision but not
   authoritative state, ledger, grants or ad hoc approvals.
3. Register real independently verifying Core host authority providers
   for both transition planning and effective-state reads. The transport
   endpoints are implemented and fail closed, but the host has no production
   source-of-truth verifier or DM acquisition policy.
4. Connect future independently authorized production acquisition and
   transition operations to the existing complete-plan commit path. New
   transitions now retain ordered step audits; audit persistence alone does
   not prove receipt validity, revocation, source entitlement, DM scope or
   form-specific effects. Those must come from trusted production verifiers.
5. Extend the now-shared primary adapter to revalidate the latest
   `NativeOriginProfile.Underlying`, independently owned Class/Background/
   Upbringing entitlements, trained skills, inventory and resource facts.
   The current bounded Class/ability/option subset is intentionally not a
   complete primary or form-specific entitlement verifier.
6. Close the incomplete cases in the now-recovered 32-scenario checklist
   below, particularly real supernatural acquisition and DM authorization,
   authenticated Origin reconstruction, resource policies and Site
   revocation. Literal host-process restart now has test-only Site delegation
   coverage in scenario 32, but that is not production entitlement.
7. Verify no regression to existing initial-Origin verification, mixed
   Origin DM approval, older content compatibility, and ordinary character
   API behavior. Do not migrate old source-occurrence IDs into canonical
   `origin-trait` identities by inference.

### Current fail-closed policy

The absence of first-party playable Werewolf, Vampire Lord or curse
acquisition/progression rules means no production authority may approve
those forms yet. Controlled synthetic tests can validate orchestration,
but they must not become production entitlements.

**Do not merge or deploy these branches as a complete Increment 4.**

## Additional security review — post-plan Site revocation

The internal transition application service now repeats the current Site
ownership/lifecycle check after Rules Core has planned the transition and before
it begins the PostgreSQL CAS commit. It rejects newly archived characters,
revoked owner access, and changed campaign associations. A real Core
HTTP/PostgreSQL test exercises all three changes **after** a successfully
source-verified synthetic plan, verifies that no ledger revision or event receipt
was written, then restores Site scope and completes the original Wolf flow.

This is a defense-in-depth check against a concrete time-of-check/time-of-use
window, not a distributed transaction with Site. Site may still revoke access
between that final check and the Character Sheet commit; production activation
requires a commit-bound trusted Site authorization or equivalently enforced
revocation/version mechanism. No production supernatural source provider or
new gameplay acquisition policy has been registered.

## Acceptance accounting boundary

The original 32-scenario checklist has been recovered and checked into
[the Character Sheet feature branch](character_state_increment4_acceptance_32_scenarios.md).
The exact evidence-based status of each case is documented in the final
reconciliation below. The original S1–S15 architecture design scenarios are
separate from these Increment 4 implementation scenarios.

## Additional primary owner-input coverage

The primary adapter now explicitly marks non-native Character Sheet choices,
trained skills/competencies, persisted resource inputs, currency balances, and
advancement progress as unresolved for manifestation transitions. It preserves
the supported Class/ability native projection for read-only use and does not
grant any of those unsupported effects. Currency rows are now part of the
authoritative primary dependency fingerprint, preventing a remote plan from
being committed over a concurrent currency change.

A unit test verifies the independent issue classifications and retained Class
projection. The existing real Rules Core HTTP/PostgreSQL case now exercises
trained skills, non-native choices, resource inputs, and currency in addition
to its prior Background, HP, death-save and inventory refusals. These are
negative production-safety checks using test-only supernatural authorization,
not new first-party transformation mechanics.

## Increment 4 continuation — requirement-by-requirement accounting

The categories below distinguish implemented **mechanism** from independently
authorized **production gameplay**. The integration tests use synthetic
sources, not approved Werewolf/Vampire Lord acquisition or progression rules.

| Requirement | Implemented and tested | Remaining boundary |
| --- | --- | --- |
| 1. Independently trustworthy supernatural and DM source authorization | The native authority interfaces and fail-closed delegation boundaries are implemented and tested with independently checking synthetic providers. | **Blocked:** no approved first-party supernatural acquisition/progression definitions, production source/DM decision evidence verifier, or revocation contract; no provider is registered. |
| 2. Authenticated acquisition and transition workflows with server-derived state | An internal Character Sheet transition service reloads the owned ledger and primary values; it refuses injected primary mechanics and now rechecks Site ownership, lifecycle and campaign association after remote planning. | **Partial/blocked:** no production acquisition service, browser/API transition mutation route or trusted production transition-source policy. Site authorization can still change between the final check and the database commit. |
| 3. Rules Core production authority registration | The planning and verified-read HTTP endpoints are implemented; absent/unverified authority produces explicit refusals. New request-shape guards run before authorization. | **Blocked:** both production authority-provider registrations depend on independent source-of-truth verification; installing permissive test providers would violate the contract. |
| 4. Authorized operations and atomic persistence | PostgreSQL serializable CAS, revision/event receipts, ordered request audit, resource/interaction history guards, deterministic conflict handling and restart/reload are covered by real database and Core tests. | **Partial:** production acquisition and transition application can use this boundary only when approved authorization providers and commit-bound Site authorization exist. |
| 5. Verified primary Origin, Class, Background, Upbringing, inventory, skills and resource facts | Class/ability/native-option inputs are reconstructed from current Character Sheet rows; unsupported independently owned facts are surfaced as issues rather than invented grants. Rules inputs, inventory, HP, death saves, currency and advancement progress block unsupported form transitions; currency joins the dependency digest. | **Partial/blocked:** fully verifying Origin Underlying, class features, independently earned background/upbringing/skill grants, inventory transfer/equipment, form HP and resource policy needs authored rules and source verifiers. |
| 6. Acceptance and concurrency/regression tests | The recovered original 32-scenario checklist is now audited against exact methods and CI evidence in the matrix below. Structural and native tests cover the major persistence, ownership, conflict and read-only invariants. | **Partial:** cases 2, 6, 7, 9, 21, 22, 24, 26 and 32 retain production-authority or cross-pillar/host-restart gaps. |
| 7. Cross-pillar and backward-compatibility regression | Existing branch CI builds the service and runs the established suites; read-only adapters fail closed on old catalog pins and unverified underlying Origin. | **Partial:** complete source evidence migration and first-party cross-pillar gameplay acceptance remain outstanding. No legacy occurrence ID should be converted into a canonical origin-trait identity by inference. |

### CI and review evidence

* Rules Core head `2de1587037ed469289131e8bc53d17875ed2b387`:
  `Validate Rules Core Rewrite Structure` [run 43777973860](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43777973860)
  and `Rules Core Phase 3B Public Native API CI`
  [run 43777973876](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43777973876)
  **passed**.
* Character Sheet previously passed both full validation and its real
  Rules Core HTTP/PostgreSQL suite at
  `0bf2e200e9accb3ca818efe6f6175a24b919924e`:
  [validation 43775774159](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43775774159),
  [cross-repository 43775774162](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43775774162).
  The continuation implementation at `d03ea5c1b61b68f6ea0d4dc07f853ba2607afb23`
  has now passed [full validation 43778756141](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43778756141):
  **134 unit tests, 205 integration tests, frontend build, Compose validation,
  container build, and PostgreSQL smoke test**. The same head passed
  [real Core HTTP/PostgreSQL integration 43778756140](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43778756140):
  **9 of 9 tests**. These tests validate the synthetic authority
  boundary; they do not authorize playable supernatural acquisitions.
* Security findings: external Site authorization is not transactionally
  coupled to Character Sheet PostgreSQL; currency was missing from the
  authoritative primary input digest (now fixed); unrelated Character Sheet
  choices, trained skills and resource facts had no dedicated form-interaction
  issue classification (now fixed); malformed Core planning graphs were not
  rejected before the provider boundary (now fixed).
* There is no approved playable Werewolf, Wolf, Hybrid or Vampire Lord source.
  Do not register synthetic fixture providers as production authority,
  infer old source IDs into canonical Origin traits, or close this increment.


## Original Increment 4: 32-scenario acceptance reconciliation

The [recovered original acceptance checklist](character_state_increment4_acceptance_32_scenarios.md) is preserved without replacing its source language or November 15 branch snapshot. These 32 implementation scenarios are separate from the architecture's S1–S15 design cases. The statuses apply to tested mechanics/persistence and current production boundaries, not to hypothetical first-party playable Werewolf/Vampire Lord rules.

**Accounting after the November 15 independent review and executed continuation:** 23 complete at the tested implementation boundary, 9 partial, 0 wholly blocked, 0 untested. Independent review initially found 21 complete and 11 partial; scenarios 23 and 32 now have new paired-revision CI evidence. **Complete does not mean a synthetic source is authorized in production.**

### Evidence key

- **X** — `tests/CharacterSheet.IntegrationTests/CharacterStateCrossRepositoryTests.cs` (real Rules Core HTTP + PostgreSQL, synthetic only)
- **P** — `tests/CharacterSheet.IntegrationTests/CharacterSupernaturalPersistenceTests.cs` (real PostgreSQL integration)
- **R** — `tests/CharacterSheet.IntegrationTests/CharacterEffectiveStateReadTests.cs` (real PostgreSQL; Core gateway stub when applicable)
- **M** — `tests/RulesCore.Mechanics.Tests/NativeCharacterManifestationTests.cs` (Rules Core mechanics unit)
- **I** — `tests/RulesCore.Mechanics.Tests/NativeCharacterInfluenceResolverTests.cs` (Rules Core influence unit)
- **O** — `tests/RulesCore.Mechanics.Tests/NativeCharacterSheetOriginBaselineAdapterTests.cs` (Rules Core Origin adapter unit)
- **A** — `tests/CharacterSheet.IntegrationTests/NativeOriginInitialActivationTests.cs` (Character Sheet Origin integration)
- **D** — `tests/CharacterSheet.IntegrationTests/MixedOriginCrossRepositoryPersistenceTests.cs` (real Core HTTP + PostgreSQL mixed-Origin integration)
- **H** — `tests/CharacterSheet.IntegrationTests/AuthorizedOriginStatePersistenceTests.cs` (Origin PostgreSQL integration)
- **N** — `tests/RulesCore.Host.Tests/NativeAuthenticatedApiTests.cs` (Rules Core authenticated HTTP integration)

### Per-scenario evidence

| # | Scenario | Status | Exact test method(s) | Test type | Remaining gap / qualification |
| --- | --- | --- | --- | --- | --- |
| 1 | Ordinary primary character loads | **complete** | **X** `OwnedPrimaryCharacterUsesRealNativeHttpAndPostgresWithoutMutatingState`; **R** `OrdinaryAndArchivedCharacterReadsArePartialAndNonMutating` | Core HTTP+PG (synthetic), PostgreSQL/read | None; no supernatural state is synthesized. |
| 2 | Verified Werewolf ownership reload | **partial** | **P** `OwnedAlternateSheetAndEventSurviveFreshContextWithoutAnotherCharacter`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | PostgreSQL, Core HTTP+PG (synthetic) | Stored ownership is durable; independently verified production Werewolf acquisition/DM evidence is missing. |
| 3 | Shared parent progression without duplication | **complete** | **M** `WolfActivationReturnsPartialProjectionWithOwnedAdvancement`; **M** `SwitchingToHybridDoesNotClaimWolfOwnedProgression`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | mechanics unit, Core HTTP+PG (synthetic) | Complete engine ownership invariant for synthetic grants; no playable progression rules implied. |
| 4 | Wolf progression remains separate from Hybrid | **complete** | **M** `SwitchingToHybridDoesNotClaimWolfOwnedProgression`; **P** `AcquisitionPreservesPreviouslyEarnedProgressionAndExistingLedger` | mechanics unit, PostgreSQL | Synthetic sheet-specific advancement is retained without transfer. |
| 5 | Latest primary Class progression propagates dynamically | **complete** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **M** `CurrentClassAndUnderlyingOriginChangesFlowIntoActiveFormAndReversion` | Core HTTP+PG (synthetic), mechanics unit | Class levels and abilities propagate; unverified Class feature applicability remains withheld. |
| 6 | Reversible influence expression preserves earned progression | **partial** | **I** `EarnedProgressionSurvivesExpressionLossAndUseRequiresAuthoredDecision`; **I** `ReversibleMultidimensionalCurseUsesLatestPrimaryNotStaleSnapshot`; **P** `SeparateAcquisitionCanAppendProgressionButNotRewriteInfluenceExpression` | influence unit, PostgreSQL | Resolver retains earned data; production-authorized revisioned influence expression updates do not exist. |
| 7 | Authorized Wolf activation is durable and atomic | **partial** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **P** `TransitionIsAtomicDurableAndDuplicateOrStaleEventsDoNotChangeIt` | Core HTTP+PG (synthetic), PostgreSQL | Real commit uses independently checked synthetic authorities, not approved production Wolf acquisition/activation rules. |
| 8 | Active Wolf identity survives PostgreSQL reload | **complete** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **P** `ActivationAndReversionPreserveImmutableOwnersAndPriorEventsAcrossReload` | Core HTTP+PG (synthetic), PostgreSQL | Persisted instance and receipt reload without reactivation. |
| 9 | Reversion resolves the latest normal character | **partial** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **M** `AuthorizedReconstructedUnderlyingPropagatesThroughActiveWolfAndReversion` | Core HTTP+PG (synthetic), mechanics unit | Class and ability refresh works; real reconstructed Origin Underlying is not fully bridged into transition. |
| 10 | Reentering Wolf preserves advancement | **complete** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **M** `ReversionAndReactivationPreservePersistentProgressionAndHistory` | Core HTTP+PG (synthetic), mechanics unit | Sheet progression persists independently of manifestation instance. |
| 11 | Wolf-to-Hybrid switch is all-or-nothing | **complete** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **P** `OrderedStepAuditMustMatchEachCommittedInstanceLifecycleDelta` | Core HTTP+PG (synthetic), PostgreSQL | Rejected second step writes nothing; valid ordered End/Activate audit reloads. |
| 12 | Stale expected revision rejected | **complete** | **P** `TransitionIsAtomicDurableAndDuplicateOrStaleEventsDoNotChangeIt`; **X** `AbsentHostAuthorityStaleRevisionAndArchivedActorNeverCommitTransition` | PostgreSQL, Core HTTP+PG (synthetic) | Serializable compare-and-swap enforced. |
| 13 | Duplicate event cannot duplicate transition or resources | **complete** | **P** `TransitionIsAtomicDurableAndDuplicateOrStaleEventsDoNotChangeIt`; **X** `OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation` | PostgreSQL, Core HTTP+PG (synthetic) | Event identity and resource ownership reject replay. |
| 14 | Simultaneous conflicting transitions cannot both commit | **complete** | **P** `CompetingTransitionsAtSameRevisionCanNotBothCommit`; **M** `StaleRevisionAndDuplicateEventRejectBeforeTransitionAuthority` | PostgreSQL, mechanics unit | Real PostgreSQL concurrent writers allow only one commit. |
| 15 | Failed authorization/evaluation never mutates | **complete** | **X** `ForgedFormAndSavedUnverifiedOwnerNeverBecomePlayable`; **X** `OverlappingManifestationsRequireIndependentInteractionAuthorityAndAtomicSelection`; **M** `TransitionPreviewRejectsFormResourcePolicyConflict` | Core HTTP+PG (synthetic), mechanics unit | Current internal mutation boundary commits no denied or unresolved plans. |
| 16 | Four resource owner kinds retain their identities | **complete** | **P** `FirstAcquisitionCanPersistSourceQualifiedPrimarySheetOrInfluenceResource`; **X** `FourResourceOwnersRemainDistinctThroughTransitionReadAndReload`; **X** `OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation`; **M** `ResourceOwnershipIsStableAndMaximumChangeNeverRefills` | PostgreSQL, Core HTTP+PG (synthetic), mechanics unit | All four source-qualified owner identities and quantities survive a verified Wolf activation, fresh PostgreSQL context, real Core read, and reversion. First-party acquisition and resource applicability remain unapproved, but are not required to validate the owner identity invariant. |
| 17 | Ending an activation does not refill pools | **complete** | **X** `OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation`; **M** `ResourceOwnershipIsStableAndMaximumChangeNeverRefills` | Core HTTP+PG (synthetic), mechanics unit | Exit/reentry never changes recorded current quantity. |
| 18 | Different activation cannot borrow private pool | **complete** | **X** `OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation`; **M** `NewFormCanNotBorrowAnotherActivationPrivateResource` | Core HTTP+PG (synthetic), mechanics unit | Future transfer requires independent authorized rules. |
| 19 | Effective maximum changes do not change current | **complete** | **M** `CapacityReductionDoesNotClampRecordedCurrentQuantity`; **I** `NumericMaximumChangesAreProjectedWithoutRefillingCurrentResources` | mechanics unit, influence unit | Read-only capacity changes cannot refill current values. |
| 20 | Unsupported HP/transfer policies unresolved | **complete** | **X** `CanonicalBackgroundAndHitPointsBlockUnverifiedFormTransition`; **M** `NativeProjectionWithholdsUnverifiedFormFeaturesButPreservesNumbers` | Core HTTP+PG (synthetic), mechanics unit | No current HP transfer or recovery mechanics are invented. |
| 21 | Reconstructed authoritative Origin used by active form | **partial** | **M** `AuthorizedReconstructedUnderlyingPropagatesThroughActiveWolfAndReversion`; **O** `UpdatedUnderlyingOriginIsReadWithoutRestoringHistoricalOrCurrentForm` | mechanics unit, Origin adapter unit | Native adapter test exists; real saved Origin Underlying is not revalidated in the active-form host path. |
| 22 | Reversion does not restore historical Origin snapshot | **partial** | **O** `UpdatedUnderlyingOriginIsReadWithoutRestoringHistoricalOrCurrentForm`; **M** `AuthorizedReconstructedUnderlyingPropagatesThroughActiveWolfAndReversion`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | Origin adapter unit, mechanics unit, Core HTTP+PG (synthetic) | No real persisted Rite/reconstruction through live activation and reversion integration test. |
| 23 | Existing Origin activation/receipts/projections still work | **complete** | **A** `NativeAuthorizedDirectOriginCommitsOnceAndBlocksLegacySpeciesChanges`; **D** `SiteScopedHumanElfApprovalEvaluatesPersistsAndReloadsRealNativeGrants`; **H** `CommittedOriginAndEventSurviveReloadWithoutActivatingDraftRows` | Origin integration, Core HTTP+PG (Origin), Origin persistence | Existing initial/mixed Origin regressions remain exercised in CI. |
| 24 | Mismatched source occurrences and progression receipts fail closed | **partial** | **M** `IncorrectNonemptyFormOccurrenceAndReceiptFailHostVerification`; **M** `UnauthorizedInheritedProgressionAndGrantBlockTransitions`; **X** `ForgedFormAndSavedUnverifiedOwnerNeverBecomePlayable` | mechanics unit, Core HTTP+PG (synthetic) | Native/test authority denies forgeries; production acquisition, progression and DM source verifier remains absent. |
| 25 | Stale content and ruleset versions rejected | **complete** | **X** `NativeCatalogChangeRejectsPreviouslyAcquiredStateBeforeAnyTransitionCommit`; **R** `OriginPinnedToObsoleteNativeCatalogIsExplicitlyUnresolved`; **M** `NativeProjectionRejectsStaleNativeCatalogVersion` | Core HTTP+PG (synthetic), PostgreSQL/read, mechanics unit | Catalog mismatch is explicit; no historical occurrence-ID inference. |
| 26 | Unauthorized actor cannot read or mutate another character | **partial** | **R** `UnauthorizedActorNeverContactsNativeMechanics`; **X** `AbsentHostAuthorityStaleRevisionAndArchivedActorNeverCommitTransition`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | PostgreSQL/read, Core HTTP+PG (synthetic) | Owner gating works; production mutation endpoint and atomic Site authorization/revocation at PostgreSQL commit remain absent. |
| 27 | Archived character lifecycle still applies | **partial** | **R** `OrdinaryAndArchivedCharacterReadsArePartialAndNonMutating`; **X** `AbsentHostAuthorityStaleRevisionAndArchivedActorNeverCommitTransition`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | PostgreSQL/read, Core HTTP+PG (synthetic) | Read-only access remains available; synthetic pre/post-plan archived mutation is denied, but the production gateway reuses the request-entry Site snapshot. A real mid-plan archive/revocation is not independently detected. |
| 28 | Untrusted form, grant, ledger, DM claims cannot authorize | **complete** | **X** `ForgedFormAndSavedUnverifiedOwnerNeverBecomePlayable`; **N** `ManifestationPlanRequiresIndependentAuthorityNotJustDelegationOrEvidenceLabels`; **P** `AcquisitionRejectsForgedTransitionStepsWithoutCreatingNewReceipt` | Core HTTP+PG (synthetic), Core host HTTP, PostgreSQL | No browser supernatural mutation route; evidence labels do not confer authority. |
| 29 | Reloaded mechanics/provenance equal committed result | **complete** | **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **M** `RepeatResolutionIsDeterministicAndDoesNotDoubleGrantOrConsume` | Core HTTP+PG (synthetic), mechanics unit | New deep effective-character parity assertion passes real Core HTTP/PostgreSQL CI at canonical primary planning commit 8d24ff28d363. |
| 30 | Unsupported mechanics remain partial/unresolved | **complete** | **R** `StoredSupernaturalRecordRemainsUnresolvedUntilAuthorityIsAvailable`; **M** `NativeProjectionWithholdsUnverifiedFormFeaturesButPreservesNumbers`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | PostgreSQL/read, mechanics unit, Core HTTP+PG (synthetic) | Class/Origin/HP applicability withheld; effectiveMechanicsReady remains false. |
| 31 | Simultaneous effective reads do not mutate | **complete** | **R** `ConcurrentPrimaryEffectiveReadsRemainReadOnlyAndIndependent`; **R** `ConcurrentSupernaturalEffectiveReadsDoNotAlterResourceOwnershipOrReceipts`; **X** `FourResourceOwnersRemainDistinctThroughTransitionReadAndReload` | PostgreSQL/read, concurrent real Core HTTP+PG (synthetic) | Three concurrent independently authorized Core HTTP reads using separate PostgreSQL contexts preserve complete resource records, ledger revisions, and event receipts; first-party authority remains absent. |
| 32 | Character Sheet restart restores stored activation | **complete** | **X** `RealCharacterSheetProcessRestartPreservesVerifiedActiveFormAndAudit`; **X** `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore`; **P** `OwnedAlternateSheetAndEventSurviveFreshContextWithoutAnotherCharacter` | Real Character Sheet child process x2, test Site delegation, real Core HTTP + PostgreSQL (synthetic), PostgreSQL | A real Character Sheet host process is terminated and relaunched against the same disposable PostgreSQL database. Fresh ticket introspection and delegated Core read restore the identical effective manifestation. Complete owned sheet/influence/ledger JSON, saved progression, resource records, revision, ordered audit and receipts remain unchanged; no extra activation event. Site and source authority are test-only, not production entitlements. |

### Regression and authorization conclusion

- **Resource-ownership continuation:** Character Sheet commit `77891c88d742fc7d981c9a6f4365235bbde97a25` passed [real Core HTTP/PostgreSQL run 43784252732](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43784252732): **10 of 10 integration tests**. The new `FourResourceOwnersRemainDistinctThroughTransitionReadAndReload` exercises all four owner kinds together, with independent test-only source verification, a fresh database context, full effective projection comparison, durable ledger inspection, and authorized reversion. It does not register production acquisition or grant a form.
- Character Sheet at `8d24ff28d36349152f7fe804f4bf5794ca8ea76a` passed [real Core HTTP/PostgreSQL run 43780514554](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43780514554): 9 of 9 tests. That run includes the newly strengthened scenario 29 assertion comparing the *complete* planned effective character to the freshly loaded, source-verified Core projection. The next documentation-only commit triggers its own CI.
- The preceding complete Character Sheet validation at `b27e7d2db4f15bfbbf75d7f297c1f87befa82632` passed [run 43779111839](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43779111839): 134 unit tests, 205 integration tests, frontend build, container build and PostgreSQL smoke. Rules Core branch `2de1587037ed469289131e8bc53d17875ed2b387` passed [host CI 43777973876](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43777973876) and [structure validation 43777973860](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43777973860).
- Scenario 29 revealed a concrete primary-serialization mismatch: a source-verifier may supply an optional empty native selection as `null`, whereas Character Sheet builds the canonical empty array. The transition service now forwards `primary.Mechanics.Value` **after** source equivalence verification. This makes planned and fresh-read mechanics/provenance consistent while preventing source-supplied primary content from bypassing the canonical adapter.
- The remaining partial cases are **2, 6, 7, 9, 21, 22, 24, 26, and 27**. The missing production verifiers are cross-cutting, especially independent source occurrences and grants, DM evidence and revocation, the latest reconstructed `Origin.Underlying`, form-specific resource/HP/inventory policies, and a commit-bound Site authorization contract. Additional integration gaps include revisioned influence expression mutations. Scenario 16's combined four-owner resource test now passes without production supernatural authorization.
- **No production supernatural source/DM authority provider or browser mutation endpoint is installed.** No test-only source receipts, synthetic Wolf form or matching native name is proof of production eligibility; do not merge or deploy Increment 4 as finished.


### November 15 independent-validation continuation — verified revision pairs

- **Rules Core F3:** activation now rejects null nested resource policies before matching newly minted resources or invoking transition-authority callbacks. The pure-planner and authenticated-host regressions execute at Rules Core `b8d92634afe2709d984da9a2de266a376b7b0ad8`. [Public native API CI run 43795998402](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43795998402) passed 121 host and 332 mechanics tests (453 C# tests total); [structure CI 43795998208](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43795998208) also passed. No permissive production provider was added.
- **Scenario 23, actual paired compatibility:** Character Sheet `25e0cd6557a42dd491f45e7e8c60cc6536207116` with Rules Core `b3a6b9f843730f903deef6a7701103dab0281945` ran the branch-scoped real-HTTP/PostgreSQL CI [43795585312](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43795585312). The existing Character State suite passed **10/10** and the newly compiled/discovered `PILLAR2_PHASE5_ORIGIN` Mixed-Origin suite passed **13/13**. Character Sheet [general validation 43795585289](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43795585289) also passed. These are exact tested pairs; the later Core host change is separately covered by its own successful CI, not falsely claimed as part of the earlier pair.
- **Compatibility correction:** canonical `trait.shared.low-light-vision` representation was not recognized by the Character Sheet low-light vision reader, which had accepted only the older source-local ID. Exact canonical-ID recognition now preserves original species source occurrence, grant ID, and verified trait payload; unrelated suffix-matching definitions remain rejected. No historical `origin-trait` migration or equivalence has been inferred.
- **Site authorization F1:** production `ToolHostSiteCharacterAccessGateway` reads `HostedToolAuthenticationMiddleware.GetAuthenticationContext(HttpContext)`, a request-scoped Site character/campaign projection. Re-invoking it after remote planning does not fetch a newer Site decision; tests with mutable fake gateways do not establish production revocation detection. Site must expose an independently fresh, character-scoped authorization check and ideally a commit-fenced version/lease protocol to close the stronger race. Even an additional remote check alone is not atomic with the Character Sheet PostgreSQL commit. Existing fail-closed interfaces remain unchanged; no Site protocol is invented here.
- **F6 expanded real-HTTP regression:** Character Sheet commit `cdf12b04d7026d79ba2763e4d477503aac3f75c6` passed [paired Core HTTP/PostgreSQL CI 43797039557](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43797039557): 10/10 Character State and 13/13 Mixed-Origin tests. The four-owner test now runs three independent PostgreSQL-context effective reads concurrently through the authenticated real Core HTTP host; verifies unchanged ledger JSON, revisions and complete receipt tuple arrays; and compares the entire persisted resource array (identity, source, evidence, owner, quantity, maximum) before and after reversion. This improves concurrency and durability evidence, but does not create a source-authorized production entitlement or cover all adversarial PostgreSQL record mutations.
- **F6 real-HTTP adversarial coverage:** Character Sheet `9e3606a5338e60fde85256ca2fe5cb128b0fd58a` passed [cross-repository CI 43797438709](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43797438709), again 10/10 Character State and 13/13 Mixed-Origin tests. The real verified Core HTTP read now individually rejects altered resource definition ID, source occurrence, evidence receipt, owner kind, and owner ID against the PostgreSQL snapshot, returning `supernatural_authority_unverified` without changing the ledger. This tests forged delegated payloads, not independently corrupted persisted PostgreSQL rows.
- **Still incomplete:** further F6 adversarial mutations of stored PostgreSQL evidence, persisted reconstructed `Underlying` and other authoritative primary reconciliation, a Site-owned authorization freshness/fence interface, and the deferred shared Pillar 2/Character State testing override. These requirements must not be counted as passing based on application-service reinstantiation, synthetically authorized forms, or success of unrelated tests.

- **F5 literal process restart:** Character Sheet `4877b2f6a962bcadb82e72803fe392c4c82d10f1` passed [real Core HTTP/PostgreSQL CI 43798492110](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43798492110): 11/11 Character State and 13/13 Mixed-Origin tests. `RealCharacterSheetProcessRestartPreservesVerifiedActiveFormAndAudit` launches the actual Character Sheet Web binary on a disposable database; a separate locally hosted, ticket-verifying Site test server issues the standard delegation capability, and a real independently verifying Core HTTP host projects an acquired, activated form. The first Character Sheet process is killed, a new process starts against the same database, and a newly redeemed Site ticket returns an identical active-form projection. Full persisted alternate sheets (including owned progression), influences, ledger, resource provenance, revision and immutable event receipts are compared against the committed pre-restart records. No restoration activation event is inserted. This is a test-only Site and source authority fixture; it verifies the host restart contract, not authorization by a live production Site.

### F1 direct Site contract investigation (read-only, November 15)

Inspected current `LegendMaster03/dorks-and-dice-site` `main`:
`dorks-and-dice-site/Controllers/ToolHostApiController.cs`,
`Services/Tools/ToolHostAuthenticationContextFactory.cs`, and
Character Sheet's production `HostedToolAuthenticationMiddleware`,
`ToolHostSiteCharacterAccessGateway`, and `CharacterManifestationTransitionService`.

- Site's current Tool Host `introspect` endpoint redeems a **one-time** entry ticket and returns a snapshot; Character Sheet stores this in the request `HttpContext`. The production access gateway reads that same snapshot on both calls. It does **not** contact Site at post-plan time.
- Site's existing `session`, `campaigns`, and `campaigns/{campaignId}/context` endpoints use the logged-in browser user's identity. They do not provide a backend mutation authorization decision for an already-introspected application request or a Site-issued commit fence.
- Site's `ToolHostAuthenticationContextFactory.BuildCharacterAccessProjectionAsync` reloads owner and archived character state from `ICharacterService.GetForOwnerIncludingArchivedAsync` when constructing an applicable context; this is an existing Site-owned source for a future fresh check, **not** an existing service-to-service authorization protocol. `CreateTargetScopedAsync` copies campaign role summaries from the previously issued source context, rather than independently retrieving all Site permissions for a Character Sheet mutation.
- No usable character-specific permission revision, fence, or authorization lease was found in the reviewed Tool Host contracts. Character Sheet must not invent one from its own database revision, cached claims, evidence IDs, or repeated introspection of a one-time ticket.

**Smallest proposed integration dependency (not approved or implemented):** Site would need an authenticated, independently fresh **character mutation eligibility check** scoped to the original Site actor and requested character, optionally campaign/DM authorization where applicable. It must reload current ownership, active/archive lifecycle, campaign associations, and DM/ruling scope from Site's authoritative stores. A service-bound Site-issued capability must authenticate this check without replaying the consumed entry ticket. A stronger revocation-at-commit guarantee additionally needs a Site-owned revision/fence/lease or another coordinated commit protocol; a fresh check alone can still race the Character Sheet PostgreSQL transaction. The current implementation retains request-entry authorization and its existing fail-closed behavior until such a contract is designed and approved.

This investigation is read-only for the Site repository. It does not authorize changes to `dorks-and-dice-site` or mark F1 resolved.
