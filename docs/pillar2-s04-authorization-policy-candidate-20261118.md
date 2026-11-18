# Pillar 2 — S04 Commit Authorization Policy Candidate

**Date:** 2026-11-18  
**Disposition:** **PROPOSAL ONLY — NOT APPROVED, NOT IMPLEMENTED**  
**Scope:** The DM-authorized native Human/Elf mixed-Origin activation vertical slice. Do not generalize to unrelated Site actions, other origination procedures, Feats, or new native mechanics.

## Decision being requested

**Recommended:** Treat a *precise, short-lived Site-issued commit authorization* as an irrevocable authorization snapshot **only within its stated deadline**, even if the DM is demoted or the target unlinked *after* Site issues it. A DM may not create a fresh approval after that revocation.

This is **not** a claim that the current 30-second Tool delegation capability is sufficient. Current behavior persists without any Site-issued per-Origin commit authorization, and S04 remains open. The [genuine Site/Sheet/Core revocation evidence](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44254425623) shows that a previously checked DM can commit after revocation. A policy decision and a new, auditable issuance protocol are still required.

The alternative is an **immediate-revocation-through-commit** contract. That requires a coordinated Site/Sheet lock, reservation, or fencing protocol, not a second call to the cached request context. It carries materially greater operational and failure-recovery cost for this narrow tabletop ruling.

## Source and responsibility boundaries

- **Site** is the sole authority for a current DM role, active campaign, active DM membership and active campaign-character association; `ToolHostAuthenticationContextFactory.BuildDmCharacterScopesAsync` recomputes live scopes for delegated contexts.
- **Character Sheet** owns the target draft revision and the durable Origin profile/event. `CharacterOriginMixedActivationService.ApplyAsync` validates selections and current native evaluation, but reads `ToolHostOriginDmAccessGateway` only at request entry.
- **Rules Core** owns eligible source characteristics, evaluation, the chosen native offers and the constructed profile. It must not mint Site permissions or infer parent-character provenance.
- The character root lock, draft lock and immutable Origin event are contained within **one Character Sheet PostgreSQL transaction**. The Site data store is separate. Neither proves authorization after the first Site check.

## Proposed policy: exactly bounded authorization at issuance

Site authorizes an exact action at **commit-permit issuance**. That issuance is the permission decision's linearization point. Site role revocation and target unlink take effect against permit issuance occurring *after* the revocation, but do not retroactively invalidate a previously issued, still-valid permit.

To minimize the authorized window, Character Sheet must request the commit permit **after** Rules Core has evaluated the final choices and **immediately before** the PostgreSQL commit. Request initialization and a delegated native Core check are neither the permit nor its issuance point. The selected policy would intentionally allow a valid permit to finish after its issuance even if Site revokes the actor within the bounded deadline.

### Required server-to-server issuance contract

The source is an authenticated Character Sheet server, not a browser. The Site endpoint must validate a source-tool capability tied to the already authenticated actor and registration, then re-read Site's current campaign/role/character-link state from its authoritative database. It must not trust actor identity, roles or permissions supplied in the request body.

A permit must bind, at minimum:

| Binding | Purpose |
| --- | --- |
| Nonce/authorization ID | Non-guessable unique issuance ID recorded durably in Site, never a browser-selected evidence ID |
| Actor, campaign, character, source-tool identity | Exact privileged subject and target |
| Origination rule/method and proposal ID | Exact mixed construction action and procedure |
| Draft revision and native ruleset fingerprint | Prevent later edits or native content changes from inheriting the approval |
| Canonical digest of selected native offer IDs **and** verified result/decision payload | Prevent substituting a different set of traits or constructed grants after issuance |
| Issued-at and expires-at | Explicit, short, server-enforced validity interval; suggested **15 seconds**, provisional |
| Authorization schema/version and issuer | Versioning, audit correlation and key or capability rotation |

The digest must cover a canonical representation with deterministic ordering and exact byte encoding. Do not hash arbitrary JSON property order. Include the permitted operation type and subject in the digest's domain separation. Site may store the payload digest rather than potentially sensitive full proposal data.

Issue and audit the permit transactionally with the **Site authorization read**. Ensure the issuance record and DM role/character association changes have a defined serialization order, rather than relying on concurrent unrelated snapshot reads. On SQLite, the issuing transaction must take an appropriate write reservation (for example `BEGIN IMMEDIATE`) before checking the active records and inserting the issuance row; confirm that the chosen EF/database approach actually holds the relevant lock. For other providers implement equivalent isolation and test it.

The permit must reach Character Sheet through the authenticated backend channel, not be accepted from an arbitrary browser field. A signed, audience-bound token or an opaque Site-issued ID with a trusted Site verification response are both possible designs. Specify the authenticity mechanism and key/secret rotation during implementation; an existing Tool delegation capability alone is **not** the approval.

### Character Sheet consumption contract

- Character Sheet must verify Site permit identity, scope, draft revision, fingerprint and canonical result digest against its own independently verified inputs.
- At the **database write transaction**, check expiry with the trusted clock, using a conservative allowance for clock skew. If expiry has passed, do not write any profile or event. If the operation can exceed a 15-second deadline because of row contention, fail rather than extending a permission implicitly.
- Use one stable Site permit ID as the immutable `AuthorizationEvidenceId`; do not generate a new `origin-dm:` GUID in its place. Store issuance time and expiry as auditable metadata.
- Enforce **global uniqueness of the issued permit ID** in Character Sheet PostgreSQL, consumed in the same transaction as the Origin profile and event. Current per-character event ID uniqueness alone is insufficient to assert global one-use consumption. Retried responses may read their previous receipt; they must never create additional grants.
- The Sheet transaction must continue to check the target's draft revision, any existing committed Origin and legacy selection conflict. A valid permit must **not** override any of those restrictions.
- Site's issuance audit may be reconciled with Sheet's final receipt asynchronously. Do not treat an asynchronous Site “consumed” flag as atomic with PostgreSQL. The unique consumed-ID constraint in Sheet is the authoritative one-use condition for this workflow.
- A lost response after a successful commit is resolved by reading the receipt tied to the permit ID, not by granting another permit automatically.
- Site unavailability, malformed permit, failed signature/verification, expired permit, unknown issuer, mismatched actor/scope or content hash, and ambiguous issuance must **fail closed** without an Origin write.

### What this policy intentionally does *not* guarantee

An actor revoked *after a valid issuance* may finish before that permit expires, even if Site now denies all fresh actions. A maximum 15-second authorization window is a **proposed policy guarantee**, not current behavior and not immediate revocation. The owner must explicitly approve that semantics. The actual Site-issued audit record is the reason such a later commit is authorized.

## Required acceptance matrix

| Case | Proposed authorization decision |
| --- | --- |
| Genuine Site DM and active target when the permit is issued; exact action and native result | Issue one scoped permit |
| DM removed before permit issuance, even if request entry and native evaluation passed | Deny issuance; no profile/event |
| Campaign-character link removed or character archived before permit issuance | Deny issuance; no profile/event |
| DM removed/target unlinked **after** valid issuance, before Sheet commit | Permit may commit if still valid and exact, with a completed audit |
| Permit expired while waiting for character/draft lock | Reject; zero profile/event |
| Same permit concurrently submitted or retried | At most one globally consumed receipt/Origin write |
| Owner edits draft, changes native catalog, or changes offer IDs after issuance | Reject; zero profile/event |
| Wrong actor/campaign/target, source Tool, signature/issuer, digest | Reject; zero profile/event |
| Site unavailable, timeout, malformed issuance or verification reply | Fail closed; zero profile/event |
| PostgreSQL commit succeeds but HTTP response or Site reconciliation fails | Read existing receipt; no duplicate effect |
| Site role revocation races issuance on SQLite and other supported stores | Deterministic issue-or-revoke ordering |
| Genuine Chrome, Site, Rules Core, Sheet and PostgreSQL | End-to-end results and immutable event evidence, no credentials in artifacts |

Test using the existing disposable three-service browser fixture with controlled barriers **before issuance**, **after issuance**, and **at the Sheet write**. The existing post-native-evaluation race currently commits without any Site permit and must be changed to **deny before issuance**; an **after-permit** race should be explicitly documented as allowed only under the approved snapshot contract. Do not weaken existing invalid-approval, draft concurrency, and native-catalog freshness cases.

## Alternative: immediate revocation through commit

If the required semantics are instead that a DM revocation or target unlink *must cancel even an already-running activation until the final storage write*, create a coordinated authorization reservation:

1. Site creates a reserved, actor/campaign/character-specific permit whose acquisition is serialized with Site role/association changes.
2. Site revocation or unlink cannot complete before the associated permit is released/fenced, unless the Sheet is already guaranteed unable to write under that reservation.
3. Character Sheet uses the reservation's fencing token during its PostgreSQL transaction and publishes final completion to Site.
4. Define expiry, process crash, network partition, retries, double-spend, and the case in which PostgreSQL commits but Site acknowledgement is lost.

A late Site recheck **without a reservation** still leaves a new check-to-commit window and is not an immediate-revocation protocol. A lease whose expiration does not fence late Sheet writes is also insufficient. This option is more complex, but is appropriate if immediate revocation is a hard security requirement.

## Proposed implementation sequence — conditional on approval

1. Specify the exact Site contract and issue/revoke serialization, trust boundary, short lifetime and canonical payload digest in a **Site** feature branch.
2. Implement Site durable issuance, proof validation, current-authority checks and targeted unit/integration tests.
3. Add Character Sheet server-side permit acquisition after native evaluation; bind it to the final result and actor; consume its globally unique ID atomically with the Origin event and snapshot.
4. Run all negative tests plus the genuine three-service barriers. Recheck both draft revisions and native fingerprint. Validate Site/Sheet process restart, network timeout and duplicate activation.
5. Update S04 from **OPEN-GAP** only after the agreed consistency policy passes independently and is documented. Keep PRs draft and do not deploy until approved.

## Recommendation rationale

For a narrow campaign DM adjudication, a **commit-time Site-issued, exact-action, 15-second authorization snapshot** is a reasonable default: the permission has a clear issuance moment; an operation already authorized can finish predictably; duplicates and stale drafts still fail; and the system does not need a cross-store distributed reservation. The cost is a deliberate and auditable post-issuance revocation window.

If the owner instead requires instant revocation of all pending actions, choose the reservation/fencing design despite its higher complexity. **This recommendation is not authorization to implement it.**
