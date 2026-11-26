# Guided Custom Origin Completion — implementation and acceptance checkpoint

**Verdict: incomplete; do not merge or deploy.** The missing editor registrations, application composition and canonical draft transport have been implemented. Full method-specific runtime acceptance, production creation authority, and several advanced configuration contracts remain outstanding. This report records observed evidence, not a completed acceptance assignment.

## Exact revisions

| Repository | Revision |
| --- | --- |
| Character Sheet main (unchanged) | `a95f5f775c93b257f9530a972d4512d5c5f9d317` |
| Character Sheet implementation and browser-test checkpoint (preceding this report) | `44f10fe3cfb9ab0e7e37eff372e45a1914eb8a37` |
| Rules Core main (unchanged) | `28138c6b38157a9872999cfcfe40bf12dca8f914` |
| Site main (unchanged) | `aa15e85c5edf032275320b20442fe6c09501be5f` |

Branch: `feature/guided-custom-origin-completion-20261126`. [Draft PR #85](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/pull/85). All main revisions were rechecked at the end of this implementation checkpoint. The report-only commit does not alter the code or test inputs. No main merge, deployment, or production data mutation occurred.

## Implementation audit and delivered work

Read the approved UX design, integrated acceptance report, A/C/D handoffs, native API documentation and relevant Sheet/Core source. The existing editors already implement bounded native-exemplar biological construction and initial construct configuration; they were never mounted in the application. The original ordinary draft save evaluated every proposal through the ordinary Origin endpoint; it did not persist or evaluate assembly input.

| Requirement | Status / evidence |
| --- | --- |
| Exact native editor registry; typed application composition | Implemented; registry/context frontend tests pass. Conventional browser remains default. |
| Native biological exemplar contributors, distinct occurrences, inherited/expression/latent states, finite choices | Existing editor connected to canonical draft and full ordinary native preview including allocation. End-to-end custom browser acceptance incomplete. |
| Contributor change retention | Existing exact occurrence dependency review reused. Explicit transmission drafts excluded from older provisional mixed-source editor. Full real-native retention not yet accepted. |
| Native allocation and safe over-budget drafts | Existing allocation adapter connected to actual preview envelope; no browser pricing or hardcoded allowance. Real custom evaluation acceptance outstanding. |
| Characteristic categorization, descriptions, progression and arbitrary native configuration/reconciliation | Partial. Grouped finite-choice UI exists; full native metadata, reconciliation/recombination and linked/open eligibility adapters remain incomplete. |
| Verified existing-character contributors, multigeneration, scoped exceptions | Not implemented. Native host authority provider requires independent creation eligibility and exact parent relationship/profile revision evidence. Owner inspection is not parentage. |
| Construct exact component instances, native discovery/configuration, resources, power, validation | Existing construct editor connected to fingerprint-scoped catalogs/configuration and dedicated assembly preview. Runtime acceptance incomplete. |
| Construct durable persistence | Implemented within existing revisioned Origin JSON record, nested `constructAssembly` field, not a second store. Server separates it from native proposal, checks current native method allowance and sends exact assembly to the dedicated evaluator. Separate preview response. No EF schema migration required. PostgreSQL test added; initial run exposed store issue, corrected, full revalidation pending. |
| Initial incomplete and invalid draft preservation; navigation, reload, errors | Implemented source paths. Failed edit held in memory with explicit retry/discard and conflict detection. No browser localStorage. Unsaved edits do not survive full page reload or character change; saved edits use server state. |
| Switching methods | Explicit confirmation before replacing a supported saved proposal; the current draft is preserved until new save succeeds. Unsupported legacy proposals remain protected; no automatic conversion. |
| Ruleset updates | Explicit current-fingerprint review for supported drafts in the same ruleset; reload authority and exact revision, re-query construct allowance, preserve choices, native re-evaluation. Different ruleset requires migration. Not yet independently tested. |
| Read-only and committed Origin protection | Existing service/store gates retained. No new activation route or active grants. No generalized classification of established characters with uncommitted Origin. |
| Creation confirmation and finalization | Blocked by unresolved lifecycle/authority decision; draft validation is not activation. |
| Construct reference assemblies | Four documented fixtures not independently evaluated in this pass and not published as presets. |
| Whole-character native projection, desktop/mobile visual conformance and cross-feature regressions | Not accepted. Browser script exists and executes against real isolated services; only conventional scenario verified to date. |

## Coherent commits

- `c4507da24afa7ca73f4dd3d73f643d27d921db83`: canonical server construct evaluation and persistence.
- `4aa54c1ea4ae5817be5dd2a45da7e99e532a7e00`: client canonical method draft transport and separate preview handling.
- `60ba966379a094446c8440a78f8b3aebd9c364f7`: mount registered editors with typed application contexts.
- `04daf067e9fb582ca05b2506a15e4044e578c1b0`: save confirmation, pending retry/conflict, architecture boundary and tests.
- `a996ff8b6f0abcd326fe7e73e3f72acd0674e95e`: current guided browser acceptance and exact Core pin; retain old browser fixture code.
- `f07abec79f20c961f6a930a69e3c2ed7eac0cf96`: return revision conflict for repeated initial saves rather than EF tracking exception.
- `7fcaa059b29429d92c00425672fbf82277068229`: explicit same-ruleset stale draft review.
- `44f10fe3cfb9ab0e7e37eff372e45a1914eb8a37`: fix browser locator scope following actual Chromium failure.

## Validation evidence

Local Git access failed DNS resolution and .NET is not available in the chat execution container. Source edits use GitHub's revision-checked connector, and builds/tests run in the repository's actual GitHub Actions environments. No mocked render is presented as browser acceptance.

- [Validate run 45899150849](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899150849) at `04daf067`: **431/431 frontend tests**, strict typecheck, production Vite build, embedded lifecycle guard, and **158/158 backend unit tests** passed. PostgreSQL integration: **186/187** passed; new same-context first-save test exposed duplicate entity tracking when an expected revision of zero was retried. Fixed by checking existing draft under the existing character-root lock before attaching an entity. Container and HTTP smoke steps were skipped because the test failed. Later validation runs were superseded by subsequent commits.
- [Real native contract smoke 45899469187](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899469187) at `f07abec7`: passed. This is the existing native contract smoke, not proof of all custom Origin behaviors.
- [Pinned integration run 45899469408](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899469408) at `f07abec7`: failed at the existing Core/Sheet low-light vision reconciliation probe (exit 134). Earlier run [45898982381](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45898982381) reports "Sheet could not reconcile the Core low-light effect with its exact Origin grant." Cause and baseline attribution not yet established. Do not relax the provenance gate merely to obtain a pass.
- [Old browser run 45899150850](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899150850): actual Chromium failed waiting for the retired provisional Species Choose control. The old script is retained as historical fixture code; current workflow exercises the new guided UI instead.
- [Current guided browser run 45899469384](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899469384) at `f07abec7`: built all three actual applications, logged into isolated Site fixture, obtained delegated native catalog/methods and successfully saved an Orc conventional draft. Failed on ambiguous Playwright Biological reproduction Configure locator before entering that editor. The locator was corrected at `44f10fe3`. [Evidence artifact](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899469384/artifacts/15154818879) contains the recorded event JSON, logs and conventional desktop screenshot, not custom-workspace visual acceptance. No custom screenshot was reviewed.
- Latest code checkpoint `44f10fe3`: [full validation 45899717718](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899717718), [browser acceptance 45899717747](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899717747), [native smoke 45899717653](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899717653), [pinned integration 45899717719](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899717719). Frontend build and embedded lifecycle check passed at latest observation; other steps not completed at the report checkpoint. Report-only commit is marked skip CI to avoid canceling the existing code-head validation.

The new browser scenario includes native Red/Bronze inheritance, expression, replacement, save/reload; native structural, power and locomotion components and configuration; screenshots, and owner boundary checks. These scripted assertions are not reported as passing until the actual browser runs pass. Coverage still needs expanded finite configuration, latent allocation, exact component edits/retention, cross-guided-step navigation, failure recovery, and actual visual comparison against the approved mockups.

## Architectural decisions requiring approval

The previous integrated acceptance lifecycle decision remains unresolved. Site Active is identity/access state, not creation completeness; Sheet's existing BuildInProgress does not define an authoritative transition to established gameplay. The absence of a committed Origin does not prove that a character is new.

Recommend the prior Sheet-owned durable creation boundary: editable creation and established states, a revision-bound validation receipt, and conservative locked legacy classification for ambiguous records. Invalidate the validation receipt when relevant drafts or native rules change. Finalization must atomically verify character access, exact draft and aggregate revisions, native results and applicable authority, then establish grants, experiences, advancement and initial ordinary inventory exactly once. Do not call independently committing services in sequence and label it atomic. Existing committed Origins are not reopened.

The current Core endpoint `/origins/biological/verified-preview` requires a trusted `INativeOriginHostBiologicalAuthorityProvider` even for an empty set of manifested donor references. Its provider must independently verify target creation access, source identities and revisions, and applicable eligibility; the endpoint returns preview only, not a commitment. A parent relationship/consent or approval authority and verified profile transport must be defined and implemented; character ownership or inspection is insufficient. No fabricated authority, grants, exceptions, operating power or fuel were introduced.

## Remaining acceptance work

1. Complete the currently running suites and fix reproducible failures, including root cause of the low-light probe discrepancy. Verify the exact latest implementation revision.
2. Complete both real custom browser scenarios with screenshots, test navigation and reload, compare against approved visual designs, resolve actual UI/contract defects and expand regression coverage.
3. Implement general native configuration/reconciliation and appropriate metadata presentation; independently evaluate the documented construct reference assemblies without publishing them.
4. Obtain lifecycle/authority decision and implement the trusted host and atomic establishment path. Until that point, all custom results remain explicitly draft-only.
5. Do not merge or deploy until the assignment's completion criteria are independently met and the user authorizes it.


## Continuation checkpoint — runtime and native persistence remediation

Implementation head: `de9505bad9d9d62e7f76eab42cb6ba0f2680214b`. Core and Site pins remain unchanged. No main merge or deployment.

- [Full validation 45899717718](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45899717718) passed at `44f10fe3`: 431 frontend, 158 unit and 187 PostgreSQL tests, strict typecheck, production build, embedded lifecycle check, container build and HTTP smoke. This supersedes the earlier failed first-save test.
- `d31d09c69c032d6197d85e51750cda64cefee989`: fixed the actual guided sheet mount predicate, which still admitted only conventional drafts. Both custom drafts now remain in their registered workspace after saving; stale supported custom drafts also remain accessible for explicit review. Regression test exercises the production mount policy. Fixed the exact low-light canonical trait ID following the Core gene migration, retaining historical IDs and all grant provenance checks.
- [Browser run 45900245917](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900245917) at `d31d09c6` passed conventional selection and the Biological scenario through seven saved revisions, Red/Bronze contributors, native allocation, distinct inherited instance requests, expression request, contributor replacement preserving the unaffected selection, and exact persisted proposal after reload. Configuration and review screenshots were captured on desktop/mobile. [Evidence ZIP](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900245917/artifacts/15155029424). Visual comparison against approved mockups has not been performed. The run then failed Construct initialization with native HTTP 400.
- `dde8f8b2fc39e3067ffda6f121be468c2c447b6d`: pinned the cross-repository workflow to current Core main `28138c6b38157a9872999cfcfe40bf12dca8f914`, rather than the older integrated candidate, and added native Construct reference evaluator tests.
- `58f4a313c91e7ba2d23dac7b55fb28b36bf5ecc6`: corrected obsolete Drow Faerie Fire/Darkness test identifiers; real native ledger behavior remained unchanged. The pinned probe and mixed-Origin integration passed.
- `a29d9bee2e64e23e79d6e14e02cb75344dbec6d1`: added production draft-service real HTTP/PostgreSQL coverage for Red/Bronze exact transmission retention, and each of the four documented Construct assembly fixtures. Initial execution reproduced the same Construct failure as the browser; Biological test passed.
- `de9505bad9d9d62e7f76eab42cb6ba0f2680214b`: normalize named Custom intent to native enum value 2 at both save-time and reload-time request boundaries, without changing persisted input. Added exact forwarded wire value assertion.

### Exact code-head validation

[Run 45900654189](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900654189) passed at the exact implementation head and Core pin: native reference evaluator **12/12**, real Core HTTP plus PostgreSQL integration **19 passed, 1 skipped** (the existing S04 authorization-policy characterization). All four reference assemblies now pass both native evaluator and Character Sheet draft-service persistence tests, verifying native manufacturing quote, cost, size, exact JSON round-trip and no committed Origin. Standard Medium 163, Small 147, Lightweight 187, Armored 187. No test fixture was published as a selectable preset.

[Real native smoke 45900654241](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900654241) passed. [Full validation 45900654201](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900654201) and [browser 45900654285](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/45900654285) were still executing at this report checkpoint. Do not infer a browser pass from the native persistence tests.

**Remaining:** full latest-head CI; full Construct UI scenario and screenshot inspection; expanded finite/configured biological choice, latent cost, component edit retention, navigation and save-failure browser cases; native combination/reconciliation and richer characteristic metadata; approved authoritative creation lifecycle and parent/eligibility authority. The previous low-light failure and reference fixture evaluation tasks are resolved. The existing authorization-policy skip is not acceptance of that policy. PR #85 remains draft and unmerged.
