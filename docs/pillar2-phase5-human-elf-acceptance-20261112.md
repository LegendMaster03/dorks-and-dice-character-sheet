# Pillar 2 Phase 5 — Human–Elf mixed Origin acceptance checkpoint

**Date:** November 12, 2026  
**State:** Branch implementation and automated acceptance; not deployed to the shared development environment and not the end of the full Phase 5 roadmap.  
**Branches:** `dorks-and-dice-rules-core:feature/pillar2-phase5-origin-api-20261112`, `dorks-and-dice-character-sheet:feature/pillar2-phase5-origin-integration-20261112`, and `dorks-and-dice-site:feature/pillar2-origin-dm-campaign-authority-20261112`. All PRs remain drafts.

## Available, deliberately narrow behavior

- Site issues `dmCharacterScopes` only in Character Sheet's redeemed backend Tool context. Each scope requires **current** DM membership in an active campaign and an **active** association of that campaign to an active character. DM approval scope does not grant ownership of another character's sheet. Newly issued tickets reflect campaign role changes.
- The existing owner-editable Origin draft remains *untrusted*. It can record two native Human/Elf biological source references without granting any inherited traits.
- `POST /api/characters/{characterId}/origin/activate-mixed-approved` accepts only `campaignId`, `expectedDraftRevision`, and a list of native `candidateIds`. Before accessing the other owner's draft, Character Sheet checks the exact authenticated Site DM/campaign/character scope.
- For this first vertical slice, Character Sheet accepts only a basic `species.human` + `species.elf` proposal and a specific, manually selected subset of **current native offered characteristics without unresolved creation choices**. The DM's chosen offers are not treated as a default or biologically compelled inheritance policy.
- Character Sheet independently obtains offers from the live Rules Core native preview, synthesizes exact campaign-scoped DM decisions on the server, and submits them using Site-delegated `POST /api/mechanics/v1/origins/evaluate-approved-mixed`. The Rules Core ingress verifies the delegated Site user and DM role, evaluates through its general native Origin constructor, and returns a source-attributed result. No browser-supplied authoritative profile is accepted.
- A successful authorized result, exact DM decision audit, immutable construction receipt, catalog fingerprint, and source-attributed intrinsic/vision projection are committed transactionally to PostgreSQL. Concurrent revisions and legacy Species selections are rejected without replacing the prior state.
- Character Sheet reads supported **creature type, size, walking speed, and individually authorized native low-light vision** from that saved profile only when the current Core snapshot matches its committed fingerprint and the exact native grant occurrences remain verifiable. The native character kernel still calculates effective walking speed. Other incomplete mechanics are explicitly marked partial.

## Acceptance evidence

- Site authorization and role-revocation tests: `dorks-and-dice-site.Tests/ToolHostCharacterAuthorizationIntegrationTests.cs`. They distinguish ownership from DM targets and check that a new ticket loses another owner's target when the approver is demoted.
- Core authenticated-route and native construction tests: `RulesCore.Host.Tests/NativeAuthenticatedApiTests.cs` and `RulesCore.Mechanics.Tests/NativeOriginConstructionTests.cs`. The ordinary direct-only intrinsic/vision interpreters remain closed to mixed outcomes.
- Character Sheet live cross-repository acceptance: `MixedOriginCrossRepositoryPersistenceTests.cs`, compiled only with `PILLAR2_PHASE5_ORIGIN` and the actual Rules Core Host reference. It starts the real native HTTP endpoint and disposable PostgreSQL, verifies the manually authorized Human–Elf five-grant profile, reloads the profile and exact DM decisions, and rejects stale/forged/duplicate inputs without a commit. The Site actor in this test is a controlled fixture; real Site ticket construction is tested separately.
- Character Sheet presentation acceptance: `CharacterPresentationWorkflowTests.cs`. Controlled saved-state fixtures verify that a mixed Origin's pinned movement and low-light-vision occurrences reach presentation without being misclassified as a direct first-party Origin, and that a changed catalog suppresses those origin-dependent values. This is not a replacement for the real HTTP/PostgreSQL acceptance.

The temporary branch-to-branch workflow `.github/workflows/pillar2-phase5-origin-cross-repo.yml` runs the real mechanics tests and the PostgreSQL save/reload tests. The ordinary native smoke workflow still uses Core `main` for compatibility and does **not** substitute for the Phase 5 workflow.

## Remaining work before the roadmap's playtest-ready milestone

1. Build the authenticated **DM review and selection interface**, including a safe way for a DM to inspect a campaign-linked character's proposed Origin and current native offers without receiving general access to that character's private sheet. The approval endpoint is available, but the ordinary player-side mixed Origin editor still describes itself as evidence-only and does not complete that interaction.
2. Coordinate branch deployment to the shared development environment and run an actual user/DM session, including a second account, role revocation, user-facing save/reload and visible supported mechanics.
3. Provide an explicit refresh-and-re-review process after native content evolves. A pinned approval now **fails closed** if its draft lacks a fingerprint or the current native snapshot differs; it never silently approves under a new ruleset.
4. Complete the roadmap's more general reconstruction/update transitions, nested contributor histories, independent-grant application, additional choice catalogs, and later post-origination transformations. Neither this slice nor its five selected offers implements universal inheritance or the missing Human Origin Feat choice.

## Deployment and safety boundary

Do not merge any of these drafts to `main` until their dependent Site, Rules Core, and Character Sheet changes are coordinated. An accepted mixed Origin is not a complete playable Class/Background/Upbringing build: public responses continue to report partial or unavailable mechanics where integration is unfinished.
