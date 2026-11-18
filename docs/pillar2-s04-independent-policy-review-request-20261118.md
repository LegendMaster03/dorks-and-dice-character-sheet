# Dorks & Dice — Pillar 2 S04 Independent Authorization Policy Review

**Date:** November 18, 2026  
**Assignment:** Independent security/architecture design review and adversarial acceptance-plan assessment. **Do not implement production authorization, merge, deploy or modify `main`.** This prompt does not approve a policy.

## Scope and source revisions

Study the actual sources and the complete existing design candidate, not just this prompt:

- Character Sheet: `LegendMaster03/dorks-and-dice-character-sheet`, branch `feature/innate-spell-ledger-persistence-20261116`; review the HEAD of that branch (at preparation, `5916bcb9c6bbfcf103fad3fd0484bac72d6f74bc`).
- Site: `LegendMaster03/dorks-and-dice-site` at `e8d0cb7d89de71aa337293b8f36d22bac98687c3`, branch `feature/pillar2-origin-dm-campaign-authority-20261112`.
- Rules Core: `LegendMaster03/dorks-and-dice-rules-core` at implementation SHA `23919c3ce00825f2da6204f218786957e9f1f1c2`, branch `feature/pillar2-power-evaluation-prototype-20261114`.
- Primary policy candidate: `docs/pillar2-s04-authorization-policy-candidate-20261118.md`.
- Current S04 evidence and design boundary: `docs/pillar2-inflight-dm-authorization-consistency-20261117.md`.
- Service implementation: `src/CharacterSheet.Application/Characters/CharacterOriginMixedActivationService.cs`.
- Persistence implementation: `src/CharacterSheet.Infrastructure/Persistence/PostgresCharacterAuthorizedOriginStore.cs` and `src/CharacterSheet.Infrastructure/Persistence/CharacterSheetDbContext.cs`.
- Source authority: `dorks-and-dice-site/Services/Tools/ToolHostAuthenticationContextFactory.cs`, `dorks-and-dice-site/Services/Tools/ToolDelegationCapabilities.cs`, `dorks-and-dice-site/Modes/DorksAndDice/Campaigns/CampaignContextService.cs`.

## Existing evidence: no need to rediscover

In [CI #44254425623](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44254425623), an actual Site DM was revoked after successful real Rules Core evaluation, while the old activation was paused; Site denied a fresh DM review (404) and the original operation still committed an Origin and exactly one event receipt. In [CI #44257608305](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44257608305), a campaign-character link was removed in the actual Site store while a DM remained authorized; a fresh review was denied and the held operation again committed. These are **successful reproductions of an S04 security acceptance gap, not accepted behavior**.

The supported browser slice is passing; Feats, skill advancement, balance, other origination methods, and unrelated mechanics are deferred. Do not expand their scope.

## Decision under review

Candidate A: a precise, short-lived, globally one-use **Site-issued authorization snapshot**, requested after successful Rules Core evaluation and immediately before Character Sheet's PostgreSQL commit. Revocations before issuance prevent authorization; revocations after valid issuance do not retroactively cancel that exact action until the stated expiry.

Candidate B: revocation must prevent commitment until a cross-service serialized commit boundary. This requires a coordinated reservation or fencing mechanism shared with Site role/association mutation.

The candidate document recommends A for this narrow DM-adjudication operation, with a provisional 15-second lifetime. **Neither semantics nor lifetime has been approved.** Review both independently; do not infer the user's choice from the document or from this review request.

## Required adversarial assessment

1. **Who can issue a permit?** Trace the actual Site ticket, source-tool delegation capability, actor identity, registration, and DM target projection. Show whether a request-scoped capability expiring after 30 seconds is sufficient for post-evaluation issuance, what to do when Core evaluation outlasts it, and how an attacker could exploit any actor-supplied request field. Distinguish an identity witness from a commit authorization.
2. **Serialization with revocation.** For the actual Site database and repository APIs, show how Site permit issuance and all revocation operations (DM-role changes, member removal, campaign archive, character archive, unlink) obtain a consistent serialization order. Merely reading a projected role before inserting a permit is insufficient. Evaluate the proposed SQLite write-reservation mechanism and any provider-specific/multi-instance issues.
3. **Exact binding and evidence ordering.** Compare the current Core evaluation correlation ID `origin-dm:` and response `approvalEvidenceId` to the future Site-issued permit ID. Check the action/draft/actor/campaign/target/fingerprint/offers/result digest fields and how the Sheet receipt can truthfully reference both identities without rewriting Core-verified history.
4. **Expiry under contention.** Trace the Sheet root and draft locks and final write. Identify the clock sample/SQL predicate needed to reject permits that expire while waiting. Specifically assess PostgreSQL `now()` versus `clock_timestamp()`, clock-skew bounds, and whether the contract refers to authorization *at a defined point* or actual WAL commit time.
5. **Exactly-once across characters and deletion.** Check current key constraints on `character_origin_event_receipts`, the cascading character foreign key, and any existing uniqueness of authorization evidence. Specify a minimal replay ledger or partial index with a verifiable retention policy. Analyze duplicate concurrent activation and lost Site/Sheet responses.
6. **Native rules version policy.** Distinguish current Core evaluation fingerprint verification from a globally latest catalog guarantee. Decide what could be enforced with a pinned snapshot versus new Core coordination; do not claim a catalog can not change between independent transactions.
7. **Failure mode and recovery.** Site slow/unavailable, lost issuance response, expired tool delegation, malformed or tampered permit, partial Site audit write, DB transaction timeout, actor/campaign/character mismatch, retired actor, unexpected multi-node configuration, and stale frontend replays must fail closed without a new Origin.
8. **Minimal cross-repository change plan.** Identify exact new interfaces, schemas/migrations, server endpoints, dependency flow and audit evidence. Keep the native Human/Elf slice narrow. Note which files would change, where branches are needed, and which existing tests remain authoritative.

## Required test design and verdict

Propose deterministic, disposable automated tests that cover both policy A and B's key discriminators. At a minimum, specify barriers before/after Site permit issuance, before/after receiving real Core evaluation, after PostgreSQL locks, at concurrent permit submission, and during Site revocation; include DM-role revoke, unlink, archival, expiry, native fingerprint mismatch, replay across different characters, deletion/recreation, clock skew, network failures, and migration downgrade/rollback. For policy A, explicitly distinguish *allowed* post-issuance completion from *denied* pre-issuance revocation.

Produce a decision matrix separating: (i) confirmed current defects, (ii) gaps in the unapproved candidate, (iii) implementation constraints, and (iv) remaining owner policy choices. State a clear conditional recommendation for A or B with the strongest objection to your preferred choice. Do not mark S04 accepted or suggest that green diagnostic CI indicates the production permission protocol is secure.

**Deliverable:** a standalone Markdown review with source locations, factual evidence, failure cases, minimal proposal corrections and acceptance prerequisites. Review only. Do not modify application code or PR state.
