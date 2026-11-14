# Pillar 2 Phase 5 — Coordinated deployment and live acceptance readiness

**Reviewed:** November 12, 2026  
**Status:** Branch implementation tested; **not merged, not deployed together, and not approved for deployment**.  
**Immediate objective:** Verify the existing Human–Elf DM-authorized mixed-Origin workflow in a real Site + Rules Core + Character Sheet development environment. This is not the completion of the full Phase 5 roadmap.

## Source branches at this review

| Service | Branch | Verified commit | Integration status |
| --- | --- | --- | --- |
| Rules Core | `feature/pillar2-phase5-origin-api-20261112` | `a08e349a2e01bb6997315846d306345625d07ee8` | Draft PR #152; main-targeted full release-suite and structure CI passed |
| Character Sheet | `feature/pillar2-phase5-origin-integration-20261112` | `03039d60ae88defbf57d53b547d45068cec4ea71` | Draft PR #67; explicit native draft re-review and audit hardening, full CI passed |
| Site | `feature/pillar2-origin-dm-campaign-authority-20261112` | `e8d0cb7d89de71aa337293b8f36d22bac98687c3` | Draft PR #50; delegated-role-refresh correction included |

All three source branches were zero commits behind `main` at review. The Core Phase 5 branch is 375 commits ahead of `main`, with 155 changed files including the surviving parent Pillar 2 history and the source-divergence reconciliation. The GitHub issue timeline confirms that the repository owner closed PRs #141 and #151 and deleted the original parent branch on November 12; no merge occurred, and their history remains reachable in the Phase 5 branch. The reason for cleanup is not recorded. Do not recreate the removed branch, reopen the PRs, or rewrite history. New [draft Rules Core PR #152](https://github.com/LegendMaster03/dorks-and-dice-rules-core/pull/152) now targets `main` directly from the preserved branch, without reopening the previous PRs. This makes the entire cumulative change set available for review; it does not grant merge permission.

## Verified cross-service contract

1. **Site:** Active campaign DM access and active character association generate restricted `dmCharacterScopes` in Character Sheet's authenticated Tool context. The Site campaign detail page links to `/tools/character-sheet/dm-origin-review/{campaignId}/{characterId}`, without exposing another owner's private sheet. Commit `e8d0cb7` makes both ordinary delegation and private-tunnel tickets recompute current campaign roles instead of copying stale roles from the source ticket.
2. **Character Sheet:** The owner persists a revisioned native Human–Elf draft. When its native catalog is outdated, the owner now has an explicit current-rules re-review action, which reloads current authorization and the draft before a revision-checked save. The separate DM review page requests current source offers through the campaign-scoped `mixed-review` endpoint. The DM selects offered occurrences; `activate-mixed-approved` verifies Site authorization, draft revision, native catalog, and selected candidate identities, and creates DM decisions server-side. The PostgreSQL commit includes authorized profile, origin audit, source attribution and immutable event receipt. The committed reader cross-checks each approved candidate and individual `tableDecisionId` against the stored grant.
3. **Rules Core:** A Character Sheet-delegated `POST /api/mechanics/v1/origins/evaluate-approved-mixed` authenticates the Site actor and campaign DM role and evaluates the proposed mixed Origin through the existing universal engine. The response is stateless and preview-only. It is not itself an authorization ledger or a database mutation.
4. **Presentation:** Character Sheet verifies supported saved intrinsic and Low-Light Vision occurrences and forwards supported movement through the authoritative character projection. Unsupported effects remain explicitly partial. A catalog mismatch fails closed rather than extending an older approval to changed native content.

The restricted Human–Elf example accepts an explicit table-approved set of native occurrences. It does not establish universal inheritance, proof of parentage, a mandatory number of parents, or a universal five-trait rule.

## Automated evidence

- **Rules Core branch validation:** [run 43339910285](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43339910285) passed complete .NET build/tests, Pillar 2 studies, complete Python content/migration tests and strict native/working validation at `58b08a2a`. The [main-targeted full Phase 3B release suite](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43342640244) **passed at `a08e349a`**, including Docker and ingress smoke steps. The temporary branch-specific full-release push trigger was retired; PR CI against `main` remains.  This includes the 33-record [source-divergence reconciliation](https://github.com/LegendMaster03/dorks-and-dice-rules-core/blob/feature/pillar2-phase5-origin-api-20261112/docs/content-promotion/phase5-preserved-source-reconciliation-20261112.md); native/working gameplay JSON was not edited.
- **Character Sheet full CI:** [run 43348206209](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43348206209) passed at `03039d60`: **354 frontend tests (including the three explicit draft refresh cases), 127 .NET unit tests, and 158 PostgreSQL integration tests**, plus TypeScript and container validation.
- **Real Core HTTP + PostgreSQL branch acceptance:** [run 43348206140](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43348206140) passed **11/11** at `03039d60`. It uses the real native Rules Core host and PostgreSQL, but a controlled Site actor fixture. This is not a live three-service test.
- **Site with the role-refresh regression:** [run 43323129891](https://github.com/LegendMaster03/dorks-and-dice-site/actions/runs/43323129891) passed at `e8d0cb7`. The regression checks DM demotion when reissuing both delegated and private-tunnel contexts.

## Release gates and confirmed limitations

1. **No deployment permission:** Current authorization permits feature-branch commits and pushes, not merges into `main` or deployments.
2. **Core release CI gate:** The Core `deploy.yml` permits only a manual `main` release whose exact commit has passed the **main-push** `phase3b-native-public-api-ci.yml` check. The source-evidence reconciliation, complete Python suite, Docker/ingress validation and entire release workflow **passed on the main-targeted PR at `a08e349a`**. The temporary feature-branch push trigger has been removed. **A successful PR run is not a qualifying main-push run**, and the eventual integrated `main` SHA must still pass its own CI before the deployment job can proceed.
3. **Runtime deployment type:** Core Phase 5 changes executable Host and Mechanics code, so its first release requires **Full deploy**, not the content-only path.
4. **Character Sheet release:** On `main`, a successful `Validate Character Sheet` push run initiates its deployment workflow. Its startup/readiness and persistent database migration behavior must be confirmed with the actual environment.
5. **Site release:** Site `deploy.yml` deploys automatically on `main` pushes, and also provides manual dispatch; coordinated deployment must prevent a prematurely visible broken DM link.
6. **Unverified live facts:** Actual environment configuration, cross-service tickets, migrations and rollback, active campaign/character relationships, two-account browser behavior, and current deployed image SHAs have **not** been inspected or validated. No deployed success should be inferred from isolated branch CI.

## Proposed controlled release sequence (requires explicit authorization)

1. The Core source reconciliation is complete and both expanded branch CI and the full release-validation suite have passed. Review the entire cumulative diff in new **draft PR #152**, without reopening the deliberately closed PRs or resurrecting their parent branch. Review Character Sheet PR #67 and Site PR #50 against compatible heads. PR-triggered Core CI passed at the reviewed head, but does not replace the required integrated **main-push** release check before deployment.
2. Confirm database backups, migration compatibility, service addresses, authentication ticket audiences, and rollback approach before any release. Confirm both distinct Site accounts and a disposable test campaign/character.
3. After coordinated merge authorization, **deploy Rules Core using Full deploy first** and verify its authenticated endpoint and readiness. Then deploy Character Sheet and confirm its readiness and owner flow. **Deploy the Site navigation last** to avoid displaying links to unavailable Sheet routes. DM access before Site's new authorization projection must fail closed. Alternative branch-based staging requires a separately established and reviewed deployment path; the existing production workflows do not deploy arbitrary feature branches.
4. Record the deployed Git SHAs. Stop and roll back according to the approved procedure if any compatibility, readiness, or authorization check fails.
5. Run the real browser acceptance sequence below under two separate signed-in accounts, not only mocked tickets or local HTTP fixtures.

## Real two-account acceptance checklist

- [ ] Owner creates or loads an active character associated with an active campaign.
- [ ] Owner saves a Human–Elf native mixed-Origin draft. Draft remains uncommitted and has no active traits.
- [ ] A **different** campaign DM account opens Site campaign details and follows the restricted Origin-review link. A non-DM cannot access the same target.
- [ ] DM sees only the authorized target and current native candidate offers, not another owner's private Character Sheet.
- [ ] DM selects the currently supported coherent native occurrences and submits the reviewed approval.
- [ ] Rules Core evaluates the real selected proposal under current catalog content and delegated campaign authority.
- [ ] Character Sheet transactionally persists the authorized profile, decision audit and immutable event receipt.
- [ ] Owner reloads the character; Species/Ancestry, source identity, authorized grants, supported movement and Low-Light Vision remain consistent.
- [ ] Unsupported mechanics remain partial rather than silently invented.
- [ ] A repeated or stale approval is rejected without modifying committed state.
- [ ] Revoking the DM role prevents **new** approvals, including when originating from an older source session; a past valid decision remains recorded as history.
- [ ] Changed native content cannot silently reuse a different catalog fingerprint or substitute a different source occurrence.
- [ ] After native content changes, the owner explicitly refreshes the existing draft against current rules. The draft revision changes, old DM candidate selections are not reused, and a separate DM account must review the new offers before approval.
- [ ] Actual results, errors, session role transitions, deployed SHAs and any necessary corrections are recorded.

## Not included in this checkpoint

This checkpoint does not implement authorized Origin updates, persisted Rite reconstruction, full nested-contributor resolution, unapproved native Feat selections, independent-grant execution, universal transformation overlays, or the future proposed partial character-state slots. Their Phase 5 and cross-pillar work remains separate and must use the shared reconstruction and provenance contracts.

**Decision needed for deployment:** Explicit approval for an agreed merge and deployment sequence, only after the main-only Core full-release CI gate and database/service compatibility have been established. Until then, keep all three branches unmerged.
