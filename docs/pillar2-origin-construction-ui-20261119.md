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


## Adversarial self-review (2026-11-19)

This review was performed after the initial successful Character Sheet run
[44468062732](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44468062732).
The review corrections retain the same client-only ownership and do not amend
any server authorization, migration, Rules Core source, or Site code.

### Confirmed defects and corrections

1. **Preview/source binding:** a preview with the same catalog fingerprint could
   offer a candidate from a different contributor snapshot. Mixed editing now
   requires an exact proposal ID, intent, method, contributor IDs, source references,
   saved manifestation/choice selections, and matching draft revision. Even
   direct activation now rejects an otherwise authorized-looking preview whose
   evaluated proposal identity differs from the persisted draft. Candidate IDs
   must match native source occurrences; malformed and duplicate IDs fail
   closed. This is a proposal-snapshot consistency check, **not** a replacement
   for Rules Core lineage/eligibility adjudication.
2. **Preview semantics:** an evaluated manifestation request was treated as though
   it necessarily appeared as a grant in the evaluated native profile. The UI
   now differentiates an available offer, selected draft request, evaluated
   manifestation, native preview profile grant, and committed state. The last
   two are **not** interchangeable; a preview grant is never active.
3. **Unusable mixed Skill chooser:** an unsupported mixed open-catalog Skill
   choice previously displayed the direct-only chooser action. It now explicitly
   identifies the missing native authorization adapter without offering an inert
   control.
4. **Draft revisions:** unchanged source selections previously cleared valid
   selected manifestations and choices. They now remain untouched. Mixed
   characteristic requests no longer lose choices when reselected. Selections
   no longer offered by the current native preview remain visible as unresolved,
   and the owner can remove them without injecting a replacement grant.
5. **Long native evidence:** complete offered values and parameters now use
   wrapping, formatted JSON under an expandable details element rather than
   overflowing as unbroken inline payloads.

Adversarial tests were added for mismatched source/intent/method/contributor
snapshots, malformed and duplicate candidate IDs, missing/altered occurrence
paths, re-selection preservation, selection removal after an offer disappears,
preview grants versus unresolved manifestation requests, read-only access,
same-fingerprint stale data, and delayed competing Character loads.

### Client and server authority

- The owner UI persists only untrusted proposal selections. It never sends
  a native evaluated profile, parameter overrides, table decisions, or grant
  receipts as committed mechanics.
- Character Sheet's draft service independently checks Site owner/lifecycle
  permission, reevaluates proposals through Rules Core, and compares persisted
  revisions. An HTTP 409 leaves the last loaded proposal unchanged; retry
  requires loading the new revision.
- Conventional activation is still a separate server-side re-evaluation of
  the exact saved draft under its existing first-party restrictions.
- The production mixed Human–Elf path continues to require its exact two-source
  *plain* draft and independently scoped DM verification. A three/four-source
  mixed draft or a mixed draft containing selected manifestations does not
  qualify for that existing route.
- Owner-side native Species/Ancestry source references are **not** authenticated
  parent Character profiles, even when a read-only source alignment succeeds.
- Four is only this editor's source-reference limit; the universal Origin
  contributor and input-role contracts remain unmodified.
- The known S04 revocation-after-evaluation security gap remains **open**;
  browser testing must not report it as repaired. This work does not implement
  S04 or alter the restricted production approval path.

### Integration relationship

At final review, Workstream A was at
`3ff1ef89b3e3900d4d2f6c42098afd0724d6b580` and Workstream C was at
`002ccef2fa2c0f25ae299f69a822ed25bb5e3c7a`. These are newer than
the revisions listed in the incoming review prompt. Neither branch was
integrated here.

- Workstream B currently depends on existing native preview/deconstruction,
  candidate occurrence/source identity, and draft persistence contracts, not
  on the unmerged A/C implementation.
- A extends post-reconciliation prerequisite closure and dependent resource
  consistency. Its future evaluated relationships and unresolved issues must be
  rendered accurately, but this UI makes no reconciliation decisions.
- C adds read-only passive native entitlement projection. It does not establish
  authenticated Character Sheet entitlements, effective mechanical totals,
  spell spending, combat execution or new construction permissions. No client
  consumption of this unmerged projector was added.
- A's exact final head passed [full hosted validation
  44469764137](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/44469764137)
  (140 Host, 582 Mechanics and 808 Python tests).
- C's exact final head passed [isolated hosted validation
  44469602610](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/44469602610)
  (12 focused, 583 full Mechanics, 140 Host and 808 Python tests).
  The earlier C documentation text saying its .NET tests were unexecuted was
  superseded by this hosted run.
- These checks validate A and C **separately**, not their combination or the
  Character Sheet user experience against that combined Rules Core revision.
  Integration acceptance must test their actual final HEADs together.

### Browser acceptance blocker and independent procedure

The review environment has Chromium/Playwright but no `dotnet` executable,
no Docker daemon/CLI, and no network DNS resolution for GitHub cloning. The
existing browser acceptance script launches three real .NET services and
requires an isolated PostgreSQL service. Therefore this review cannot truthfully
claim execution of end-to-end Site/Rules Core/Character Sheet browser acceptance,
nor can it provide actual screenshots from that flow. The normal branch push
workflow validates the frontend, integration tests, image and HTTP smoke tests,
but does **not** run the browser script.

For a runner with .NET 10, Node, PostgreSQL, Site/Rules Core checked out, a
locally trusted disposable TLS certificate and a Chrome/Playwright install:

1. Use GitHub Actions > **Pillar 2 / Isolated Three-Service Browser Acceptance**
   > **Run workflow**, selecting the current Character Sheet feature branch.
   The workflow already defines disposable PostgreSQL, builds three services,
   and invokes `python tests/browser/pillar2_acceptance.py`.
2. Inspect `.pillar2-browser/result.json`, browser screenshots, and three
   service logs from `pillar2-isolated-browser-evidence`. Verify expected
   S04 entries remain `OPEN-GAP`, not `PASS`.
3. **Additionally** exercise the new three/four-source cards, individual
   candidate toggles and source paths, finite multi-select, save/reload,
   stale preview, 409 revision conflicts and read-only owner contexts. The
   existing narrow browser script does not automatically assert these new UI
   controls. Compare current native preview and persisted draft revision/data
   with the visible cards; confirm no unauthorized activation requests.
4. The workflow's pinned Rules Core and Site SHAs are historical acceptance
   fixtures. Re-pin both to the integration-reviewer's explicitly selected
   A/C integration heads and Site revision **in an isolated review workflow**
   before treating it as cross-workstream acceptance; do not assume that
   running its present pins validates unmerged A/C together.

### Review disposition

The scoped client corrections are committed to the Workstream B branch.
Successful Node/.NET/hosted checks are the executed code evidence; actual
three-service browser acceptance and A/C cross-repository integration remain
independent acceptance tasks. No merge or deployment is authorized by this note.


## Final bounded independent-review follow-up

The baseline verified at `b5184301cd58dc78357d752be12a228f1f1e4890`
was independently re-read alongside the existing Character Sheet services,
the native Rules Core construction engine, and the browser workflow pins.
Two additional client-only consistency defects were corrected:

1. Native candidate cards already failed closed on malformed or duplicate
   candidate identities, but the separate mixed finite-choice reader still
   accepted choice requirements from the rejected offer list. Mixed finite
   choices now require that the entire offered candidate set passes the same
   read-only candidate provenance check. This does not grant eligibility.
2. Basic source replacement could silently discard unfamiliar contributor
   fields. Contributors with unsupported metadata or process-specific role
   evidence are now noneditable in this basic source-reference editor. Their
   original proposal remains persisted and intact for a future trusted editor.

The snapshot comparison now also rejects additional/duplicate *evaluated*
choice records absent from the saved mixed proposal, while allowing legitimate
server-derived constraints, native inferred intrinsic values, authenticated
candidate selection evidence, and native Ancestry lineage. Ordinary Species
sources must match their offered Species occurrence; ancestry-to-ancestor
validation continues to belong to Rules Core.

Regression tests verify malformed mixed offer lists cannot supply finite
choices, a later preview cannot inject player selections, source-specific
evidence is not discarded, unavailable persisted selections can be removed
via the complete client workflow, and a late successful save response cannot
replace a subsequently loaded draft. Existing tests cover ordinary Species
selection/activation, multi-value choice persistence, revision conflicts,
read-only editing, earlier character requests, direct preview identity,
and unresolved mixed authorization.

[Hosted corrected-code run 44473381095](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44473381095):
377/377 frontend, 158/158 .NET unit, 183/183 .NET integration;
frontend build, disposable PostgreSQL, Docker image and HTTP smoke all pass.
The CI run does **not** execute a real authenticated three-service browser.

The opt-in browser workflow retains these **historical pins**:
Rules Core `23919c3ce00825f2da6204f218786957e9f1f1c2`,
Site `e8d0cb7d89de71aa337293b8f36d22bac98687c3`.
These revisions must not be claimed as combined Workstream A/C.
The current review environment has Chromium and Node, but lacks both
`dotnet` and Docker, and the available GitHub connector has no workflow
dispatch operation. The browser acceptance script therefore was not run.
Workstream D should dispatch the isolated workflow on the reviewed Sheet SHA,
inspect its evidence artifacts, and supplement the existing narrow script
with the newly added multi-source, candidate toggle, finite choice,
persistence, concurrency and read-only UI assertions. Combined A/C
acceptance requires a separately pinned integrated Rules Core revision.

S04 remains an explicitly open gap. This work changes no production
authorization, Site, Rules Core, Pillar 4 or gameplay mechanics.
