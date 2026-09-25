# Character Mechanics Presentation Contract

This document defines the frontend-facing projection boundary for the Dorks & Dice Character Sheet. It is the handoff contract between Character-owned persistence, Rules Core mechanics, backend calculation/orchestration, and the browser UI.

The frontend consumes two independent nullable projections:

```text
Character-owned state
+ Rules Core definitions and relationships
+ backend calculation/orchestration
    -> CharacterAdvancementView
    -> CharacterMechanicsView
    -> Character Sheet presentation
```

The browser does not consume source-native Rules Core DTOs and does not reproduce D&D formulas. A missing projection or missing optional mechanic is rendered without inference. Value-oriented sheet surfaces remain present where practical and use `-` for an unavailable value rather than inventing `0`, `false`, or another game value.

## Responsibility model

The terms used below mean:

- **Stable identity**: an identifier that must remain stable enough to join a frontend occurrence to Character-owned state. It is not necessarily player-facing.
- **Character-owned**: persisted because this Character owns or selected the occurrence/state.
- **Rules Core-derived**: definition, relationship, source, or rule meaning comes from Rules Core.
- **Backend-calculated**: the backend has already applied the relevant rules and supplied the final displayable mechanical result.
- **Display-only**: prepared by the backend or integration layer for presentation. The frontend may format or group it but does not assign rule meaning.
- **Required/optional**: required or optional within the frontend projection shape, not necessarily within every game rule.

## Character identity and authored biography

Character identity is composed from owner-specific sources rather than flattened into one Character Sheet record:

| Data | Authority | Character Sheet behavior |
| --- | --- | --- |
| Character name | Site | Display transiently; never duplicate as local authoritative identity. |
| Player Name | Site authenticated-user projection | Keep it in Site account context when supplied; never infer it from local ownership state or duplicate it in Character Sheet header chrome. |
| Campaign name(s) | Site Tool Host campaign projection | Display safe names when supplied; raw Campaign IDs remain implementation identity. |
| Race / Species | Character selection + Rules Core concept | Persist only the stable concept key; resolve presentation from Rules Core and keep compact identity in the Character header. |
| Background | Character selection + Rules Core concept | Persist only the stable concept key; expose choose/replace/clear and present it in the Background tab. |
| Deity | Character selection + Rules Core concept | Persist only the stable concept key; present the rule-backed selection in the Background tab and keep authored profile Deity text separate for custom/legacy biography. |
| Alignment | Character-authored profile today | Persist/display as authored text; do not pretend it is a canonical Rules Core concept until such a contract exists. |
| Size | Rules Core Character mechanics projection | Render the authoritative projected value and any source details. |

A missing optional Site display projection is rendered as unavailable rather than reconstructed from IDs. Character Sheet does not infer Player Name, Campaign display names, Background, Deity, Alignment, or Size from labels, source documents, or adjacent Character state.

## Character-owned advancement progress

The routine Character state contains nullable `advancementProgress`, a nonnegative integer. This is intentionally a semantic-neutral storage slot rather than an XP model.

The browser may display and edit it as **Advancement Progress** within Advancement details. It must not place the generic value in the Character identity header, label the value XP, calculate thresholds, level the Character automatically, or send the value to Rules Core under an invented mechanic key. If Rules Core later supplies a progression label or ruleset-specific interpretation, that semantic layer may relabel the same Character-owned value without changing its storage identity.

## Character portrait and art

Character art is Character-owned nonmechanical state. PostgreSQL stores asset metadata and portrait designation; application-managed storage contains the image bytes.

The presentation contract permits:

- multiple art assets per Character;
- zero or one selected portrait;
- portrait display in the Character header;
- a Details gallery with full-size viewing;
- upload, portrait selection/replacement, portrait clearing, and deletion for editable Characters;
- view-only behavior for archived/read-only Characters.

Uploads are technically constrained to validated PNG, JPEG, WebP, or GIF images up to 8 MiB. These constraints are storage/security limits, not game rules. Character Sheet does not assign mechanical meaning to an image, extract Character facts from art, or copy image bytes into Rules Core.

## Advancement

`CharacterAdvancementView` contains `occurrences: AdvancementOccurrenceView[]`.

| Field | Required | Classification | Frontend responsibility |
| --- | --- | --- | --- |
| `occurrenceId` | yes | stable identity; Character-owned | join/render only; keep implementation identity out of ordinary visible copy |
| `conceptKey` | yes | Rules Core-derived identity | retain for integration/debug attributes; do not interpret by name |
| `kind` | yes | Rules Core-derived | open-ended string; render as supplied |
| `kindLabel` | no | display-only | preferred human label when supplied |
| `displayName` | yes | Rules Core-derived/display-only | visible advancement name |
| `progression` | no | Rules Core-derived/backend-prepared | render supplied label/value; never assume Character level |
| `parentOccurrenceId` | no | stable relationship supplied by backend | resolve only against supplied advancement occurrences; never infer parentage |
| `progressionDetails` | no | backend-prepared/display-only | render as additional facts |
| `grantedFeaturesOrMechanics` | no | Rules Core-derived/backend-prepared | present references/details; do not create a separate feature engine |
| `sourceAttributions` | no | Rules Core-derived/display-only | render attribution and validated external source link |

`AdvancementProgressionView` contains a required `label` and `value`, plus optional `formattedValue`. The label is rule-defined. Examples include **Level**, **Rank**, **Tier**, **Standing**, or future labels.

The following are distinct advancement kinds even though each participates in Character advancement:

- Class;
- Subclass;
- Prestige Class;
- Feat;
- Position;
- future rule-defined advancement kinds.

The frontend does not equate Class level, Prestige Class level, Position rank, or Feat occurrence. Subclasses normally have a backend-supplied parent Class occurrence. Prestige Classes and Positions remain independent unless their rules explicitly supply a relationship. `kind` is intentionally open-ended.

## Calculated mechanical values

`CalculatedMechanicalValueView` is the common display primitive used by saving throws, defenses, combat fundamentals, movement, checks, attacks, spellcasting values, and item mechanics.

| Field | Required | Classification | Frontend responsibility |
| --- | --- | --- | --- |
| `key` | yes | stable projection identity | DOM/presentation identity only |
| `label` | yes | Rules Core-derived/display-only | render |
| `effectiveValue` | yes | backend-calculated | render; never recalculate |
| `formattedValue` | no | backend-calculated/display-only | preferred display when supplied |
| `unit` | no | Rules Core-derived/display-only | append only when no formatted value is supplied |
| `breakdown` | no | backend-calculated | progressive disclosure |
| `relatedValues` | no | backend-calculated/display-only | progressive disclosure |
| `sourceAttributions` | no | Rules Core-derived/display-only | render attribution |

The frontend may choose `formattedValue` over raw `effectiveValue`, but it does not derive modifiers, totals, DCs, Armor Class, save values, load thresholds, attack sequences, or other rule outcomes.

### Contribution breakdowns

`MechanicalContributionView` supplies a backend-calculated contribution with `key`, `label`, `effectiveValue`, and optional formatting/unit/source attribution. Contributions are display explanation, not operands for a frontend formula.

`RelatedMechanicalValueView` is similar but represents a related resolved value rather than a calculation contribution.

## Ability values

`CharacterMechanicsView.abilityValues` contains backend-prepared effective Ability values using the calculated-value primitive.

For the six structural Ability cards, the stable join is exact key equality between `CalculatedMechanicalValueView.key` and the existing Character `CharacterAbilityKey` values:

- `strength`;
- `dexterity`;
- `constitution`;
- `intelligence`;
- `wisdom`;
- `charisma`.

This join is a presentation contract, not name matching. The frontend does not compare labels such as "Strength" to identify an Ability.

| Data | Classification | Frontend behavior |
| --- | --- | --- |
| persisted base Ability input | Character-owned | remains the editable structural input and is shown as base-input context |
| effective `effectiveValue` / `formattedValue` | backend-calculated | displayed as the normal effective Ability value when supplied |
| `relatedValues` such as a supplied modifier | backend-calculated/display-only | displayed exactly as supplied; the frontend does not derive which modifier should exist |
| `breakdown` | backend-calculated/provenance | shown through progressive disclosure |
| `sourceAttributions` | Rules Core-derived/display-only | shown with the effective Ability details |
| `key` | stable projection identity | joins the six structural cards when it exactly matches a `CharacterAbilityKey` |

The base input and effective value are deliberately separate. Editing a base score changes only Character-owned base input state through the existing structural mutation path. It does not overwrite the effective projection.

If an effective Ability entry uses a key that is not one of the six current structural `CharacterAbilityKey` values, the frontend renders it in the generic **Additional Abilities** presentation instead of silently discarding it. Such an entry has no structural base-score editor unless the Character backend later adds an owned input contract for that key.

Omitted or unmatched effective data never causes the frontend to calculate a value or modifier. The browser does not implement an Ability modifier formula and does not assume a relationship such as `(score - 10) / 2`.

The six structural Ability cards use the same dense primary/subordinate pattern as Armor Class. The effective score occupies the primary region. The subordinate row contains the supplied **Modifier** and the matching six-Ability **Save** when that save is supplied. Saving throws are associated by normalized `governingAbility` or their stable Ability-save mechanic key. Fortitude, Reflex, and Will are separate 3.x saving-throw categories and never replace Constitution, Dexterity, or Wisdom saves in the Ability cards. Those three saves occupy the dedicated Saving Throws card in the left reference rail. Missing modifiers or saves render as `-`; the frontend never derives a save value. Additional future save models that do not map to a structural Ability are preserved in that dedicated card rather than discarded.

## Persistent support surfaces

The Character Sheet keeps a Beyond-style left reference rail with **Saving Throws**, **Senses**, and **Proficiencies & Training**. The Senses card combines **Passive Values** with **Additional Senses** while preserving their separate backend collections. Rules Core mechanics with generic kinds `passive-value`, `sense`, `proficiency`, or `training` project into those collections without the browser inventing a fixed taxonomy. Non-skill competency entries with a supplied training state may also appear in **Proficiencies & Training**. Empty collections use a neutral `-` state. The frontend does not fabricate Perception/Investigation passives, Armor/Weapons/Languages rows, senses, or any other named concept that the projection did not supply.

## Saving throws

`SavingThrowView` extends the calculated-value primitive with optional `governingAbility` and `training`.

The collection is arbitrary. A backend may supply Fortitude/Reflex/Will, six Ability saves, or another rule-defined save model. The frontend never computes a save value. When Rules Core supplies a saving-throw definition but Character-specific inputs are not yet sufficient to evaluate it, the backend keeps that save in the projection with `-` as its value rather than dropping it. Saving throws that identify one of the six structural Ability saves are presented inside that Ability card. Fortitude, Reflex, and Will remain independent save categories and are shown in the dedicated Saving Throws card. Unmapped future save models remain valid projection data and are also preserved there rather than being forced into an unrelated Ability card.

## Defenses

`DefenseGroupView` contains:

- optional `primaryKey`;
- required `values: DefenseView[]`.

`DefenseView` is a calculated value with an optional extensible presentation `role`. `primaryKey` identifies the primary defense when present. The backend may supply only Armor Class, or Armor Class plus Touch, Flat-Footed, Damage Reduction, Spell Resistance, or future defenses. When Rules Core supplies an applicable defense definition but Character-specific state is insufficient to evaluate it, the backend preserves the defense with `-` as its value.

The Character Sheet promotes the Armor Class family to the core-stat region. Primary Armor Class occupies the dominant portion of one card beside Movement and Initiative; Touch AC and Flat-Footed AC are subordinate variants in that same card. The frontend does not calculate any of the three. Their agreed presentation slots remain visible with `-` when unresolved. The combat-summary **Defenses** card keeps Resistances, Immunities, Vulnerabilities, Damage Reduction, and Spell Resistance together. Damage Reduction and Spell Resistance remain separate mechanics from the resistance/immunity/vulnerability collections, but they share the same player-facing card. They are not duplicated in a lower Skills-side defense panel.

## Inspiration

Inspiration is a Character-owned boolean runtime fact. The top strip always reserves the Inspiration presentation slot. Once Character routine state is loaded, absence of the fact means **off** and the player can toggle it on or off directly; the value persists through the existing Character boolean-fact state contract under the stable key `inspiration`. Read-only Characters display the state without mutation. While routine state is unavailable, the slot shows `-`. Rules Core may still project Inspiration-related mechanics or provenance, but it does not own this player-controlled on/off state and the frontend does not invent spending, granting, recovery, or automatic reset behavior.

## Health tracks

`HealthTrackView` contains:

| Field | Required | Classification |
| --- | --- | --- |
| `key` | yes | stable projection identity |
| `label` | yes | Rules Core-derived/display-only |
| `role` | yes | Rules Core-derived extensible presentation key |
| `current` | no | backend-calculated/display-only unless a Character-owned runtime value overrides that track |
| `maximum` | no | backend-calculated |
| `formattedValue` | no | backend-calculated/display-only |
| `detail` | no | display-only |
| `sourceAttributions` | no | Rules Core-derived/display-only |

Known role hints include hit points, temporary hit points, nonlethal damage, and resource. The frontend may use these role hints for placement only; it does not apply calculation behavior based on them, so future backend-supplied roles remain permitted.

Hit points, temporary hit points, and nonlethal damage are distinct tracks and must not be merged. For the primary Hit Points track, Character Sheet-owned routine state is authoritative for **Current HP** when that state is available; Rules Core remains authoritative for calculated/maximum mechanics. The Hit Points card gives Current and Maximum the primary visual emphasis and follows the same compact interaction hierarchy as the reference sheet: Current, Max, Temporary HP, and the adjustment controls remain visible without opening a generic editor. The Character Sheet adds its required cross-edition state, including Nonlethal Damage and arbitrary additional health/resource tracks. Editable Characters expose direct **Damage** and **Heal** controls around a numeric adjustment amount; direct Current HP assignment, additional health tracks, and source details remain available through secondary disclosure. All mutations persist only Character-owned current HP. Healing is capped at a supplied numeric maximum, while damage is deliberately allowed below zero because 3.x-style negative hit points remain meaningful. The frontend does not invent an edition-specific death floor. Temporary HP, when supplied, and Nonlethal Damage occupy secondary fields beneath them.

The same Health surface exposes **Short Rest** and **Long Rest** actions as stable UI affordances. Those buttons represent rest intent only. Character Sheet does not hard-code 5e, 5.5e, or 3.x rest recovery semantics. Until the effective rules projection supplies an executable rest contract, both actions remain disabled; when a rules-backed handler is available, the button emits only the selected rest kind and the backend/rules layer owns all resulting recovery and resource changes. Nonlethal Damage retains a persistent `-` presentation slot when unresolved; Temporary HP is shown when the backend supplies that track. Additional health/resource roles render generically below the primary presentation. A supplied track with no resolved value renders as `-`; the frontend does not relabel unresolved state as `Available`.

## Combat fundamentals

`CharacterMechanicsView.combatFundamentals` is an arbitrary list of calculated mechanical values. Examples include Base Attack Bonus, Grapple or another maneuver value, Initiative, Proficiency Bonus, or future rule-defined combat fundamentals.

The backend remains authoritative for combat values and their calculations. Base Attack Bonus and Proficiency Bonus are not equivalent and may coexist. Applicable Rules Core combat-value definitions remain present with `-` until the Character backend can supply an authoritative evaluation. The agreed Character Sheet Base Attack Bonus and Grapple Modifier slots remain visible with `-` when no matching projection is available. A supplied Initiative value is promoted to the compact quick-stat region beside Movement and is not duplicated in the lower combat group; this is presentation placement only and does not alter its calculation.

## Competencies

`CompetencyView` extends the calculated-value primitive with optional:

- `kind`: extensible presentation key; known hints include skill/specialized-skill/tool/other;
- `ranks`;
- `governingAbility`;
- `training`;
- `classSkill`;
- `trainedOnly`;
- `armorCheckPenalty`;
- `family`;
- `specialty`;
- `supportsRanks`;
- `supportsClassSkillState`;
- `supportsTrainingState`;
- `presentationCategory`: open-ended Rules Core-owned placement metadata, with `skill`, `competency`, and `supporting` as current values.

The three `supports...` fields and family/specialty metadata describe the normalized Rules Core competency contract. They do not assert that this Character has configured ranks, training, or class-skill state. When a state dimension is supported but the Character-owned value is absent, the frontend displays `-`; it must not substitute `0`, `false`, or another inferred value.

Rules Core now exposes an authoritative universal competency catalog separately from its source-shaped implementation mechanics. Character Sheet treats the universal catalog as the presentation identity. A universal entry uses a semantic key such as `competency.alchemy`, while its `mechanicKeys` point to the Rules Core implementation mechanics used for evaluation and compatibility. Historical skill names, tool names, and other import aliases do not become duplicate Character Sheet rows.

### Skills and Proficiencies & Training placement

The Character Sheet partitions the universal catalog only from Rules Core `presentationCategory` metadata:

- `skill` entries render in the **Skills** card;
- `competency` entries render in a **Competencies** subsection inside **Proficiencies & Training**;
- `supporting` entries remain available to backend/rules reconciliation but do not render as direct Character-facing rows;
- other explicit future non-`skill`, non-`supporting` categories remain visible through the Competencies subsection rather than being silently discarded;
- responses that predate `presentationCategory` remain in **Skills** as a compatibility fallback.

The **Skills** card therefore remains focused on ordinary/historical skills and Rules Core-supplied composite skill relationships. **Proficiencies & Training** owns the normalized broader competency presentation alongside other training and proficiency state. The generic Craft family and its 3.x-only specialties do not become a visible `Craft -> specialties` list. Reviewed shared Craft/tool identities such as Alchemy render once as normalized competencies, while their historical ranked-skill and later tool facets remain available behind that identity.

A shared Craft/tool identity renders once because the split occurs after Rules Core has already reconciled the source facets into one semantic competency. Tool proficiency state is displayed as training/proficiency state; it is never converted into ranks. Rank editing remains available only when Rules Core supplies `supportsRanks` and an unambiguous `rankInputKey`.

The frontend does not classify entries by parsing `Craft (...)`, `Tools`, `Kit`, `Supplies`, publisher names, source editions, or competency `kind`. `supporting` is the only current category intentionally omitted from direct Character presentation.

Character Sheet may carry `rankInputKey` for the one unambiguous implementation concept that owns rank state. This is an implementation-state join only. The visible row remains keyed by the universal semantic competency. If no unambiguous rank implementation is supplied, the frontend does not invent one and does not expose a rank editor.

Ranks, final modifiers, class-skill effects, trained-only rules, and Armor Check Penalty effects are backend/Rules Core responsibilities. The frontend displays supplied facts.

### Specialty competencies

Specialized entries are represented by ordinary `CompetencyView` data. `family` and `specialty` remain separate normalized fields when Rules Core supplies them. A Rules Core entry marked `isFamily: true` is rendered as an organizational family. When `childCompetencyKeys` are supplied, those keys are authoritative for family membership. Matching a family label is retained only as a compatibility fallback for older Rules Core responses.

This allows Rules Core to expose reviewed catalog members even when no source-shaped implementation mechanic exists for a particular family child. For example, Character Sheet can render the complete reviewed Craft family from semantic entries while only those members with applicable implementation mechanics expose calculation/rank controls.

The frontend does not identify specialties, parse names such as `Craft (...)`, deduplicate tools and skills by display name, or maintain a hard-coded list of family names. Family nesting is driven only by authoritative Rules Core semantic metadata. This is distinct from composite relationships, which represent explicit mechanical composition rather than taxonomy.

### Composite relationships

`CompetencyRelationshipView` contains required `parentKey` and `componentKeys[]`, plus optional backend-supplied `composition` and `resolutionKind` presentation metadata. `buildCompetencyPresentation()` groups entries only from these supplied relationships.

The frontend never decides that one named skill contains another named skill. Relationships can have arbitrary component counts. The collapsed Character Sheet keeps the existing parent-left/components-right composite layout. The whole composite block is one progressive-disclosure unit: expanding it may show component calculation details and the backend-supplied composition/resolution metadata, but the browser does not calculate the parent competency itself.

## Actions and attacks

`ActionAttackView` contains required `key` and `name`, with optional:

- action type;
- backend-calculated attack/check value;
- damage and damage type;
- critical range and multiplier;
- range;
- reach;
- ammunition;
- target;
- notes;
- source attribution.

The backend supplies attack sequences, bonuses, damage, critical information, and other mechanics. The frontend does not derive iterative attacks or edition-specific formulas.

When an Actions or Spellcasting collection is absent or empty, the section remains present and uses the same neutral `-` convention as other unresolved sheet surfaces instead of a large explanatory unavailable-state message.

## Source attribution presentation

Source attribution is provenance, not the primary mechanic. Entries with `presentationRequired: true` remain directly visible wherever the attribution contract requires them. Optional source metadata may be consolidated behind a secondary **Sources (N)** disclosure so edition/source provenance does not overwhelm the normal Character Sheet. Collapsing optional provenance never changes the underlying mechanic or removes access to the supplied attribution.

## Movement

`CharacterMechanicsView.movement` is an arbitrary list of calculated values. Each entry can represent walking speed or another backend-defined movement mode such as swim, climb, fly, burrow, or a source-specific movement type. The frontend never calculates speed.

The compact top-row Movement card keeps **Walk** visible as the primary speed while **Swim**, **Climb**, **Fly**, and arbitrary additional backend-defined modes remain available through a compact secondary disclosure. This avoids making Movement taller than the neighboring top-row cards or introducing an internal horizontal scrollbar. Missing common values render as `-`; arbitrary supplied modes such as Burrow are not discarded.

Character Sheet recognizes normalized Rules Core mechanics with kind `movement` and projects all such effective entries into this collection. The frontend never calculates a speed or invents a movement capability.

## Item-occurrence mechanics

`ItemOccurrenceMechanicsView.occurrenceId` is a stable Character-owned join key matching an owned Inventory occurrence. Optional calculated values, facts, and source attribution are rendered inside that occurrence.

Two occurrences of the same Rules Core item concept remain separate because joins use occurrence identity, not `ConceptKey` or display name.

Ownership, add/remove behavior, and duplicate occurrence semantics remain part of Character-owned Inventory state.

## Carrying and load

`CarryingLoadView` may supply:

- current carried amount;
- current load/status;
- arbitrary threshold display fields;
- source attribution.

These fields are backend-calculated/display-only. A simple carried-weight model and a threshold-based load model use the same frontend shape. The frontend does not calculate carrying capacity or determine a load category.

## Checks

`CharacterCheckView` contains required `key` and `name`, plus optional display fields for ability, competency/tool, target, a backend-calculated modifier/result, source attribution, and the display-only `supplemental` hint. The backend derives `supplemental` from generic Rules Core applicability; the frontend does not identify publishers or rule families by name.

A check is a Rules Core-backed presentation description. The browser does not select governing abilities or proficiencies. When its final modifier/result is unresolved, the backend supplies a `Result` value of `-` rather than omitting the result surface.

## Multi-part procedures

`CharacterProcedureView` contains:

- required `key`;
- required `name`;
- required `components: CharacterCheckView[]`;
- optional backend-supplied `result`;
- optional state;
- optional source attribution;
- optional display-only `supplemental` hint.

Component count is arbitrary. A combined procedure result is supplied by the backend. When that result is unresolved, the backend supplies `Result -`. The frontend does not add component results together or implement a source-specific procedure formula.

Standalone checks/procedures render in the Actions workflow area unless already nested under a more specific surface such as Inventory/Crafting.

## Component/material presentation

`ComponentMaterialView` contains required `key` and `label`, with optional quantity, arbitrary display fields, and source attribution.

The frontend does not assume a fixed number of component types, tiers, ranks, or material fields.

## Crafting state

`CraftingProcedureView` contains required `key` and `name`, with optional:

- target output;
- required inputs;
- competency/tool fields;
- nested generalized procedure;
- progress;
- result;
- source attribution.

Recipe requirements, progress rules, success criteria, and results are backend/Rules Core responsibilities. The projection is presentation state, not a frontend crafting rules engine.

## Source attribution

`SourceAttributionView` contains required `key` and `label`, plus optional detail, official URL, link label, and `presentationRequired`. The latter preserves Rules Core's compliance/presentation requirement rather than asking the frontend to infer importance from a publisher or work name.

`CharacterMechanicsView.sourceAttributions` is retained for projection-wide or rules-module attribution that legitimately applies across several Character mechanics. The sheet renders it once in a restrained Character-mechanics-level **Rules modules** surface. It is not intended to be a roll-up of every child mechanic source; when attribution applies only to a particular Ability, defense, action, component, procedure, or other mechanic, it belongs on that specific projection instead. The backend/integration layer should therefore avoid repeating the same attribution at both levels without a semantic reason.

Attribution text is always preserved when supplied. The frontend validates `officialUrl` before rendering a link. The current presentation policy permits absolute HTTPS URLs only. Unsupported schemes such as `javascript:`, HTTP, relative URLs, and malformed values render attribution without a clickable link.

Rules Core mechanics with generic `external-public-rules` applicability are projected as supplemental. In the Actions workflow, supplemental checks and procedures are collapsed by default so optional external modules do not dominate the ordinary Character Sheet. Any supplied presentation-required source attribution remains visible outside that collapsed disclosure, including creator credit and a required official-rules link. This behavior is driven by applicability and attribution metadata, not a Loot Tavern or publisher-specific frontend branch.

External-rule integrations should identify the source and official creator/site where available without redistributing protected source prose, tables, recipes, books, files, artwork, or layouts.

## Spellcasting profiles

`SpellcastingProfileView` contains required `key` and `label`, with optional:

- casting source;
- casting ability;
- backend-calculated save DC;
- backend-calculated spell attack;
- resource system;
- domains;
- specialty school;
- prohibited schools;
- Arcane Spell Failure display field;
- bonus-spell metadata;
- arbitrary metadata;
- source attribution.

This projection intentionally does not define a complete preparation/slot engine. The frontend displays supplied spellcasting metadata and values without computing spell resources or DCs.

## CharacterMechanicsView top-level shape

The projection may supply:

- `abilityValues`;
- `inspiration`;
- `passiveValues`;
- `training`;
- `senses`;
- `savingThrows`;
- `defenses`;
- `combatFundamentals`;
- `healthTracks`;
- `competencies`;
- `actions`;
- `movement`;
- `inventory`;
- `checks`;
- `procedures`;
- `spellcastingProfiles`;
- `sourceAttributions` for projection-wide rules-module attribution.

Every collection is optional so the backend can expose mechanics incrementally. Omitted data means unavailable. An explicitly supplied empty collection means the backend resolved the category and found no entries.

## Current production bridge state

Production now reads `GET /api/characters/{characterId}/presentation` and passes the returned `advancement` and `mechanics` projections into `renderCharacterWorkspace(...)`. The frontend owns loading/error/stale-request state only; it does not call Rules Core mechanics endpoints directly.

The Character Sheet backend uses the Site's source-bound Tool-to-Tool delegation capability to call Rules Core as the same authenticated Site user. Rules Core therefore continues applying that user's source grants. The bridge uses global effective rules only. A Character's Campaign associations never select a Campaign mechanics scope implicitly.

Advancement always preserves Character-owned occurrence identity, concept identity, persisted parent occurrence identity, and open-ended kind. The backend resolves display/source metadata by the stable concept key itself; it does not use advancement `kind` as a Rules Core `entityType` filter. An inaccessible or unresolved reference remains present as an unavailable occurrence. Progression is omitted because the current Character-owned model does not establish Class level, Prestige Class level, Position rank, tier, standing, or another progression value.

Mechanics are capability- and input-driven. The bridge consumes Rules Core's universal `competencies` catalog for Character-facing competency identity and keeps the raw `mechanics` collection only for calculation, relationship, and compatibility joins. Multiple source-shaped skill/tool implementations of one semantic competency therefore render once. Reviewed family members with no implementation row can still appear under their Rules Core-defined family. Effective `derive-parent` relationships are translated to semantic competency keys before reaching the browser.

Rules Core defaults are not treated as proof that missing Character state is configured. A competency whose Character inputs are not modeled is shown as `-`, never zero. Base Ability inputs are not relabeled as effective Abilities.

Rules Core failure degrades mechanics independently. The Character page continues to load its build, Inventory, and Notes state, and Character-owned advancement occurrences remain present even when Rules Core display metadata is unavailable.

## Explicit frontend non-responsibilities

The frontend does not:

- calculate ability modifiers, saving throws, AC, Touch AC, Flat-Footed AC, BAB, Proficiency Bonus, maneuver values, DCs, attack sequences, load thresholds, crafting outcomes, or composite procedure results;
- infer competency relationships from names;
- infer advancement parents from advancement kinds;
- equate Class level, Prestige Class level, Position rank, Feat occurrence, or other progression values;
- hard-code Acquisitions Incorporated Position mechanics;
- hard-code Loot Tavern harvesting/crafting formulas or prose;
- change Character Inventory ownership semantics;
- turn unavailable backend mechanics into guessed values.
