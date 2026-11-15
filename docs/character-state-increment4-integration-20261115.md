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
The event table retains event IDs, state/ledger revisions, event kind, and
authority-evidence reference. Complete event-step auditing is not yet
implemented; a referenced string is not its own proof.

### Transaction boundary

`PostgresCharacterSupernaturalStateStore.CommitVerifiedAsync` accepts only
an internal application-layer command. It validates bounded JSON structure,
ledger character ID and revision, duplicate owned IDs and event identity.
Inside a PostgreSQL serializable transaction it locks the canonical character
root, compares a digest of authoritative Class, Origin, ability, rules, HP,
condition, inventory and other relevant input rows, compares supernatural
state and ledger revisions, writes the new state, inserts the unique event
receipt and commits. PostgreSQL serialization failures are returned as
conflicts, including exceptions wrapped by EF Core.

The application must obtain its digest from the current authoritative rows,
run native planning using the verified values, then commit with exactly that
digest. The DB transition has atomicity, CAS and durable idempotency
constraints. This does **not** establish that the proposed result was
mechanically valid or independently authorized. A production transition
service must prove both before calling this store. Any new material
dependency must be added to the digest before enabling live transitions.

### Read-only transport

Character Sheet now exposes
`GET /api/characters/{characterId}/state/effective`. It uses the established
Site-authorized Build, State and Origin services to assemble a primary
character and sends a delegated server-side request to Rules Core
`POST /api/mechanics/v1/characters/effective-state-baseline`.

Rules Core checks the existing Site tool-to-tool ticket and accepts **only**
an empty supernatural ledger with no alternate sheets or influences. It
requires the current native ruleset identity and calls the existing
manifestation projection mechanics under a strict primary-only authority.
It explicitly rejects forged supernatural events, instances and stored
grants. The Character Sheet consumer checks the response metadata and
rechecks authoritative input digest after the remote calculation.

Every returned read is `partial` or `unresolved`, never
`effectiveMechanicsReady: true`. Origin Underlying grants, Class/Origin
entitlement interaction, equipment, resource and Hit Point policy are not
yet fully wired into this evaluator. Persisted supernatural state is **not**
sent to the primary-only evaluator and remains unresolved. An archived
character may read but does not gain mutation rights.

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
  concurrent input mutation and read-only projection.
* Core host tests cover delegated baseline, missing/wrong ticket,
  content mismatch and forged event ledger refusal.

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
3. Add a delegated Rules Core endpoint for full verified manifestation
   planning and resolution using the existing Core implementations and an
   actual verifying authority; no independent character engine.
4. Connect the authorized complete plan to `CommitVerifiedAsync`, proving
   exact provenance, source versions, effects, interaction decisions and
   resource ownership; persist complete transition-step audit data.
5. Build a proper primary state assembler from the verified latest
   `NativeOriginProfile.Underlying`, independently owned Class/Background/
   Upbringing entitlements, trained skills, inventory and resource facts.
   Distinguish unresolved support from playable numerical mechanics.
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
