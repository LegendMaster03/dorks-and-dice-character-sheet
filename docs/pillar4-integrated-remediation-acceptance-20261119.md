# Pillar 4 — integrated remediation acceptance record

**Date:** November 19, 2026  
**Repositories:** `LegendMaster03/dorks-and-dice-rules-core` and `LegendMaster03/dorks-and-dice-character-sheet`  
**Branches:** `integration/pillar4-infrastructure-remediation-20261119` and `integration/pillar4-character-state-remediation-20261119`  
**Pinned Rules Core:** `e277b79a49a44ad27da5c352ffdad263985a3bbf`  
**Disposition:** **Partially complete. Not approved for merge or deployment.**

## Scope and accounting

The work covers Pillar 4 acquisition/progression (A), effective-character composition and source-qualified overlaps (B), and Character Sheet persistence/integration (C). First-party transformation content (D) and a unified Origin UI (E) were explicitly deferred to separate passes. Successful synthetic tests are not permission to register any playable supernatural entitlement.

| Finding | Implemented evidence | Closure |
| --- | --- | --- |
| A1 — Source-authored effects have one structural validation policy | `NativeCharacterInfluenceResolver.ValidAuthoredEffect`, `NativeSupernaturalProgressionPlanner` and malformed-effect regressions | Implemented at native library level |
| A2 — Progress despite unrelated effect collisions | `ValidateState` independently verifies stored ownership without demanding resolved mechanics | Implemented at native library level |
| B1 — Sheet-scoped overlap decisions | Required `TargetSheetId`; resolver matches exact active writer set on the selected sheet | Implemented at native library level |
| B2 — Source-authorized overlap creation and replacement | New `NativeInfluenceOverlapDecisionPlanner`, independent Core `INativeInfluenceOverlapHostAuthorityProvider`, backend Sheet coordinator and strict synthetic cross-repository test | Implemented for test-only independently verified authority; no production source provider |
| B3 — Fully playable effective mechanics | Typed numeric/condition/earned-grant, expression and partial-form mechanisms already exist | **Partial**; specific rules and contracts below remain unresolved |
| C1 — Canonical acquisition/progression planner | Version-2 `/characters/supernatural/ownership/plan`, independently verified host provider, exact request/intent/revision checks | Implemented; test-only independent source |
| C2 — Durable progression journal | Serializable PostgreSQL CAS, consumed eligibility history, immutable receipts, reload and tamper tests | Implemented at storage boundary |
| C3 — Durable overlap decisions | Separate version-1 `/characters/supernatural/overlaps/plan`, `overlap-decision` receipt kind, same-collision-only replacement, restart/forgery/concurrency checks | Implemented at storage boundary, no production source policy |
| C4 — Full combined lifecycle | Individual real Core HTTP/PostgreSQL acquisition, advance, overlap replacement, manifestation/reversion and read scenarios; native combined advancement-after-overlap regression | **Partial**; one orchestrated acquisition-to-overlap-to-form lifecycle is not complete |

The original requested **20 integrated acceptance scenarios** must not be marked complete from the above independent slices. The older Character State Increment 4 **32-scenario** matrix is separate and retains its own outstanding production-authority cases.

## Verified overlap-decision protocol

1. Character Sheet checks fresh and scoped Site access, lifecycle, committed supernatural and manifestation revisions, authoritative Class/Origin/ability inputs and source evidence.
2. The backend-only `ITrustedCharacterInfluenceOverlapDecisionSource` supplies an event- and intent-bound source assertion; there is no production registration or public mutation route.
3. Rules Core independently resolves `INativeInfluenceOverlapHostAuthorityProvider` and verifies a new `NativeInfluenceOverlapSelection` and optional superseded decision against the current character, source and actor. An absent provider rejects the operation.
4. The native planner validates all preexisting influences and decisions, requires a unique new decision ID, restricts replacement to the exact same sheet and effect-ID set, rejects duplicate scope and checks both original and proposed decisions with `INativeCharacterInfluenceOverlapAuthority`.
5. Character Sheet accepts only a correlated `committed: false` plan, rechecks Site and authoritative dependencies, and writes a standalone `IsOverlapDecision` operation under its serializable character lock.
6. The PostgreSQL guard allows only the stated one-decision append or same-scope replacement. It preserves owned influences, progression, manifestation ledger and unrelated decisions and stores an immutable `overlap-decision` snapshot receipt. Subsequent commits reject missing or corrupted historical receipt chains.

The synthetic host verifier reads the persisted owner, current primary scores and Class occurrences from a separate PostgreSQL context; it does not echo the caller's decision or establish production table/DM permission. Reconstructed decisions still require independent verification on mechanical reads.

## Executed acceptance slices and tests

- Native: `SeparateVerifiedOverlapEventAddsThenSupersedesExactWriterWithoutChangingOwners`, `OverlapEventRejectsUnauthorizedDecisionAndCrossScopeReplacement`, `IndependentlyVerifiedOverlapDecisionRemainsOwnedThroughFurtherAdvancement`, and existing expression/sheet/manifestation composition suites.
- Native HTTP: `VerifiedSupernaturalOwnershipHttpPlansAcquisitionAndAdvanceButRejectsReplayedEvidence`.
- Character Sheet contracts: `OverlapPlanRequiresExactIdentityAndImmutableDecisionEvidence`, `OverlapSourceMustBeBoundToTheExactOwnedCharacterAndIntent`, plus ownership-plan correlation tests.
- PostgreSQL: `OverlapDecisionsCommitSeparatelyAndSupersedeOnlyExactScopeAfterReload`, `HistoricalOverlapSnapshotCorruptionRejectsSubsequentCommit`, `ConcurrentOverlapWritersCanNotBothCommitTheSameSupernaturalRevision`, `OwnershipProgressionReceiptChainRejectsLostEligibilityAfterDatabaseReload`.
- Real Core HTTP + PostgreSQL: `SyntheticSupernaturalOwnershipAcquiresAndAdvancesThroughRealCoreAndPostgres`, `IndependentlyVerifiedOverlapDecisionSurvivesRealCoreHttpAndPostgresRestart`, and existing independently authenticated form entry/reversion, composite form, Origin and Class inheritance tests.

Each assertion is scoped to its actual synthetic fixture. An independently verified Core plan remains a proposal until the Character Sheet commit succeeds.

## Outstanding acceptance and design gates

1. **Combined lifecycle:** One character acquisition of multiple independently verified effects through the Core ownership coordinator, subsequent overlap decision, expression change, form activation and reversion, Class/Origin changes, persisted reconstitution and conflicting writes must be exercised in a single authenticated acceptance sequence. Existing targeted suites do not substitute for that scenario.
2. **Source authority:** Independently authenticated production first-party acquisition/progression, DM scope and source-rule ownership are not installed. The absence is intentional while first-party content remains deferred; synthetic providers must never be registered in production.
3. **Mechanical policy:** Creature type/targeting; non-walking movement modes; action and speech capabilities; form-specific Class feature and spell use; HP/current-max transfer; equipment shape/fit/usability; cross-influence condition/exposure histories; resource spending/recovery and permanent reconstruction still require approved shared contracts or authored gameplay rules. Do not infer policies from monster stat blocks.
4. **Commit-time Site revocation:** Fresh Site checks are not a distributed commit fence. The separate R3 admission/revocation work remains unmerged; this integration does not claim to resolve a Site revocation occurring between the final check and PostgreSQL commit.
5. **Acceptance closure:** The original twenty-scenario list and older thirty-two-scenario Character State matrix must be reconciled against actual CI evidence before any “complete” statement. No implementation here changes either deferred content or unified UI scope.

## Validated revisions and CI

- Rules Core `e277b79a49a44ad27da5c352ffdad263985a3bbf`: full native mechanics/host regression, [GitHub Actions run 44650583200](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/44650583200) — **passed**.
- Character Sheet `afff26a36ac589f3f07cccaa9484728ac35ca9c6`: full validation, PostgreSQL and container smoke, [run 44650374602](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44650374602) — **passed**.
- Paired real Core HTTP/PostgreSQL and Mixed-Origin compatibility on that Character Sheet revision, [run 44650374706](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44650374706) — **passed**. The next CI run pins the exact Rules Core revision above.

Neither integration branch is merged into `main`. The branches should remain available for the remaining acceptance work and an independent review.
