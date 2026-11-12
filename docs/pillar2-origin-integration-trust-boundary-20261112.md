# Character Sheet — Pillar 2 Phase 5 trusted Origin state boundary

**Date:** 2026-11-12  
**Development branch:** `feature/pillar2-phase5-origin-integration-20261112`  
**Rules Core dependency:** `feature/pillar2-phase5-origin-api-20261112`

## Current status

The existing `POST /api/native-rules/origins/preview` forwards a stateless construction proposal to the authenticated, user-delegated Rules Core endpoint. The initial preview itself does not save a record. A separate character-scoped draft route can now persist decisions; neither route authorizes a current character grant. A `previewOnly: true` result is advisory; a caller must not copy its resulting profile into a trusted current-character record.

**The existing Character Sheet PostgreSQL database remains the sole character state owner.** Site supplies the canonical character ID, the authenticated user, campaign associations and lifecycle/access projections. Rules Core supplies the executable meaning of selections, eligibility, effective inherent grants and permanent reconstruction. No direct browser credentials for Rules Core are issued.

## Required authoritative state records

The persisted Origin must distinguish:

1. **Decisions/draft:** origination method and intent, exact contributor references and occurrences, eligible characteristic and parameter/choice selections, current unresolved issues, and all unapplied proposed changes.
2. **Authorized current Origin:** the most recently validated `Underlying` and `Current` configurations; activated inherent grant IDs and their exact source provenance; independent grants as independently acquired entitlements; and current applicability per source.
3. **History:** completed creation/reconstruction events with stable IDs, previous Species/Ancestry snapshots, original contributors, prior manifestation history, and all active or persistent transformation evidence.
4. **Authority:** server-verified campaign/table decision identity, outcome, effective scope, source/native rule reference, and a revocable/revalidatable link to the authorization evidence. Client-supplied DM JSON is not proof.
5. **Version:** ruleset identity, originating native content metadata, material source revision and an application-owned monotonic character Origin revision.

The serialized `NativeOriginProfile` is a mechanics projection, **not a complete character ledger**: Class and Background/Upbringing entitlement evidence, trained competencies, inventory, resources, and earned supernatural advancements retain their established owners. Do not make an `ExistingIndependentGrants` array in a client request an entitlement ledger.

## Safe route split and access enforcement

- **Unauthenticated:** deny.
- **Authenticated initial preview:** accept a client proposal; never load or trust a caller-supplied old profile, history, independent grants, persistent influences or DM authority. The current bridge implements this part only.
- **Owned character draft save/load (implemented):** `GET/PUT /api/characters/{characterId}/origin/draft` check Site-authorized access and initialized character state. An active character may save an incomplete non-active proposal after an authenticated native preview. Archived state is read-only. PostgreSQL enforces one draft per character and compare-and-swap draft revision updates. No candidate grant enters the live sheet.
- **Owned character authorize/apply:** reload actual saved baseline, entitlements, influences, history and revision through the server database; obtain verified campaign/table decisions; invoke Rules Core with those trusted inputs; atomically commit only a wholly authorized result at the expected revision.
- **Rite/other permanent transition:** use Rules Core's common permanent-reconstruction executor with the existing authorized prior Origin, exact method completion and form interaction. The operation must have an idempotent event ID; a repeated or stale request returns the existing result or a conflict rather than appending history again.
- **Sheet read/projection:** revalidate material content changes, combine only earned, applicable grants through Rules Core projection and expose unresolved executable effects without inventing their numbers.

Access checks must distinguish **owner/view**, **ordinary edit**, **DM outcome approval** and **campaign rule modification**; one permission must not imply another. The authenticated Site Tool Host context **already supplies campaign-scoped role entries**, including the native DM role. The existing `ToolHostSiteCharacterAccessGateway` exposes a **character-owner/lifecycle projection**, not the DM decision authority or an authenticated ruling record. A future DM adapter can check the trusted campaign-role entries in `ToolHostAuthenticationContext.Campaigns`, but must also verify the exact decision actor, campaign scope, target, outcome and evidence. A role entry alone does not authenticate a caller-supplied ruling. Until that evidence/decision boundary is implemented, new DM rulings must fail closed.

## Concurrency and effects

Use one transactional PostgreSQL update of Origin decisions, verified outcome, affected inherent grants, and history. Require an expected monotonic Origin revision or equivalent row-version comparison. Changes that fail validation or have a stale expected revision must leave the last authorized Origin untouched. Do not reset the database to add this model.

The active form can change `Current` without changing `Underlying`, but must originate in an authorized future Pillar 4 execution. A permanent Rite during an active form needs an explicit rule/DM interaction. Reversion must evaluate the **latest committed Underlying**, never a cached activation-time baseline. A form may suppress independent grant usability but does not revoke the underlying earned Class, Background, Upbringing, training or supernatural progression.

## Scope for the next integration step

The untrusted **draft** state model, database migration, compare-and-swap store, scoped save/load service, and focused PostgreSQL tests are now implemented. The **authorized active-Origin** state model and migration, trusted entitlement/history loader, transactional apply/Rite service, DM authorization gateway, source-to-mechanics projection, and builder UI are **not yet implemented**. Never promote the draft JSON to those privileges. Begin these only after their trusted inputs are available. Test owner isolation, archived denial, forged previous state, self-asserted DM decisions, duplicate/stale transition, save/reload, evolution of native source content, and the actual Human–Elf mixed-Origin workflow across both applications. Do not label local gateway mocks as live integration.
