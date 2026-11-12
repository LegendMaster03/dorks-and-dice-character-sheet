# Pillar 2 Phase 5 — Coordinated deployment and live acceptance readiness

**Reviewed:** November 12, 2026  
**Status:** Branch implementation tested; **not merged, not deployed together, and not approved for deployment**.  
**Immediate objective:** Verify the existing Human–Elf DM-authorized mixed-Origin workflow in a real Site + Rules Core + Character Sheet development environment. This is not the completion of the full Phase 5 roadmap.

## Source branches at this review

| Service | Branch | Verified commit | Integration status |
| --- | --- | --- | --- |
| Rules Core | `feature/pillar2-phase5-origin-api-20261112` | `299719905261e908a809d22b03e9b9c9327d69a6` | PR #151 closed unmerged; source branch preserved |
| Character Sheet | `feature/pillar2-phase5-origin-integration-20261112` | `4fd0b67e1a1e726ee6d8a2102e202191e7694cb7` (before this document) | Draft PR #67 |
| Site | `feature/pillar2-origin-dm-campaign-authority-20261112` | `e8d0cb7d89de71aa337293b8f36d22bac98687c3` | Draft PR #50; delegated-role-refresh correction included |

All three source branches were zero commits behind `main` at review. The Core Phase 5 branch was 371 commits ahead of `main` and included the surviving parent Pillar 2 history. Original Core PRs #141 and #151 were closed without merging; parent branch `feature/pillar2-characteristic-inventory-20261111` was absent. Do not reconstruct that branch or rewrite history without understanding the deliberate cleanup. A future replacement draft integration PR may target `main` directly from the preserved branch, after reviewing its complete 150-file change set.

## Verified cross-service contract

1. **Site:** Active campaign DM access and active character association generate restricted `dmCharacterScopes` in Character Sheet's authenticated Tool context. The Site campaign detail page links to `/tools/character-sheet/dm-origin-review/{campaignId}/{characterId}`, without exposing another owner's private sheet. Commit `e8d0cb7` makes both ordinary delegation and private-tunnel tickets recompute current campaign roles instead of copying stale roles from the source ticket.
2. **Character Sheet:** The owner persists a revisioned native Human–Elf draft. The separate DM review page requests current source offers through the campaign-scoped `mixed-review` endpoint. The DM selects offered occurrences; `activate-mixed-approved` verifies Site authorization, draft revision, native catalog, and selected candidate identities, and creates DM decisions server-side. The PostgreSQL commit includes authorized profile, origin audit, source attribution and immutable event receipt.
3. **Rules Core:** A Character Sheet-delegated `POST /api/mechanics/v1/origins/evaluate-approved-mixed` authenticates the Site actor and campaign DM role and evaluates the proposed mixed Origin through the existing universal engine. The response is stateless and preview-only. It is not itself an authorization ledger or a database mutation.
4. **Presentation:** Character Sheet verifies supported saved intrinsic and Low-Light Vision occurrences and forwards supported movement through the authoritative character projection. Unsupported effects remain explicitly partial. A catalog mismatch fails closed rather than extending an older approval to changed native content.

The restricted Human–Elf example accepts an explicit table-approved set of native occurrences. It does not establish universal inheritance, proof of parentage, a mandatory number of parents, or a universal five-trait rule.

## Automated evidence

- **Rules Core focused Origin CI:** [run 43300906170](https://github.com/LegendMaster03/dorks-and-dice-rules-core/actions/runs/43300906170) passed at executable implementation commit `dcec6e8c1537`. The current Core head adds only one documentation audit, so no new executable delta is present, but that exact head has not independently run this CI.
- **Character Sheet full CI:** [run 43318573637](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43318573637) passed at `4fd0b67e`.
- **Real Core HTTP + PostgreSQL branch acceptance:** [run 43318573162](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/43318573162) passed at `4fd0b67e`. It uses the real native Rules Core host and PostgreSQL, but a controlled Site actor fixture. This is not a live three-service test.
- **Site with the role-refresh regression:** [run 43323129891](https://github.com/LegendMaster03/dorks-and-dice-site/actions/runs/43323129891) passed at `e8d0cb7`. The regression checks DM demotion when reissuing both delegated and private-tunnel contexts.

## Release gates and confirmed limitations

1. **No deployment permission:** Current authorization permits feature-branch commits and pushes, not merges into `main` or deployments.
2. **Core release CI gate:** The Core `deploy.yml` permits only a manual `main` release whose exact commit has passed main-push `phase3b-native-public-api-ci.yml`. This CI runs the **full Python native/working suite**. Existing preserved-source consistency failures (33 reported evidence conflicts; two failing tests in earlier broader runs) require explicit reconciliation or an independently verified passing release run. Focused Origin CI alone does not satisfy the release gate. Do not weaken these tests or modify selected-native content just to obtain a green result.
3. **Runtime deployment type:** Core Phase 5 changes executable Host and Mechanics code, so its first release requires **Full deploy**, not the content-only path.
4. **Character Sheet release:** On `main`, a successful `Validate Character Sheet` push run initiates its deployment workflow. Its startup/readiness and persistent database migration behavior must be confirmed with the actual environment.
5. **Site release:** Site `deploy.yml` deploys automatically on `main` pushes, and also provides manual dispatch; coordinated deployment must prevent a prematurely visible broken DM link.
6. **Unverified live facts:** Actual environment configuration, cross-service tickets, migrations and rollback, active campaign/character relationships, two-account browser behavior, and current deployed image SHAs have **not** been inspected or validated. No deployed success should be inferred from isolated branch CI.

## Proposed controlled release sequence (requires explicit authorization)

1. Resolve the Core main-release validation blocker without silent content rewrites. Review and prepare a safe Core integration PR from the preserved head; review Character Sheet PR #67 and Site PR #50 against the exact compatible heads above.
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
- [ ] Actual results, errors, session role transitions, deployed SHAs and any necessary corrections are recorded.

## Not included in this checkpoint

This checkpoint does not implement authorized Origin updates, persisted Rite reconstruction, full nested-contributor resolution, unapproved native Feat selections, independent-grant execution, universal transformation overlays, or the future proposed partial character-state slots. Their Phase 5 and cross-pillar work remains separate and must use the shared reconstruction and provenance contracts.

**Decision needed for deployment:** Explicit approval for an agreed merge and deployment sequence, only after the Core full-CI gate and database/service compatibility have been established. Until then, keep all three branches unmerged.
