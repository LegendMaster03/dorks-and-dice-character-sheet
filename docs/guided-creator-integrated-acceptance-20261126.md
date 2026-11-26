# Guided Character Creator — interim integrated acceptance report

**Verdict: Not ready for WorkChat acceptance.** This is a persistent checkpoint, not a completed acceptance assignment or authorization to release. All five workstream sources have been consolidated, but the replacement editors are not production-mounted, method-specific and Life Experiences persistence is incomplete, native host authority is incomplete, and the creation lifecycle requires explicit approval. Existing preview and committed gameplay state must remain distinct.

## Candidate identities and integration

All five original workstream refs and all three main refs matched the assignment when checked. No main merge or deployment occurred.

| Repository / candidate | Exact revision |
| --- | --- |
| Sheet main baseline | `c3a5e27d95e70307901e85ee46a3faa59f069899` |
| A shared infrastructure | `5edfe758a14a37809d5239c4358e5aa2a4303e18` |
| B conventional | `8110574ff9409f5457037487f60b7ff1a3f68c50` |
| C biological | `096d12366b896b2adba4f333a6fe2788af9ddebf` |
| D construct | `0f49d8443665112e7d1ecce2a81c93bad9e31164` |
| E life experiences | `e249ca95b8754a79a86d60478dda11c2d667c83b` |
| Consolidated A–E source | `ccc9ae73a2b6adc0274bba473cc14d699d71a054` |
| Sheet reviewed code/workflow checkpoint (before this report) | `98ccbe4276d1cfa4a0c4ef50e7b4c46baf2ebe1f` |
| Core main baseline | `0baa466b7fc8f55e5d675b886f76e95f9a4b551f` |
| Core D presentation input, draft #217 | `a3cb5d22f3e8430e34766839b63dc82fa1579c6f` |
| Core integrated native presentation candidate | `8a6c1ee2c133189bc3e5eedf9f3ba1441e9f7441` |
| Site main, unchanged | `aa15e85c5edf032275320b20442fe6c09501be5f` |

Sheet branch: `feature/guided-creator-integrated-acceptance-20261126`, [draft PR #81](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/81). A is the branch foundation; B–E were merged into that branch only via PRs [#77](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/77), [#78](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/78), [#79](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/79), [#80](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/80). The four specialized branches diverged from A's shared checkpoint; only E edited the common package/test guard. A's post-checkpoint commit added its handoff document only. Original workstream history is retained. Existing PRs #75 and #76 are not approved for main.

Core branch: `feature/guided-creator-native-integration-20261126`, [draft PR #218](https://github.com/LegendMaster03/dorks-and-dice-rules-core/pull/218). Based on the exact #217 candidate, which remains a separate unmerged draft. Its additive allowance, component category, rated power and quotation presentation were inspected; no new gameplay grants or preset publication authorized. Added bounded authored method/preset descriptions, with no definition or review-note exposure. Actual Elf/Wood Elf definitions at the current native baseline lack descriptions; do not manufacture them.

## Scope and evidence limits

Read the approved UX specification and workstream handoffs, compared branch file changes, inspected editor interfaces, relevant native endpoints, and key application and storage service paths. No local checkout is available: git ls-remote failed to resolve github.com and .NET is not installed. GitHub connector supports source inspection, branch/PR changes, and CI observation. This is not exhaustive line-by-line review or a completed runtime acceptance. Do not infer passing tests from existence of tests or from historical workstream reports.

## Requirements coverage matrix (interim)

The classifications below refer to the actual integrated candidate; a code adapter with no application host is explicitly not connected. Source-level review does not constitute independent runtime verification.

| Requirements / assignment sections | Classification | Evidence / remaining work |
| --- | --- | --- |
| Source consolidation (4) | Implemented and independently verified | Exact refs and four integration merges confirmed. |
| Revised guided steps and shared preview/navigation (6, 14) | Implemented but not independently verified | A shell and shared contracts; review is not finalization. |
| Conventional browser default; C/D registry; E Background replacement (6, 8–11) | Implemented but not connected to the application | Actual provisional UI remains; each adapter needs canonical host and current permissions. |
| Shared save state, errors, revision and fingerprint handling (6) | Partially implemented | Existing ordinary Origin save/preview; general pending-write/retry/conflict reconciliation and shared composition unfinished. |
| Durable method-specific construct drafts and Life Experiences drafts (6, 10–11) | Missing | No authorized persistence model or round-trip for the new state. |
| Common nested/configurable parameters, contextual choice and dependency controls (6) | Partially implemented | Shared finite/count/validation/review components; general parameter/reconciliation UI incomplete. |
| Lifecycle, confirmation, finalization, legacy classification (5) | Requires explicit design approval | See decision below; no lifecycle changes made. |
| Nullable method stage and explicit post-origination exclusion (7) | Implemented but not independently verified | Decoder and dispatch changed; regression cases added. |
| Native method and preset description transport (7) | Implemented but not independently verified | Core #218 and Sheet decoder/isolated conventional editor; absent authored descriptions remain unavailable. |
| Conventional native hierarchy, direct preset, finite/catalog choices, suggestions, retention (8) | Implemented but not connected to the application | Model and workspace present; bounded real-native fixture tests reported; no live save/reload or final result. |
| Conventional rich mechanical detail, character-specific feat eligibility and restrictions (7–8) | Partially implemented / blocked by a native contract | Typed detail and character-context authority still needed; no fallback grants. |
| Biological contributor/expression/latent/finite configuration/retention UI (9) | Implemented but not connected to the application | Native-exemplar adapter exists. Actual allocation and zero-expression results not independently exercised. |
| Biological verified parentage, multigeneration, reconciliation/recombination, linked parameters, scoped exception (7, 9) | Blocked by application architecture / native contract | Host provider, exact source revision/relationship/eligibility, choice adapters, and independent real-native acceptance missing. |
| Red/Bronze native evaluation, latent cost and grant assertions (9) | Missing integrated acceptance | Isolated mock cases are not native evaluator proof. |
| Construct exact instances, config, retention, candidate/power/quote display (10) | Implemented but not connected to the application | D editor/model present; bounded quotation fields in Core candidate; no durable method-specific host. |
| Construct reference assemblies, disconnected supply and operating authority (10) | Partially implemented | Native reference fixtures and existing evaluators; not re-evaluated in this pass. No published presets or runtime fuel authority assumed. |
| Construct optional starting templates (10) | Requires explicit design approval | Documentation fixtures remain non-runtime. |
| Independent Life Experiences sources, nested choices and source retention (11) | Implemented but not connected to the application | E preview/model/render contracts; actual persisted source pair absent. |
| Life Experiences native hierarchy and labels (7, 11) | Blocked by a native contract | Identity/title discovery intentionally marks entries browse-only. |
| Contextual languages, verified benefit and training composition, equipment resolution (7, 11–12) | Blocked by application architecture / native contract | Host providers and verified source/eligibility/equipment evidence required. |
| Guild Merchant nested professional choice (11) | Implemented but not independently verified | Fixture model verifies exclusive branch and independent baseline language; actual native authenticated workflow not tested. |
| Deity in Other Choices and hierarchical Skill/Competency UI (11, 14) | Implemented but not independently verified | A moved Deity; normal hierarchy must remain intact; E requires verified training hierarchy. |
| Authorized whole-character mechanical projection, ordinary inventory, idempotent resource ledgers (12) | Blocked by application architecture | Existing services are reusable; proposed experiences and construct assembly are not committed. No preview promoted to gameplay state. |
| Adversarial authorization and state-integrity acceptance (13) | Missing integrated acceptance | Existing gates inspected, not a dedicated adversarial test of the new composed workflow. |
| UX conformance, keyboard/mobile, complete class/Warlock/ability/advancement and embedded regressions (14) | Missing integrated acceptance | No actual independent browser sessions or screenshots of integrated UX. |
| Common validation registration and obsolete workflow cleanup (15) | Implemented but not independently verified | Feature tests registered and test paths reconciled; CI required at exact code revision. |
| WorkChat acceptance assignment and readiness (18–19) | Blocked by application architecture | Not ready; no WorkChat browser acceptance claimed. |

## Confirmed remediation

1. Native discovery rejected legitimate null method stages. The Sheet contract accepts string or null and explicitly refuses post-origination dispatch even if a preview-support field is supplied. Tests cover null and malformed stages, exact registry, and post-origination exclusion.
2. Core now projects only bounded authored description text (maximum 4000 characters; null for missing, whitespace-only or oversized values). Sheet consumes it, preserves compatibility when an older response omits the field, and presents it in the isolated conventional editor without inventing descriptions or granting authority. Regression cases cover valid and oversized descriptions and the Core disclosure boundary.
3. Conventional, biological, construct and Life Experiences tests are registered in the ordinary frontend test script. The biological and construct tests consume the shared compiled test output. Temporary B/D workstream workflows removed; their tests are retained. Strict full frontend typecheck remains in the ordinary build.
4. Historical Phase 5 workflow attempted checkout of removed branch `feature/pillar2-phase5-origin-api-20261112` and failed before tests. The current pinned integration workflow already runs the same native probe and MixedOriginCrossRepositoryPersistenceTests, adding current coverage. Removed the obsolete duplicate workflow rather than restoring the historical branch. Pinned the current contract and isolated browser regression workflows to Core `8a6c1ee2c133189bc3e5eedf9f3ba1441e9f7441`; Site remains `aa15e85c5edf032275320b20442fe6c09501be5f`. The old browser test is regression evidence only, not new UX acceptance.

## Lifecycle checkpoint — decision requested

### Independently inspected existing mechanisms

- Site `CharacterService.CreateAsync` sets Active at identity creation; archive closes active campaign associations, restore makes identity Active. It does not express creation completeness.
- Sheet `CharacterSheetRoot` has only `CharacterBuilderStatus.BuildInProgress`, but already tracks `MutationRevision` and persistent foundational selections, advancement entries, inventory, currency, and rules inputs.
- `CharacterOriginDraftService.SaveAsync` verifies Site access, archived status, root initialization, absence of committed Origin, and exact expected revision. `PostgresCharacterOriginDraftStore` locks the character root. `CharacterOriginInitialActivationService.ApplyAsync` checks the exact draft revision, native authorized result, and permanent commitment; `PostgresCharacterAuthorizedOriginStore` locks root/draft/origin under its own transaction.
- `CharacterStartingEquipmentService` materializes normal inventory, independently of Origin confirmation. `CharacterAdvancementService` independently previews/applies persistent advancement. `CharacterInnateSpellLedgerService` requires committed Origin and revision-bound trusted evaluation; the PostgreSQL ledger store locks root, origin, ledger and rechecks aggregate mutation revision.
- These are useful existing primitives. They do not form a single all-or-nothing character creation transaction. Several existing writes use independent EF SaveChanges. A new orchestrator must not simply call independently committing services in sequence and label the result atomic.

### Recommendation (not approved)

Sheet should own the creation boundary, not Site and not Rules Core. Preserve the three player-visible stages, but use two durable lifecycle classifications (editable creation / established) plus an exact revision-bound confirmation receipt rather than an independent mutable Confirmed flag. This avoids contradictory status and duplicate truth. Add a third conservative legacy-unclassified/locked state for migration only; do not automatically make historical characters editable.

- New character: explicit authorized initialization or start-creation command creates an editable Sheet creation aggregate; Site Active remains a separate access status. A draft has no newly earned grants solely by existing.
- Confirmation: server verifies exact revisions of Origin including method-specific payload, Life Experiences, class/advancement proposal, abilities, choices and equipment intentions; records character aggregate mutation revision, ruleset fingerprint and relevant scope/authorization evidence. A changed relevant input invalidates the receipt. Players can return to editing before finalization without undoing gameplay.
- Finalization: re-evaluate and verify exact native results, provenance, character-specific eligibility, Site actor/ownership/campaign authority, and scoped approvals. Use a single Sheet transaction with root lock, exact revision conditions, deterministic source receipts and a unique idempotency key scoped to character and normalized request. Commit authorized Origin, earned experiences, approved initial advancement/abilities, ordinary inventory/currency once, and established state together. Reuse existing grant/materialization logic via transaction-aware internals rather than separate commits. If a resource ledger is lazily initialized by existing policy, require established and committed verified evidence, and never reset spent history. If an external effect is unavoidable, record a durable pending recovery receipt; never expose partial state as established.
- Mutation boundaries: backend gates for all creation-only and gameplay mutation entry points. Archive/read-only and revoked/mismatched scope fail closed. Do not infer eligibility from Guided Setup or from absence of an Origin. Preview and confirmation do not expose earned grants or unblock gameplay changes.
- Migration: committed Origins or clear existing gameplay/advancement/inventory-materialization evidence remain protected; ambiguous existing roots become locked/unclassified, not automatically draft or automatically granted mechanics. Legacy Species/Subspecies remain an explicit reconciliation problem. No automatic grant recreation, inventory deletion, resource resets, or reopening. The actual rule for classifying existing records and a review path for ambiguous records requires approval.
- Persistence: add creation state/confirmation receipt and finalization idempotency/receipt structures with EF migration and constraints; extend canonical Origin draft to typed per-method input, add revisioned Life Experiences state as part of aggregate. Extend bootstrap with server capabilities; maintain existing MutationRevision and root lock protocol. Stage initial advancement and equipment intentions without creating a second authoritative gameplay inventory.
- Services: CharacterSheetRoot, bootstrap, build/advancement, Origin draft/activation, Life Experiences persistence, inventory materialization, innate spell ledger, and their stores/endpoints. Site identity remains unchanged unless a trusted authority/creation initiation integration proves necessary; no Site code modified in this pass.
- Cross-service race: existing S04 policy candidate notes that Site authorization and Core catalog can change independently of Sheet PostgreSQL. Rechecking remotely immediately before local commit alone cannot prove instantaneous revocation or global latest fingerprint. Recommend explicitly scoped short-lived authorization evidence and a verified pinned native snapshot, with exact revision and single-use commit receipt. Owner must approve bounded-snapshot versus stronger immediate-revocation/latest-at-commit semantics; stronger guarantees require additional cross-service coordination. Do not silently select a policy.

Alternative of using Site Active/Archived or existing wizard status fails to distinguish creation from established gameplay. Reversible activation violates existing committed Origin invariants. A separate creation database/duplicate character aggregate would complicate transaction ownership and migration without demonstrated need.

Required approval: Sheet-owned boundary with revision-bound confirmation, conservative legacy handling, authorized atomic finalization, and agreed cross-service authority/snapshot semantics. No migration, finalization endpoint or new authority semantics implemented.

## Validation snapshot

Existing #217 exact candidate: [structure](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/45762928941) and [native CI](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/45762928771) both completed successfully. These do not cover new Core description changes. New Core candidate #218: [structure](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/45763747259) passed; [native CI](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/45763747260) in progress when checked.

Sheet intermediate `eab64668db47072e112832386ad15da0a33b0459`: [main-compatible smoke](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763548940), [old pinned Core integration](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763548930), and [old pinned isolated browser regression](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763548897) reported success; [ordinary validate](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763548884) cancelled; [obsolete Phase 5](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763549024) failed Core checkout, not a test assertion. None establishes final candidate success.

Sheet intermediate `296777cbf24d76142513a1a2948785f44171812b`: [main-compatible smoke](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763712134) passed; [ordinary validate](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763712070), [old pinned integration](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763712055), and [old pinned browser](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763712145) in progress when checked; [obsolete Phase 5](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45763712120) failed. Current code/workflow checkpoint `98ccbe4276d1cfa4a0c4ef50e7b4c46baf2ebe1f`: connector had not yet returned PR runs when checked. No complete integrated test suite result claimed. Run full validation on the exact eventual code/report candidate, including pinned Core/Site, and examine job logs, failure causes, and artifacts. Workstream handoff tests and historical browser runs are not independent acceptance of the new UX.

## Next unblocked work and WorkChat prerequisites

1. Finish native bounded Life Experiences hierarchy, training hierarchy, benefit names and descriptions, nested requirement relationships, contextual eligibility and source-specific equipment evidence. Core descriptions remain unavailable where not authored. Complete native method-specific parameter, reconciliation and characteristic progression adapters. Review #218 exact CI and address failures.
2. Build the authorized shared draft controller, pending/retry/conflict handling, method-specific persistence and Life Experiences persistence. Wire actual conventional and specialized workspaces using exact native references; do not remove provisional functionality until safe replacements are operational. Do not introduce duplicate stores, raw catalog ingress or frontend authority. Review lifecycle-independent portions with exact tests.
3. Obtain lifecycle/migration/cross-service approval; implement transaction-aware confirmation/finalization and all gameplay/legacy/read-only guards. Verify committed mechanical output, provenance, equipment and resource ledger behavior. Preserve Skill and Competency hierarchy, Deity and existing class/advancement flows.
4. Run complete current native and application CI; independently execute actual native Red/Bronze donor, four documented construct reference assemblies, Guild Merchant nested choice, conventional ancestry and finite/catalog choice paths, source-change retention, stale/failure/concurrency, read-only/campaign/revocation and partial-finalization tests. Review responsive desktop/tablet/mobile, keyboard and accessibility and ordinary sheet regression.
5. Only after these blockers are resolved, construct WorkChat assignment with exact deployable branches and revisions, owner/other-user/read-only/DM test accounts, disposable characters and campaign permissions, actual native expected results, save/reload, state integrity, and screenshots. Use an isolated authorized environment; no production merge or deployment without separate approval.

This report records a partial acceptance/remediation checkpoint, not the final independent review or completion of all assignment conditions.
