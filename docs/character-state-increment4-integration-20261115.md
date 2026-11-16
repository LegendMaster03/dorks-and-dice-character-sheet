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
6. Add real PostgreSQL and Core HTTP integration for all 32 scenarios in
   the user acceptance specification, including Wolf/Hybrid switching,
   progression persistence across reversion, simultaneous conflicting
   operations, replay, resource policy, stale Origin/advancement, restart,
   read-only previews and unauthorized DM/owner cases.
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

The approved architecture document
`character_state_inheritance_architecture_2026-11-14.md` defines S1–S15
as conceptual design cases. The working integration record refers to a
**separate original Increment 4 specification with 32 acceptance scenarios**.
That complete numbered list is not included in these two repository branches,
so the 32 cases must not be claimed individually completed or mapped
one-to-one from nearby tests without retrieving the original specification.
The existing test evidence supports synthetic orchestration, persistence,
conflict handling, reload and trusted transport, but not production supernatural
acquisition. The seven outstanding requirements in the preceding section
remain the implementation reconciliation checklist until the original
numbered specification is available.

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
| 6. Acceptance and concurrency/regression tests | Existing tests exercise synthetic Wolf/Hybrid inheritance, form switch, replay, conflicting transitions, immutable histories, resource ownership, origin/primary freshness, reload, source forgery and read-only behavior. Added post-plan revocation, non-native choice, skill/resource, currency and malformed-request cases. | **Partial:** original separately referenced 32-scenario acceptance document is not present on these branches or in the available project context. Tests must not be declared exhaustive against an unseen checklist; production acquisitions have not been exercised. |
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

