# Pillar 2 remediation — Guided Setup status acceptance decision

**Date:** 2026-11-19  
**Scope:** Character Sheet PR #70 (`fix/pillar2-origin-draft-retention-20261119`)  
**Source:** Independent `pillar2-remediation-browser-retest-20261119.md`  
**Decision:** Retain the existing shared **Resolved** status label. The owner accepts `Origin · Resolved` as the expected Guided Setup display for a committed initial Origin.

## Original finding and revised acceptance

The independent retest against Character Sheet `eba8418fd1841d901a281e425945fe8e0271150d`, Rules Core `6987ea9f39accbcfe7d5ce365e0f6ce3d27beac1`, and Site `e8d0cb7d89de71aa337293b8f36d22bac98687c3` verified:

- **P2-01 — Pass:** Unchanged contributors preserved their exact selections and finite choices through edits, reloads, stale evidence, and revision conflicts. Mixed drafts did not become active.
- **P2-02 — Functional behavior passed; literal-label test failed:** A conventional Orc activated, committed revision 1, and reloaded all nine grants. Its Guided Setup navigation showed `Origin · Resolved`, not the originally requested `Origin · Complete`. Incomplete Human, uncommitted draft, absent Origin, downstream section status, and effective-mechanics warning checks passed.

The original test's reported failure remains part of the independent evidence; it is **not retroactively represented as a passing run**. This decision revises the presentation acceptance criterion to:

> After a valid conventional initial Origin is committed and reloaded, Guided Setup must show **Origin · Resolved** (internal status `resolved`), while separately reporting that broader effective character mechanics remain incomplete. Uncommitted or unresolved Origins must show **Origin · Incomplete**.

## Rationale and existing implementation

- `getGuidedBuilderSectionStates` in `src/CharacterSheet.Web/Client/src/ui/sheet-model.ts` uses the shared status `resolved` after a committed Origin exists and legacy Species reconciliation does not block it.
- `guidedStatusLabel` in `src/CharacterSheet.Web/Client/src/ui/sheet.ts` consistently renders `resolved` as **Resolved** for Guided Setup sections. No Origin-only exception or global relabeling is needed.
- `src/CharacterSheet.Web/Client/test/guided-builder-routing.test.mjs` asserts the `resolved` state for a committed Orc and `incomplete` for the relevant uncommitted cases.
- **Resolved** means the *initial construction step* is finished, not that the entire character is mechanically ready or that Pillar 2 is complete.

The independent retest recorded 318 passing browser checkpoints and one literal-label failure before this decision. The owner has now accepted the observed wording; the code behavior is unchanged and no browser rerun is claimed by this documentation-only decision.

## Remaining scope

P2-01 is independently verified. P2-02 is accepted under the revised status-label criterion. General mixed Origin activation, Origin Feat implementation, S04 authorization, Pillar 4 gameplay, and broader mechanical execution remain outside PR #70.

This record does not authorize merging PR #70 into the Pillar 2 target branch or `main`, and does not alter production authorization or deployment pins.
