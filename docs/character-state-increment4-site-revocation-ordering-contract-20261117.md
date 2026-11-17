# Increment 4 — Site Revocation Ordering and Protected-Commit Contract

**Date:** November 17, 2026  
**Status:** **Behavior approved by project owner; protocol and implementation are design proposals, not yet approved or implemented.**  
**Context:** [Site-to-Character Sheet commit-consistency investigation](character-state-increment4-site-commit-consistency-investigation-20261116.md).  
**Baseline:** Character Sheet `b85ce9c4fce1660609552d774aa8028e7bad6df5`; Site `0eeaf232e2a3a11d4aac495c58b85ef6e3799a0e`; Rules Core `0d340c43248d2ae257302fe933fa712f82b1d045`.

## 1. Approved behavior and the distinction between request and effect

The owner selected the **second revocation-ordering model**. The following are requirements, not mere proposals:

1. Once a revocation request is durably registered, **no new conflicting protected operation may be admitted**.
2. A protected mutation that was admitted *before* the revocation request may **commit or be durably canceled**. Do not retrospectively claim that an already admitted operation was unauthorized simply because its commit happened after the request.
3. The revocation may be marked **Effective** only after every earlier conflicting admitted operation is proven `Committed` or permanently `Aborted`, **and** the underlying Site authority change is durable.
4. An unavailable or ambiguously responding Character Sheet leaves the revocation **Pending** until the previously admitted operations have durable outcomes. Site must not report pending as effective.
5. Any nonconflicting operations remain eligible under their existing authorization rules; this model does not authorize broad gameplay restrictions.

**User-facing semantics:** a revocation request means “new conflicting work has stopped”; revocation completion means “the change has taken effect and no prior admitted work can still cross that authorization boundary.” The pending state must be visible, recoverable and queryable. A success response that claims effective revocation must not precede the actual durable effect.

These requirements deliberately prioritize **safety over immediate revocation availability** when a previously admitted protected operation is unresolved. A request can remain pending across service outages.

### Scope of the behavior, not a new game rule

Apply the ordering mechanism to operations whose permissions derive from character ownership/lifecycle, active campaign association, campaign membership and DM role, tool-registration state, and live account identity, as applicable to the specific operation. The currently known protected writes are:
- Supernatural acquisition / manifestation transition via the verified Character State store.
- Direct initial-Origin activation.
- DM-approved mixed-Origin activation.
- Future source-authorized character mutations only after their dependency scopes are explicitly registered.

Rules Core stays the source of mechanics evaluation, **not** a Site identity or authorization database. Character Sheet continues to own its character data and immutable receipts. Site remains the authority for user, campaign, lifecycle, Tool and DM access. Existing read-time R1/R2 protections remain separately mandatory.

## 2. Precise ordering and invariants

Use two durable orderings:

- **Admission versus revocation request:** both must serialize against a Site-owned authorization gate for every overlapping dependency scope. If admission wins first, it is an older admitted operation. If revocation request wins first, admission is denied. An authorization check before obtaining this gate is not enough.
- **Protected commit versus effective revocation:** a Site revocation becomes effective only when there are no unresolved earlier conflicting admissions, and the actual Site authority mutation has durably completed. A Character Sheet `Committed` receipt means the gameplay write reached its own PostgreSQL commit; a durable `Aborted` tombstone means the operation can never commit later.

The following must hold:

**I1 — No new admission after request.** Pending scopes deny conflicting new admissions even while existing Site account/campaign/registration records still contain the old role or association. Pending-state checks must not rely on stale Tool Host introspection.

**I2 — No commit after effective revocation under old authority.** Every authorized write is tied to a uniquely admitted immutable operation. Before Site acknowledges effective revocation, every earlier conflicting operation is terminal. Later replays of an aborted operation cannot produce a commit; later new operations must be admitted under new authority generations.

**I3 — Commit and terminal identity are atomic.** A protected Character Sheet transaction writes the gameplay state, immutable event receipt, and an operation-specific `Committed` marker in **one** PostgreSQL transaction under the existing canonical character-root lock. An abort writes a terminal `Aborted` marker under that same lock domain and must race correctly with commit. Never infer abort from absence of a record.

**I4 — Site owns the admission decision.** No browser or Rules Core result can mint an admission. Delegated service identity, actor identity, operation identity, source tool, mode, exact target character/campaign, operation kind, and immutable command/evidence digest are bound together. A site-issued admission is authenticated and must not be transferred, edited, replayed with a different digest, or manufactured from a character snapshot.

**I5 — No orphan or ABA authority.** Restart, revocation then regrant, archive then restoration, campaign reconnect, actor reuse and registration re-enable cannot resurrect an admission from an older generation. A durable operation ID remains unique forever within the retention horizon; terminal tombstones remain enforceable for as long as admitted messages can arrive.

**I6 — Fail closed on ambiguous outcomes.** Timeout, lost acknowledgement, crash and Site/Sheet partition result in `Unknown` for the observer until the actual durable terminal result is retrieved or a durable abort fence is installed. Network success alone is not proof of database commit. Site may not garbage-collect unresolved admitted operations to clear a revocation.

**I7 — Revocation coverage is complete.** All paths that can revoke an admission dependency must respect the same gate, including administrative mutations and relevant account/tool registration paths. A direct update in another authority database can violate the guarantee unless coordinated. No assumptions about automatic atomicity across different EF contexts or PostgreSQL databases.

**I8 — Read authorization remains fresh.** R1's effective-state and inspection checks continue to use current Site authority. For protected reads that conflict with a pending revocation, the proposed Site policy is to **deny new read access immediately** while allowing revocation-status inspection. If a product exception is desired, it must be explicitly reviewed; the approved “block new operations” rule is not a license to expose private data through cached contexts.

## 3. Proposed protocol state machines

The following names, storage choices and API shapes are **proposed**, not approved interfaces.

### Site admission

`Admitted -> TerminalObserved(Committed | Aborted)`. An admission is immutable. An attempted admission against a pending revocation is `Denied` (not an `Admitted` record). A repeated request with the same operation ID and digest may return its existing state; changed digest/scope is a conflict.

### Character Sheet protected operation

`Absent -> Committed | Aborted`. Terminal states are irreversible; no transition from `Aborted` to `Committed` or the reverse. Multiple calls with the same identity and digest must resolve to the same durable outcome, not duplicate events. A different digest with an existing operation ID must be rejected.

### Site revocation

`Requested/Pending -> Applying -> Effective` or an explicitly recorded `Failed`/cancelled request under a separately reviewed policy. `Requested/Pending` blocks admission immediately. `Applying` continues to block it. `Effective` requires both durable settlement of all previously admitted conflicting operations and durable application of the underlying Site mutation.

A failed attempt to apply a Site mutation **must not silently unblock** protected work or report success. Pending recovery must be idempotent. Decisions about cancelling a revocation request itself require a separate authorization policy and are not presumed here.

## 4. Proposed Site and Character Sheet durable records

### Site coordinator (ownership boundary)

A coordinator-owned persistence domain needs, at minimum:

| Concept | Fields and constraints |
| --- | --- |
| Scope gate | Canonical scope key, monotonic authorization generation, pending-change barrier; unique scope identity; deterministic multi-scope lock order |
| Admitted operation | Server-generated operation ID; actor/character; optional campaign/role/association; source Tool key and mode; operation kind; versioned immutable digest; immutable scope-key set and generations; admission time; terminal observation; unique operation ID |
| Revocation request | Idempotency key, initiator, requested authority change and expected preconditions, affected scope keys, pending/active/effective status, requested/effective timestamps, observed settled operation IDs, recovery metadata |
| Reconciliation | Durable retries/outcomes and progress cursors; authenticated reference to Character Sheet's terminal records; no “success” from a missing receipt |

The Site coordinator must serialize **all overlapping scopes** in one authoritative gate before admitting a write or durably marking revocation pending. For multi-scope mutations, acquire locks in stable order and commit the pending barrier atomically. Role removal affecting many characters should use a campaign-role scope, not an unbounded best-effort per-character scan; global Tool disablement should use a Tool-wide scope. Actual dependencies must be enumerated for each mutation; do not accept caller-chosen scope keys as authority.

### Character Sheet terminal operation

Add a durable operation identity (exact schema deferred), associated with a scope/evidence digest and operation kind, plus a terminal `Committed` receipt reference or `Aborted` tombstone. The table and transaction must guarantee:
- A commit and terminal `Committed` marker are one atomic transaction with the existing Origin / supernatural state and receipt.
- A request to abort races by acquiring the same character-root row lock and writes an irreversible `Aborted` marker if the operation has not committed. Once a commit has won, abort returns `Committed`.
- A delayed, duplicated or reordered write consults the durable terminal marker **inside the same transaction** before any state change.
- Missing local marker, a Site-side receipt claim, or an HTTP acknowledgement is not itself authorization or evidence of an abort.

This record complements, not replaces, the existing immutable Origin and supernatural receipts, input fingerprints, CAS and native evidence binding.

### Proposed service surfaces

These are contract concepts rather than approved route paths:

1. `AdmitProtectedOperation(scope, operationId, commandDigest)` — privileged Character Sheet-to-Site request; returns immutable admission evidence or denial/pending.
2. `CommitProtectedOperation(admission, verifiedCommand)` — Character Sheet-only write boundary that atomically checks admission/terminal identity and commits state+receipt.
3. `GetProtectedOperationOutcome(operationId)` — authenticated, read-only Character Sheet terminal lookup; `Absent` means **unknown**, not aborted.
4. `AbortProtectedOperation(operationId, expectedAdmission)` — authenticated Site-to-Sheet fence; returns durably `Aborted` or `Committed`. Authorization and scope of the abort request are validated.
5. `RequestSiteRevocation(change, idempotencyKey)` — Site-owned entry point that atomically marks relevant gates pending; returns a durable request identifier. It **must not** claim effect yet.
6. `GetSiteRevocationStatus(requestId)` — distinct `Pending`, `Applying`, `Effective`, `Failed`/recovery-needed states, with appropriate actor authorization and no sensitive leakage.

All remote calls need explicit bounded transport, authenticated backend context, replay protection and cancellation propagation. Use existing Site delegation architecture where possible; never send a bearer capability to a browser or accept a browser-supplied admitted operation as proof.

**Digest binding:** define one versioned canonical serialization of the exact server-composed command, native Rules Core evidence identity, expected revision and underlying source inputs. Hashing arbitrary JSON strings without canonicalization or excluding a mutable source is insufficient.

## 5. Required execution order

### Protected mutation

1. Validate browser/user/session through the existing Site path; perform existing draft, Source, Rules Core and snapshot validation.
2. Construct the **exact** verified command and dependency-scope list; derive canonical digest. Do not admit broadly before native work is resolved.
3. Ask Site to admit the operation. Site serializes against all relevant pending revocations and checks live owner/DM/campaign/tool/account authority under its coordinator.
4. Character Sheet begins its existing PostgreSQL transaction and canonical character-root lock. Validate the admission binding, current command digest, existing operation-terminal record, input fingerprint, revisions, draft identity, and current native/provenance evidence.
5. Atomically persist state, event receipt and `Committed` terminal identity, or return a stable denial/conflict without a partial write.
6. Reconcile outcome to Site. Losing an acknowledgement does not cause a second application or an inferred abort.

### Revocation

1. Authorize the initiator; compute exact affected scope keys from authoritative Site state and requested change.
2. Durably mark those gates **Pending** using the same serialization boundary as admission. New conflicting admissions and proposed protected reads are denied from this point.
3. Inspect previously admitted conflicting operation IDs. For each, obtain authenticated durable `Committed`/ `Aborted` outcome or install a durable Sheet abort fence. **A timeout alone does not resolve it.**
4. After all older conflicting admissions are terminal, apply the actual Site role/lifecycle/association/deletion/registration/identity change, respecting the separate storage owner's transaction and idempotent retry semantics. Keep the barrier pending through any ambiguous partial application.
5. Confirm the underlying change durably, advance affected authorization generations and transition the revocation to **Effective**. Only then acknowledge completed revocation. Regrant/restore is a **new** change under a new generation.
6. If any step is unknown or unavailable, persist pending/recovery-needed state and retry; do not claim effective revocation or clear the barrier.

**Critical cross-database boundary:** the Site coordinator, Dorks & Dice EF context, Tool registry and Identity context may be different transaction domains. It is **not enough** to implement steps 2 and 4 in separate databases without governing all intermediate failure states and bypass routes. An actual design review must prove that a partly applied mutation cannot permit a conflicting new admission.

## 6. Failure and interleaving truth table

| Event | Required visible outcome and durability |
| --- | --- |
| Revocation is recorded before admission | Admission denied; zero new protected writes under old scope |
| Admission is recorded before revocation request | Previously admitted commit or durable abort can win; revocation stays pending until settled |
| Revocation requested during Rules Core evaluation before admission | Final admission denied; no Origin/supernatural write |
| Revocation requested during admitted PostgreSQL write | Commit may win; revocation must wait for durable receipt |
| Site loses commit acknowledgement | Query Sheet terminal; never assume failed commit from timeout |
| Sheet never received admitted operation | Site installs durable Sheet `Aborted` tombstone before completing revocation |
| Sheet unavailable while admitted operation unresolved | Revocation remains pending and retryable; new conflicting work remains blocked |
| Site crashes after barrier but before authority change | Barrier survives; recovery resumes without admitting a conflicting write |
| Underlying Site change commits, but effective acknowledgement fails | Barrier stays pending; recover actual authority state idempotently, then mark effective |
| Retry with same operation ID and digest | Returns original durable result; no new side effect |
| Retry with same ID but altered digest/actor/scope | Rejected as a conflict, even after regrant |
| Delete/regrant/restore/tool re-enable | Must advance generation and prevent old admission reuse |
| Nonconflicting character operation | May proceed; global Tool revocation remains an intentionally global gate |

## 7. Required implementation proof and open technical decisions

The above is a **behavioral contract and design target**, not a claim of available transactional plumbing. Before implementing, separately review:
- Canonical scope-key taxonomy (actor, character, campaign membership/role, association, Tool and account identity), immutable operation digest, and how authorization checks are serialized with Site database changes.
- Whether the coordinator's authoritative state lives inside the Dorks & Dice mode database or a dedicated Site authority store; the interfaces required to gate separate Tool registry and Identity changes.
- Terminal-record retention and delayed-message window, authenticated commit/abort channels, and recovery after unclean process termination.
- Exact read behavior during pending revocation, including privileged owner read-only inspection where applicable.
- Site response/status contracts and idempotency of revocation requests, including partial failure and explicit pending UI.
- Migration/backward compatibility and rolling deployment rules. A version in which some protected writers lack the new terminal check **must not** be declared fenced.
- A scope-consistent approach for all future character writes, rather than a transformation-only special case.

**Important test consequence:** the existing R3 negative tests assert `Applied` after a *synthetic immediate Site revocation*. With the newly approved deferred-effective semantics, the correct future test is not simply “every such write must fail.” If an operation **was durably admitted before** the pending revocation, it may commit, but revocation must not become effective until that commit or a durable abort is recorded. Tests must establish both linearization orders with **real Site and Sheet databases**.

**Acceptance remains unchanged:** WorkChat R1/R2 closed. R3 remains open until a real enforced contract, cross-service interleaving tests, independent retest, and authorized browser validation prove it. Original matrix: 23 complete / 9 partial. No merge or deployment authorization.
