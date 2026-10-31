# Character Sheet native mechanics consumer — implementation increment

Upstream reference: `LegendMaster03/dorks-and-dice-character-sheet` main
`3ef460315fda49d3c71335fd7cb5b6b6056b2cef` (2026-10-30).

The new `rules-core-native-api.ts` calls **named Character Sheet backend endpoints**
through Character Sheet’s existing Tool Host upstream route. The Character Sheet
backend uses its server-side Site delegation gateway to call Rules Core. The
browser does not possess delegated Rules Core credentials. Requests communicate exclusively in native `rulesetId`, catalog fingerprint and native
entity IDs. It does **not** claim that a persisted legacy `RuleConceptKey` is the
same entity as a native ID. The old builder workflows are **not yet migrated**.

## Implemented native consumer operations

| Operation | Character Sheet backend endpoint | Delegated Rules Core target |
| --- | --- | --- |
| Load native catalog identity and capabilities | `GET /api/native-rules/ruleset` | `GET /api/mechanics/v1/ruleset` |
| Search class / subclass / patron / pact / invocation / condition summaries | `GET /api/native-rules/options` | `GET /api/mechanics/v1/catalog/options` |
| Evaluate a prospective native advancement selection | `POST /api/native-rules/advancements/eligibility` | `POST /api/mechanics/v1/advancements/eligibility` |
| Project a version-pinned native character | `POST /api/native-rules/characters/project` | `POST /api/mechanics/v1/characters/project` |

Each response is checked against the exact ruleset and fingerprint supplied by
Rules Core. `409` means the caller must reload the ruleset and re-evaluate the
current character; it must not silently change the snapshot mid-operation.
Options are reduced to ID/name/kind even if an accidental server response
contains more fields. Access denied and unsupported results remain explicit.
The client does not fetch raw native rule bodies, present Site delegation tickets to Rules Core, or use the Wiki private tunnel. The backend routes require a redeemed Character Sheet Site session and forward only four named operations.

## Tests and limitations

`Client/package.json` includes the new module and its tests in the existing
client test runner. An isolated TypeScript 5.8.3 compilation and five Node 22
mock-transport tests passed, using local type/transport stubs for the surrounding
repository. GitHub Actions subsequently validated the full Character Sheet build and tests for the initial client branch; the revised backend transport still requires its own CI and live Site/Rules Core acceptance.

This module is an **implemented API client**, not a completed Character Sheet
migration. Remaining priorities:

1. Replace legacy `searchResolvedRules`/`resolveRuleConcept` callers in builder,
   conditions, inventory, spells and advancement workflows with native ID-based
   requests, preserving current user-facing semantics where supported.
2. Implement explicit migration/identity crosswalk for existing saved
   `RuleConceptKey` selections; preserve unknown entries and rollback capability.
3. Convert the persisted per-occurrence advancement history to the native
   cumulative class-level input without losing acquisition order, subclass
   relationships, hit-point rolls, or option targets. The native kernel currently
   rejects repeated class entries, so a direct DTO rename would be incorrect.
4. Map native projection statuses/resources/features into Character Sheet's
   presentation and save/reload flows. Do not interpret `partial` or `unsupported`
   calculations as resolved zeros.
5. Implement campaign-effective rules separately; the current native endpoint
   advertises `campaignEffectiveRules=false` and is not a substitute for
   the old campaign-specific mechanics routes.
6. Verify Site-issued delegation, real responses, state persistence and
   end-to-end user workflows on the development deployment before calling
   Character Sheet functionality restored.

External-rule import and converted monster content are not prerequisites to
this Character Sheet consumer migration.
