# Official-sheet completeness audit

This audit maps the useful union of the 3e, 3.5e, 5e, and 5.5e Character Sheet requirements onto the current Character Sheet architecture.

The classification is about semantic ownership, not merely whether a label can be drawn in the browser. A concept is not considered complete when the only possible storage location is Notes or when the frontend would have to invent rules arithmetic.

## Status key

- **Modeled + displayed**: the semantic contract and normal presentation both exist.
- **Modeled, presentation incomplete**: a semantic source exists, but the normal sheet does not yet give all supplied data an appropriate home.
- **Presentation-ready, source incomplete**: the presentation contract can preserve the concept, but the current backend/Rules Core bridge does not yet supply the required state or breakdown.
- **Missing semantic model**: a proper owning contract must be designed before implementation.

## Gap matrix

| Official-sheet concept | Current ownership / source | Status after this pass | Follow-up boundary |
| --- | --- | --- | --- |
| Character name | Site Character projection | Modeled + displayed | None for the name itself. |
| Player Name | None | Missing semantic model | Decide whether Site account identity, Character metadata, or an explicit authored field owns it. |
| Background | None as a Character selection/profile | Missing semantic model | Add a semantic Background selection/profile contract rather than a plain header string. |
| Alignment | None | Missing semantic model | Character metadata or rules-backed selection, depending on campaign rules. |
| Deity | None | Missing semantic model | Character details/profile; retain rules provenance if a rules source grants constraints. |
| XP | No Character-owned progression value | Missing semantic model | Advancement/progression contract must distinguish XP from levels, ranks, tiers, and other progression. |
| Size | No authoritative Character projection | Missing semantic model | Rules-derived Character mechanic/profile value. |
| Campaign name | Site projection supplies Campaign IDs only | Missing upstream display data | Site must project a display-safe Campaign name; the frontend must not expose or reinterpret raw IDs. |
| Effective Ability score/modifier | Rules mechanics presentation shape exists | Presentation/source dependent | Continue to use backend-supplied effective values only. |
| 3.x temporary Ability Score / Modifier | No distinct temporary-effect state | Missing semantic model | Rules/effect projection must identify temporary state distinctly from base and effective values. |
| Current HP | Character runtime state | Modeled + displayed | Existing behavior retained. |
| Maximum HP | Rules mechanics projection | Modeled + displayed when supplied | Existing behavior retained. |
| Temporary HP | Rules resource projection | Modeled + displayed when supplied | A mutation contract is still required before editing it. |
| Nonlethal Damage | Rules resource projection | Modeled + displayed when supplied | A Character-owned mutation contract is still required before editing it. |
| Hit Dice | Rules resource projection | Modeled + displayed when supplied | This pass recognizes the backend identity as a `hit-dice` presentation role and promotes it beside HP without calculating it. |
| Death Saves | Character runtime state | Modeled + displayed | This pass persists successes/failures (0-3 each) and exposes runtime controls. No automatic reset or edition-specific consequence is invented. |
| Armor Class | Rules defense projection | Modeled + displayed | Existing compact shield retained. |
| Touch AC | Rules defense projection | Modeled + displayed | Existing compact secondary value retained. |
| Flat-Footed AC | Rules defense projection | Modeled + displayed | Existing compact secondary value retained. |
| AC contribution breakdown | Generic breakdown/related-value presentation contract | Presentation-ready, source incomplete | This pass adds an AC Details surface and server presentation fields. Rules Core/bridge still must supply authoritative contribution data; the frontend never reconstructs armor/Dexterity/size/etc. |
| Initiative breakdown | Generic calculated-value shape supports breakdown, compact Initiative surface does not expose it yet | Modeled, presentation incomplete | Add the same progressive detail pattern when authoritative breakdown data is supplied. |
| Saving Throw breakdown | Saving Throw presentation now has generic breakdown/related-value fields | Presentation-ready, source incomplete | Populate from authoritative evaluation data; existing save renderer already supports progressive mechanical details on the client. |
| Miss Chance | Arbitrary defense values can be preserved | Modeled + displayed when supplied | This pass places non-promoted defenses such as Miss Chance under **More defenses**, not in another permanent card. |
| Damage Reduction | Rules defense projection | Modeled + displayed | Existing Defenses card retained. |
| Spell Resistance | Rules defense projection | Modeled + displayed | Existing Defenses card retained. |
| Resistances / Immunities / Vulnerabilities | Rules mechanics projection | Modeled + displayed | Existing Defenses card retained. |
| Alchemy (3e skill) | No Character-side named reconciliation | Rules Core responsibility | Preserve as a competency identity/provenanced relationship in Rules Core. Do not substitute Alchemist's Supplies. |
| Pick Pocket | No Character-side named reconciliation | Rules Core responsibility | Reconcile through competency relationships/provenance (for example with Sleight of Hand) without erasing historical identity. |
| Wilderness Lore | No Character-side named reconciliation | Rules Core responsibility | Reconcile through competency relationships/provenance (for example with Survival) without erasing historical identity. |
| Languages | Generic training/proficiency and non-skill competency presentation | Modeled + displayed when supplied | Rules Core must supply language entries through one of the supported semantic collections; the frontend already preserves all supplied rows. |
| Inventory item occurrence | Character runtime ownership + Rules Core item identity | Modeled + displayed | Existing duplicate-occurrence identity retained. |
| Quantity / stacks | None | Missing semantic model | Character inventory occurrence state. |
| Equipped / carried state | None | Missing semantic model | Character inventory occurrence state; derived AC/load remains Rules Core/backend work. |
| Armor / shield equipped relationship | Item definitions exist, usage state does not | Missing semantic model | Inventory/equipment subsystem. |
| Ammunition state/count | None | Missing semantic model | Character inventory/resource state, not arbitrary item metadata. |
| Currency / coins | None | Missing semantic model | Dedicated Character inventory currency state. |
| Attunement | None | Missing semantic model | Character item-occurrence usage state with rules validation outside the frontend. |
| Encumbrance/load | Client projection shape exists; production backend does not currently supply a complete inventory mechanics projection | Presentation-ready, source incomplete | Backend derives carried amount/status from Character inventory state and Rules Core definitions. |
| Manual Feats | Character advancement occurrences | Modeled + displayed | Existing behavior retained. |
| Class/Subclass/Species granted Features & Traits | Advancement selections exist, granted-feature projection does not | Missing semantic projection | Rules Core should project grants from effective Character selections; do not copy granted feature prose/state into Character storage. |
| Spellcasting metadata | Client presentation shape exists | Presentation-ready, source incomplete | Current production backend does not expose a complete spellcasting projection. |
| Actual spells | None | Missing semantic model | Add spell identity/selection projection and Character-owned choices where applicable. |
| Spell slots/resources | None as complete Character runtime contract | Missing semantic model | Rules Core defines resource model; Character state owns mutable consumption. Support slots, spell points, Pact Magic, and future systems without frontend formulas. |
| Prepared/known state | None | Missing semantic model | Model only where the active rules configuration needs it. The project's house rules remove prepared-spell restrictions; do not hard-code stock 5e preparation behavior. |
| Pact Magic | No complete spell-resource contract | Missing semantic model | Must remain a valid resource system alongside slots/spell points rather than being filtered out. |
| Appearance / age / height / weight | None | Missing semantic model | Character Details/Profile data. |
| Personality Traits / Ideals / Bonds / Flaws | None | Missing semantic model | Character Details/Profile data, not Notes. |
| Backstory | Notes are not a semantic substitute | Missing semantic model | Character Details/Profile long-form field. |
| Allies & Organizations / Symbol | None | Missing semantic model | Character Details/Profile structured data. |

## Implementation included in this pass

1. Persist Death Save successes and failures as Character-owned runtime state with database migration, API mutation, authorization/read-only behavior, and regression coverage.
2. Keep Death Saves visible beside HP and provide persisted increment/decrement/reset controls. The UI does not decide what happens at three successes or failures.
3. Recognize backend-supplied Hit Dice as a health/resource presentation role and show it in the HP area. Character Sheet does not calculate Hit Dice.
4. Add progressive AC details that render arbitrary supplied contribution labels, related values, and provenance instead of a hard-coded 3.x formula.
5. Preserve arbitrary non-promoted defense mechanics under **More defenses**, providing a natural home for Miss Chance and future defense values without recreating an `Other Defensive Mechanics` card.
6. Extend server presentation records for Defense and Saving Throw breakdown/related-value data so authoritative future bridge data is not blocked by the Character Sheet API shape.

## Deliberately deferred

Identity/biography, rich inventory/equipment state, granted Features & Traits, and full spell state require new semantic contracts and often coordination with Site or Rules Core. They are not implemented as Notes, opaque JSON, arbitrary item metadata, or frontend-only state in this pass.

The current Rules Core evaluation API also does not provide a general named contribution breakdown for standard scalar defenses. The AC Details surface is therefore ready to display authoritative components, but this branch does not manufacture an AC breakdown from known 3.x categories or reverse-engineer one from a final value.

## Reference-sheet verification

The official fifth-edition Character Sheet was checked for the first-page play-state fields (including Hit Dice, Death Saves, equipment/currency, languages, and identity metadata), second-page biography/organization fields, and third-page spellcasting resource/list structure:

- Wizards of the Coast, *D&D 5e Character Sheets*: https://media.wizards.com/2020/dnd/downloads/dnd_5e_charactersheets.pdf

The 3.x-specific requirements in this audit are treated as cross-edition project requirements and remain subject to Rules Core reconciliation/provenance rather than being hard-coded by name in the Character Sheet frontend.
