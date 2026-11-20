# Pillar 4 — integrated remediation acceptance record

**Updated:** November 20, 2026  
**Repositories:** `LegendMaster03/dorks-and-dice-rules-core` and `LegendMaster03/dorks-and-dice-character-sheet`  
**Branches:** `integration/pillar4-infrastructure-remediation-20261119` and `integration/pillar4-character-state-remediation-20261119`  
**Pinned Rules Core (including current main crafting/competency work):** `dbed4584febfa1b56c1ec3969ae650f12bca04ac`  
**Disposition:** **Ready for independent final review of the scoped synthetic infrastructure. Not approved for merge, gameplay release, or deployment.**

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
| C4 — Full combined lifecycle | Real Core HTTP/PostgreSQL acquisition, progression, overlapping writer decisions, expression, form activation/reversion, Class update, restart, and two cross-operation CAS races in `FullSyntheticLifecycleComposesAcquisitionProgressionOverlapExpressionAndFormReversion`. Separate `RevisedOriginWhileWolfIsActiveRetainsOwnedFormButBlocksStaleEffectiveMechanics` proves that changing Origin does not manufacture unapproved mechanics. | **Covered for in-scope synthetic infrastructure.** Full Origin grant reconstruction and production Site revocation are separate policy/authorization work. |

The original **20 required integrated acceptance scenarios** are reconciled individually below. Each result is limited to the tested synthetic/source-verified contract. The separate Character State Increment 4 **32-scenario** matrix and deferred R3 terminal authorization are not silently included.

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
- Real Core HTTP + PostgreSQL: `SyntheticSupernaturalOwnershipAcquiresAndAdvancesThroughRealCoreAndPostgres`, `CompatiblePersistentSupernaturalInfluencesComposeThroughRealCoreAndPostgres`, `IndependentlyVerifiedOverlapDecisionSurvivesRealCoreHttpAndPostgresRestart`, `FullSyntheticLifecycleComposesAcquisitionProgressionOverlapExpressionAndFormReversion`, `RevisedOriginWhileWolfIsActiveRetainsOwnedFormButBlocksStaleEffectiveMechanics`, and existing source-checked form entry/reversion, composite form, Origin and Class inheritance tests.

### Original twenty-scenario acceptance matrix

The original prompt's scenarios are not interchangeable with the older thirty-two-case Character State Increment 4 matrix. “Pass” below means the named scope ran against the integrated native/Sheet contract, with the noted complementary library, contract or PostgreSQL regression where appropriate. It never means that unapproved first-party supernatural content or a production source adapter was installed.

| # | Original scenario | Integrated and complementary evidence | Disposition |
| --- | --- | --- | --- |
| 1 | Verified synthetic modification-only acquisition | `SyntheticSupernaturalOwnershipAcquiresAndAdvancesThroughRealCoreAndPostgres`; combined lifecycle | Pass: Core host independently authorizes the exact source/actor |
| 2 | PostgreSQL acquisition persistence/reload | Same ownership acceptance plus fresh-context reconstruction in combined lifecycle | Pass |
| 3 | Source-authored independent advancement | Same ownership acceptance; `FullSyntheticLifecycleComposesAcquisitionProgressionOverlapExpressionAndFormReversion` | Pass |
| 4 | Atomic advancement plus consumed eligibility evidence | Ownership acceptance, `OwnershipProgressionReceiptChainRejectsLostEligibilityAfterDatabaseReload` | Pass |
| 5 | Distinct-event reuse of spent evidence rejected | Ownership acceptance's `synthetic:replay` attempt | Pass |
| 6 | Reversible expression preserves earned progression | Combined lifecycle's advancement/expression race and retry; `ExpressionCoordinatorUsesRealCoreForGatedEarnedProgression` | Pass |
| 7 | Verified persistent characteristic | Combined lifecycle: active choice on strength; compatible-influences case | Pass |
| 8 | Two compatible independently owned influences | `CompatiblePersistentSupernaturalInfluencesComposeThroughRealCoreAndPostgres` checks two disjoint adjustments and identical PostgreSQL reload result; native `DisjointNumericChangesComposeWithoutInventingPrecedence` | Pass; fixture ownership is test-seeded, not a claim of new acquisition entitlement |
| 9 | Exact authorized competing-writer decision | `IndependentlyVerifiedOverlapDecisionSurvivesRealCoreHttpAndPostgresRestart`; combined lifecycle | Pass |
| 10 | Forged or wrong-scope decision denied | `OverlapPlanRequiresExactIdentityAndImmutableDecisionEvidence`, `OverlapEventRejectsUnauthorizedDecisionAndCrossScopeReplacement`, `HistoricalOverlapSnapshotCorruptionRejectsSubsequentCommit` | Pass across native, API response contract and PostgreSQL |
| 11 | Authorized temporary alternate form activated | Combined lifecycle; `StrictSyntheticAuthorityPlansAndPersistsWolfReversionAndHybridThroughRealCore` | Pass with preowned source-verified test forms |
| 12 | Effective projection follows latest underlying character | Combined lifecycle changes Class level/ability while Wolf is active; native `AuthorizedReconstructedUnderlyingPropagatesThroughActiveWolfAndReversion` covers the fully authorized source projection | Pass for supported numeric/Class projection; unresolved Origin-grant reconstruction is withheld |
| 13 | Reversion retains history and resource ownership | Combined lifecycle; `OwnedResourceSurvivesReversionWithoutLeakingToLaterWolfActivation` | Pass |
| 14 | Underlying Class or Origin changes with dependency handling | Combined lifecycle's Class update; `RevisedOriginWhileWolfIsActiveRetainsOwnedFormButBlocksStaleEffectiveMechanics` checks latest independently verified Origin identity and fail-closed mechanics | Pass for dependency handling, not authorization of new inherited mechanics |
| 15 | Concurrent advancement or form operations remain atomic | Combined lifecycle races advancement versus expression, then overlap replacement versus form reentry with distinct PostgreSQL contexts; `ConcurrentOverlapWritersCanNotBothCommitTheSameSupernaturalRevision` | Pass |
| 16 | Stale catalog, revisions, ownership, and evidence rejected | `NativeCatalogChangeRejectsPreviouslyAcquiredStateBeforeAnyTransitionCommit`; `AbsentHostAuthorityStaleRevisionAndArchivedActorNeverCommitTransition`; exact Core ownership and overlap contract tests | Pass |
| 17 | Fresh-process/database reproduction | Combined lifecycle reload; `RealCharacterSheetProcessRestartPreservesVerifiedActiveFormAndAudit`; compatible-influences identical projection on reload | Pass |
| 18 | Ordinary character unaffected | `OwnedPrimaryCharacterUsesRealNativeHttpAndPostgresWithoutMutatingState` | Pass |
| 19 | Existing Pillar 2 and Character State regression | Full Character Sheet test workflow and `MixedOriginCrossRepositoryPersistenceTests` in the paired Core/PostgreSQL workflow | Pass in-scope: 20 Mixed-Origin tests passed, one R3 revocation characterization skipped |
| 20 | Unsupported mechanics explicit, no unauthorized grants | `RevisedOriginWhileWolfIsActiveRetainsOwnedFormButBlocksStaleEffectiveMechanics`; `NativeProjectionWithholdsUnverifiedFormFeaturesButPreservesNumbers`; effective-state unresolved issue reporting | Pass for withholding; **not** a fully playable mechanics-policy implementation |

**Explicit limitation for scenarios 8 and 11:** The compatible-pair test and initial form ownership use strict, test-seeded source fixtures. The independent Core acquisition and advancement path is separately exercised by scenarios 1–5 and the combined lifecycle. No test fixture is registered as production first-party authority.

**Explicit limitation for scenario 19:** The skipped `S04CurrentBehaviorCharacterization_RevocationAfterCoreApprovalStillCommits` characterizes the already-known cross-service Site revocation race. R3 terminal authorization and its fix are explicitly excluded from the November 19 remediation; the skip is reported, not counted as a passing security guarantee.

### Combined lifecycle acceptance and integration fix

The single-character test first records eight immutable supernatural receipts: a test-setup seed of preowned Wolf/Hybrid form ownership, two independently authorized Core-planned influence acquisitions, one source-verified advancement, the exact writer-selection decision, an expression change, Wolf activation, and reversion. It verifies that unadjudicated acquired influences do not create a playable effective projection, that independently authorized advancement and expression can be committed in either order with the losing operation retried against the new revision, that a later exact writer decision resolves the active collision, and that Class/ability advancement, reversion and fresh DbContext reconstruction preserve supernatural ownership and progression.

The initial combined test correctly returned an unresolved mechanics read after source-verified overlap issuance. The defect was in the Character Sheet request adapters: `CharacterEffectiveStateService` and `CharacterManifestationTransitionService` forwarded persistent influences but omitted the already persisted `InfluenceOverlapSelectionsJson`. Both now forward this exact committed collection. Rules Core still separately verifies every decision and rejects missing or forged source authority. No UI, acquisition grants, or gameplay rule was added to fix the transport omission.

The test-only second acquisition source uses its own definition occurrence and event receipt. Its first attempt failed the canonical Core source-occurrence comparison, and the corrected fixture preserved that fail-closed check. After reversion, two independently authorized coordinators concurrently submit an exact writer replacement and Wolf reentry against supernatural revision eight and manifestation revision two. The test requires one and only one applied event, revision nine, exactly nine immutable receipts, unchanged native progression revision three, and the matching ledger revision. It uses separate database contexts and the real Core HTTP host, not two operations on a shared EF Core context.

The preowned forms are deliberately **not** acquired through an independently verified form-acquisition planner. Test setup commits them through the internal store with an authored synthetic receipt, after which the separate manifestation host checks their exact persisted ownership and source evidence before transitions or reads. This is a combined infrastructure test, not evidence of independently authorized or playable first-party form acquisition.

Each assertion is scoped to its actual synthetic fixture. An independently verified Core plan remains a proposal until the Character Sheet commit succeeds.

## Outstanding acceptance and design gates

1. **Combined-lifecycle limits:** In-scope acquisition/progression and expression concurrency, overlap/form concurrency, Class changes while active, reversion and restart are now covered. A source-verified updated Origin identity is tested while Wolf remains active, but the generic inherited Origin-grant reconstruction policy is not established; effective mechanics deliberately become unresolved. The fixture's preseeded form owner is not a production acquisition protocol.
2. **Source authority:** Independently authenticated production first-party acquisition/progression, DM scope and source-rule ownership are not installed. The absence is intentional while first-party content remains deferred; synthetic providers must never be registered in production.
3. **Mechanical policy:** Creature type/targeting; non-walking movement modes; action and speech capabilities; form-specific Class feature and spell use; HP/current-max transfer; equipment shape/fit/usability; cross-influence condition/exposure histories; resource spending/recovery and permanent reconstruction still require approved shared contracts or authored gameplay rules. Do not infer policies from monster stat blocks.
4. **Separate R3 terminal authorization (explicitly excluded):** Fresh Site checks are not a distributed commit fence. The independent R3 admission/revocation work remains unmerged. The known Site revocation-at-commit race is an explicit production authorization limitation, **not** a required migration or implementation in this remediation.
5. **Review and release boundary:** All twenty original scenarios have traced synthetic/test-only evidence with stated limits. The older thirty-two-scenario Character State matrix is separate. Independent final review is still required before any merge recommendation is approved, and the unapproved generic mechanics/content/source policies remain nonplayable.

### Generic mechanics disposition (B3)

The detailed baseline is `docs/rewrite/character-origins/pillar4-workstream-b-effective-character-status-20261119.md` in Rules Core. This is a deliberately limited support matrix, not an implementation of unspecified gameplay policies.

| Generic capability | Verified baseline | Outstanding semantics |
| --- | --- | --- |
| Persistent ownership, expression, earned grants and effects | Source-qualified independent records, finite eligibility, scoped typed modifiers and condition IDs | Duration/exposure and cross-influence trigger ownership |
| Compatible and competing characteristic writers | Disjoint adjustments; one verified writer for an exact sheet-qualified collision | No implicit stacking, selection reuse or general arbitrary feature replacement |
| Temporary and partial manifestations | Activation/reversion, contribution slots, latest primary numeric inputs and separate resources | General character feature/action usability beyond scoped numeric projection |
| Creature classification, movement and speech | Base walk speed plus native Origin intrinsic source projectors | Effective transformed creature type/targeting, flight/climb/swim/burrow and speech/action restrictions |
| Class features, spellcasting and equipment | Latest Class numeric advancement; stable persistent inventory ownership | Per-form feature/spell eligibility, transformed equipment shape/fit/use and spell resource policy |
| Hit Points, resources and conditions | Typed HP gain input, resource owner/maximum/quantity provenance, condition identifiers | Current/max HP transfer, consumption/recovery, persistent exposure histories |
| Permanent reconstruction | Existing native Origin reconstruction boundary and source verification | Source-approved supernatural finalization and exact cross-domain retention policy |

**Rule:** Missing semantics remain explicit unresolved outputs; no first-party lycanthropy, vampirism, ceremorphosis or Draconic content, balancing, unified UI, biological inheritance, new inventory engine or R3 protocol is introduced.

## Validated revisions and CI

- Rules Core `dbed4584febfa1b56c1ec3969ae650f12bca04ac`: full native mechanics/host regression, [GitHub Actions run 44651420294](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/44651420294) — **passed**.
- Rules Core full regression: 146 Host and 729 Mechanics tests passed, plus native structural/Python checks, on the pinned revision above.
- Character Sheet implementation `0522d7985edf36f1a39c4489851b076e4fc1567f`: full validation, PostgreSQL and container smoke, [run 44691711793](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44691711793) — **passed** (196 unit; 312 integration).
- Paired real Core HTTP/PostgreSQL integration at the same implementation revision, pinned to the exact Core revision above, [run 44691711777](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44691711777) — **passed** (23 integrated tests; 20 Mixed-Origin pass, one excluded R3 characterization skip).
- Follow-up test-only analyzer clean-up `780e27d2f4886d95a402c8deb53ab94d3be1e948` awaits exact paired CI result in this document's revision history. No gameplay code or source policy was changed by that clean-up.

## Final review recommendation

**Ready for independent final review of the in-scope synthetic infrastructure**, not for immediate merge or production release. Verify the canonical Core/Sheet pairing, exact decision scopes, source authority isolation, receipt-history integrity, two coordinator races, dormant/compatible/conflicting influence behavior, and the B3 unresolved mechanics boundaries. The production supernatural source adapter and first-party content are intentionally absent. Do not conflate separate R3 revocation closure with this scoped implementation.

Neither integration branch is merged into `main`. The branches should remain available for the independent review; any later merge still requires explicit approval.

