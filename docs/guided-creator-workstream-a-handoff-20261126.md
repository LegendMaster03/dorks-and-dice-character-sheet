# Guided Character Creator — Workstream A integration checkpoint

Date: 2026-11-26

Status: **partial shared infrastructure checkpoint; not a release or finalization implementation.**

## Exact baseline

- Development branch: `feature/guided-creator-shared-infrastructure-20261126`
- Code checkpoint: `b31f4778f6765b65e9139977a2745ce94dd9e503`
- Approved UX specification: [character-origins-guided-ux-design.md](character-origins-guided-ux-design.md), commit `cc5b82f7ca588be51c5970de9222b1cd1cb72b93`.
- Character Sheet main inspected: `c3a5e27d95e70307901e85ee46a3faa59f069899`
- Rules Core main inspected: `0baa466b7fc8f55e5d675b886f76e95f9a4b551f`
- Site main inspected: `aa15e85c5edf032275320b20442fe6c09501be5f`
- [Code-checkpoint validation](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45761377628): frontend build (including frontend tests and type checking), embedded module verification, .NET tests with disposable PostgreSQL, and Compose validation passed at time of recording. Container build and smoke test had not finished.

Branch from the code checkpoint for parallel editor development. Do not merge into main. The documentation commit following the checkpoint does not change the code contract.

## Shared infrastructure

Client paths below are relative to `src/CharacterSheet.Web/Client/src/`.

- `features/guided/contracts.ts`: RuleNode, RuleDetail, DisplayFact, Provenance, ChoiceRequirement, ChoiceOption, AllocationView, DependencyChange, ValidationIssue, DecisionLink, DraftSaveStatus, DraftView<T>, EvaluationView<T>, EditorContext<TDraft,TPreview>, MethodEditor<TContext>.
- `features/guided/components.ts`: renderRuleBrowser, renderRuleDetail, renderFacts, renderDecisionNavigation, renderChoice, renderValidation, renderAllocation, renderDependencyReview, renderDraftStatus, renderWorkspace. Components do not own gameplay selections or calculate grants, resource prices, eligibility, allocation totals, or conflicts. The caller supplies labels, choices, native-derived restrictions and callbacks. Browse-only nodes may be inspected, not selected by the adapter. Nested controls render only the active choice branches.
- `features/guided/contracts.ts`: matchingRuleNodes includes ancestors of matching nodes; isCurrentEvaluation compares revision, ruleset and fingerprint; resolveMethodEditor requires one exact ruleId/methodId match and native support of initial-preview or assembly-preview. Reference-only and established-character methods are not dispatched by this registry. Registry integration into the actual method workspace remains outstanding.
- `styles/guided-workspace.css`: shared responsive single-, two-, and three-area layouts; collapsible browser/preview areas, labels and focus styling. Authenticated browser and accessibility testing remain outstanding.

The seven steps are Class (`class`), Character Origins (`species`), Life Experiences (`background`), Ability Scores (`abilities`), Other Choices (`other`), Equipment (`equipment`), Review (`review`). Existing route/step identifiers are retained. The existing Deity selection and its source are now in Other Choices, as are unowned nonability choices. The old Background editor remains temporarily available and is not represented as a completed Life Experiences pair. Review links to steps and is explicitly not finalization. The shared summary reads existing build, routine, mechanics and Origin state; it is not a proposed whole-character mechanical evaluation.

## Application state, draft and preview contracts

The shared EditorContext is a presentation adapter contract, not another persistence store. It receives characterId, draft, preview, decisionId, readOnly, navigate, save and retry. Adapt from the existing authorized app state; no independent mutable species/ancestry state, no client-synthesized grant, no client authority.

For B and C, preserve the existing canonical `OriginUiState.draft` and server Origin draft service. Existing `origin-api.ts` defines NativeOriginDraftProposal, NativeOriginDraftView, NativeOriginPreview and authorized-state views. Existing `origin-workflow.ts` owns loading, immediate draft saves, optimistic revision checks, request sequencing and refresh of stale previews. A shared general save adapter and a production registration composition point are still needed; coordinate these changes with A rather than creating parallel origin stores.

The current backend evaluates through Rules Core before storing an Origin proposal. It will not accept an ordinary new draft after authorized Origin activation. Transport failures leave the persisted revision intact but the current client does not yet retain the unsaved proposal for explicit retry. Reconciliation of concurrent changes, durable offline editing, a shared pending-write controller, and cross-method dependency-change persistence are unfinished. Never silently overwrite on 409 or substitute a stale fingerprint. A response matching a revision and fingerprint is still only a preview. The application must enforce authorization, not a disabled control.

D requires a canonical method-specific assembly input persisted alongside the Origin proposal, not a second Origin. E requires independent durable Life Experiences selection state with revision, authorization and confirmed/earned separation; the legacy Background selection is not sufficient. Neither storage extension is implemented here.

## Native discovery and preview interfaces

Use the authenticated same-origin Character Sheet bridge and the existing delegated gateway. Browser requests do not carry service principals, claimed authoritative profiles, earned grants, or verified-parent evidence.

| Client function in rules-core-native-api.ts | Character Sheet bridge path (relative to /api/native-rules) |
| --- | --- |
| discoverNativeOriginMethods | GET /origins/methods |
| discoverNativeOriginPresets | GET /origins/presets |
| discoverNativeOriginChoiceCatalog | GET /origins/choice-catalog |
| discoverNativeConstructConfiguration | GET /origins/construct/components/{definitionId}/configuration |
| previewNativeConstruct | POST /origins/construct/preview |
| previewNativeLifeExperiences | POST /origins/life-experiences/preview |

Methods and presets retain native references, hierarchy, acquisition restrictions and availability. Choice catalog discovery preserves requiresCharacterAuthority. Preview envelopes identify the ruleset/fingerprint and are preview-only. Life Experiences preview rejects an earned projection or committed response. Construct configuration and nested result payloads are not fully typed; D must decode the native records and validate their shape before presentation. Life Experiences nested issues/equipment payloads also need typed mapping. Do not introduce a permissive generic gateway for unreviewed endpoints.

Verified biological parent inspection, reconstruction, earned Life Experiences, equipment materialization, advancement and resource-ledger operations require their appropriate server-side authority. A browser preview is not permission to execute them. Core's Life Experiences evaluation requires both UpbringingId and BackgroundId; partial selection persistence needs Sheet storage, not fake IDs or grants.

## Parallel file ownership

The following are proposed exclusive feature folders; create them in the corresponding branch. Shared files are coordinated by A.

| Stream | Exclusive editor folder (Client/src relative) | Responsibility |
| --- | --- | --- |
| B | features/origin/conventional/ | Default hierarchy, Species and Ancestry, native requirements and review; adapt existing canonical Origin proposal |
| C | features/origin/biological/ | Method-specific contributor, inheritance and configuration editor; use native and host-verified parent evidence |
| D | features/origin/construct/ | Construct configuration discovery, component/assembly editor, typed assembly and native result adapters |
| E | features/life-experiences/ | Upbringing and Background workflow, native choices and candidate preview, proposed training/equipment presentation |

Use colocated tests or uniquely named test files. A owns features/guided/, ui/sheet.ts, ui/sheet-model.ts, app.ts, app-state.ts, shared styles, shared gateway and shared client API, and the common persistence composition. Existing origin-api.ts, origin-workflow.ts and origin-builder-model.ts are shared between B/C/D; coordinate required changes rather than editing all three independently. D/E may propose server persistence files with unique names, but coordinate database root schema, migration, service registration, endpoint registration and transaction ownership with A. Do not independently change the root, shared navigation, build scripts or canonical state model.

Each editor supplies an explicit adapter and decision list using the shared presentation contracts. Conventional selection is the default workspace, not a fake origination method. Biological and Construct editors register with exact native reference pairs and native execution support. Method adapters own layouts and transient UI state. A integrates registrations once adapters are available. E mounts in the existing background step. Keep previews, confirmed configuration and authorized gameplay state distinct. The existing temporary editors remain until replacements are wired and tested. Do not use legacy activation as confirmation.

## Lifecycle review and proposal (approval required)

Site's CharacterService creates an Active identity immediately; Site active/archive state governs access and campaign membership, not creation completeness. Sheet's CharacterSheetRoot currently has only BuildInProgress in CharacterBuilderStatus, and Guided Setup is reopenable. CharacterOriginInitialActivationService commits an authorized Origin once, with separate scoped review for approved mixed Origins. CharacterOriginDraftService checks Site access, initialized root, absence of an authorized Origin, and an exact expected revision. Its persistence serializes against activation. CharacterStartingEquipmentService materializes actual ordinary inventory with a duplicate-prevention marker. CharacterAdvancementService applies independent persistent advancement, and CharacterInnateSpellLedgerService depends on verified committed state and revisions. These operations do not collectively provide a creation-finalization boundary.

Propose that Sheet own an explicit creation lifecycle on its character aggregate, independent of Site's archival/ownership lifecycle:

1. Editable creation draft: revisioned proposals, no granted characteristics solely because a proposal exists.
2. Confirmed creation configuration: server-verified complete configuration at specific revisions, fingerprint and relevant scope. Returning to draft or changing any contributing selection invalidates confirmation without undoing gameplay state.
3. Established character: finalization has committed; existing Origin protections and ordinary gameplay acquisition, advancement and resource rules apply.

New creation is initiated explicitly by an authorized Sheet operation. Bootstrap exposes server-derived lifecycle/capabilities. Do not infer creation from the open wizard or BuildInProgress. Existing characters with committed Origins, materialized starting equipment, advancement or gameplay resource history must not be silently reopened. Treat ambiguous legacy records conservatively (locked pending explicit review); design a separate reconciliation policy with the owner. The exact migration rules require approval.

Confirmation records exact relevant draft revisions, aggregate mutation revision, ruleset fingerprint and authorization scope; changes invalidate it. Finalization accepts expected revisions and an idempotency key tied to the exact request, not client-computed mechanics. Re-evaluate against Core, independently verify actor, ownership, current Site access/campaign context, verified parent evidence and scoped approvals. Under the Sheet aggregate lock, recheck revisions and current authority, and atomically materialize authorized Origin/event receipt, earned Life Experiences source occurrences, required advancement/ability configuration, and ordinary starting inventory using existing one-time grant semantics, then mark established. Fail closed if the catalog or scoped authority changes. Preserve owned inventory and spent resources; initialize ledgers only from verified committed state and actual progression. Return the original result for a repeated successful identical finalization request; reject reuse with different input. If any required effect can not be committed in the same transaction, use a durable idempotent recovery protocol and do not expose a partially finalized character as established.

Audit all existing mutation entry points against the lifecycle, including activation, structural editing, inventory materialization, advancement, resource expenditure and archived characters. Stage creation-only selections without duplicating existing authoritative state. Decide which ordinary edits are permitted during creation. Existing established Origin replacement protections must remain unchanged. Do not route initial creation reconsideration through reconstruction or introduce a UI-only bypass.

This boundary is **proposed, not implemented or approved**. Until approved, B–E may develop discovery, preview, decision UI and nonauthoritative drafts only. No production finalization, reversible Origin activation or new creation grants are authorized by this checkpoint.

## Remaining shared work and validation

- Wire default conventional workspace and method registry into the existing guided creator; preserve navigation state across feature rerenders.
- Generalize draft mutation/preview state, retain failed pending inputs, implement explicit retry and dependency reconciliation, distinguish read-only/established and contextual authorization.
- Complete D/E persistence and typed native DTOs; coordinate verified biological and earned Life Experiences server authority contracts.
- Provide generic configurable characteristic parameters and richer conflict-resolution controls. Current shared primitives only cover choice lists, allocation display, validation and dependency review.
- Replace transitional legacy presentation diagnostics and verify no internal IDs or fingerprints are exposed as normal content.
- Validate browser rendering, focus, keyboard, touch, responsive reflow, actual editor registration and server round trips on authorized test characters. No authenticated browser acceptance is claimed.
- Finish cross-workstream and lifecycle tests after the architecture decision. No main merge until explicitly approved.
