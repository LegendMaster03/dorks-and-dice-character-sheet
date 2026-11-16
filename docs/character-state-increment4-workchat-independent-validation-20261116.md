# Character State Increment 4 — independent WorkChat validation handoff

**Status:** NOT EXECUTED. This file is an executable review brief, not a WorkChat testing result or an authority grant.

**Goal:** Independently assess implementation consistency, security and infrastructure review readiness. Do not certify playable supernatural gameplay until approved first-party policies and source-authorized mechanics exist.

## Exact branch inputs (November 16, 2026)

| Repository | Existing branch | Last tested code SHA |
| --- | --- | --- |
| LegendMaster03/dorks-and-dice-character-sheet | `feature/character-state-inheritance-increment4-20261114` | `d322c3811c558cae6da8af26f40295cbabe94e3d` |
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

At the tested code SHA, Character Sheet [run 43965325731](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325731) passed **136 unit + 236 integration**, zero failures/skips; [paired run 43965325732](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43965325732) passed **15 Character State + 17 Mixed-Origin**, zero failures/skips. The paired workflow printed the exact Core SHA above. Site [run 43960608984](https://github.com/LegendMaster03/dorks-and-dice-site/actions/runs/43960608984) passed **595** tests. Rules Core [run 43812564554](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43812564554) passed **123 Host + 333 Mechanics** tests, plus [structure validation 43812564562](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43812564562).

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

## Acceptance report required from WorkChat

Preserve the **original 32 scenario numbers**. Prior accounted baseline: **23 complete, 9 partial, 0 wholly blocked, 0 untested**, with partial **2, 6, 7, 9, 21, 22, 24, 26, 27**. Change classifications only when independently observed evidence supports the change. For every changed classification supply the exact executable test, actual revision, outcome, and remaining qualification.

Provide: severity-ordered findings with file/line references; a reproduction and expected-versus-observed result for each defect; exact HEADs, CI run IDs and counts, skipped/excluded tests, PostgreSQL migrations and process-restart evidence; assessment of authority and source-provenance bypasses; risks of new fixes; proposed changes that need architectural approval; and two separate judgments: **infrastructure review readiness** and **playable supernatural content readiness**.

**WorkChat signoff:** PENDING — no independently executed WorkChat pass has been supplied. Leave this status pending until an actual independent report is returned and incorporated into the integration record.
