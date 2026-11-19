# Pillar 4 Character Sheet Integration — November 19, 2026

## Reconciliation

- Repository: `LegendMaster03/dorks-and-dice-character-sheet`.
- Current `main` at inspection: `c3a5e27d95e70307901e85ee46a3faa59f069899`.
- Requested Increment 4 baseline: `8d60a9840d35419d08bb874663531b1d7c2e7311`.
- Composite projection: `ccf4590d1d4065defa70bad1534e5d98e4a19c11`.
- Integration merge: `cc24b4242aa7399997e6dd6da6c4325232781730`, with both feature commits as parents and the composite content tree. All three files touched on both sides had identical final blobs. No duplicate migrations or contracts.
- R3 authorization branch: `6c203f6a614bbcb0c1db2cb04b25343df3d5a1a3`. Not merged or activated.

## Available backend foundations

Character Sheet already owns the PostgreSQL supernatural record, durable event receipts, revision-controlled commits, expression history, trusted-source transition interfaces, native primary-state adapter, and site-authenticated inspection/effective-state services. Composite manifestation contributor identities remain part of the effective read. Existing core gateway calls remain the only mechanics delegation path.

An additional read-only `GET /api/characters/{characterId}/origins/overview` correlates two existing authenticated reads and refuses mismatched catalog, lifecycle, ownership, supernatural revision, or manifestation revision. Owned alternate sheets are not eligible forms, and all action-availability entries remain `not-evaluated` until an independently trusted source can authorize them. No UI or mutation route was introduced.

## Proposed Rules Core coordination contracts — not implemented without A/B agreement

Workstream A must define verified acquisition and advancement output (canonical character and source occurrence identity, event identity, expected character/supernatural/ledger revisions, ruleset and catalog fingerprint, immutable evidence, earned grants and progression, candidate alternate sheets and persistent influences). Character Sheet should accept only source-verified server material, forward it through `INativeRulesCoreGateway`, validate the returned canonical state and revision, then call existing `ICharacterSupernaturalStateStore.CommitVerifiedAsync`. A receipt or client-supplied grant must never count as a source verifier.

Workstream B remains responsible for evaluating applicable vs. earned characteristics, coexistence, suppression, replacements, effective resources, and unresolved mechanics. Its verified response must carry the full contributor identities and current catalog pin. Character Sheet persists approved transition plans and exposes their returned projections, never computing mechanics itself.

A provisional internal-only acquisition/progression adapter and strict response verifier are now implemented in Character Sheet. The new default gateway method fails closed and requires Workstream A to finalize and independently verify the Core host contract; the new coordinator is not registered or reachable by HTTP. Receipt kinds now distinguish `progression` from `acquisition`, while prior earned history and resources remain guarded by PostgreSQL. The existing `ITrustedCharacterManifestationTransitionSource` and `ITrustedCharacterInfluenceExpressionSource` remain unregistered in production. This is an intentional fail-closed boundary, not completed production acquisition.

## Security and remaining work

- Register first-party acquisition/progression source authority only after A's verified contract and entitlement policy are available; add synthetic and real Core/PostgreSQL tests without treating synthetic success as production authority.
- Integrate B's finalized manifestation/effective-character response, including earned-versus-applicable fields, primary reconstruction, catalog changes, and resource policies.
- Add backend-only application acquisition/progression coordination and verified persistent test fixtures following the approved A contract; keep source checks outside the public mutation request.
- Keep R3 commit-time Site revocation fencing separately deferred. Fresh Site reads do not provide a distributed commit fence; this remains a known gap for mutation safety.
- Full 32-scenario ledger remains in `docs/character-state-increment4-integration-20261115.md`. Prior status: **23 complete, 9 partial** (2, 6, 7, 9, 21, 22, 24, 26, 27). Do not upgrade scenarios without new live production-authority and test evidence.

## CI evidence

Prior composite head `ccf4590d1d4065defa70bad1534e5d98e4a19c11`: full validation run `44602679090` and cross-repository run `44602679137` succeeded November 19. New-branch validation is requested by push to the integration branch; its final outcome must be reported separately. This document does not claim a passing local test run.

Do not merge into `main`, deploy, or implement the unified Origins frontend.

## Provisional ownership coordinator (added after the initial read model)

`CharacterSupernaturalOwnershipService` obtains fresh and scoped Site access, matches current PostgreSQL supernatural and manifestation revisions, reconstructs the authorized primary state, requires server-produced source material, delegates a verified plan through `INativeRulesCoreGateway`, checks identities/catalog/revisions/evidence, and commits only via the existing serializable PostgreSQL CAS store. The Core gateway deliberately throws until A implements the verified host operation. The coordinator is not registered and there is no public mutation endpoint. This is an adapter seam and synthetic-testable structure, not completed first-party entitlement.

The existing immutable receipt stream distinguishes `acquisition` and `progression` in new writes; the record remains backward-compatible without a schema migration. A transition, expression event, or first acquisition cannot masquerade as advancement. The original 32-scenario production closure totals remain unchanged until live A/B tests.

## Synthetic progression and response-integrity tests

The new internal source/plan tests verify exact identity, event, catalog, evidence, and revision bindings. A PostgreSQL regression verifies a separate immutable `progression` receipt, preserved earlier earned progression, duplicate-event rejection, and persistence after reloading the DbContext. These tests verify the adapter and storage boundary only; production authority remains deferred.

## Deferred R3 contract compatibility

The unmerged R3 branch adds a `ProtectedMutationTerminal` journal and migration `20261117040000_AddProtectedMutationTerminals` with admission/command digests, irrevocable `Committed`/`Aborted` outcomes, and a character-scope root-lock ordering proposal. The approved revocation behavior blocks newly conflicting admissions when a request is durable, then waits for earlier admitted operations to reach terminal outcomes before reporting revocation effective. The exact cross-service protocol remains a separate approval item. This branch adds neither the R3 table nor its migration, and no result here is evidence of commit-time revocation fencing. A later integration must tie the new ownership coordinator's event and digest to a Site-owned admitted operation under that protocol.
