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

The browser does not consume source-native Rules Core DTOs and does not reproduce D&D formulas. A missing projection or missing optional mechanic is rendered as unavailable or omitted. It is not inferred.

## Responsibility model

The terms used below mean:

- **Stable identity**: an identifier that must remain stable enough to join a frontend occurrence to Character-owned state. It is not necessarily player-facing.
- **Character-owned**: persisted because this Character owns or selected the occurrence/state.
- **Rules Core-derived**: definition, relationship, source, or rule meaning comes from Rules Core.
- **Backend-calculated**: the backend has already applied the relevant rules and supplied the final displayable mechanical result.
- **Display-only**: prepared by the backend or integration layer for presentation. The frontend may format or group it but does not assign rule meaning.
- **Required/optional**: required or optional within the frontend projection shape, not necessarily within every game rule.

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

## Saving throws

`SavingThrowView` extends the calculated-value primitive with optional `governingAbility` and `training`.

The collection is arbitrary. A backend may supply Fortitude/Reflex/Will, six ability saves, or another rule-defined save model. The frontend neither selects the save model nor computes values.

## Defenses

`DefenseGroupView` contains:

- optional `primaryKey`;
- required `values: DefenseView[]`.

`DefenseView` is a calculated value with an optional extensible presentation `role`. `primaryKey` controls visual ordering when present. The backend may supply only Armor Class, or Armor Class plus Touch, Flat-Footed, Damage Reduction, Spell Resistance, or future defenses. The frontend does not invent absent defenses.

## Health tracks

`HealthTrackView` contains:

| Field | Required | Classification |
| --- | --- | --- |
| `key` | yes | stable projection identity |
| `label` | yes | Rules Core-derived/display-only |
| `role` | yes | Rules Core-derived extensible presentation key |
| `current` | no | Character-owned and/or backend-calculated |
| `maximum` | no | backend-calculated |
| `formattedValue` | no | backend-calculated/display-only |
| `detail` | no | display-only |
| `sourceAttributions` | no | Rules Core-derived/display-only |

Known role hints include hit points, temporary hit points, nonlethal damage, and resource. The frontend currently does not apply special calculation behavior based on these role strings, so future backend-supplied roles are permitted.

Hit points, temporary hit points, and nonlethal damage are distinct tracks and must not be merged.

## Combat fundamentals

`CharacterMechanicsView.combatFundamentals` is an arbitrary list of calculated mechanical values. Examples include Base Attack Bonus, Grapple or another maneuver value, Initiative, Proficiency Bonus, or future rule-defined combat fundamentals.

The backend decides which values apply. Base Attack Bonus and Proficiency Bonus are not equivalent and may coexist.

## Competencies

`CompetencyView` extends the calculated-value primitive with optional:

- `kind`: extensible presentation key; known hints include skill/tool/other;
- `ranks`;
- `governingAbility`;
- `training`;
- `classSkill`;
- `trainedOnly`;
- `armorCheckPenalty`;
- `specialty`.

Ranks, final modifiers, class-skill effects, trained-only rules, and Armor Check Penalty effects are backend/Rules Core responsibilities. The frontend displays supplied facts.

### Specialty competencies

Specialized entries such as a named Craft specialty are represented by ordinary `CompetencyView` data, including `specialty` when useful. The frontend does not identify specialties by parsing competency names.

### Composite relationships

`CompetencyRelationshipView` contains required `parentKey` and `componentKeys[]`. `buildCompetencyPresentation()` groups entries only from these supplied relationships.

The frontend never decides that one named skill contains another named skill. Relationships can have arbitrary component counts.

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

## Movement

`CharacterMechanicsView.movement` is an arbitrary list of calculated values. Each entry can represent walking speed or another backend-defined movement mode. The frontend does not assume one movement mode or calculate speed.

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

`CharacterCheckView` contains required `key` and `name`, plus optional display fields for ability, competency/tool, target, a backend-calculated modifier/result, and source attribution.

A check is a resolved presentation description. The browser does not select governing abilities or proficiencies.

## Multi-part procedures

`CharacterProcedureView` contains:

- required `key`;
- required `name`;
- required `components: CharacterCheckView[]`;
- optional backend-supplied `result`;
- optional state;
- optional source attribution.

Component count is arbitrary. A combined procedure result is supplied by the backend. The frontend does not add component results together or implement a source-specific procedure formula.

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

`SourceAttributionView` contains required `key` and `label`, plus optional detail, official URL, and link label.

Attribution text is always preserved when supplied. The frontend validates `officialUrl` before rendering a link. The current presentation policy permits absolute HTTPS URLs only. Unsupported schemes such as `javascript:`, HTTP, relative URLs, and malformed values render attribution without a clickable link.

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
- `sourceAttributions`.

Every collection is optional so the backend can expose mechanics incrementally. Omitted data means unavailable. An explicitly supplied empty collection means the backend resolved the category and found no entries.

## Current production bridge state

The current application passes `null` for both generalized projections:

```text
renderCharacterWorkspace(
    ...,
    advancement = null,
    mechanics = null,
    ...
)
```

This is intentional. Test fixtures exercise the presentation contract, but production does not substitute fixture values.

The next Character Sheet backend task should assemble `CharacterAdvancementView` and `CharacterMechanicsView` from Character-owned state plus Rules Core semantics/calculations. It should not require changes to the sheet layout or introduce source-native DTO knowledge into the frontend.

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
