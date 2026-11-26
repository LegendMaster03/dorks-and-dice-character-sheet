# Workstream D — Construct Assembly adapter

**Status: partial implementation; not production integrated or accepted.**

Baseline: Workstream A code checkpoint `b31f4778f6765b65e9139977a2745ce94dd9e503`. Core catalog inspected at
`0baa466b7fc8f55e5d675b886f76e95f9a4b551f`.

The exclusive `features/origin/construct/` directory provides initial-only
draft input operations, strict native configuration/assembly decoding,
same-origin catalog/configuration/preview adapters and a shared method
editor. It does not mutate shared state or register another Origin store.
The editor can add duplicate component definitions as exact instances,
edit native option identities, retain unrelated selections, explicitly remove
affected relationships, show native resource/power/mass/operating evidence,
and select candidate characteristics from a current preview. Unavailable
previous selections remain visible until reviewed; no client issues grants.

## Coordination required with A

- Compose `constructMethodEditor` under the native method reference using
  the guided workspace registry. Supply canonical `ConstructDraft` (Origin
  proposal plus assembly input), exact revision and fingerprint, a single
  authorized persistence path, current preview, configuration cache scoped
  by fingerprint, and transient browser selection/query. Request configuration
  on demand and pass errors and pending/retry/conflict state through the host.
  The editor is not wired into the existing provisional guided renderer.
- The current draft service only stores the ordinary Origin proposal and
  evaluates it through the generic preview; do not store assembly in an
  independent mutable store or call that service as if it validated assembly.
  Add a coordinated method-specific payload to the canonical draft persistence,
  evaluate through native Construct preview on the server, retain revision and
  authorization guards, and re-evaluate on change. No localStorage workaround.
- A's lifecycle remains a blocking approval decision. No activation button or
  new initial grant path has been added. Do not treat a native Authorized
  preview as permission to persist established characteristics.
- Native method discovery does not publish initialConstructionAllowance;
  the current delegated method/preset/identity endpoints do not expose a
  trusted value to this editor. Supply an authored, fingerprint-pinned
  allowance through an additive bounded discovery contract (not a client
  constant, numeric text input or private rule payload). Until available,
  use null and keep the review incomplete.
- Native option search supports component/material identities but not authored
  component role facets; configuration discovery provides base capabilities,
  not complete component categories, material compatibility, source operating
  principle/restrictions, protected-region choices or all recipe-stage detail.
  Add bounded projections in Core and reviewed delegated routes as needed.
  The current adapter shows names and discovered capabilities, material candidates
  and native validation; existing region values and top-level explicit links
  remain read-only rather than fabricating choices. Source-specific runtime
  authority is unresolved. The native assembly quote contains aggregate resource
  quantities and known cost, not a per-stage manufacturing quote. Reuse the
  manufacturing/crafting evaluator and expose bounded results; no independent
  economy or client mechanical inference.
- Shared open-catalog and parameter choice controls need integration for
  non-finite requirements. This increment renders finite choices only and
  identifies unsupported choices as requiring the shared editor.

## Optional starting configurations

The four reference manifests under Rules Core `data/reference/` are not
runtime content. The delegated presets endpoint explicitly returns only Species
and Ancestry. Standard Medium, Small, Lightweight and Armored are therefore
not selectable presets. Their documented native reference quotations are 163,
147, 187 and 187 points. They are appropriate optional initial templates if
published with explicit content approval, a native discovery and load contract,
exact component/variant/material/link identities, current native allowance and
fingerprint, and native reevaluation before review. Do not publish them or
copy their prices or allowance into the production UI. No Core catalog
changes have been made.

## Verification

The isolated model tests are colocated with the feature. From the Client
directory, run:

```sh
tsc --ignoreConfig src/features/origin/construct/model.ts --outDir .construct-test-dist --module ESNext --target ES2022 --moduleResolution Bundler --lib ES2022,DOM --skipLibCheck
node --test src/features/origin/construct/model.test.mjs
rm -rf .construct-test-dist
```

Workstream A owns the shared test command and registration. Coordinate adding
these tests to the common command after the editor is wired. The model tests
exercise exact-instance identity, retention, removals, native quote status,
malformed responses and configuration identities. They do not exercise
actual native Core quotation or authorization. Full reference fixture
acceptance, operating restrictions, browser responsiveness, persistence
reload and resulting sheet mechanics remain outstanding. Local checkout
and .NET execution are not available in the current environment; run
the integrated suite and actual authenticated browser acceptance before
considering production activation.
