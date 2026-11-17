# Character State Increment 4 — independent WorkChat validation handoff

**Status:** NOT EXECUTED. This file is an executable review brief, not a WorkChat testing result or an authority grant.

**Goal:** Independently assess implementation consistency, security and infrastructure review readiness. Do not certify playable supernatural gameplay until approved first-party policies and source-authorized mechanics exist.

## Exact branch inputs (November 16, 2026)

| Repository | Existing branch | Last tested code SHA |
| --- | --- | --- |
| LegendMaster03/dorks-and-dice-character-sheet | `feature/character-state-inheritance-increment4-20261114` | `cd556874a3d705631a108026ef19c190e67c0f7c` |
| LegendMaster03/dorks-and-dice-rules-core | `feature/character-state-mechanics-adapter-increment4-20261114` | `0d340c43248d2ae257302fe933fa712f82b1d045` |
| LegendMaster03/dorks-and-dice-site | `feature/site-fresh-character-authorization-increment4-20261116` | `0eeaf232e2a3a11d4aac495c58b85ef6e3799a0e` |

The Character Sheet documentation-only HEAD may move after these tested code SHAs. **Always record actual checked-out HEADs before running any test** and compare code changes since the stated baseline. Do not conflate a passing branch run with a different revision.

**Required primary record:** `docs/character-state-increment4-integration-20261115.md`. Read it completely. Then read `docs/character_state_increment4_acceptance_32_scenarios.md`, `increment4-independent-validation-20261115.md` if available, the Origin contracts and provenance rules, Site fresh authorization, and all three complete branch diffs against `main`.

## Restrictions

1. Do not merge, deploy, modify `main`, or register any synthetic/test-only authority provider in production.
2. Do not treat ownership as a source of supernatural acquisition, omit unmodeled mechanics, add another Origin construction engine, invent HP/resource/form policies, or implement a separate self-override.
3. Do not claim synthetic acquisition, a green workflow, or a read-time Site check provides authorization at PostgreSQL commit.
4. Test in independent disposable databases and processes. Do not use live Site/production databases or credentials. If a direct tool/environment is unavailable, report the test as **not run**, not passed.
5. An independent WorkChat assessment must include findings and its own test execution or verified reproduction; merely accepting this document's existing CI results is not a completed independent pass.

## CI evidence to reproduce

At prior tested code SHA d322c3811c558cae6da8af26f40295cbabe94e3d, Character Sheet [run 43965325731](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325731) passed **136 unit + 236 integration**, zero failures/skips; [paired run 43965325732](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325732) passed **15 Character State + 17 Mixed-Origin**, zero failures/skips. The paired workflow printed the exact Core SHA above. [Latest paired run 43966396008](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43966396008) at code commit cd556874a3d705631a108026ef19c190e67c0f7c passed **15 Character State + 19 Mixed-Origin** tests after two real Core/Postgres same-revision draft-race cases were added. The full [Character Sheet validation run 43966396183](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43966396183) passed **136 unit + 238 integration** tests, zero failures and skips, with frontend, container and PostgreSQL smoke checks. These results belong to code commit cd556874a3d705631a108026ef19c190e67c0f7c; independent WorkChat execution remains pending. Site [run 43960608984](https://github.com/LegendMaster03/dorks-and-dice-site/actions/runs/43960608984) passed **595** tests. Rules Core [run 43812564554](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43812564554) passed **123 Host + 333 Mechanics** tests, plus [structure validation 43812564562](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43812564562).

Run both Character Sheet workflows in the branch and the actual Site/Core workflows if available. For local paired execution, follow `.github/workflows/character-state-increment4-cross-repo.yml` (do not approximate its flags):

```sh
# With Character Sheet checked out, Rules Core checked out at ./rules-core,
# .NET 10, and a disposable local PostgreSQL at 127.0.0.1:55434.
export RULES_CORE_CONTENT_ROOT="$PWD/rules-core/data/rulesets"
export ConnectionStrings__CharacterSheet='Host=127.0.0.1;Port=55434;Database=character_sheet_test;Username=character_sheet_test;Password=character_sheet_test'
dotnet test tests/CharacterSheet.IntegrationTests/CharacterSheet.IntegrationTests.csproj \
  --configuration Release \
  --filter FullyQualifiedName~CharacterStateCrossRepositoryTests \
  -p:BuildClient=false -p:RulesCoreCheckout="$PWD/rules-core" \
  -p:DefineConstants=CHARACTER_STATE_INCREMENT4
dotnet test tests/CharacterSheet.IntegrationTests/CharacterSheet.IntegrationTests.csproj \
  --configuration Release \
  --filter FullyQualifiedName~MixedOriginCrossRepositoryPersistenceTests \
  -p:BuildClient=false -p:RulesCoreCheckout="$PWD/rules-core" \
  -p:DefineConstants=PILLAR2_PHASE5_ORIGIN
```

Counts must include tests actually discovered and executed, including conditional suites; explicitly report skips, filters and missing dependencies.

## Adversarial tests WorkChat must challenge independently

**Authorization and Site:** Verify source-scoped backend capability, mode and registration constraints; direct and delegated/tunneled access refusal; fresh owner and DM requests from Character Sheet to actual Site (not ticket-cache fallbacks); role revocation, archive, ownership loss, campaign disconnect, tool de-registration and HTTP timeout/body failure. Check the Site DM query's single-statement status/membership/role/association predicate. **Known failure:** After Character Sheet's last fresh Site check but before its PostgreSQL commit, an external revocation can race and the transition may still commit. Reproduce or trace the three synthetic-Site/real-Core/real-Postgres cases in `SiteRevocationAtCommitBoundaryIsNotFencedByEarlierFreshReads`; do not classify these as authorization success. A live two-database interleaving requires an approved Site-owned commit-spanning consistency contract and is not currently covered.

**Origin integrity:** Confirm exact origin revision, native catalog fingerprint, method/source receipt continuity, full source identity and object-tree key uniqueness. Distinguish raw JSON duplicate keys from PostgreSQL `jsonb` canonicalization. Check differently cased property aliases, nested occurrence evidence, false DM decision scope, and empty/altered history. Test the latest direct-Origin draft guard (case alias, extra method fields) and commit-time draft proposal+fingerprint comparison against a same-revision SQL rewrite. Production initial/mixed services must supply exact stored draft identity. Identify any internal-only synthetic command calls that omit it; report whether those can be reached from production.

**Mixed-Origin:** Verify a fresh non-owner DM can only review actively associated characters while eligible; role removal or draft revision/source change mid-preview withholds offers. Check duplicate candidate IDs, changed approval decisions, mismatched source occurrence, stale fingerprint, unrelated independently owned Background/Upbringing, feat, inventory and class inputs. Do not infer universal biological inheritance rules from the narrow Human/Elf slice.

**Transformation and persistence:** Verify immutable transition receipts, serializable CAS, idempotency, active-form selection, reversion against latest Underlying and primary Class progression (not an activation-time saved Current), independently retained resources, correct owner/source occurrence, inability to transfer synthetic pool balances to another activation, process restart, migration upgrade and rollback compatibility, corruption rejection and ordinary-character behavior. Missing playable first-party supernatural source authority must remain fail-closed.

**Unresolved scope:** Explicitly identify the absent production `INativeOriginHostUnderlyingAuthorityProvider`, non-authoritative full effective-character Origin reconstruction, form-specific Class/Origin applicability, progression, HP, equipment/inventory/resource behavior, and deferred Pillar 2/Character State shared testing override.


## Separate WorkChat deployed-product/browser acceptance pass

WorkChat is the **independent acceptance-testing context**. A static code review and the branch CI suites are preparation, **not** the WorkChat browser pass. Use a deployed environment only if the owner has separately approved that exact disposable test environment, commit set, accounts and data. These feature branches have **not** been merged or deployed, so tests against the currently deployed production application do not exercise the new Increment 4 behavior. Do not deploy merely to satisfy this checklist.

With an owner-authorized isolated test environment, independently perform and record:

1. Test an ordinary character end to end: create, change primary ability/Class, select independently owned Background/Upbringing, manage inventory and equipment, save, refresh and restart. Verify ordinary character behavior has not regressed and no supernatural record appears without acquisition authority.
2. Test supported direct native Origin workflows through the actual UI/API: draft, review, revision changes and authorized activation. Attempt malformed and stale method identity/source data; ensure refused operations do not create a receipt or usable Origin.
3. Test Human/Elf mixed-Origin through two distinct accounts: player-owned draft and connected campaign DM, nonowner DM review, actual selected offers, native approval audit and persisted reload. Revoke DM role, disconnect/archive the character, disable the Tool or alter the draft between review and activation. Expect bounded refusal, no unexpected mutation or private offer disclosure.
4. Inspect browser console, requests/responses, page refresh, the relevant editable/read-only states, responsive layout and player/DM distinction. Record actual HTTP errors, missing UI affordances and stale or unexpectedly populated mechanics. No fake success UI for unmodeled origin/form behavior.
5. Confirm an active supernatural form **cannot be made playable in production merely by owner control or synthetic tests**. Without a separately approved acquisition/progression policy and production Core authority provider, the correct acceptance outcome is explicit unresolved/fail-closed behavior, not a successful Wolf/Hybrid gameplay walkthrough.
6. Capture exact deployed image/commit IDs, test account roles, database isolation, steps, reproducible screenshots/network traces (redact authentication capabilities), failures, and regression outcomes. Retest after authorized fixes, not against an unrecorded replacement build.

If no approved isolated deployment exists, classify this deployed-product pass as **not run / owner-authorized environment unavailable**. Do not report it as a failure of existing CI, and do not infer that production is playable. WorkChat's formal signoff must reflect that limitation.

## Acceptance report required from WorkChat

Preserve the **original 32 scenario numbers**. Prior accounted baseline: **23 complete, 9 partial, 0 wholly blocked, 0 untested**, with partial **2, 6, 7, 9, 21, 22, 24, 26, 27**. Change classifications only when independently observed evidence supports the change. For every changed classification supply the exact executable test, actual revision, outcome, and remaining qualification.

Provide: severity-ordered findings with file/line references; a reproduction and expected-versus-observed result for each defect; exact HEADs, CI run IDs and counts, skipped/excluded tests, PostgreSQL migrations and process-restart evidence; assessment of authority and source-provenance bypasses; risks of new fixes; proposed changes that need architectural approval; and two separate judgments: **infrastructure review readiness** and **playable supernatural content readiness**.

**WorkChat signoff:** PENDING — no independently executed WorkChat pass has been supplied. Leave this status pending until an actual independent report is returned and incorporated into the integration record.

## WorkChat follow-up: required independent retest after R1/R2 changes

The first WorkChat report examined Character Sheet HEAD
`8488e0a2598588adfe5d2d7271dd7d167bcefb88` and concluded **not ready
for merge**. Its repeated CI checks were independent, but browser tests and
new runtime reproductions of R1/R2 were not performed.

**New code to retest:** Character Sheet
`1bd5272eac0dd2cab84ff318599605acc57b7ad4`
(on the same existing feature branch). Core remains
`0d340c43248d2ae257302fe933fa712f82b1d045`; Site remains
`0eeaf232e2a3a11d4aac495c58b85ef6e3799a0e`.
If the documentation-only branch HEAD differs, inspect its diff before signoff.

1. Independently reproduce/assess **R1** on both read endpoints. Deny fresh
   ownership while cached ownership remains authorized; revoke after native
   evaluation; archive while cached lifecycle is active. Require no private
   projection or ledger/receipt leakage after denial, and live read-only state
   for permitted archived reads. Verify the actual process-restart fixture
   queries the Site's fresh endpoint, not cached context.
2. Independently reproduce/assess **R2** with headers immediately delivered
   but stalled or slow-drip body delivery for both Site fresh gateways **and**
   the delegated Rules Core success/error paths. Require bounded completion,
   fail-closed results, correct body-size budgets, and propagation of caller
   cancellation. These exact tests have passed developer CI, but WorkChat has
   not yet independently executed or reviewed them after correction.
3. Preserve R3's negative characterization and check it has not been masked
   by additional fresh reads. Review the proposed Site-owned commit-consistency
   requirements separately; there is **no approved implementation**.
4. Verify unrelated ordinary characters, Origin activation, owner/DM
   provenance, process restart, migrations and the original 32 numbered
   scenarios. Repeat conditional test discovery and capture exact HEADs,
   commands, successes, failures, and skips.
5. If an owner-approved isolated deployment exists, execute the separate
   browser/player/DM workflows specified above. Otherwise report **not run**.
   Do not deploy these unmerged branches or test only the current production
   site as a substitute.

**Existing developer evidence:** [full CI 43981207619](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43981207619)
136 unit/253 integration and [paired CI 43981207259](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43981207259)
15 Character State/19 Mixed-Origin, all green. Independent WorkChat source-and-logged-CI follow-up at `28af4b3f7f0032dbbe953bd70c0533daba9e95fe`:
**COMPLETE — R1 CLOSED, R2 CLOSED; CI independently inspected, not rerun in this follow-up.** Browser acceptance: **NOT RUN**. R3: **OPEN**.
