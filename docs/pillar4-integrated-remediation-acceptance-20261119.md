# Pillar 4 — integrated remediation acceptance record

**Updated:** November 20, 2026  
**Repositories:** `LegendMaster03/dorks-and-dice-rules-core` and `LegendMaster03/dorks-and-dice-character-sheet`  
**Branches:** `integration/pillar4-infrastructure-remediation-20261119` and `integration/pillar4-character-state-remediation-20261119`  
**Pinned Rules Core (including current main crafting/competency work):** `dbed4584febfa1b56c1ec3969ae650f12bca04ac`  
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
| C4 — Full combined lifecycle | `FullSyntheticLifecycleComposesAcquisitionProgressionOverlapExpressionAndFormReversion` now exercises independent acquisitions, progression, source-scoped overlap selection, expression, Wolf activation, Class change while active, reversion, full receipt chain and restart on one character through real Core HTTP/PostgreSQL, followed by an overlap-replacement-versus-Wolf-reentry race from separate PostgreSQL contexts | **Partial**; Origin mutation while active, cross-service concurrent revocation, and unapproved generic mechanics policies remain separate acceptance gates |

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
- Real Core HTTP + PostgreSQL: `SyntheticSupernaturalOwnershipAcquiresAndAdvancesThroughRealCoreAndPostgres`, `IndependentlyVerifiedOverlapDecisionSurvivesRealCoreHttpAndPostgresRestart`, `FullSyntheticLifecycleComposesAcquisitionProgressionOverlapExpressionAndFormReversion`, and existing independently authenticated form entry/reversion, composite form, Origin and Class inheritance tests.

### Combined lifecycle acceptance and integration fix

The single-character test first records eight immutable supernatural receipts: a test-setup seed of preowned Wolf/Hybrid form ownership, two independently authorized Core-planned influence acquisitions, one source-verified advancement, the exact writer-selection decision, an expression change, Wolf activation, and reversion. It verifies that a dormant first writer does not displace the active second writer, that expression activation applies the previously authorized choice, that primary Class/ability advancement changes the active form projection without rewriting supernatural ownership, and that reversion and fresh DbContext reconstruction reproduce the latest primary plus its unchanged influence progression.

The initial combined test correctly returned an unresolved mechanics read after source-verified overlap issuance. The defect was in the Character Sheet request adapters: `CharacterEffectiveStateService` and `CharacterManifestationTransitionService` forwarded persistent influences but omitted the already persisted `InfluenceOverlapSelectionsJson`. Both now forward this exact committed collection. Rules Core still separately verifies every decision and rejects missing or forged source authority. No UI, acquisition grants, or gameplay rule was added to fix the transport omission.

The test-only second acquisition source uses its own definition occurrence and event receipt. Its first attempt failed the canonical Core source-occurrence comparison, and the corrected fixture preserved that fail-closed check. After reversion, two independently authorized coordinators concurrently submit an exact writer replacement and Wolf reentry against supernatural revision eight and manifestation revision two. The test requires one and only one applied event, revision nine, exactly nine immutable receipts, unchanged native progression revision three, and the matching ledger revision. It uses separate database contexts and the real Core HTTP host, not two operations on a shared EF Core context.

The preowned forms are deliberately **not** acquired through an independently verified form-acquisition planner. Test setup commits them through the internal store with an authored synthetic receipt, after which the separate manifestation host checks their exact persisted ownership and source evidence before transitions or reads. This is a combined infrastructure test, not evidence of independently authorized or playable first-party form acquisition.

Each assertion is scoped to its actual synthetic fixture. An independently verified Core plan remains a proposal until the Character Sheet commit succeeds.

## Outstanding acceptance and design gates

1. **Remaining combined-lifecycle variants:** Acquisition, progression, overlap, expression, form, Class advancement, reversion, restart and an overlap-versus-form cross-coordinator revision race now pass on one persisted character. Still missing are an independently authorized underlying Origin change while transformed; conflict/replay permutations involving acquisition and expression coordinators; and a commit-time Site authorization fence. The test-only preseeded form ownership is not a production form-acquisition path.
2. **Source authority:** Independently authenticated production first-party acquisition/progression, DM scope and source-rule ownership are not installed. The absence is intentional while first-party content remains deferred; synthetic providers must never be registered in production.
3. **Mechanical policy:** Creature type/targeting; non-walking movement modes; action and speech capabilities; form-specific Class feature and spell use; HP/current-max transfer; equipment shape/fit/usability; cross-influence condition/exposure histories; resource spending/recovery and permanent reconstruction still require approved shared contracts or authored gameplay rules. Do not infer policies from monster stat blocks.
4. **Commit-time Site revocation:** Fresh Site checks are not a distributed commit fence. The separate R3 admission/revocation work remains unmerged; this integration does not claim to resolve a Site revocation occurring between the final check and PostgreSQL commit.
5. **Acceptance closure:** The original twenty-scenario list and older thirty-two-scenario Character State matrix must be reconciled against actual CI evidence before any “complete” statement. No implementation here changes either deferred content or unified UI scope.

## Validated revisions and CI

- Rules Core `dbed4584febfa1b56c1ec3969ae650f12bca04ac`: full native mechanics/host regression, [GitHub Actions run 44651420294](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/44651420294) — **passed**.
- Character Sheet `423a5838f99b4cc124fb7bad6df60adac58840e5`: full validation, PostgreSQL and container smoke, [run 44688875214](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44688875214) — **passed**.
- Paired real Core HTTP/PostgreSQL and Mixed-Origin compatibility at the same Character Sheet revision, pinned to the Rules Core revision above, [run 44688875239](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44688875239) — **passed**.

Neither integration branch is merged into `main`. The branches should remain available for the remaining acceptance work and an independent review.
