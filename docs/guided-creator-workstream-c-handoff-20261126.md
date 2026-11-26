# Guided Character Creator — Workstream C partial integration handoff

**Status:** Isolated biological native-exemplar draft editor and adapter implemented. Not wired into the guided creator; not an authorized biological construction or release candidate.

- Workstream A code checkpoint: `b31f4778f6765b65e9139977a2745ce94dd9e503`.
- Branch: `feature/guided-creator-biological-origin-20261126`.
- Specification: `docs/character-origins-guided-ux-design.md`.
- Shared checkpoint: `docs/guided-creator-workstream-a-handoff-20261126.md` on the A branch.

## Implemented

- `Client/src/features/origin/biological/model.ts`: explicit transmission plans, native biological combination adapter, separate effective expression and inherited-instance states, source-local finite choices, exact candidate/contributor preservation on change, native allocation status and charge evidence. No pricing or cardinality rules are synthesized.
- `Client/src/features/origin/biological/editor.ts`: exported `biologicalEditor` with exact native rule/method reference; Contributors, Inheritance & Expression, Configuration and Review; searchable source and characteristic lists; reusable shared navigation, allocation, validation, choice and change-review components.
- `Client/test/biological-model.test.mjs`: focused standalone Node tests. Tested exact Git blobs `541a1f6ee08c7d6d14a52a00593a9d3891b2df9d` (model) and `77de9ae1d87a27b4ed61aa5b6a931ec33039a10b` (tests) in a local snapshot using Node 22.16.0: **7 passed, 0 failed**. Fixture tests validate the adapter, not the Rules Core evaluator, persisted records, browser UI, or authorization. Node 24 (repository CI version) supports the standalone test directly.

Run from `src/CharacterSheet.Web/Client`:

```sh
node --experimental-strip-types --test test/biological-model.test.mjs
```

The shared build/test command was not changed, per A file ownership. Integrate the standalone suite into the shared test command during composition. Full frontend typecheck/build, backend integration and browser acceptance were not executed here (no checkout network access or .NET runtime in the local validation environment).

## Integration contract for A

Import `biologicalEditor` from the exclusive biological folder and register using the existing MethodEditor contract. Supply `BiologicalEditorContext` from the canonical Origin draft controller. `preview.result` in the shared EvaluationView must contain the complete native preview envelope (including top-level allocation) and its exact draft revision, ruleset and fingerprint; do not pass only the nested construction result. Supply native preset discovery and, where available, native presentation metadata callbacks for labels, descriptions and categories. The editor does not own another draft store. Existing direct or legacy mixed drafts require an explicit method switch or reviewed migration to an explicit transmission plan; never silently convert them. Mount only after the shared composition and server round trips are verified.

## Outstanding / blocking work

1. Workstream A lifecycle decision remains proposed, not approved or implemented. No production confirmation, activation, or finalization control is supplied. The existing initial activation service is irreversible and must not be reused as draft confirmation.
2. The current Sheet draft service calls the stateless ordinary native preview. Verified biological evaluation requires the Core `/origins/biological/verified-preview` route and an independent host provider for target creation access, exact parent profile revision, relationship scope and character-specific eligibility. The existing parent inspection/alignment routes explicitly do not verify parentage or authorize inheritance. Supply an independently authenticated provider and a bounded server-side gateway; do not accept client profile copies, approval claims or synthetic eligibility evidence. Native-only verified creation also requires this provider. Existing manifested-profile or scoped authority-bearing drafts are not editable through this partial editor.
3. Wire the canonical save/retry/409 controller and actual registration, retaining pending edits and protecting against stale responses. Confirm exact source occurrence matching after contributor replacement via Core and show newly discovered offers. Current change review shows retained and affected selections from the preceding evaluation only; missing saved occurrences remain unresolved, and prospective newly available offers are not computed.
4. Add native discovery/adapters for restricted offers, transmission capacity and contextual authorization, source-specific persistent catalog choices, inherited-gene combinations, explicit reconciliation and configurable linked parameters. The current editor only edits eligible offers from BiologicalCombination and finite creation choices. It presents evaluated expressions and validation but does not offer generic reconciliation or recombination decisions. No unsupported choices or authoring rules have been invented.
5. Complete full characteristic metadata, progression, category and dependency presentation, review details, accessible save failure and responsive browser testing. Native categories depend on host-provided metadata; generic fallback is Physical/Other.
6. Complete integration tests against pinned current Core/Site/Sheet builds, including actual Red/Bronze Breath Weapon, mixed Species/Ancestry, latent configuration, multiple donors, multigenerational authenticated inheritance, scoped approvals, recombination and dependencies, allocation gate, persistence/reload/revision conflict, and final authorized sheet mechanics. The current test fixtures do not satisfy these acceptance cases.

No Core pricing or mechanics changes; no shared component, application root, authorization or main changes. No merge requested.
