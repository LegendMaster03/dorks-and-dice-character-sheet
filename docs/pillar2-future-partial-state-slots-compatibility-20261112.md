# Character Sheet — Future partial character-state slot compatibility

**Recorded:** 2026-11-12  
**Status:** Documentation of future design constraints, not an approved implementation.  
**Related Rules Core review:** `docs/rewrite/character-origins/pillar2-future-partial-state-slots-compatibility-20261112.md` on the Pillar 2 Rules Core development branch.  
**Phase boundary:** Continue Phase 5 as planned. Do not change runtime behavior, database schema, migration policy, or release criteria for this proposal.

## Future concept

Pillar 4 may eventually represent temporary character states as **partial
state slots**: a slot explicitly replaces or modifies selected characteristics,
while absent characteristics resolve from the latest authoritative character
baseline and separately authorized concurrent effects. Examples for later
review include supernatural forms, Wild Shape and Polymorph. The concept may
also affect Rite of Rebirth interactions and which state of an actual parent
contributes hereditary mechanics.

This is neither an authorized new Character Sheet schema nor a claim that
those mechanics are already supported.

## State ownership and provenance to preserve

- `CharacterAuthorizedOriginState` in
  `src/CharacterSheet.Domain/Characters/CharacterAuthorizedOriginState.cs`
  is the server-owned current **Origin** record, not the complete character
  ledger. Class, Background, Upbringing, inventory, and independently
  acquired benefits keep their own authoritative sources.
- `CharacterOriginEventReceipt` persists completed event ID, committed
  revision, method, authorization evidence and, for scoped DM construction,
  exact approving actor, campaign and decision JSON. Retain that independent
  provenance rather than making materialized effective values the sole
  evidence of an authorized change.
- `PostgresCharacterAuthorizedOriginStore` locks the character root and
  checks draft/Origin revisions, prior events, and legacy foundational
  selections while committing Origin data and the immutable event receipt in
  one transaction. A future transformation representation should retain
  comparable atomicity, idempotency, authorization and conflict protection.
- The Rules Core contract separates authoritative `Underlying`, effective
  `Current`, source-qualified grants and transformation histories. Character
  Sheet must preserve that **semantic distinction** even if effective
  `Current` becomes a derived projection after a later migration.
- The read/activation APIs continue to use Site-verified owner or exact
  campaign DM scopes. A partial-slot implementation must not allow the
  browser to submit an authoritative past profile, privileged grant, DM
  ruling, or form activation evidence in place of a server-held decision.

## Existing dependencies requiring a later migration or adapter

| Present location | Observed assumption | Future compatibility consideration |
| --- | --- | --- |
| `CharacterAuthorizedOriginState.ProfileJson`, EF mapping, and `character_authorized_origins` | One serialized complete `NativeOriginProfile` is persisted, including materialized `Underlying` and `Current`. | It is a **current persistence representation**, not an approved mandate to persist a complete separate copy for every form. A future state resolver, versioned wire/storage contract and migration/read adapter may be necessary. |
| `CharacterAuthorizedOriginState.EvaluatedProjectionJson`, `CharacterPresentationService`, `VerifiedCommittedMixedOriginReader` | Current initial-Origin scalar/vision output is cached with a fingerprint and verified against exact committed grants/receipts before use. | Keep caches derived and disposable. Future read paths must recalculate effective values from the latest baseline and authorized overlays, then recheck source attribution and rule version; cached values cannot replace the authoritative overlay ledger. |
| `VerifiedOriginIntrinsicReader` and `VerifiedOriginVisionReader` | The supported **initial-Origin** projections require stored `underlying` and `current` to be deeply equal and their relevant source occurrences to match. | That equality is a deliberately narrow initial-creation guard, **not** a general rule that all forms must use the baseline. Future projection requires a separate verified effective-state path; do not relax existing guards prematurely. |
| `VerifiedCommittedMixedOriginReader` | Expects one matching completed construction event for the current revision, with an exact DM audit, initial Origin profile, and current fingerprint. | A later series of authorized form activations must be read alongside (not disguised as) this initial construction receipt. A revision-aware overlay/evidence reader would need a separate contract. |
| `CharacterOriginParentSnapshotService` and `CharacterOriginParentNativeReviewService` | Inspection distinguishes `Underlying` and stored `Current` identities; the trusted first-party native review currently requires revision-one, direct initial construction and an unchanged baseline/effective pair. It does not authenticate a transformed or mixed parent profile for inheritance. | Later reproduction mechanics must select and verify the contributing individual and **which approved source state** is relevant. A stored visible `Current` label alone is not hereditary evidence. Do not change the existing limited parent review to trust arbitrary submitted forms. |
| `CharacterOriginDraftEndpoints` and `CharacterOriginStateService` | Return the saved, character-owned profile and event projection under current Site authorization rules. `effectiveMechanicsReady: false` is explicitly retained where the whole-character projection is incomplete. | If effective state becomes derived, distinguish the versioned saved authoritative baseline/slot evidence from the evaluated effective output, without breaking old consumers or representing incomplete mechanics as ready. |
| Existing cross-pillar character projection | Native Origin, advancement/class, background/upbringing, independent benefits and character state have separate owners and are only partially combined today. | A future full-character resolver would need to specify the scope of each slot, including class/feature changes. Origin reconstruction alone cannot certify class replacement. |

The table identifies **adaptation points**, not a reason to add parallel
persisted character snapshots or begin a data migration now. Versioned
persistence evolution must preserve the original authorization and
source provenance when a derived representation becomes available.

## Rules to retain during Phase 5

1. Keep the authoritative baseline distinct from temporary effective state;
   do not treat a transformation activation-time copy as a new permanent
   baseline or restoration target.
2. Do not make a complete, independently stored per-form character snapshot
   a new architectural prerequisite. The current Origin profile is a
   validation/output record under the existing contract.
3. Retain stable character, event, source, occurrence, activation and DM
   authorization identities so later resolution can be audited.
4. Preserve independently earned entitlements when an active form changes
   their applicability. Do not reclassify those grants as inherent Species
   traits or erase them when a form is deactivated.
5. Continue to fail closed on absent Site permissions, unresolved native
   eligibility, stale catalog content, unsupported parent history and
   unverified transformations.
6. Finish the existing Human–Elf Phase 5 acceptance, tests and review
   without introducing slot tables, new polymorph/Wild Shape features,
   generalized form precedence, or an early migration.

## Separate review required after Pillar 2

A future proposal should define sparse-slot semantics, scope and precedence,
whether an override replaces a value or entire feature, stacking and
suppression, character-level class mechanics, activation/reversion history,
dependent native decisions, authoritative baseline changes during active
forms, and how nested/reproductive contributors select a verified source
state. It should then plan explicit migration and backward-compatibility
tests for these Character Sheet readers, stores and API payloads.

**No Phase 5 design decision is changed by this note.**
