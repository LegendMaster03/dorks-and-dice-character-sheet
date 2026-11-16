# Dorks & Dice — Character State Inheritance Increment 4: Original 32 Acceptance Scenarios

**Source:** Original Increment 4 implementation prompt in the Rules Content conversation, before the first implementation chat. This restores the checklist the later implementation report stated had not been recovered.

**Purpose:** Reconcile the existing Character Sheet and Rules Core implementation against each original acceptance scenario. Do not regard synthetic-source test success as proof of production entitlement authorization. Record test references, CI evidence, and any unsupported production authority for each scenario.

## Persistence and inheritance

1. An ordinary character with no alternate sheets loads successfully.
2. A verified persistent Werewolf sheet is stored and reloaded without creating another character identity.
3. Wolf and Hybrid inherit their parent's applicable progression without duplicate ownership.
4. Wolf-specific progression remains separate from Hybrid.
5. Updating the primary Class progression changes dynamically inherited values without rewriting alternate-sheet records.
6. A reversible influence expression change does not delete earned progression.

## Transitions

7. An authorized Wolf activation produces an atomic, durable transition.
8. The activated Wolf state remains identifiable after a database reload.
9. Authorized reversion returns to the latest normal character state.
10. Reentering Wolf retains persistent progression.
11. A Wolf-to-Hybrid switch commits the complete transition or none of it.
12. A stale expected revision is rejected.
13. A duplicate event ID does not create another transition or duplicate resources.
14. Simultaneous conflicting transitions cannot both succeed.
15. A failed authorization or mechanical evaluation produces no state mutation.

## Resource integrity

16. Primary, sheet, influence, and manifestation-owned resources preserve their identities.
17. Ending an activation does not silently refill persistent resource pools.
18. A different activation cannot acquire a prior instance's private resource pool without independently authorized rules.
19. Changes to effective resource maxima do not silently change current quantities.
20. Unsupported current HP or HP-transfer policies remain unresolved rather than being guessed.

## Origin and provenance

21. A reconstructed authoritative Origin is respected during active-form resolution.
22. Reversion never restores a superseded historical Origin snapshot.
23. Existing Origin activation, receipts, and verified projection paths continue functioning.
24. Unauthorized or mismatched source occurrences and progression receipts fail closed.
25. Stale native-content identities or incompatible ruleset versions are detected rather than silently accepted.

## Authentication and effective-state projection

26. An unauthorized actor cannot read or mutate another character's state.
27. Archived characters remain subject to existing lifecycle restrictions.
28. An untrusted request cannot submit an authoritative alternate sheet, grant, ledger, or DM ruling.
29. Reloaded state produces the same effective mechanics and provenance as the successfully committed transition.
30. Unsupported form-specific mechanics are reported as partial or unresolved, not playable.
31. Two simultaneous effective-state reads do not change the character's revision or resources.
32. A restart of Character Sheet does not lose or reconstruct active manifestation state from obsolete snapshots.

## Review method

For each numbered scenario, record:

- **Status:** complete / partial / blocked / untested.
- **Evidence:** exact test method(s), files, CI run, and any supporting source authorization.
- **Test type:** unit / synthetic-source integration / real PostgreSQL and Rules Core HTTP / production-authority validation.
- **Remaining gap:** concrete implementation or authoritative policy required.

Do not mark a scenario production-complete merely because a synthetic test-only source authority returned approval. Conversely, do not block purely infrastructural scenarios solely because first-party playable Werewolf or Vampire Lord rules are not authored.

## Current branch references (verified November 15, 2026)

- Character Sheet: `feature/character-state-inheritance-increment4-20261114` at `b27e7d2db4f15bfbbf75d7f297c1f87befa82632`.
- Rules Core: `feature/character-state-mechanics-adapter-increment4-20261114` at `2de1587037ed469289131e8bc53d17875ed2b387`; draft PR #158.

These refs are starting points, not instructions to merge or deploy.

## November 16 execution and status cross-reference

The authoritative, numbered **32-row evidence matrix** remains in
[character-state-increment4-integration-20261115.md](character-state-increment4-integration-20261115.md#original-increment-4-32-scenario-acceptance-reconciliation).
It identifies exact tests, test types, CI evidence and outstanding authority
for each original scenario. Preserve the original scenario language above.

At the current tested branch revisions: **23 complete, 9 partial,
0 wholly blocked, 0 untested**. The partial scenario IDs are
**2, 6, 7, 9, 21, 22, 24, 26, 27**. In particular:
`SiteRevocationAtCommitBoundaryIsNotFencedByEarlierFreshReads` is
deliberately **negative** security evidence for 26/27 and not a passing
commit-time fence. The two new bounded-live-response tests verify a
fail-closed transport limit, not supernatural entitlement. See the
integration record's November 16 follow-up for the exact paired CI runs
and the unapproved Site-owned commit-consistency contract.

### Final tested revision

Character Sheet `66d738f9a0a2282f148c4d961527396ac5c872f8`
passed [full validation 43950164734](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43950164734)
(136 unit and 231 integration; no skips) and
[paired real Core/PostgreSQL 43950164501](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43950164501)
(15 Character State and 13 Mixed-Origin; no skips).
This includes unknown-length and interrupted live Site HTTP response
fail-closed regressions. Existing scenario classifications remain unchanged.
The commit-time revocation characterization uses synthetic Site scope
rather than a live distributed transaction; it must not be used as
positive authorization acceptance.

## November 16 second continuation (final code evidence)

The exact original 32 scenario numbers and classifications remain unchanged:
**23 complete, 9 partial**; partial **2, 6, 7, 9, 21, 22, 24, 26, 27**.
The authoritative per-row test-method and remaining-dependency matrix
is in [the integration record](character-state-increment4-integration-20261115.md#original-increment-4-32-scenario-acceptance-reconciliation).

Character Sheet code at `d2eed8c753022880d34954502ccc45d6b8cdb0b4`
passed [full validation 43962922741](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43962922741)
(136 unit, 231 integration) and
[paired real Core/PostgreSQL 43962922699](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43962922699)
(15 Character State, 17 Mixed-Origin). Site at
`0eeaf232e2a3a11d4aac495c58b85ef6e3799a0e`
passed [595 tests 43960608984](https://github.com/LegendMaster03/dorks-and-dice-site/actions/runs/43960608984).
All had zero failures and skips.

Additional evidence improves structural source identity, live Site DM
decision snapshot consistency, and rejection of stale Origin review
candidates/method aliases. PostgreSQL `jsonb` canonicalizes exact
duplicate keys, so tests for those belong at the raw JSON boundary;
only persisted structures and case aliases that survive PostgreSQL
normalization count as PostgreSQL tampering evidence. No change
advances scenario 21/22 reconstruction or closes 26/27 commit fencing.

## Latest incremental security evidence — independent review pending

Character Sheet code `d322c3811c558cae6da8af26f40295cbabe94e3d` passed [136 unit + 236 integration in CI 43965325731](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325731) and [15 Character State + 17 Mixed-Origin in CI 43965325732](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325732), with no failed/skipped tests. Direct-Origin method case-alias/extra fields are rejected before Core preview; initial/mixed production commands now carry an exact saved draft identity for PostgreSQL commit validation. The guard is optional for legacy internal synthetic commands, not a Site cross-service fence. Independent WorkChat testing remains **pending**, with a scoped handoff at [character-state-increment4-workchat-independent-validation-20261116.md](character-state-increment4-workchat-independent-validation-20261116.md). The original **23 complete / 9 partial** classifications are unchanged.

At code commit `cd556874a3d705631a108026ef19c190e67c0f7c`, two direct and two real Core/PostgreSQL mixed-Origin service races now verify that same-revision draft proposal/fingerprint tampering denies activation before any receipt. [Paired run 43966396008](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43966396008) passed 15 Character State + 19 Mixed-Origin tests, zero skips. Independent WorkChat code review and owner-authorized browser acceptance remain pending; the 23-complete/9-partial count is unchanged.

Final checked code: `cd556874a3d705631a108026ef19c190e67c0f7c` — [full CI 43966396183](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43966396183) passed 136 unit/238 integration and [paired CI 43966396008](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43966396008) passed 15 Character State/19 Mixed-Origin; zero failures or skips. Independent WorkChat browser and code assessment: **not executed**. No scenario closure beyond the established 23 complete / 9 partial classification.
