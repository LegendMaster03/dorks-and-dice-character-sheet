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
