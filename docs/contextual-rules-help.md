# Contextual Rules Help

Character Sheet is responsible for presenting rules help, not authoring the underlying rules semantics.

Rules Core may attach contextual help to resolved mechanics and may expose reusable help topics for structural concepts that are not independent calculated mechanics. Character Sheet passes those fields through its Rules Core projection contract and associates the projection with the corresponding presentation response. The help text itself remains owned by Rules Core.

The normal sheet renders a compact help affordance only for topics Rules Core marks `prominent`. Standard help remains available in the projection for future learning-oriented or expanded-help interfaces without adding an icon beside every familiar field.

The current prominent-use cases include uncommon or edition-specific concepts such as Touch Armor Class, Flat-Footed Armor Class, Base Attack Bonus, Grapple Modifier, Damage Reduction, Spell Resistance, Nonlethal Damage, Miss Chance, Class Skill, Trained Only, and Armor Check Penalty when Rules Core provides those topics.

## Attack resolution context

Character Sheet also consumes the explicit attack-resolution context returned by Rules Core:

- `targetDefenseKey` identifies the defense the source rule actually targets;
- `rollMode` identifies the d20 selection mode;
- `targetStateKeys` preserves relevant source-defined target states.

These fields are displayed independently. Character Sheet does not infer Touch Armor Class from spell attacks, does not translate advantage into Flat-Footed Armor Class, and does not treat every denied-Dexterity situation as the flat-footed state. If Rules Core does not provide a target defense, Character Sheet does not invent one.

## Rendering lifecycle

Contextual help is applied after the normal explicit render lifecycle completes. It does not use `MutationObserver`. The enhancer reads stable semantic keys already present on rendered Character Sheet elements, finds matching Rules Core help metadata, and adds accessible hover/focus/click/tap controls. Competency help is selected from the competency's structured state rather than by parsing labels.
