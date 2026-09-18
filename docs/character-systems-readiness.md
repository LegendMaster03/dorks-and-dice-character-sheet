# Character systems readiness audit

This audit is based on Character Sheet `main` at
`1aaa46c6524387f26e04118e345d96e8cc3a954c` and Rules Core `main` at
`650868cfa616d33672089cbe91c78ee471b33904`, verified before the feature branch was created.

The governing boundary remains:

```text
persisted Character decision/state
!= Rules Core-derived contribution
!= calculated/effective state
!= explicit override
```

## READY and implemented

### Character notes

Plain Character-owned notes have unambiguous ownership and lifecycle semantics. They are persisted as
stable Character-owned note occurrences with content and timestamps. They are not Campaign-scoped
modules and do not contain rule mechanics.

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
attack, or other effect semantics are inferred.

### Existing Feat occurrence exposure

Feat occurrences were already correctly persisted and exposed before this branch through
`CharacterBuildView.ProgressionEntries`. Their Character-owned advancement IDs distinguish duplicate
grants of the same Rules Core concept. No parallel Feat resource was added.

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
