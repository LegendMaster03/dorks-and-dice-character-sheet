# Increment 4 — R3 Cross-Service Revocation Implementation Plan

**Date:** November 17, 2026  
**Status:** Detailed plan for review. The project owner **approved the deferred-effective revocation behavior**, not this implementation, API, schema or merge.  
**Companion:** [Approved behavior and proposed Site revocation ordering contract](character-state-increment4-site-revocation-ordering-contract-20261117.md).

## Goal and non-goals

Close R3 across direct initial-Origin, DM-approved mixed-Origin and supernatural acquisitions/transitions while preserving Rules Core as the mechanics authority, Site as the identity/campaign/Tool authority and Character Sheet as gameplay-state owner.

**Not part of R3:** finalizing supernatural acquisition or progression rules, making full Origin reconstruction authoritative, merging the Pillar 2/Pillar 4 testing self-overrides, changing player-facing species balance, or declaring Increment 4 playable/merge-ready. Those remain independent acceptance blockers.

## Dependencies and branch rules

- **Character Sheet:** `LegendMaster03/dorks-and-dice-character-sheet`, feature branch `feature/character-state-inheritance-increment4-20261114`; reviewed design baseline `b85ce9c4fce1660609552d774aa8028e7bad6df5`.
- **Site:** `LegendMaster03/dorks-and-dice-site`, existing `feature/site-fresh-character-authorization-increment4-20261116` for contract review; **create a dedicated R3 branch before editing implementation in more than three files**. No modifications to `main`.
- **Rules Core:** `LegendMaster03/dorks-and-dice-rules-core`, `feature/character-state-mechanics-adapter-increment4-20261114` at `0d340c43248d2ae257302fe933fa712f82b1d045`; **read-only unless a concrete compatibility requirement is found and separately approved**.
- Coordinate schema contracts and test fixtures by pinned SHAs; batch coherent commits by layer. No merge, staging deployment or production changes without explicit authorization.

## Gate 0 — Protocol review (required before implementation)

Produce an agreed service contract and architecture record that answers the following without ambiguity:

1. **Scope locking:** which Site coordinator keys protect owner operations, DM operations, active associations, campaign-wide role changes, Tool-wide disablement and account deletion? Which nonconflicting character operations may continue?
2. **Storage ownership:** how will a Site coordinator gate serialize with authority changes in the Dorks & Dice EF database, Tool registry and Identity database? Identify every bypass and administrator path. Favor a single Site-owned gate, but prove all writers respect it.
3. **Digest/capability:** specify exact immutable server-built operation fields, canonicalization version, backend identity validation, admission issuance and rejection of replay/forgery. Reuse existing Site delegation where possible.
4. **Commit and abort:** define Character Sheet terminal journal, lock order, uniqueness, pre-commit verification, durable `Committed` and `Aborted` markers, and idempotent retries.
5. **Recovery:** define dropped HTTP response, crash before/after each commit, unreachable Sheet, ambiguous Site mutation, retention and eventual regrant with changed authorization generation.
6. **Status/UI:** determine which revocation requests return pending, which API response confirms effective, what callers see during outages, and what protected reads can see while pending.

Output: reviewed protocol ADR, schema/API examples, sequence diagrams and adversarial interleaving matrix. The chosen protocol is **not implementation-authorized** merely by completing this document.

## Gate 1 — Site authorization inventory and durable coordinator

**Repository:** Site (new R3 feature branch).

1. Inventory all actual authorization invalidation and grant paths, including:
   - `CharacterService`: `ArchiveAsync`, `RestoreAsync`, `ConnectToCampaignAsync`, `DisconnectFromCampaignAsync`.
   - `CampaignService`: archive/restore, member role edits, leave and removal.
   - `DorksAndDiceDeletionService`: permanent character/campaign removal and outbox interactions.
   - `DevelopmentToolsController` / `IToolRegistry`: Tool disable, delete and relevant policy edits.
   - `AccountController`: identity tombstone and account invalidation.
   - `CampaignInvitationService`: membership/role regrant during invitation acceptance.
   - All service registration, alternate controller, scheduled task, migration/admin and direct SQL writers found in a full repository audit.
2. Add durable scope gates, admitted intents and revocation request records with schema migrations and unique constraints. Define lock ordering and transactional barriers to prevent admission/revocation races.
3. Implement Site-internal domain services for atomic admit and request-pending operations, including idempotency and generation checks. **Do not yet claim completed revocation** until the coordinated commit/abort service exists.
4. Add isolated Site PostgreSQL tests for admission versus pending and multi-scope concurrency, role/membership and registration changes, retry/recovery, and no unrelated character blocking.

**Gate:** no external route may advertise a security guarantee until the Site store and all relevant state-change writers are integrated.

## Gate 2 — Character Sheet commit/abort terminal

**Repository:** Character Sheet R3 feature branch, coordinated with Site contracts.

1. Add operation-terminal persistence and a schema migration. Keep a single immutable operation identity under the existing per-character root lock.
2. Update both `PostgresCharacterAuthorizedOriginStore.CommitVerifiedAsync` and `PostgresCharacterSupernaturalStateStore.CommitVerifiedAsync` to require a **verified, bound Site admission** for production protected writes. Keep existing snapshot CAS, draft/proposal fingerprint, native evidence, immutable receipts, history guard and serializable transaction rules.
3. Make commits and terminal records atomic; add the distinct `Abort` transaction under the same row lock. A `Committed` outcome prevents abort from winning; `Aborted` prevents any later commit.
4. Add a service-only, authenticated terminal-outcome lookup and abort surface for Site recovery. Do not expose it as an unprivileged browser mutation.
5. Preserve existing test-only synthetic authorities **without allowing a production bypass**. Tests that still use internal legacy commands must be named and gated so they do not accidentally certify the new protocol.

**Gate:** PostgreSQL tests demonstrate commit-vs-abort mutual exclusion, duplicate idempotence, no partial gameplay/receipt writes and recovery after connection loss. No Site revocation success is yet implied.

## Gate 3 — Wire the three protected write paths

**Repositories:** Character Sheet and Site.

1. After existing Core/native validation, build the final exact command and request Site admission via an authenticated backend channel. Do not substitute old cached owner/DM context.
2. Bind exact owner/DM/campaign/source evidence, operation identity, command digest and current authorization generation to the admission.
3. Verify the admission at the Character Sheet commit boundary; reject mismatched or missing proof, including direct internal callers that lack production authority.
4. Record terminal commit outcome and reconcile it to Site. Add durable outcome retrieval for lost acknowledgements and caller retries.
5. Require pending Site scope barriers in the existing fresh-read paths for private read security; preserve existing archived owner read-only semantics under the approved read policy.

**Gate:** site-requested revocation before admission always blocks; an operation admitted before the request can finish, but not after an effective revocation. Tests must distinguish `Pending` from `Effective`.

## Gate 4 — Route and settle all Site revocations

**Repository:** Site, with Character Sheet recovery endpoints.

1. Each authority-invalidating path first enters the shared **pending** admission barrier for affected scopes, with idempotent request identity.
2. A reconciler enumerates earlier admitted operations, obtains durable terminal results or installs an irreversible Sheet abort fence.
3. Once all affected operations settle, apply the actual Site state change under the corresponding owning data store. If this crosses Identity/Tool registries, retain the gate until a durable, independently verifiable applied outcome is known.
4. Only then advance scope generation and mark the revocation effective. A failed/outaged path persists pending, surfaces recovery-needed status, and never silently unlocks admission.
5. Add explicit UI/API pending behavior; do not issue a success message for changes still waiting on prior operations. Confirm protection during Service restart and Site/Sheet partition.

**Gate:** no bypass through archive/delete/role edit/registration state/identity deletion; nonconflicting scopes retain normal availability.

## Gate 5 — End-to-end acceptance and independent validation

Use **two real isolated PostgreSQL authority/state stores**, actual Site and Character Sheet service hosts, and authenticated real Rules Core HTTP. The baseline negative R3 characterizations must remain as historical diagnostics but cannot be treated as the final acceptance oracle.

| Case | Required acceptance result |
| --- | --- |
| Revocation request wins admission race | Admission rejected; no protected write |
| Admission wins revocation-request race | Write may commit or be durably aborted; revocation stays pending until terminal |
| Revocation during native evaluation, before admission | No write; denied admission |
| Revocation while a PostgreSQL commit is stalled | Pending until terminal; never effective early |
| Commit wins abort on same operation | Exactly one commit and receipt, abort returns committed |
| Abort wins commit | Durable tombstone, no state/receipt write ever |
| Duplicate submit or altered digest | Idempotent original result or conflict; never a second write |
| Lost Site outcome acknowledgement | Site queries Sheet terminal; cannot infer abortion |
| Site crash after barrier, before status update | Pending survives restart; recovery resumes |
| Sheet outage during pending revocation | Block new conflicting operations, visible pending, no false effective |
| Site mutation committed, acknowledgement lost | Reconciled from actual authority state; barrier not dropped early |
| Account deletion, Tool disable, campaign archive, DM role removal, unlink, delete | Each path participates in the shared gate and eventual effective ordering |
| Delete then recreate/regrant/restore | New generation; old command/admission cannot become valid |
| Unrelated active character | Can continue under nonconflicting scopes |
| Malicious actor, forged digest, wrong Tool/campaign, stale capability | Fail closed without revealing private character data |
| Concurrent direct Origin, mixed Origin, supernatural transition | No bypass; preserved character-root CAS and immutable receipts |
| Conditional CI / process restart / migration upgrade and rollback | Zero unintended regressions; exact test counts and run IDs recorded |

Independent WorkChat should **rerun** implementation-specific tests and review each interleaving, not merely inspect prior green CI logs. Browser/player/DM acceptance needs a separately owner-authorized isolated deployment. Do not use production to substitute for undeployed branches.

## Gate 6 — Merge readiness review

Only request merge approval after:
- Gate 0 contract and protocol approval is recorded.
- No unfenced protected production path or Site revocation mutation path remains.
- Durable crash/retry/abort recovery and live two-database interleavings pass.
- Exact branch SHA, CI outcomes, conditional suites and security review are recorded.
- WorkChat performs an independent retest against the implementation and identifies any residual gaps.

**Do not conflate R3 security closure with completed Increment 4 acceptance.** Production supernatural acquisition/progression, authoritative Origin Underlying and form-specific Class/HP/resources/equipment behavior, and browser acceptance are separately unresolved. The original 32-scenario baseline remains **23 complete / 9 partial** until independent evidence justifies changes.

## Immediate authorized next step

This plan and the behavioral contract are documentation work authorized by the owner's selection of delayed-effective revocation. **Implementation awaits review/approval of the protocol, schemas and integration boundaries.** No migrations, service endpoints, Site changes, Rules Core changes, merges or deployment are authorized by this document.
