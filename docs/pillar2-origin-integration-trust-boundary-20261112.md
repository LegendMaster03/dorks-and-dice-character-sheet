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

## Implemented initial native activation (Phase 5 increment)

The owner-only `POST /api/characters/{characterId}/origin/activate-native` accepts
**only** an expected saved-draft revision. `CharacterOriginInitialActivationService`
reloads the server-held proposal, requires an active, Site-owned character,
refuses any existing committed Origin, reevaluates the exact draft through
the Site-delegated Rules Core native preview, and checks **all** of the
following before committing:

- Latest catalog ruleset identity, fingerprint and `previewOnly: true`;
  `Authorized` status with zero issues, not merely a non-null profile.
- Direct initial construction and the same selected Species/Ancestry IDs,
  proposal ID and native method as the saved draft.
- Supported first-party adjudication, no table-decision evidence, no
  independently asserted grants, no persisted supernatural influences or
  temporary forms, and one **completed** native construction history event.
- `Current` equal to `Underlying`, with no replacement or unresolved
  effective-form basis. The complete authorized Core profile, rather than a
  caller-provided grant list, is the value committed.

The store acquires the character root lock before checking drafts, prior
Origin and old foundational state. Existing `Species`, `Subspecies` or
`RaceSpecies` selections cause a **conflict requiring explicit reconciliation**;
nothing is auto-migrated, erased or silently overwritten. The older
Species/Subspecies writes now take the same character root lock and fail once
an authorized Origin exists. Background, Deity, Class and other independently
earned choices remain in their original owner stores. On a successful commit
the server writes the new Origin profile and a unique completed event receipt
atomically. A stale proposal, duplicate initial construction, archived character
or failed native adjudication makes no authorized-state change.

The native content test runs directly against
`feature/pillar2-phase5-origin-api-20261112` rather than the current
unmerged Rules Core `main`:
`.github/workflows/pillar2-phase5-origin-cross-repo.yml`.
It proves **Orc** directly authorizes under selected native rules.
The latest branch-to-branch probe also proves a **Human Skill** creation
selection is eligible when its actual native catalog membership is verified,
while a substituted Competency is rejected. Human's required Origin feat
selection still lacks an authored executable choice catalog and remains
unresolved. Do **not** manufacture feat approval or mark the Human–Elf
mixed workflow complete. The ordinary cross-repo contract workflow
continues to test Core `main` for existing consumers.

The response explicitly reports `effectiveMechanicsReady: false`.
Persisting an authorized inherent Origin **does not yet implement** the
cross-pillar class/background/upbringing grant and applicability projection.
The native-only activation path is deliberately **not** a DM override API:
future DM decisions require independently authenticated campaign-scoped
ruling evidence and the corresponding trusted Rules Core authority adapter.
Post-origination Rites, mixed/nested decisions, form activation/reversion,
supernatural progressions, and exceptional ancestry acquisition do not
use this direct-initialization endpoint. A DM override remains possible
under the game rules but is not forged through ordinary player access.

## Native Skill choices and browser API boundary

Rules Core Phase 5 now verifies `any-native-skill` selections against the
selected-native Skill catalog and mints its own eligibility-evidence reference
under a **first-party-only** evaluator. The stateless endpoint rejects
browser-supplied `CatalogEligibilityEvidenceId`. Character Sheet always
rechecks the **saved raw proposal** on activation; it never commits a
previously returned `evaluatedProposal` or treats an evidence reference as
a permission. Unsupported catalogs, including Human's `feat:origin` choice,
remain unresolved. No selected-native Origin feat prerequisite executor has
been introduced.

The frontend now has an independently typed API at
`src/features/origin/origin-api.ts` for owner-scoped draft GET/PUT, read-only
authorized Origin GET, and restricted native initial activation. It transmits
only `expectedDraftRevision` to the activation endpoint, rejects
client-supplied prior profiles/table decisions/catalog proof, and checks
the `activeGrants: false` / `effectiveMechanicsReady: false` response
boundaries. Its Node tests exercise embedded Tool Host URLs, rejected claims,
stale authorization responses and exact mutation bodies. **The typed API now supports a restricted direct native Origin builder.**
The former Species/Subspecies edit widgets are no longer offered in guided
setup or the general Character editor, and previously saved legacy selections
are displayed as reconciliation conflicts. A limited mixed biological
**source-evidence-only** editor is now available, but inherited candidate
authorization, custom/DM/Rite editing and current-effective character
projection are not finished.

## Origin guided builder integration

The Species navigation ID is retained for the existing guided builder route,
but its visible section is now **Origin**. The guided Origin section and
ordinary Edit Character overlay use `renderOriginBuilder` instead of the
legacy Species/Subspecies mutators. The UI loads Site-authorized Origin draft
and committed state, gets native ruleset metadata, and discovers Species,
Ancestry and Skill identities from Rules Core. It creates a direct biological
proposal without inventing contributors, grant lists or authority; changing
source identity clears stale manifestations.

Creation-time finite choices come from evaluated source candidates rather
than UI-authored rules, and Human's `any-native-skill` choice uses the
native Skill option catalog. Unsupported choices (including
`feat:origin`, unresolved multiplicity, mixed/nested inheritance,
and source-specific forms) remain visible as incomplete and are not offered
an unauthorized shortcut. The UI can save and resume incomplete drafts.
A native-authorized preview enables a separate **Activate Native Origin**
action; the server performs a fresh trusted reevaluation and atomic commit.
Optimistic-concurrency conflicts do not automatically overwrite a newer
saved draft.

A legacy Species/Subspecies selection is shown as a **reconciliation conflict**
rather than being silently copied, discarded or treated as an authorized
Origin. The server also rejects the conflict during activation. The
character header distinguishes committed **Underlying Species/Ancestry**
from legacy selections and does not present a temporary form as the baseline.
For a committed Origin, the editor is read-only pending an authorized
post-origination transition. The guided Origin step remains *incomplete*
until the cross-pillar effective-character projection is implemented;
`effectiveMechanicsReady: false` is not transformed into a finished sheet.

The client workflow uses `src/features/origin/origin-workflow.ts`,
the pure choice model uses `origin-builder-model.ts`, and the UI uses
`origin-builder.ts`. The rest of the builder continues to own
independent Background, Class, Deity and advancement state. This is not
the mixed Human–Elf editor or a DM/Rite/supernatural form workflow, and
no full browser/production acceptance is claimed.

## Human–Elf mixed biological source draft (evidence only)

The Origin editor now offers a separately labeled **Mixed biological sources**
draft mode for the common two-source example. Each source is selected from the
native Species or Ancestry option catalog and stored as a
`BiologicalParent`-role contributor with a `Native` source reference. The
editor can create, save, reopen, replace, or remove the two native source
references through the ordinary owner-scoped draft/revision API. Switching
between Direct and Mixed discards previous **draft-only** source selections
and manifestations; it can not rewrite an already committed Origin.

This deliberately **does not** authenticate the existing individual parent,
load a parent Character Sheet's `Current` profile, infer biological
compatibility, decide which of two sources is dominant, combine traits, or
authorize a resultant Species/Ancestry. A native source label is an
*offered source exemplar*, not a verified parent's complete manifested
configuration. The generic native contract supports more than two inputs
and other input roles; this limited example UI is not a universal parent
count or mechanics rule. A full nested parent requires a trusted source
loader and the exact persisted parent configuration.

The branch-to-branch test now checks the actual native Human and Elf
candidate origins: both source occurrences remain inspectable, while the
mixed profile has no authorized inherent grants, no selected Species or
Ancestry and no `Authorized` status. The editor shows native unresolved
issues and exposes **no mixed activation** action, even when a preview
object is returned. The server also refuses `Mixed` intent at its native
initial-activation boundary **before** consulting a possibly spoofed
`Authorized` result. A `Direct` proposal carrying extra contributors,
claimed DM rulings, process type, existing configuration, or history is
also refused before evaluation; a completed direct history receipt cannot
list contributors. Integration tests cover these refusal cases and verify
that the authorized state/receipts remain untouched.

The next mixed-Origin steps still require an authenticated and
freshly revalidated parent-individual *mechanical profile loader* (the
owner-only inspection above is not sufficient), an actual campaign-scoped
DM ruling interface and evidence for
exact inherited source occurrences, then a separate authorized
mixed-construction commit flow. Merely selecting both Human and Elf
source references does not grant Half-Elf traits, an Elf ancestry, or any
new independent entitlement.

## Owner-scoped parent snapshot inspection (not mixed authorization)

The new read-only
`GET /api/characters/{characterId}/origin/parent-snapshot/{parentCharacterId}`
endpoint checks the current Site projection **independently for both** the
target character and proposed parent character before reading parent
persistence. It is intentionally **owner-only** at this stage: a character
visible solely because the user is a DM in its campaign is not automatically
a trusted biological parent. Archived parent characters are readable if
Site continues to grant owner access. A client-provided parent ID by itself
confers no authority.

The response exposes only the saved parent's last `Underlying` and
`Current` Species/Ancestry identities and profile IDs, saved ruleset,
fingerprint, revision, and snapshot timestamp. It does **not** expose
inherent/independent grant arrays or authorization receipts. The service
requires an initialized parent sheet, a committed Origin snapshot, matching
completed event revision/count, and structurally present Current and
Underlying configurations. Missing, incomplete or inconsistent state fails
closed. The typed frontend function
`inspectOwnerVisibleParentOrigin` preserves `inspectionOnly: true`,
`inheritanceAuthorized: false`, and `effectiveMechanicsReady: false`
and rejects responses claiming otherwise.

**This is stored-state inspection, not a trusted effective-profile loader.**
It does not revalidate the latest native source catalog, recalculate
applicability, verify the parent-child relationship, or bind stored grants
to a mixed proposal. A parent may have a `Current` temporary form different
from `Underlying`; the read keeps both distinct rather than collapsing
them. No mixed Origin activation endpoint uses these records.

### Committed source draft immutability

A successful first-party initial activation fixes the exact source draft
revision used in its completed construction receipt. Draft edits now acquire
the same PostgreSQL character-root row lock as authorized Origin commits
and refuse writes once any committed Origin is present. The service returns
`origin_already_committed` for both an existing committed Origin and one
committed during the native preview. It does not automatically replace an
activation or clear the original proposal. PostgreSQL tests cover post-commit
edits and concurrent draft-update/activation races.

## Scope for the next integration step

The draft state and its owner-scoped save/load service are implemented.
A committed Origin freezes its original draft under the character-root
database lock so later draft edits can not invalidate an event's provenance. The additional `character_authorized_origins` and `character_origin_event_receipts` tables now provide a **separate server-held authorized-state storage substrate**, without copying drafts into it. The `PostgresCharacterAuthorizedOriginStore.CommitVerifiedAsync` method requires a separately verified application command and commits a snapshot plus unique event receipt in one PostgreSQL transaction. It locks the applicable draft and current Origin rows, compares expected revisions, rejects repeated event IDs and prevents stale tracked objects from overwriting newer revisions. A character deletion cascades the Origin, receipts and draft. The store has no direct HTTP write route; only the separately authorized `activate-native` application service can currently call it through a restricted HTTP workflow.

The read-only `GET /api/characters/{characterId}/origin/state` requires Site character ownership, works for archived characters as read-only, omits private authorization-evidence IDs, and returns `effectiveMechanicsReady: false`. A stored profile is a verified-history input **only after** a future service revalidates it against current native rules and independently earned entitlements.

**Not yet implemented:** a general authorized modification/reconstruction service (the new private caller supports **initial direct first-party construction only**), trusted independent entitlement/applicability loading for derived mechanics, campaign-scoped DM decision verification, executable Rite/other transition APIs, cross-domain effective projection, and mixed/nested builder UI. No user-supplied draft or receipt is an authorized Origin. Validate forged prior state and grants, unauthorized DM claims, real native content evolution, source-specific form interactions and full Human–Elf workflows before granting those capabilities. Neither mocked gateway tests nor a stored record demonstrate live end-to-end rules authority.
