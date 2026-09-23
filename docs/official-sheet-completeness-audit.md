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
| Player Name | Site/account or explicit Character attribution | **Upstream contract required** | Do not infer account identity or duplicate Site identity. |
| Background | Rules Core concept identity; Character owns selection | **Upstream contract required** | Rules Core currently catalogs backgrounds and defines the ownership boundary, but the Character projection does not yet expose a dedicated background-selection contract. Do not reduce Background to freeform profile text. |
| Alignment | Rules Core identity when rules-defined; authored profile currently exists | **Modeled, presentation partial** | Current profile can preserve/display authored text, but it is not a canonical Rules Core concept selection. Future Rules Core identity selection should supersede/validate the authored fallback when applicable. |
| Deity | Rules Core identity when rules-defined; authored profile currently exists | **Modeled, presentation partial** | Same boundary as Alignment. Preserve current authored text without pretending it is canonical rule identity. |
| XP | Character-owned mutable advancement state | **Character semantic model required** | Rules Core now explicitly assigns current XP to Character state. A generalized progression-value contract is still needed; do not assume every ruleset uses XP. |
| Size | Rules Core `character.size-category` metadata projection | **Modeled + displayed** | Multi-size source choices remain Rules Core choices; Character Sheet renders the projected result generically. |
| Campaign name | Site projection | **Upstream contract required** | Site currently supplies Campaign IDs, not a display-safe Campaign name. Raw IDs must not be promoted as identity text. |
| Effective Ability score/modifier | Rules Core Character projection | **Modeled + displayed** | Frontend renders authoritative effective values only. |
| 3.x ordinary/temporary Ability Score and Modifier | Rules Core related ability mechanics | **Modeled, source-dependent** | Character Sheet already preserves `ordinary-score`, `ordinary-modifier`, `temporary-score`, and `temporary-modifier` when Rules Core supplies them. |
| Current HP | Character runtime state | **Modeled + displayed** | Persisted and editable. |
| Maximum HP | Rules Core projection | **Modeled + displayed** | Source-derived. |
| Temporary HP | Rules Core resource projection + generic Character resource input | **Modeled + displayed** | Mutable resource state is transported generically; exact availability/recovery remains Rules Core-defined. |
| Nonlethal Damage | Rules Core resource projection + generic Character resource input | **Modeled + displayed** | Same generic resource boundary. |
| Hit Dice | Rules Core resource projection | **Modeled + displayed** | Projected beside HP; recovery semantics remain Rules Core procedures. |
| Death Saves | Character runtime state | **Modeled + displayed** | Persisted successes/failures with explicit controls; frontend does not invent consequences. |
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

## What this branch now establishes

1. A generalized Rules Core -> Character Sheet projection path for abilities, saves, defenses, combat, movement, competencies, character metadata, inventory mechanics, actions, spellcasting, features, choices, conflicts, and recovery.
2. Character-owned rules inputs for choices, competency ranks, training, class-skill state, known spells, resources, integer/boolean/string facts, advancement levels, and per-level HP gains.
3. Persisted runtime health state including current HP and Death Saves, plus rule-driven recovery consequence application.
4. Rich inventory occurrence state: quantity, carried/equipped/attuned flags, containers, Rules Core item identity, and generic currency balances.
5. Character Details/Profile state for player-authored biography fields, plus generic display of Rules Core Character metadata such as Size.
6. Cross-edition competency presentation that preserves historical identities and supports family/facet/shared-proficiency relationships rather than flattening them into a single edition.
7. No frontend formulas for edition rules, currency conversion, encumbrance, AC, spell progression, recovery, or other rules-owned calculations.

## Remaining blockers before full official-sheet semantic completeness

The remaining gaps are now narrow and have explicit owners:

- **Site:** Player Name semantics and a display-safe Campaign name.
- **Rules Core:** a concrete Character projection/selection contract for rules-defined Background, Alignment, and Deity identities. The ownership boundary is documented in Rules Core, but Character Sheet should not invent the missing projection.
- **Character Sheet:** generalized mutable progression state for values such as current XP. This should not hard-code XP as universal because non-XP progression systems must remain possible.
- **Presentation polish:** Initiative can expose projected contribution details using the same progressive-disclosure pattern already used elsewhere.

These are not reasons to reintroduce edition-specific fields or arithmetic into the Character Sheet frontend.

## Merge-readiness interpretation

This branch is suitable for progress evaluation once validation is green. It closes the large Character Sheet-owned backend cycle and leaves remaining cross-repository semantics explicitly identified rather than hidden behind placeholder strings or Notes.

The branch should not be described as final official-sheet completeness until the Site identity/display contracts, Rules Core identity-selection projection, and generalized Character progression value are completed.

## Reference-sheet verification

The official fifth-edition Character Sheet was used to verify first-page play-state fields, second-page biography/organization fields, and third-page spellcasting structure. The 3e/3.5e-specific fields remain cross-edition project requirements and are handled through Rules Core provenance/reconciliation rather than hard-coded edition modes.

- Wizards of the Coast, *D&D 5e Character Sheets*: https://media.wizards.com/2020/dnd/downloads/dnd_5e_charactersheets.pdf
