# Pillar 2 Workstream B — Player-Facing Origin Construction

**Repository:** `LegendMaster03/dorks-and-dice-character-sheet`  
**Base:** `feature/innate-spell-ledger-persistence-20261116` at `82ad0a625b99c463df195bee2af4b3441ee48a53`  
**Working branch:** `feature/pillar2-origin-construction-ui-20261119`

## Boundaries and audit

The owner-scoped draft service persists proposal JSON only after a Site-authorized access check and a delegated native preview. It uses a compare-and-swap revision. `GET /origin/draft` returns the last draft without executing grants; saving returns a `previewOnly: true` evaluation. Existing conventional initial activation requires a separate server-side re-evaluation of the stored draft. The narrow approved Human–Elf route independently checks its DM authorization; this work does not call or extend it.

Before this work, the UI supported: direct Species/Ancestry selection and supported single native creation choices; two unverified biological source references in a mixed draft; draft save/reload; native issues; a restricted read-only owned Character-source comparison for the two-source case; and the existing direct activation operation. The UI did **not** enumerate all characteristic offers, distinguish selected from merely offered candidates, edit mixed candidate selections, display candidate-level provenance or selected parameters, or present multi-valued finite choices. The source editor could not express more than two exemplars despite the generalized native contributor contract.

The native Rules Core preview already returns evaluated candidate identities, source occurrence paths, native offered values/parameters, selection bases, requirements and unresolved evidence. Native option search only provides Species/Ancestry/Skill catalog identities, not a general authenticated characteristic-selection eligibility or parameter-editor contract. The distinction matters: client discovery is **not** the acquisition authority.

## Supported player paths after this work

| Path | Supported after this work | Authorization |
| --- | --- | --- |
| Conventional Species/Ancestry | Select preset; make existing authorized native choices; inspect offers; activate through unchanged native initial service | Existing first-party direct activation only |
| Mixed biological source draft | Select up to four native Species/Ancestry source *references*; inspect associated offers; select/deselect multiple candidate IDs; save/revise finite native choice selections | Draft only; no grant or general activation |
| Existing two-source parent inspection | Compare both source IDs with authenticated owner-visible Character profiles by existing inspection endpoint | Inspection only; no parentage assertion |
| Mixed approved Human–Elf | Unchanged restricted, campaign-scoped DM process and contract | Not generalized by this work |
| Other methods and custom/nested origins | The raw universal model is preserved, but the basic editor will not overwrite unsupported evidence | Requires an additional trusted editor and server contracts |

A newly added source remains a typed native exemplar, not an authenticated individual contributor. Four is a **bounded UI support limit**, not a biological or architectural cardinality rule. The existing two-source parent-alignment UI is deliberately disabled for drafts with more than two source slots; it does not silently substitute a native source ID for a real Character identity.

## Exact characteristics and editing

- The client reads native evaluated candidates without copying them into `proposal.candidates` or copying offered values/parameters into `proposal.manifestations`.
- Cards distinguish *available offer*, *selected draft characteristic*, and *native preview manifestation*. Each shows candidate/definition identity, source ID, occurrence path, optional contributor ID, eligibility text, native authored name and description, optional selection explanation, unresolved evidence, and creation-choice count.
- Expandable evidence includes the complete native offered value (including progression fields when offered), offered parameters and previously selected parameters. Native relationships and constraints are shown only when returned by the evaluated proposal. No local progression rule or mechanical valuation is invented.
- Supported mixed candidate selection persists only `{id: "selection:" + candidateId, candidateId}`. A finite native choice adds only `{requirementId, selectedOptionIds}`. A selected candidate is **not** an authorized characteristic.
- Native finite choice requirements support single and multiple selection up to the authored maximum, validating option membership and unique IDs. Unsupported open-catalog requirements remain visibly unresolved; the existing directly authorized single native Skill flow remains separate.
- Source changes clear prior manifestations, including selected candidate IDs and choices. Switching between direct and mixed resets source and choice evidence with a visible warning; existing committed state is immutable through this UI.
- The client requires matching draft/catalog/preview ruleset IDs and fingerprints for candidate and choice edits. Changing native content requires explicit re-review. Stale authorized-looking direct previews can no longer trigger an activation request.
- Draft saves use expected revisions and fail closed on HTTP 409 without replacing a newer draft. A reload control is displayed after concurrency conflicts. Empty/partial drafts remain persistable.

## Deferred server and gameplay dependencies

1. A native authenticated source/candidate discovery contract for arbitrary nonbiological contributors, actual parent-character manifested profiles, and trusted broader characteristic eligibility.
2. Reconciliation and inheritance policy for mixed selections, together with any balancing rules, restrictions, and explicit resolution actions. Native unresolved decisions remain unresolved.
3. Authored parameter-option/range contracts and validators before allowing the client to change arbitrary `selectedParameters` or `selectedValue`; offered parameters alone do not establish safe selectable alternatives.
4. Authoritative adapters for unsupported catalog choices, including Origin Feats and non-Skill open catalogs.
5. A generalized production activation/DM authorization contract, separate from the narrow Human–Elf route; no such authority was added.

## Validation and evidence

Focused Node tests cover native candidate selection/deselection, untrusted payload rejection, multi-source references, ancestry-lineage offers, finite choice bounds and revision, reload persistence, 409 revision conflicts, preview failures and stale catalog checks, plus preservation of conventional direct activation. Existing API tests cover no active grants and server-only state rejection.

Full push validation uses `.github/workflows/validate.yml`: frontend test/build/typecheck, .NET solution unit and integration suites with disposable PostgreSQL, Compose validation, container build and HTTP smoke tests. The latest successful run should be linked in the workstream completion report.

The separate isolated three-service browser acceptance workflow is opt-in (workflow dispatch or PR to main) and was **not executed as part of this branch's push validation**. No screenshots are claimed. Browser acceptance remains a separate verification step and should compare the owner editor with the native preview and persisted draft under real Site authentication; it must not conflate the existing S04 known gap with a pass.

## Scope compliance

Only Character Sheet client model, workflow, rendering, focused tests and this workstream note were modified. Rules Core and Site were read for contract verification but not changed. Production DM authorization, mixed activation, S04 controls, runtime resource use, and Pillar 4 were not modified. No merge or deployment is part of this workstream.
