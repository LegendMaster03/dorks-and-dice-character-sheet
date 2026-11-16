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
