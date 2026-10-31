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

## Still required for full tool restoration

1. Expose approved native Species, Subspecies, Background, Deity, Feat, Spell,
   and Item summaries. The current native options endpoint does not serve
   these categories; the chooser reports the missing capability.
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
