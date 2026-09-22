# Character systems readiness audit

> **Status note — September 2026:** This document is a historical readiness audit and preserves the state of the project at the commits named below. It is not the current presentation-contract status. The current Character presentation bridge is documented in [character-mechanics-presentation-contract.md](character-mechanics-presentation-contract.md). The current bridge can consume backend/Rules Core supplied Ability values and modifiers, saving throws, movement, defenses, combat fundamentals, health resources, Inspiration, passive values, senses, proficiency/training, competencies (including composite relationships), checks, and procedures without moving rules arithmetic into the browser. Product decisions such as Background semantics, generalized advancement/leveling, spell state, rich inventory state, and generic overrides remain separate unresolved work.

This audit is based on Character Sheet `main` at
`fcd823c6b833c624170cd68942c9ebb1eb3f8467` and Rules Core `main` at
`650868cfa616d33672089cbe91c78ee471b33904`, verified before this implementation pass.

On `feature/full-stack-character-systems`, the systems classified as ready below are now wired
end-to-end through frontend API clients, reducer/application state, real sheet UI, mutation behavior,
and focused frontend tests. No Rules Core branch was required for those slices.

The governing boundary remains:

```text
persisted Character decision/state
!= Rules Core-derived contribution
!= calculated/effective state
!= explicit override
```

## READY and implemented end-to-end

### Character notes

Plain Character-owned notes have unambiguous ownership and lifecycle semantics. They are persisted as
stable Character-owned note occurrences with content and timestamps. They are not Campaign-scoped
modules and do not contain rule mechanics. The Notes tab now loads this state and permits add, edit,
and delete in normal View mode for active Characters. Archived/read-only Characters render notes
without mutation affordances.

### Minimal inventory ownership occurrences

Rules Core already provides stable rule concept identity for item concepts through the ordinary
resolved catalog. Character Sheet can therefore persist a minimal ownership occurrence without
interpreting item mechanics.

Each occurrence means only:

```text
This Character owns one logical occurrence referencing Rules Core ConceptKey X.
```

Duplicate concept keys are permitted and retain separate Character-owned occurrence IDs. No equipped,
carried, active, attuned, container, currency, ammunition, quantity-stack, encumbrance, Armor Class,
attack, or other effect semantics are inferred. The Inventory tab now resolves each persisted
`ConceptKey` through the ordinary Rules Core item catalog for display, keeps unavailable references
visible by `ConceptKey`, and adds/removes individual occurrences in normal View mode. Duplicate item
concepts remain separate visible Character occurrences rather than becoming quantity stacks.

### Existing Feat occurrence exposure

Feat occurrences were already correctly persisted and exposed before this branch through
`CharacterBuildView.ProgressionEntries`. Their Character-owned advancement IDs distinguish duplicate
grants of the same Rules Core concept. No parallel Feat resource was added. The Features & Traits tab now resolves and displays those
Character-owned Feat occurrences in normal View mode. Add/remove uses the existing build APIs and is
available only through structural Edit mode. The chooser uses the ordinary Rules Core Feat catalog;
duplicate occurrences and unavailable persisted references remain distinct and visible.

## RULES CORE CONTRACT NEEDED

### General Character contribution/effect resolution

Rules Core does not currently expose an ordinary consumer contract that turns a resolved rule concept
into normalized mechanical contributions suitable for Character calculation. The full resolved rule
endpoint can return a source-backed resolved document, but Character Sheet must not parse
`Document`, `ContentJson`, source-native JSON, or imported source documents to rediscover mechanics.

Before Character Sheet can build a generalized calculation/effect pipeline, Rules Core needs an
ordinary-user consumer model that exposes Rules Core-interpreted mechanical contributions separately
from source documents. At minimum, that contract must preserve stable target identity, the operation or
combination semantics Rules Core has interpreted, applicability/condition information when required,
and source concept/provenance. Cross-edition interpretation and rule-specific semantics must remain in
Rules Core.

Until that contract exists, Character Sheet should not implement disconnected local formulas for:

- effective Ability Scores or Ability Modifiers;
- Saving Throws;
- proficiency/training contributions;
- movement;
- Armor Class;
- Initiative;
- Hit Points or Hit Dice;
- feature-granted mechanics;
- item-granted mechanics;
- calculated actions.

### Skills and composite competencies

Rules Core contains a normalized `composite-skill` model and evaluator, but the current HTTP
consumer surface is not appropriate for Character Sheet:

- the resolved catalog exposes ordinary concept relationships such as `parent-class`;
- composite skill definitions/rulings are currently exposed through
  `/api/global/rules/mechanical-relationships/...`, which requires global Rules Lawyer authority;
- Character Sheet must not copy `KnownMechanicalRelationships` or
  `CompositeCompetencyEvaluator`.

Rules Core needs an ordinary-user resolved mechanical-relationship contract for the effective rules
scope. It must expose the applicable composite relationship structure and effective resolution
(`derive-parent` versus an explicit independent-parent ruling), and either expose the normalized
evaluation contract or return a Rules Core-resolved derivation that Character Sheet can feed with
Character-owned component values/modifiers. Campaign context must be explicit when campaign rules can
change the effective relationship.

### Feature and trait presentation

The Character Sheet can identify selected Class, Subclass, Prestige Class, Feat, and Race/Species
concepts, but it can not correctly enumerate or apply feature-granted Character mechanics without the
same normalized contribution/effect contract. Rendering source documents as a substitute would violate
the Rules Core boundary.

## RULES/PRODUCT DECISION NEEDED

### Background and other additional foundational selections

Rules Core recognizes additional entity types such as `background`, but Character Sheet has not
established cross-edition cardinality, replacement, acquisition, or advancement semantics for them.
Stable Rules Core identity alone is not enough to declare a new foundational Character decision.
No new foundational category was invented in this pass.

### Advancement beyond the existing foundation

The persistence model intentionally distinguishes Class, Subclass, Prestige Class, and Feat, but the
following remain unresolved:

- generic level-up semantics and total Character level;
- multiclass acquisition and prerequisites;
- Prestige Class acquisition/progression;
- ASIs and their relationship to Feats;
- grant/acquisition provenance;
- feature-granted advancement.

The existing general advancement primitive is therefore not exposed as a generic leveling API.

### Rest actions

The Character Sheet now reserves stable **Short Rest** and **Long Rest** controls in the top Character
action bar beside the structural editing controls, but the buttons deliberately do not encode recovery rules. Rest duration, hit-point recovery, Hit Dice or
other resource expenditure/recovery, spell-slot recovery, feature recharge, exhaustion interaction,
and cross-edition differences all require an effective Rules Core rest/effect contract. Until that
contract exists for the active rules context, the controls remain visible but disabled. This preserves
the desired Character Sheet workflow without moving edition-specific rest semantics into the browser.

### Spell state

Known/prepared semantics, slot-based versus point-based casting, multiclass spell progression, and
grant provenance remain unresolved Character/product rules. Spell mechanics also depend on the missing
Rules Core contribution/effect contract.

### Rich inventory state

Owning an item is now represented. Equipping, carrying, using, attunement, containers, currency,
ammunition, stack quantities, encumbrance, and item-driven calculated mechanics remain intentionally
unmodeled until their Character-state semantics are established.

### Explicit overrides

The architecture still distinguishes calculated/effective state from an optional explicit override,
but override scope, provenance, precedence, and reset behavior have not been defined generally enough
to persist a generic override model.

## Calculation/effect foundation status

No Character-side rules engine was added. The reusable foundation is currently a documented boundary,
not an implementation, because Rules Core does not yet provide the normalized ordinary-consumer effect
contract needed to implement it correctly. Character Sheet continues to persist only Character-owned
inputs/selections/state and stable Rules Core concept references.


## Category A continuation audit

After Notes, minimal Inventory, and Feat UI were completed, the remaining placeholder regions were
re-evaluated against current Character Sheet and Rules Core contracts.

No additional unimplemented Character system is currently Category A without moving mechanical
interpretation into Character Sheet or inventing unresolved Character/product semantics.

The remaining visible regions therefore stay intentionally blocked as follows:

- Rules Core consumer contract: effective Ability Scores and modifiers, Saving Throws,
  proficiency/training, movement, Armor Class, Initiative, Hit Points/Hit Dice, calculated actions,
  effective composite Skills, feature-granted mechanics, item-granted mechanics, and complete
  non-Feat Features & Traits presentation.
- Rules/product decision: Background/additional foundational selection semantics, level/multiclass/
  Prestige Class/ASI and acquisition provenance, spell state, rich inventory semantics, and generic
  explicit overrides.

The existing Race/Species, Starting Class, Class-bound Subclass, base Ability Score inputs, Feat
occurrences, minimal Inventory occurrences, and Character Notes remain the Character-owned inputs/state
that can currently be edited without violating the Rules Core boundary.
