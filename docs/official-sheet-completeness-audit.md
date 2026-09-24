# Official-sheet completeness audit

This audit maps the useful union of the official 3e, 3.5e, 5e, and 5.5e Character Sheet concepts onto the current Character Sheet architecture after the Rules Core character-projection merge.

The classification is semantic rather than visual. A concept is complete only when the correct owning system has a durable contract and the normal sheet can consume it without inventing rules arithmetic or source semantics.

## Status key

- **Modeled + displayed**: the owning contract and normal presentation both exist.
- **Modeled, source-dependent**: Character Sheet can preserve/render the concept, but authoritative Rules Core data is required for a concrete value.
- **Modeled, presentation partial**: durable state/projection exists, but some normal editing or progressive detail remains incomplete.
- **Upstream contract required**: Site or Rules Core owns the missing semantic information.
- **Character semantic model required**: Character Sheet owns the concept but does not yet have a durable model.

## Current gap matrix

| Official-sheet concept | Current ownership / source | Current status | Merge boundary / follow-up |
| --- | --- | --- | --- |
| Character name | Site Character projection | **Modeled + displayed** | None. |
| Player Name | Site-authenticated user display projection | **Modeled + displayed** | Transient Site-owned identity is shown in the Character header and is not copied into Character Sheet persistence. |
| Background | Character foundational selection + Rules Core background concept | **Modeled + displayed** | Character Sheet persists only the selected stable concept key, resolves the Rules Core display identity, and supports choose/replace/clear without reducing Background to profile text. |
| Alignment | Rules Core identity when rules-defined; authored profile currently exists | **Modeled, presentation partial** | Current profile can preserve/display authored text, but it is not a canonical Rules Core concept selection. Future Rules Core identity selection should supersede/validate the authored fallback when applicable. |
| Deity | Character foundational selection + Rules Core deity concept; authored profile remains available for legacy/custom text | **Modeled + displayed** | The rule-backed selection is shown as Character identity. Authored profile text remains separate and is not promoted to canonical Rules Core identity. |
| XP | Character-owned neutral advancement-progress state | **Modeled + displayed** | Character Sheet persists and edits a nonnegative numeric **Advancement Progress** value without inventing XP semantics. A future Rules Core progression label can present the same state as XP or another ruleset-specific concept. |
| Size | Rules Core `character.size-category` metadata projection | **Modeled + displayed** | Multi-size source choices remain Rules Core choices; Character Sheet renders the projected result generically. |
| Campaign name | Site Tool Host campaign display projection | **Modeled + displayed** | Display-safe Site-owned Campaign names are shown in the header; raw Campaign IDs remain implementation identity rather than player-facing text. |
| Effective Ability score/modifier | Rules Core Character projection | **Modeled + displayed** | Frontend renders authoritative effective values only. |
| 3.x ordinary/temporary Ability Score and Modifier | Rules Core related ability mechanics | **Modeled, source-dependent** | Character Sheet already preserves `ordinary-score`, `ordinary-modifier`, `temporary-score`, and `temporary-modifier` when Rules Core supplies them. |
| Current HP | Character runtime state | **Modeled + displayed** | Persisted and editable. |
| Maximum HP | Rules Core projection | **Modeled + displayed** | Source-derived. |
| Temporary HP | Rules Core resource projection + generic Character resource input | **Modeled + displayed** | Mutable resource state is transported generically; exact availability/recovery remains Rules Core-defined. |
| Nonlethal Damage | Rules Core resource projection + generic Character resource input | **Modeled + displayed** | Same generic resource boundary. |
| Hit Dice | Rules Core resource projection | **Modeled + displayed** | Projected beside HP; recovery semantics remain Rules Core procedures. |
| Death Saves | Character runtime state | **Modeled + displayed** | Persisted successes/failures with explicit controls; frontend does not invent consequences. |
| Inspiration | Character boolean runtime state | **Modeled + displayed** | Player-controlled on/off toggle persists through the generic boolean-fact state contract. No automatic grant, spend, or reset rule is invented. |
| Armor Class | Rules Core defense projection | **Modeled + displayed** | None. |
| Touch AC | Rules Core defense projection | **Modeled + displayed** | None. |
| Flat-Footed AC | Rules Core defense projection | **Modeled + displayed** | None. |
| AC contribution breakdown | Generic Rules Core contribution projection | **Modeled, source-dependent** | Details UI renders arbitrary authoritative contributions/provenance. |
| Initiative breakdown | Generic calculated-value contract | **Modeled, presentation partial** | Scalar Initiative is displayed; the compact Initiative surface can still adopt the same progressive-details treatment when useful. |
| Saving Throw breakdown | Rules Core contribution projection | **Modeled + displayed** | Generic mechanical details preserve authoritative contributions/provenance. |
| Miss Chance | Rules Core defense projection | **Modeled + displayed** | Preserved under More defenses when supplied. |
| Damage Reduction | Rules Core defense projection | **Modeled + displayed** | None. |
| Spell Resistance | Rules Core defense projection | **Modeled + displayed** | None. |
| Resistances / Immunities / Vulnerabilities | Rules Core projection | **Modeled + displayed** | None. |
| 3e/3.5e competency identities including Alchemy, Pick Pocket, Wilderness Lore | Rules Core competency concepts/relationships | **Modeled, source-dependent** | Character Sheet consumes canonical competency rows and does not hard-code edition aliases. |
| Craft / Perform families and specialties | Rules Core competency family/facet metadata | **Modeled + displayed** | Character Sheet supports grouped/faceted competencies and shared training semantics generically. |
| Languages | Rules Core qualification/training projection | **Modeled, source-dependent** | Generic training/proficiency surfaces already preserve supplied language knowledge. |
| Inventory item occurrence | Character state + Rules Core item identity | **Modeled + displayed** | Duplicate occurrences remain distinct. |
| Quantity / stacks | Character inventory occurrence state | **Modeled + displayed** | Persisted and editable. |
| Carried / equipped state | Character inventory occurrence state | **Modeled + displayed** | Rules-derived load/AC effects remain Rules Core work. |
| Container relationship | Character inventory occurrence state | **Modeled + displayed** | Occurrences can reference another occurrence as container. |
| Armor / shield equipped relationship | Character equipped occurrence + Rules Core item identity | **Modeled, source-dependent** | Character supplies equipped concepts; Rules Core determines mechanical consequences. |
| Ammunition count | Character item occurrence quantity + Rules Core ammunition identity | **Modeled + displayed** | A separate ammunition counter is unnecessary for ordinary ammunition items; quantity is the runtime count. Rules-specific ammunition behavior remains Rules Core. |
| Currency / coins | Character currency balances | **Modeled + displayed** | Arbitrary normalized currency keys and signed balances; no frontend conversion ratios, fixed denominations, coin weight, or campaign assumptions. |
| Attunement | Character inventory occurrence state | **Modeled + displayed** | Rules validation/consequences remain Rules Core. |
| Encumbrance/load | Rules Core inventory mechanics projection | **Modeled, source-dependent** | Character passes item/carried/equipped state; frontend renders projected load/status without formulas. |
| Manual Feats | Character advancement occurrences | **Modeled + displayed** | Existing catalog-backed workflow retained. |
| Class/Subclass/Species granted Features & Traits | Rules Core feature projection | **Modeled + displayed** | Rules-derived grants are projected with source/provenance rather than copied into Character storage. |
| Spellcasting metadata | Rules Core spellcasting projection | **Modeled + displayed** | Save DC, spell attack, resource system, and projected resources are consumed generically. |
| Known spells | Character Rules input + Rules Core spell identity | **Modeled + displayed** | Catalog-backed add/remove workflow exists. |
| Spell slots / spell points / Pact Magic | Rules Core resource systems + Character resource inputs | **Modeled + displayed** | Standard slots/spell points and Pact Magic remain distinct. Character Sheet does not calculate resource progression. |
| Prepared spell restriction | Rules Core policy | **Modeled, source-dependent** | Dorks & Dice can remove stock preparation restrictions without frontend hard-coding; future effective rules can project different policy. |
| Appearance / age / height / weight | Character profile | **Modeled + displayed** | Authored Character data. |
| Personality Traits / Ideals / Bonds / Flaws | Character profile | **Modeled + displayed** | Authored Character data. |
| Backstory | Character profile | **Modeled + displayed** | Authored long-form Character data. |
| Allies & Organizations / Symbol | Character profile | **Modeled + displayed** | Authored Character data. |
| Recovery / rests | Rules Core recovery procedures + Character consequence persistence | **Modeled + displayed** | Generic continuation supports declared choices, rolls, and consequences; no hard-coded Short/Long Rest algorithm. |
| Character portrait / art gallery | Character-owned art metadata + application-managed image storage | **Modeled + displayed** | Multiple image assets persist independently; one optional portrait can be selected for the header. Upload, full-size viewing, portrait replacement/clearing, deletion, archived read-only behavior, and lifecycle cleanup are supported. |

## What this branch now establishes

1. A generalized Rules Core -> Character Sheet projection path for abilities, saves, defenses, combat, movement, competencies, character metadata, inventory mechanics, actions, spellcasting, features, choices, conflicts, and recovery.
2. Character-owned rules inputs for choices, competency ranks, training, class-skill state, known spells, resources, integer/boolean/string facts, advancement levels, and per-level HP gains.
3. Persisted runtime health state including current HP and Death Saves, plus rule-driven recovery consequence application.
4. Rich inventory occurrence state: quantity, carried/equipped/attuned flags, containers, Rules Core item identity, and generic currency balances.
5. Character Details/Profile state for player-authored biography fields, plus generic display of Rules Core Character metadata such as Size.
6. Cross-edition competency presentation that preserves historical identities and supports family/facet/shared-proficiency relationships rather than flattening them into a single edition.
7. Site-owned Player and Campaign display identity are consumed transiently for the Character header without duplicating Site ownership state.
8. Background and Deity are Character-owned rule selections backed by stable Rules Core concepts, with authored Deity profile text kept separate for custom/legacy biography.
9. A generalized nonnegative Character-owned Advancement Progress value is persisted and editable without hard-coding XP semantics.
10. Character art supports multiple persisted image assets plus one optional portrait, including safe upload validation, read-only viewing, replacement, deletion, and lifecycle cleanup.
11. No frontend formulas for edition rules, currency conversion, encumbrance, AC, spell progression, recovery, or other rules-owned calculations.

## Remaining blockers before full official-sheet semantic completeness

No Character Sheet-owned feature in the official-sheet union remains a pre-acceptance blocker.

The remaining items are refinements rather than blockers:

- **Rules Core / identity refinement:** Alignment is currently preserved as editable Character-authored profile text. If Rules Core later exposes canonical rules-defined Alignment choices, Character Sheet can add a stable concept selection without discarding the authored fallback.
- **Rules Core / progression semantics:** the Character-owned numeric Advancement Progress value is complete, but the ruleset-specific label and meaning (for example XP) should come from Rules Core when such a contract exists.
- **Presentation polish:** Initiative can expose projected contribution details using the same progressive-disclosure pattern already used elsewhere.

These are not reasons to reintroduce edition-specific fields or arithmetic into the Character Sheet frontend, and they do not block a complete WorkChat acceptance pass.

## Merge-readiness interpretation

This branch is ready for a complete WorkChat acceptance pass now that validation is green. It closes the Character Sheet-owned pre-acceptance work for identity display, Background/Deity selection, generalized advancement progress, portrait/art storage, persistence, read-only behavior, and the previously completed cross-edition mechanics surfaces.

The remaining Alignment/progression-label items are upstream semantic refinements, not missing Character Sheet utility. WorkChat should still verify the full 3e, 3.5e, 5e, and 5.5e union end to end and report any concrete usability or behavioral defects before merge.

## Reference-sheet verification

The official fifth-edition Character Sheet was used to verify first-page play-state fields, second-page biography/organization fields, and third-page spellcasting structure. The 3e/3.5e-specific fields remain cross-edition project requirements and are handled through Rules Core provenance/reconciliation rather than hard-coded edition modes.

- Wizards of the Coast, *D&D 5e Character Sheets*: https://media.wizards.com/2020/dnd/downloads/dnd_5e_charactersheets.pdf
