# Guided Character Creator — Workstream B integration handoff

Status: isolated conventional editor candidate; not mounted in the production guided creator, not a release, and not an authorized finalization implementation.

## Baseline

- Shared code checkpoint: `b31f4778f6765b65e9139977a2745ce94dd9e503`.
- Native Core main inspected: `0baa466b7fc8f55e5d675b886f76e95f9a4b551f`.
- Design: `docs/character-origins-guided-ux-design.md`.
- Workstream A handoff: `docs/guided-creator-workstream-a-handoff-20261126.md` on the shared branch.

## Adapter integration

Import `createConventionalWorkspace` from `features/origin/conventional/workspace.ts`. Create one instance per current character, with the actual authorized Origin workflow and active environment. Invoke `load()` after its native ruleset metadata becomes available and again after an intentional snapshot refresh; mount `render()` in the existing `species` guided step. Retain the adapter instance across ordinary app renders. Dispose/replace it when changing character. A host state notification must rerender without replacing the persisted draft.

The `ConventionalHost` contract is intentionally explicit:
- `current()` returns the canonical OriginUiState; no duplicate selected Species/Ancestry.
- `canEditDraft()` is a server-capability-derived indication of permissible draft editing, not wizard visibility or assumed lifecycle. Until established-character/legacy classification is defined, fail closed for ambiguous existing characters.
- `save(proposal, expectedRevision)` must invoke the existing authorized draft persistence path, verify current character and current ruleset/fingerprint and use exact revision checks. It must refresh the canonical state after a successful save; failures must not be reported as success. Expose this operation through the A-owned shared Origin workflow, not a parallel store.
- `retry()` must reload/reconcile after conflict, never silently rebase stale pending writes.
- `notify()` triggers the shared app render.
- `methodEditors` and `methodContext` supply exact native registered editor adapters from C/D. The conventional workspace never creates an empty custom draft merely by inspecting a method.
- `describePreset` and `labelRule` may use a bounded native presentation provider if available. Do not read private raw definitions through delegated ingress.

The adapter implements hierarchy/search, native identity proposal inspection, preview-only characteristic presentation, direct required choices, catalog discovery, source-occurrence/requirement-based retention, destructive-change review, failed-save retry/discard, and initial method dispatch. It never calls activation or mutates learned Upbringing. It can inspect restricted/nonselectable nodes as hierarchy metadata, not offer them as valid selection.

## Shared/native contract gaps requiring coordination

1. Native method discovery emits nullable `stage` (for example Biological reproduction and Construct Assembly). A's `NativeMethod.stage: string` and its decoder currently reject the entire native response when any method stage is null. Make the field nullable and retain it in decoding. Test with actual Core response; only explicit post-origination stages are excluded from initial workspace and dispatch still requires exact editor registration plus supported initial preview type. Native method descriptions are not included in the delegated projection; provide bounded description data rather than hardcoding a second method taxonomy.
2. Preset discovery returns ID/name/kind/parent/selectability/required-Ancestry flag/default Upbringing/acquisition IDs, not native description or complete ancestry allowlist. Full descriptions and contextual restriction explanations need a bounded presentation extension. Core remains the authoritative selection validator, including acquisition methods, allowed ancestry and allocation. No invented playable definition or mechanics fallback.
3. Direct preview endpoint already returns additional typed mechanical projectors, but Sheet's NativeOriginPreview only types result/profile/issues. The adapter renders native candidate descriptions, source provenance, scalar effective intrinsic values, reported native movement modes and level fields; fuller physical/physiological detail, rich structured progression, conditional movement and presentation of native names require coordinated typed presentation adapters. Preview data is not effective character mechanics.
4. Catalog discovery reports `available` (not `supported`) and flags character-specific authority for Origin feats. The adapter supports native finite and ordinary catalog choices only, and does not claim that membership authorizes a feat. Implement the separately trusted character-specific feat eligibility adapter before representing that choice as complete.
5. A owns the production composition point, persistence adapter, lifecycle/confirmed creation configuration and whole-character review. Do not reintroduce the old irreversible activate action as confirmation. The common creation-finalization boundary must be approved, implemented and independently tested before any authorized resulting character mechanics can be claimed.
6. The custom method editors and Life Experiences workflow are independently owned by C/D/E; no implementations or grants for those are included here.

## Validation

`test/conventional-origin.test.mjs` uses bounded projections from actual native Orc, Elf, Human, Tiefling and Dragonborn definitions at the pinned Core commit. It covers native selectable and nonselectable roots, required ancestry, nested ancestry, contextual search, optional cultural suggestion, identity proposal, malformed hierarchy, initial/post-origination method dispatch classification, stale preview rejection and an explicitly synthetic acquisition-gate boundary. No published Rite-only ancestry was found in the inspected catalog. That boundary test is not a playable example.

The separate branch-scoped workflow `.github/workflows/validate-conventional-origin-workstream-b.yml` compiles the workspace with strict TypeScript and executes the focused tests. The first run at `a221548cfa360bf87e57b4d2157e4343bd54e44b` passed. Register the test in A's shared test script at integration, then remove the temporary branch-only workflow.

Remaining acceptance: actual native previews for finite/catalog choices, preservation of a configured shared occurrence across native Ancestry change, restricted acquisition behavior when a published or authorized Core test fixture is available, authenticated draft save/reload and error/conflict recovery, actual browser/responsive/accessibility review, and post-finalization sheet mechanics. None of these is claimed as tested here. No Core changes, shared navigation changes, gameplay grants, production deployment, or main merge.
