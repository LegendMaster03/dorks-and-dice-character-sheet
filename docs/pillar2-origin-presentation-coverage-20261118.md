# Pillar 2 Workstream C — Verified Origin Presentation Coverage

**Starting branch:** `feature/innate-spell-ledger-persistence-20261116`  
**Working branch:** `feature/pillar2-origin-presentation-20261118`  
**Scope:** Character Sheet only, without new gameplay or authorization rules.

## Before and after

| Scenario | Before | After |
| --- | --- | --- |
| Direct committed Origin creature type, size and base speed | Exact native grants verified; metadata had no grant attribution | Verified grant ID, native source ID and occurrence path retained on each displayed baseline |
| Verified mixed Origin baseline | Pinned projection checked against receipt, DM audit and catalog | The same checked projection now retains native intrinsic grant attribution |
| Effective walking movement | Calculated by Rules Core, not copied from Species identity | Unchanged: only *base speed* has Origin grant attribution; effective movement is independent and may be unresolved |
| Origin revision changes during presentation | Live review validated persisted revision but handoff did not compare it explicitly | Presentation now passes the active committed revision and suppresses an earlier revision's Origin scalars and passive sense |
| Unsupported characteristics and executable actions | Unresolved and not automatically executed | Unchanged; no feat rules, spell spending, hit-point changes, permissions or balancing policies added |

## Source and revision verification

For direct Origins, existing immutable draft, source identity, committed event, full native profile, fingerprint and persisted revision checks remain mandatory. For mixed Origins, the existing approved decision audit, event, grant identity and pinned catalog checks remain mandatory. Both readers place the committed revision in the internal verified context. The presentation service compares this value to the saved Origin revision before passing Origin-specific values to Rules Core. A missing or stale context does not make an Origin complete.

Grant attribution is informational. The baseline's original grant does not prove that an effective movement value was produced solely by that grant; Rules Core must still evaluate current movement, including conditions.

## Tests and limits

Added unit tests cover provenance retention and stale-revision withholding. Expanded hosted direct-Origin integration asserts that returned scalar metadata exposes the exact verified grant source. Existing unit/integration tests cover forged grant IDs, mismatched native source/occurrence paths, unauthorized previews, incomplete construction and changed native fingerprints. The repository's push validation workflow runs the complete .NET solution, frontend build and disposable PostgreSQL integration tests.

Remaining dependencies: broader native characteristic and level-gated projections, runtime spell-resource expenditure, unsupported trait execution and future Workstream A/B integration need separately established native contracts. No Rules Core or Site code was modified. Do not merge or deploy this workstream before review.
