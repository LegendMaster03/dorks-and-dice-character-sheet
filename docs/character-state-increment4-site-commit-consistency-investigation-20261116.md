# Increment 4 — Site-to-Character Sheet Commit Authorization Consistency

**Status: unapproved architecture investigation, not an implementation authorization.**
**Date:** November 16, 2026
**Affected systems:** Dorks & Dice Site authorization, Character Sheet PostgreSQL
Origin activation and supernatural state transitions; Rules Core remains the
mechanics evaluator.

## Evidence and target property

Independent WorkChat finding **R3** confirmed the known failure:
`CharacterStateCrossRepositoryTests.SiteRevocationAtCommitBoundaryIsNotFencedByEarlierFreshReads`
(in `ownership`, `archive`, and `campaign` cases) intentionally revokes the
synthetic Site authority at the Character Sheet persistence boundary. Even
after two successful earlier live Site reads, the actual PostgreSQL transaction
commits and returns `Applied`. The tests use real Rules Core HTTP and
PostgreSQL, but synthetic Site authorization, not a live Site database.

The production fresh endpoints provide independently authenticated
**read-time** owner and DM decisions. A Site-owned authorization revision
checked *only before* entering another database's commit is not atomic with
that commit. An expiring bearer token, repeated HTTP recheck, or a revision
snapshot alone does not fix this interval.

The missing property must be specified precisely: when ownership, archive,
campaign association, campaign membership/DM role, or tool-registration
revocation is **effectively ordered before** a protected Character Sheet commit,
that commit must not succeed. An operation authorized and committed before the
revocation's linearization point may succeed. User-visible revocation
acknowledgement and in-flight operation ordering must agree with this contract.

## Scope of an approved solution

- Site owns the current authorization decision, all mutation/revocation
  entry points, and the ordering of revocations. Character Sheet must not
  infer Site authority from stale introspection, ownership labels, a DM
  decision payload, or a local self-override.
- Protected operations include supernatural form activation/transitions,
  direct initial-Origin commitment, campaign-DM-approved mixed-Origin
  commitment, and any future source-authorized mutation.
- Match exact actor, character, campaign (where relevant), tool/source mode,
  operation ID, scope and immutable operation identity. Replayed or
  substituted proof must fail closed.
- Preserve existing Character Sheet PostgreSQL atomicity, CAS, immutable
  receipts, and idempotent retry behavior. Do not make Rules Core an
  authorization database or add a parallel Origination engine.
- Fail closed on Site outages, ambiguous transport, expired scope, process
  death and lost acknowledgements. Explicitly define reconciliation and
  whether a revocation is pending, effective, rejected or confirmed.
- Cover *every* Site revocation path (including registration disable), not
  only the campaign-character link endpoint exercised by tests.
- Preserve independently authorized archived reads. Read-time refreshing
  and commit-time mutation fencing are separate requirements.

## Candidate approaches to evaluate, not approved decisions

### A. Site-serialized durable operation arbitration

Site could register a scoped, idempotent mutation intent and serialize that
intent with revocations of its authorization inputs. A revocation must not
announce itself effective before all prior in-flight intents are known to
have committed or aborted. Character Sheet would record the operation ID
atomically with its state/receipt in PostgreSQL; Site would durably reconcile
that receipt. To avoid authorizing a commit *after* a completed revocation,
timeouts, leases, process crashes and uncertain acknowledgements require
an explicit, tested recovery protocol. **A short-lived permit without
revocation draining or atomic enforcement is not enough.**

Questions: How does Site prove an operation terminal after a lost commit
response? Can authorization revocation be safely completed when Character
Sheet is unavailable? What is the guaranteed linearization point of a
revocation that is requested while an authorized operation is in flight?
How are multiple actor/DM/campaign relationships serialized without
unrelated characters blocking one another?

### B. Transactionally enforced Character Sheet authorization fence

A Site-owned revocation sequence could be durably reflected inside the same
Character Sheet PostgreSQL transaction/lock domain that commits protected
mutations. Site revocations would have to fence Character Sheet writes
*before* declaring the revocation effective, with robust forward recovery
for partial cross-database updates and a complete inventory of every Site
authorization mutation. This must not turn the replicated fence into a
second independent ownership system.

Questions: How do the two databases recover from partial writes? How does
Site safely acknowledge a revocation if the Character Sheet fence service is
unavailable? How are later grants/unarchives/campaign transfers ordered with
revocations? What protects the fence against unnoticed Site mutation paths?

### C. Co-located transactional authority

Co-locating the actual authorization check and protected write within one
transactional database would provide a simpler strict ordering model, but
would be a major change to current service/data ownership and is not
authorized for this increment. Separate PostgreSQL databases on the same
server are not automatically one transaction.

## Rejected shortcuts

A third fresh HTTP read immediately before commit, an unsigned or
time-limited permission token, a snapshot revision without transactional
enforcement, polling for revocations, and tests using a permissive
supernatural authority provider do **not** establish the required guarantee.

## Required independent acceptance evidence after approval

Run tests against **real Site and Character Sheet databases**, with the
approved authorization protocol and independently authenticated Rules Core
HTTP. For every protected mutation, deterministically interleave:

1. Ownership revocation, archive, campaign unlink, DM membership/role
   removal, registration disable, and simultaneous other-character activity.
2. Revocation before authorization, during native evaluation, after the final
   fresh read but before the PostgreSQL write, and during ambiguous retry.
3. Site timeout, Character Sheet timeout, actor/session change, duplicated
   operation ID, forged receipt, app/process restart, expiration, and
   dropped final acknowledgement.
4. Competing writers and duplicate operation retries; confirm no partial
   Origin/profile/ledger/receipt writes and correct revocation ordering.

Keep the three current negative characterization cases as evidence of the
unfenced baseline until they are superseded by demonstrated real-Site tests.
Do not silently change the definition of `Applied` or recategorize the
original acceptance matrix on a design proposal alone.

**Decision needed before implementation:** Select the Site-owned operation
ordering and failure/recovery semantics, then approve the cross-repository
contract. No approach is selected or implemented in this document.


## Source-level revocation and commit-path audit

The following inventory was inspected on the pinned feature branches:
Site `0eeaf232e2a3a11d4aac495c58b85ef6e3799a0e` and Character Sheet
`2a63f9c5dccdfba7c13dc98033b62606ef071bae`. It records *reachable
service paths*, not a completed exhaustive inventory of direct database
administration or future extensions.

| Authority-changing operation | Current Site implementation | Serialization impact |
| --- | --- | --- |
| Character archive, restoration, campaign link/unlink | `Modes/DorksAndDice/Characters/CharacterService.cs`: `ArchiveAsync`, `RestoreAsync`, `ConnectToCampaignAsync`, `DisconnectFromCampaignAsync` | Affects owner write lifecycle and/or active campaign association. No owner-transfer method appears in `ICharacterService`; an ownership-revocation test using synthetic Site authority does not prove a production owner-transfer route. |
| Character/campaign deletion | `Modes/DorksAndDice/Lifecycle/DorksAndDiceDeletionService.cs`: `DeleteCharacterAsync`, `DeleteCampaignAsync` | Site deletes its record and records an outbox event in the same Site transaction. Asynchronous Tool lifecycle delivery is not an authorization fence. |
| Campaign archive/restore | `Modes/DorksAndDice/Campaigns/CampaignService.cs`: `ArchiveAsync`, `RestoreAsync` | Campaign archive ends character associations, affecting mixed-Origin DM scope. |
| Member removal, voluntary leave, DM/Player role changes | `CampaignService.cs`: `RemoveMemberAsync`, `LeaveAsync`, `SetMemberRolesAsync` | Active DM role and player membership/association are independent authorization dependencies; a DM-role change can revoke access for many character IDs. |
| Tool disable/delete or delegation policy change | `Controllers/DevelopmentToolsController.cs`: `Save`, `Delete`, using `IToolRegistry` | The tool registry has its own storage abstraction (`Services/Tools/DatabaseToolRegistry.cs`); do not assume its save/delete operation participates in the Dorks & Dice mode database transaction. Any global disable must serialize against **every** outstanding Character Sheet protected intent. |
| Account identity deletion | `Controllers/AccountController.cs`: `Delete` sets `DeletedAt`, a tombstone identity, and clears credentials in the identity context | The Site character owner row is retained. A separate audit must verify that live delegation/fresh authorization cannot remain valid for a deleted actor, and that identity-level changes join the same revocation ordering contract. |
| Campaign invitation acceptance | `CampaignInvitationService.cs`: `AcceptAsync` | Can create DM or player membership; mostly grant rather than revocation, but re-grant ordering matters for avoiding an ABA reuse of stale proof. |

The live owner and DM endpoints in
`Modes/DorksAndDice/Http/CharacterToolFreshAccessController.cs`
are **point-in-time Site reads**. They validate Site-issued delegation and
registration state. The DM decision performs a single relational predicate
over active campaign/association/membership/role. Neither endpoint reserves
commit authorization or serializes against all subsequent Site mutations.

### Current Character Sheet commit boundaries

| Operation | Last Site read | Actual protected PostgreSQL commit |
| --- | --- | --- |
| Manifestation transition | `CharacterManifestationTransitionService.ApplyAsync` calls `freshSite.GetFreshAuthorizedCharacterAsync` before its final cached projection comparison | `PostgresCharacterSupernaturalStateStore.CommitVerifiedAsync`: character-root `FOR UPDATE`, serializable transaction, input fingerprint, ledger/event CAS, state/receipt write |
| Direct initial-Origin activation | `CharacterOriginInitialActivationService` calls fresh owner and checks cached owner again | `PostgresCharacterAuthorizedOriginStore.CommitVerifiedAsync`: character-root and draft locks, source proposal/fingerprint comparison, Origin/receipt atomic write |
| Mixed-Origin activation | `CharacterOriginMixedActivationService` calls fresh DM and checks cached DM again | Same `PostgresCharacterAuthorizedOriginStore.CommitVerifiedAsync` path, with campaign/DM approval evidence |

All three services perform useful checks but leave an interval between
the last Site decision and their actual `CommitAsync`.
The native mechanics evaluator cannot close the Site authorization race.
The existing Site revocation-at-store test covers supernatural transitions
with real Core HTTP and PostgreSQL but synthetic Site ownership;
**equivalent direct and mixed-Origin live interleavings are not yet proven.**

## Recommended candidate: Site-owned durable intent arbitration

**Recommendation only, pending owner approval.** Of the existing candidates,
prefer a *durable, Site-serialized intent* rather than duplicating Site ownership
in Character Sheet or moving all Character Sheet storage. This best preserves
the current service ownership **if** delayed revocation acknowledgement during
an unresolved in-flight write is acceptable.

### Safety model

Define an authorized mutation's **linearization** as the durable Character
Sheet PostgreSQL commit. Define an effective revocation as occurring only
after every previously admitted, conflicting operation is durably confirmed
`Committed` or durably fenced `Aborted`. A revocation **request** may enter
`Pending` earlier; it must never be reported as `Effective` while an
earlier operation could still commit. A newly admitted operation must see a
pending conflicting revocation and be rejected. This orders commits relative
to *effective revocations* rather than treating an earlier positive Site
HTTP response as irrevocable authority.

Proposed invariants:

1. **Admission is Site-owned.** A reserved intent binds actor, character,
   optional campaign and DM role, tool-registration identity/mode, operation
   kind, operation ID, approved payload digest and an authorization generation.
   Site must validate the actual live predicates at admission.
2. **One operation, one terminal outcome.** Character Sheet stores a durable
   `Committed` receipt or `Aborted` tombstone for the exact operation ID
   in the *same canonical character-root transaction/locking domain* as
   Origin/supernatural mutation. A late or substituted commit after a terminal
   abort cannot succeed. An operation ID is not proof of entitlement without
   a correctly bound Site admission.
3. **Site controls revocation effectiveness.** Pending revocations prevent
   additional admissions. Site cannot mark a conflicting revocation effective
   until all admitted operations have terminal outcomes verified from
   Character Sheet. Any positive, unknown or timed-out answer must be
   reconciled using the exact durable receipt/tombstone.
4. **No unilateral timeout-based abort.** Site expiration of a reservation
   does not prove a delayed Character Sheet request can no longer commit.
   A terminal abort requires a durable Character Sheet fence, or a stronger
   atomic protocol proving that the operation cannot later cross its commit.
5. **Fail closed under partial failure.** If Site cannot communicate with
   Character Sheet to settle an in-flight operation, conflicting revocation
   remains pending. An unresolved intent must not be silently dropped.
   This sacrifices immediate revocation availability to preserve strict
   commit ordering. Status must be exposed rather than implied complete.
6. **All revocation routes participate.** Character/membership/campaign
   changes, deletions, tool registration disablement, and relevant identity
   invalidation must use the same admission/revocation ordering mechanism.
   Bypassing it at any service, administrator API or storage writer breaks
   the stated guarantee. Registration and identity storage are separate
   boundaries and require explicit integration, not assumptions about one
   shared EF transaction.

### Candidate sequence (not code or an approved API)

1. Character Sheet performs its current normal Site and Rules Core
   validation; it then requests a **durable scoped Site intent** immediately
   before the protected write, minimizing the time it must be held.
2. Site serializes admission against pending/effective revocation and
   returns a source-authenticated, non-substitutable intent identifier and
   digest. Duplicate requests with changed digest are rejected.
3. Character Sheet locks the root row, checks the admitted intent's exact
   scope and local terminal marker, commits the intended state and receipt
   atomically, and records the terminal operation identity.
4. Site records the authenticated commit acknowledgement. If the HTTP
   acknowledgement disappears, Site independently queries the durable
   Character Sheet terminal outcome; **absence of a receipt is not proof
   that a delayed request cannot still commit**.
5. A concurrent Site revocation enters `Pending`, halts conflicting new
   admissions, and must either observe prior intents `Committed` or secure
   durable `Aborted` fences in Character Sheet. Only then may Site
   make the underlying authority change effective and acknowledge it
   to its caller.
6. Retries, process restarts, duplicate messages, and subsequent re-grants
   must not resurrect an older intent. Strong operation identity and a
   monotonic scope generation (or equivalent fencing semantics) are needed.

This is **not** an assertion that each step can be made atomic with the
existing tables. A design phase must specify the Site durable intent
journal, Character Sheet terminal-operation table/receipt association,
authenticating the Site service-to-service channel, admission races with
multi-scope revocations, and exact transactional sequencing. For example,
a Site journal on the Dorks & Dice mode database does not automatically
serialize with the existing tool registry or Identity database writes.

### Primary alternatives and tradeoff

| Candidate | Advantage | Blocking cost |
| --- | --- | --- |
| **Durable Site intent and revocation drain** (recommended for design) | Keeps native mechanics in Core, gameplay state in Character Sheet, and live authority in Site | Revocation can remain pending while Sheet is unreachable; every Site invalidation route must be integrated and terminal proof must be durable |
| Replicated authorization fence in Character Sheet | Local commit can check fenced Site generation atomically with its mutation | Site must propagate the fence **before** acknowledging revocation, reconcile partial delivery, and prevent another Site grant/revocation from bypassing it; replica becomes security-critical |
| Co-located authorization and mutation transaction | Stronger and conceptually simpler atomicity | Significant data-ownership or service topology change, especially with separate registration/identity stores |

### Decision gate before implementation

Obtain explicit approval for **both**:

- **Ordering semantics:** Is it acceptable that a request to archive,
  unlink, remove a DM, delete a character, or disable a Tool can remain
  visibly `Pending` until previously admitted mutations are durably
  settled? If the user requires immediate effective revocation even when
  Character Sheet is unavailable, candidate A is incompatible with that
  availability requirement.
- **Coordination scope:** Must account deletion, global Tool registration
  disable/delete, campaign changes, and future ownership-transfer paths
  all be part of the same strict fence? For the stated comprehensive
  authorization guarantee, **yes**. Any intentional exception must be
  explicitly defined as a narrower property, not assumed away.

After this decision, prepare separately reviewed contracts, migrations,
idempotence/recovery diagrams, and real Site + Character Sheet database
interleaving tests. Do not install stubs claiming the contract already works.

**Current status:** R1/R2 closed by independent WorkChat follow-up; R3
still open. This source audit and candidate protocol are design-only.
The original 23-complete/9-partial acceptance matrix and no-merge
recommendation are unchanged.
