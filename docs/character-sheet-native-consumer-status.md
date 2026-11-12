# Character Sheet — native Rules Core integration status

Character Sheet is in development and has not entered human testing. There is
**no released legacy character dataset to migrate**, and compatibility with the
retired Rules Core concepts or /api/rules endpoints is not a requirement.
Development-only records may be reset as necessary. Do not introduce an identity
crosswalk or write a dual-path mechanics adapter.

## Native API

The browser calls named Character Sheet backend routes; the backend uses
Site-issued server-side delegation for Rules Core.

| Character Sheet backend | Rules Core target |
| --- | --- |
| GET /api/native-rules/ruleset | GET /api/mechanics/v1/ruleset |
| GET /api/native-rules/options | GET /api/mechanics/v1/catalog/options |
| POST /api/native-rules/advancements/eligibility | POST /api/mechanics/v1/advancements/eligibility |
| POST /api/native-rules/characters/project | POST /api/mechanics/v1/characters/project |
| POST /api/native-rules/origins/preview | POST /api/mechanics/v1/origins/preview (Phase 5 branch only) |

Native rule IDs and the catalog fingerprint are mandatory. Character Sheet
does not infer old concept aliases or bypass the Site delegation boundary.

## Character presentation

CharacterPresentationService now uses the native gateway exclusively.
The previous character projection, rule-summary lookup, and recovery-support
fallback routes are not used by this service.

The read-only native presenter verifies selected class IDs and maps only
resolved calculations to the existing sheet widgets. Unrepresented effects and
prerequisites are explicitly partial or unavailable. No synthetic bonuses or
AC, equipment grants, hit points, or rules are invented.

The UI distinguishes resolved, partial, unconfigured and unavailable
calculations. A network failure is no longer rendered as an ordinary blank character.
When a canonical Origin has been committed, character presentation checks
the Site-authorized state and reports **partial** Species/Ancestry mechanics.
The Phase 5 branch now also revalidates a direct first-party initial Origin
against the latest Core catalog and compares its intrinsic grants with the
immutable saved profile. For verified Orc, it displays Base Size, Creature
Type, and Base Walking Speed and submits the 30-foot base value to the native
character kernel for effective movement calculation, **even without Class
or ability inputs**. Active conditions are never bypassed by treating base
speed as effective movement. The bridge also requires the exact same native
catalog fingerprint for the Origin verification and character calculation;
when catalog content changes between calls, Origin effects are withheld
while independent Class values remain visible. The branch now also exposes a first, narrowly executed passive
Species sense: source-attributed `rule.low-light-vision`, granting
perception of Dim Light as Bright Light without changing Darkness or
inventing a vision range. This value appears in `Senses` only after
server-held, current-catalog Origin revalidation and exact persisted
trait-occurrence matching. It does not infer other Species traits,
conditional bonuses, independent entitlements, Rites, mixed inheritance
or temporary transformations. Supported Class proficiency remains visible,
but other potentially Origin-dependent scores, saves and training totals
remain withheld. The native character kernel still does not accept a
full authorized Origin profile; this is deliberately a narrow subset.

## Implemented builder and advancement slice

- The starting Class chooser searches native Class IDs and stores the chosen ID.
- Saved native Class and Subclass names resolve through the authenticated native
  catalog; an unknown identity remains explicitly unavailable.
- Condition searching and selected-condition display use native regular and
  leveled-condition summaries.
- The normal Class advancement preview/apply service calls native eligibility
  and reevaluates it before mutating the Character. Class hit-point gains are
  validated against the **native class hit die** by the companion Rules Core
  feature branch `feature/native-hit-die-eligibility`; this contract must be
  available in deployed Rules Core before applying those advancements.
- The proposed Class projection is also evaluated for mandatory Patron, Pact,
  invocation and other outstanding native choices. Missing choices block apply.
- Subclass, Prestige Class, and unsupported categories do not fall back to the
  retired API or receive invented eligibility decisions.

## Pillar 2 Phase 5 Origin draft integration

The Phase 5 branches add native Species and Ancestry identity queries and
authenticated **stateless Origin preview**. Character Sheet also stores
character-owned **draft-only** Origin proposals under
`/api/characters/{characterId}/origin/draft`, with owner/lifecycle checks,
native-preview validation, PostgreSQL persistence and revision-safe writes.

These changes are development-only and **not yet deployed**. Drafts never mint active gameplay grants. A **separate**, server-held
authorized Origin state model, atomic receipt store and owner-scoped read-only
`/api/characters/{characterId}/origin/state` route have been added. The latter
returns a saved snapshot, not a current mechanically revalidated entitlement
list. The new `POST /api/characters/{characterId}/origin/activate-native`
is a strictly first-party **initial direct construction** operation: it
reloads the saved draft and demands a supported, complete, latest-catalog
Rules Core result before an atomic revisioned commit. The older Species and
Subspecies setters reject changes once a canonical Origin is stored, and
initial activation refuses legacy selections pending reconciliation.
The guided Species section is now labeled **Origin** and uses native
Species/Ancestry discovery and server-held Origin drafts instead of the
legacy Species/Subspecies setters. Finite creation choices and native Skill
selection are supported; unsupported Origin Feat eligibility remains
explicitly unresolved. A committed profile is labeled as *Underlying*
rather than a current effective transformed form. The guided Origin editor also permits two native biological
source references as a deliberately unresolved mixed evidence draft,
including Human–Elf; it does not authenticate parent individuals, inherit
traits, permit DM rulings, or expose a mixed activation route.
The read-only parent-snapshot and parent-native-review routes require
Site owner access to **both** the current and proposed parent characters.
The first presents stored Underlying/Current identity and revision **without
grants**. The second re-evaluates a parent's saved **initial direct**
construction under the current native catalog and compares the result with
the saved complete profile, rejecting changed traits and unsupported Rite
or temporary-form history. Both explicitly disclaim inheritance
authorization or general current mechanics; the latter is not a trusted
biological relationship or independent-entitlement loader.
Activated initial Origins freeze their source drafts under the same database
lock used by Origin commit. Rites,
DM overrides, subsequent modifications, full mixed/nested builder UI and
independent effective-grant projection remain incomplete. A separate
branch-to-branch native contract workflow validates the unmerged Phase 5
Rules Core; Orc native creation is authorized, while Human still has
creation-time choices to resolve. See
`docs/pillar2-origin-integration-trust-boundary-20261112.md`.

## Still required for full tool restoration

1. Complete and deploy the approved native Species/Ancestry, Background,
   Deity, Feat, Spell, and Item summaries. The Phase 5 Rules Core branch
   exposes Species and Ancestry identity search; remaining categories and
   option/parent-child selection semantics are not fully available. Do not
   treat this branch-only capability as deployed.
2. Complete native parent-child option selection, especially Warlock Patron,
   Pact, and Invocation choices, and supply those native selections in the
   projection and eligibility requests.
3. Remove retired catalog requests still used by feat, spell and inventory
   workflows, and retire unused legacy helper DTOs.
4. Extend recovery, equipment, spellcasting, and campaign-effective mechanics
   only when the native rules support their behavior.
5. Verify end-to-end Site delegation and the actual Rules Core API after both
   branches are deployed. Mocked tests do not prove live compatibility.

**There is no legacy character data to migrate, no identity crosswalk is
required, and no old Rules Core API shim should be introduced.**
