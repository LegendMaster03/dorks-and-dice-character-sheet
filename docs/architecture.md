# Character Sheet architecture

## Ownership boundaries

The Dorks & Dice Site remains authoritative for canonical Character identity and lifecycle. It owns `CharacterId`, account ownership, Character name, active/archive state, permanent deletion, Campaign identity and membership, and Character-to-Campaign associations.

Rules Core is the rule-definition authority. It owns source provenance, normalization across editions, resolved rules, mechanical relationships, combined skills, Classes, Subclasses, Prestige Classes, Feats, race/species concepts, and campaign rule resolution.

Character Sheet owns Character decisions and state: which stable Rules Core concepts a Character selected, builder progress, advancement history, future calculated Character state, and future campaign-scoped Character module state. Character Sheet is not a second rules engine and does not copy resolved rule mechanics into its database.

`CharacterSheetRoot.CharacterId` is exactly the Site-issued `CharacterId`. Character Sheet does not persist a parallel identity, authoritative owner ID, Site Character name, lifecycle, or Campaign membership as a substitute for Site authorization.

## Tool Host Character authorization

The Site supplies Character Sheet an owner-only Character projection in the freshly redeemed Tool Host authentication context. Every rich-sheet and `/build` request authorizes the requested Site `CharacterId` before Character Sheet persistence is read or mutated. A local Character Sheet database row is never sufficient authorization, and Campaign DM authority does not grant another player's Character in this contract.

An active owned Character may be mutated. An archived owned Character may read preserved rich/build state but may not initialize or mutate it. A Character missing from the owner projection returns the same unavailable response whether or not orphaned local rows happen to exist. A missing required Site projection fails closed. Direct unauthenticated backend access remains rejected.

## Rules Core read path

In embedded production use, rule discovery and rule display are browser reads through the Site's authenticated Rules Core Tool Host route:

```text
browser
  -> Dorks & Dice Site
  -> /tool-host/rules-core/api/upstream/api/...
  -> Rules Core
```

Character Sheet never forwards or derives a Rules Core request from Character Sheet's own Tool Host ticket. Tool Host tickets are Tool-scoped. It does not introduce a static shared secret or a second Rules Core authentication system.

This slice consumes the current global resolved Rules Core endpoints:

```text
GET /api/rules?entityType={type}&q={search}&limit=200
GET /api/rules/{conceptKey}
```

`GET /api/rules` is used for searchable resolved catalog choices. `GET /api/rules/{conceptKey}` resolves a persisted reference for display. Rules Core applies its existing current-user/source-access filtering to those reads. Resolved catalog items also carry stable concept-to-concept relationship metadata; Character Sheet currently consumes the `parent-class` relationship on Subclass concepts.

The builder intentionally uses global rules only. A Site Character can belong to multiple Campaigns, so Character Sheet does not arbitrarily select one Campaign's resolved rules. Campaign-specific resolution is deferred until the Character Sheet has an explicit Campaign-context selector/overlay.

For standalone ASP.NET development only, `RulesCore:DevelopmentBaseUrl` may expose an explicit direct Rules Core base URL to the frontend. The standalone shell emits that adapter only when the ASP.NET environment is `Development`; embedded mode ignores standalone adapter attributes and always uses the Site Tool Host path. Production authentication is not weakened for standalone convenience.

## Character presentation projection

The rich sheet has a backend presentation read surface:

```text
GET /api/characters/{characterId}/presentation
```

It returns the frontend-owned `CharacterPresentation` shape with independent `advancement` and nullable `mechanics` projections. The read follows the same Site Character ownership/lifecycle rules as the existing build APIs, does not initialize an uninitialized sheet, and remains readable for archived owned Characters.

For production Rules Core access, Character Sheet consumes the Site-issued Tool-to-Tool delegation capability returned with successful Character Sheet ticket introspection. Requests go through the source-bound Site route for `character-sheet -> rules-core`; Character Sheet never forwards its own Tool ticket, submits a browser/user-supplied user ID, or uses a service identity in place of the requesting user. The Site reconstructs Rules Core's target-specific Tool Host authentication context and Rules Core applies the same user's source grants.

The projection uses global effective Rules Core mechanics only. Character-to-Campaign associations are not a mechanics-scope selector. Campaign mechanics remain deferred until the sheet exposes an explicit Campaign context.

Advancement display resolution uses each persisted stable Rules Core concept key directly through `GET /api/rules/{conceptKey}`. Character Sheet does not reinterpret the Character-owned advancement `kind` as a Rules Core `entityType`; those are separate identity domains. This allows an open-ended advancement kind to reference a concept whose normalized Rules Core entity type is different without making the occurrence disappear.

Rules Core owns evaluation arithmetic. Character Sheet supplies only inputs it can establish from authoritative Character state, batches only evaluations that require no Character/runtime/source inputs, and never substitutes a default or zero for missing state. Current persisted Character state does not yet establish effective Ability scores/modifiers, competency ranks/training/class-skill state, equipment state, spellcasting state, or rule capability grants derived from selected Classes/Species/Feats. Capability-gated 3.x mechanics therefore remain omitted until those contracts/state exist. Item-occurrence mechanics also remain absent because the current Rules Core consumer contract does not expose them.

The browser stores presentation loading state in the explicit application reducer. Each request has a monotonic request ID so stale/out-of-order responses are ignored. Successful mutations of base Ability input, Race/Species, Starting Class, Subclass, Feats, and Inventory ownership refresh the projection. Notes do not trigger a mechanics refresh. Projection failure never replaces the separately loaded build/routine state.

## Stable rule-reference semantics

Character Sheet persists Rules Core `RuleConcept.Key`, exposed as `ConceptKey` by the resolved catalog, as its stable rule reference. The resolved catalog also exposes `RuleConceptId`, display metadata, relationship metadata, source IDs, and source revision IDs, but Character Sheet does not use display name, source-native ID, `SourceEntityRevisionId`, package revision, source JSON, or resolved mechanical `Document` as Character decision identity.

Before persistence, `ConceptKey` input is trimmed and normalized to invariant lowercase so Character Sheet matches Rules Core canonical key behavior. Differently cased representations therefore do not create distinct Character decisions.

A persisted row therefore means only:

```text
This Character selected Rules Core concept <ConceptKey>.
```

Rules Core remains responsible for what that concept currently means and whether its content is currently visible to the authenticated user.

The Character Sheet backend deliberately does not call Rules Core when a selection reference is persisted. Saving a `ConceptKey` is not independent verification of the user's current Rules Core source access and grants no permission to reveal rule content. The authenticated frontend resolves content through Rules Core before displaying it.

If a previously stored `ConceptKey` later resolves to `404`, resolves as the wrong expected entity type, or becomes inaccessible because source/rule resolution changed, Character Sheet retains the stored key. The UI reports the saved rule as unavailable rather than deleting it or substituting a same-named rule. A Rules Core service failure is shown separately as a resolution error, again without clearing the decision.

## Builder persistence model

The root remains:

```text
character_sheet_roots
  CharacterId      canonical Site CharacterId, primary key
  SchemaVersion
  BuilderStatus    BuildInProgress
  CreatedAt
  UpdatedAt
```

Builder choices are intentionally split between foundational rule selections, Character-owned base ability-score inputs, and progression entries.

### Foundational selections

`character_foundational_rule_selections` stores Character-owned foundational decisions:

```text
Id                Character-owned selection identity
CharacterId       Site CharacterId, FK -> root, cascade delete
Category          current value: RaceSpecies
RuleConceptKey    stable Rules Core ConceptKey
CreatedAt
UpdatedAt
```

`(CharacterId, Category)` is unique for the currently active foundational choice. The first public category is presented as `raceSpecies`, allowing the UI to use the combined `Race / Species` terminology without asserting that every source edition calls the concept the same thing. The row has its own identity rather than adding a `RaceId` column to the root, leaving room for future provenance/template/inheritance semantics to evolve independently of the root schema.

### Base ability-score inputs

`character_base_ability_score_inputs` stores one current directly assigned/base input per Character and ability axis:

```text
Id            Character-owned input identity
CharacterId   Site CharacterId, FK -> root, cascade delete
AbilityKey    normalized Character Sheet ability key
Score         directly assigned/base input only
CreatedAt
UpdatedAt
```

`(CharacterId, AbilityKey)` is unique in PostgreSQL. Replacing a value updates the existing Character-owned row instead of appending another current decision; clearing a value removes only that keyed current decision. The current playable API accepts the stable keys `strength`, `dexterity`, `constitution`, `intelligence`, `wisdom`, and `charisma`. The key remains a string-backed child-row identity rather than six columns on the root, so future additional ability axes do not require a root/table redesign.

The stored `Score` is explicitly a **base ability score input**. It is a persisted Character decision, not an effective/final ability score, ability modifier, saving throw, Rules Core-derived bonus, or explicit override. No racial/species, Class, Feat, ASI, temporary-effect, magic-item, proficiency, minimum/maximum, or other derived contribution is applied in this slice. No edition-specific numeric range is imposed.

How the input number was generated is intentionally deferred. The schema does not currently claim rolled, point-buy, standard-array, imported, racial, ASI, DM-grant, or other provenance.

### Progression / advancement entries

`character_advancement_entries` stores Character-owned advancement occurrences:

```text
Id                         Character-owned advancement identity
CharacterId                Site CharacterId, FK -> root, cascade delete
Kind                       Class | Subclass | PrestigeClass | Feat
RuleConceptKey             stable Rules Core ConceptKey
Ordinal                    optional progression order
ParentAdvancementEntryId   optional Character-owned parent entry, self FK
CreatedAt
UpdatedAt
```

`Class`, `Subclass`, `PrestigeClass`, and `Feat` are distinct kinds. The schema does not enforce one total Class, one total Prestige Class, a fixed Subclass level, one universal feat slot, total Character level, multiclass prerequisites, or mature Prestige Class advancement semantics.

The first Starting Class is represented as the `Class` entry at ordinal `0` with no parent. A filtered unique index permits at most one such Starting Class for a Character while still permitting later Class entries and any number of Prestige Class entries. Replacing the Starting Class updates that same Character-owned entry rather than appending duplicate first entries.

A `Subclass` entry must reference a Character-owned `Class` advancement through `ParentAdvancementEntryId`; it can not be parented by a Prestige Class, Feat, or another Character. A Class advancement currently permits at most one Subclass child. Replacing or clearing a Class removes its attached Subclass selection rather than leaving a Character-specific selection attached to the wrong parent Class. Rules Core determines which Subclass concepts belong to which Class concepts; Character Sheet persists only the selected concept key and the Character-owned parent advancement ID.

The persistence relationship includes `CharacterId` in both the foreign key and principal key, so a progression row can not reference an advancement owned by a different Character. The self-reference uses database `NO ACTION`; this preserves referential integrity for surviving rows while allowing the root Character cascade to remove an entire parented advancement graph in one deletion.

Feat occurrences have their own Character-owned entry identities independently from the Rules Core feat concept. Multiple occurrences may reference the same Rules Core concept key and remain distinct rows. The current occurrence contract intentionally leaves `Ordinal` and `ParentAdvancementEntryId` null because the backend does not yet know which level, class feature, ASI exchange, free/flavor grant, DM grant, or other rule produced the Feat.

Acquisition provenance is therefore deferred rather than represented by a speculative enum. A later grant/effect model can attach explicit provenance to the Character-owned occurrence without changing Rules Core identity or collapsing duplicate Feats.

No total Character level is calculated from this immature progression model.

## Current `/build` API

Character build state is exposed as one coherent resource plus class/foundational selection resources:

```text
GET    /api/characters/{characterId}/build
PUT    /api/characters/{characterId}/build/race-species
DELETE /api/characters/{characterId}/build/race-species
PUT    /api/characters/{characterId}/build/ability-scores/{abilityKey}
DELETE /api/characters/{characterId}/build/ability-scores/{abilityKey}
PUT    /api/characters/{characterId}/build/starting-class
DELETE /api/characters/{characterId}/build/starting-class
PUT    /api/characters/{characterId}/build/classes/{classAdvancementEntryId}/subclass
DELETE /api/characters/{characterId}/build/classes/{classAdvancementEntryId}/subclass
POST   /api/characters/{characterId}/build/feats
DELETE /api/characters/{characterId}/build/feats/{featAdvancementEntryId}
```

A rule-selection mutation body, including `POST /build/feats`, is only:

```json
{ "conceptKey": "<stable Rules Core ConceptKey>" }
```

Ability-score input mutation is deliberately separate from Rules Core identity:

```json
{ "score": 15 }
```

`GET /build` and successful mutation responses expose these rows as `baseAbilityScoreInputs`; no `effectiveScore` field is synthesized from the stored input.

Each Feat POST appends a new Character-owned occurrence, even when another occurrence already references the same canonical concept key. Feat deletion uses the Character-owned advancement entry ID, not the Rules Core concept key, so removing one duplicate occurrence preserves the others. Deleting an occurrence ID that is not present on the authorized Character is idempotent and returns the unchanged build; an ID that resolves to a non-Feat advancement on that Character is rejected as an invalid Feat target.

Display names and mechanical JSON are neither accepted as Character Sheet identity nor required for persistence. Mutation responses return the coherent `CharacterBuildView`, not a second Feat-state representation or copied Rules Core mechanics. The Class advancement ID in the Subclass route and the Feat advancement ID in the Feat delete route are Character Sheet-owned identities.

The existing rich-sheet bootstrap API remains:

```text
GET  /api/characters/{characterId}/sheet
POST /api/characters/{characterId}/sheet
```

An owned basic Site Character still uses `POST /sheet` to initialize rich state against the existing Site `CharacterId`; `/new` still creates the Site Character first and initializes exactly the returned canonical identity.

## Current builder UI

The rules-backed Character Builder currently exposes:

```text
<Character Name>

Character Builder

Race / Species
[current choice / Choose / Replace / Clear]

Starting Class
[current choice / Choose / Replace / Clear]

Subclass
[current choice / Choose / Replace / Clear]

Build status: In progress
```

Race / Species queries the global Rules Core catalog with `entityType=race`. Starting Class queries it with `entityType=class`. `prestigeClass` is not offered as a Starting Class. `npcClass` is not offered because the current architecture does not establish NPC Classes as ordinary player Starting Classes.

Subclass selection is unavailable until the Character has a Class advancement. The chooser queries `entityType=subclass`, then accepts only resolved Subclass concepts whose Rules Core `parent-class` relationship identifies the selected Class concept. Character Sheet does not inspect `Document`, `ContentJson`, source-native JSON, or nested Class content to create that list.

The chooser models loading, search, empty results, Rules Core error, selection, replacement, clear, save error, and cancel explicitly. It displays Rules Core identification metadata such as resolved display name, edition/source/package metadata, but does not dump raw resolved JSON or reproduce source prose.

Build status remains `In progress` after these choices are selected. This slice does not invent a complete builder-step state machine.

## Subclass boundary

Rules Core owns Subclass definition and the normalized relationship from a Subclass concept to its parent Class concept. For 5e.tools-shaped source material, the source-native `className`/`classSource` identity evidence is normalized into a persistent Rules Core `parent-class` concept relationship. The resolved catalog exposes that relationship by stable Rules Core concept identity.

Character Sheet consumes that contract only. It stores the selected Subclass `ConceptKey` as a `Subclass` advancement entry and uses `ParentAdvancementEntryId` to attach the Character-owned occurrence to the relevant Character-owned Class advancement. Source revisions can change without changing that Character relationship, and Character Sheet does not scrape source documents to rediscover it.

The current UI applies this contract to the Starting Class because that is the only Class advancement the builder currently creates interactively. The backend/domain contract accepts any existing Character-owned Class advancement entry, leaving later multiclass/level-up work independent from this slice.

## Prestige Class boundary

Prestige Classes remain distinct from ordinary Classes and Subclasses in Character Sheet as `CharacterAdvancementKind.PrestigeClass`, matching Rules Core's distinct `prestigeClass` rule concept type. This slice does not define when Prestige Classes are acquired, how their levels interact with ordinary Class levels, how prerequisites are evaluated, or how prestige spellcasting progression works.

## Routine Character-owned state

Routine Character state is separate from builder/progression decisions and from calculated/effective
mechanics. The persisted routine-state resource contains Character-owned current hit points, inventory
ownership occurrences, and plain Character notes. `CurrentHitPoints` is nullable on the Character
root: null means that current HP has not been recorded. It deliberately does not store or calculate
maximum HP, temporary HP, death thresholds, or edition-specific health rules.

The remaining routine collections are:

```text
character_inventory_item_occurrences
  Id              Character-owned occurrence identity
  CharacterId     Site CharacterId, FK -> root, cascade delete
  RuleConceptKey  stable Rules Core item ConceptKey
  CreatedAt

character_notes
  Id              Character-owned note identity
  CharacterId     Site CharacterId, FK -> root, cascade delete
  Content         Character-authored note text
  CreatedAt
  UpdatedAt
```

An inventory occurrence means only that the Character owns one logical occurrence of the referenced
Rules Core item concept. Duplicate concept keys remain distinct Character-owned occurrences. The model
does not claim equipped, carried, active, attuned, container, currency, ammunition, encumbrance, or
mechanical-effect state. As with builder rule references, the backend persists only the stable
`ConceptKey`; it does not copy display names or resolved mechanical JSON and does not call Rules Core
to grant or validate source access while persisting the reference.

Notes are global Character-owned state. They are not Campaign-scoped modules, rule definitions, or
mechanical effects.

The coherent routine-state API is:

```text
GET    /api/characters/{characterId}/state
PUT    /api/characters/{characterId}/state/health
POST   /api/characters/{characterId}/state/inventory
DELETE /api/characters/{characterId}/state/inventory/{occurrenceId}
POST   /api/characters/{characterId}/state/notes
PUT    /api/characters/{characterId}/state/notes/{noteId}
DELETE /api/characters/{characterId}/state/notes/{noteId}
```

Successful mutations return the same `CharacterStateView` used by `GET /state`. Active owned
Characters may mutate it; archived owned Characters may read it but can not mutate it. These mutation
endpoints never initialize a basic Site Character implicitly.

## Durable Site lifecycle cleanup

Permanent Site deletion is delivered through the existing trusted lifecycle inbox/outbox contract. For `character.deleted`, deleting `CharacterSheetRoot` cascades to foundational selections, base ability-score inputs, and the complete advancement graph in the same Character Sheet database transaction before the lifecycle event is acknowledged. A missing local root is still successful.

`campaign.deleted` remains unrelated to these base/global Character selections and does not remove them. Future Campaign-scoped Character module state belongs behind the existing Campaign cleanup boundary.

## Embedded Module v2 frontend lifecycle

Production registration remains Embedded Module contract v2. The Site shell owns `#tool-root` and supplies host route/context attributes. Application-owned DOM uses the explicit application state/reducer/render lifecycle; `MutationObserver` is not used.

The reducer models sheet loading, basic/rich/archived screens, builder loading/failure, saved rule-reference resolution, chooser open/search/catalog loading/catalog failure, saving, save failure, unresolved references, and read-only behavior. Chooser actions feed state and then the single render path replaces application-owned DOM.

Nested `/characters/{characterId}` routes and `/new` continue to use the host-provided Tool route/base path.

## Implementation module boundaries

The application keeps the existing Domain / Application / Infrastructure / Web layering, but large presentation and browser files are further divided by ownership so a human can locate one feature without editing unrelated features.

Backend Character presentation uses a small orchestration service plus focused projectors under `Characters/Presentation`. `CharacterPresentationService` coordinates Character-owned build state, Rules Core reads, diagnostics, and projection order. Advancement, automatic-evaluation planning, competencies, checks/procedures, mechanical collections, and source attribution remain separate projectors. `CharacterPresentationProjector` is a compatibility facade rather than a second implementation.

The browser follows these ownership rules:

```text
src/
  core/
    application/     shared workflow boundaries and request handling
    mechanics/       generic mechanic rendering/helpers with no feature dependency
  features/
    abilities/
    advancement/
    actions/
    combat/
    defense/
    features/
    health/
    initiative/
    inventory/
    movement/
    notes/
    saving-throws/
  state/             deterministic Builder, Routine, and Presentation slice reducers
  ui/                shell/composition, shared primitives, and generic presentation surfaces
  styles/            ordered stylesheet modules composed by styles.css
```

Leaf features may depend on generic core/UI contracts but should not reach into unrelated leaf features. Cross-feature layout belongs in an explicit composition boundary such as `ui/core-stats.ts`; the Combat presentation is the intentional feature-level composition of Defense, Saves, Health, Initiative, and generic combat values. Shared core code must not import feature modules.

`ui/mechanics-components.ts` remains as a compatibility re-export surface for older callers. It contains no mechanic implementation. `ui/sheet.ts` owns the Character Sheet shell, header, dashboard layout, guided setup, and composition only; Notes, Inventory, Feats, Ability cards, primary tab content, and generalized mechanic families live with their owners.

`app.ts` is the browser composition root. Loading and mutation behavior is implemented by core/feature workflows and wired there. `app-state.ts` remains the visible root reducer for screen lifecycle and cross-slice Edit/Guided coordination while delegating Builder, Routine, and Presentation transitions to `state/` reducers. This keeps state transitions deterministic without creating one switch that every feature must edit.

The stylesheet entrypoint is composition-only. The ordered files `foundation.css`, `builder.css`, `advancement.css`, `mechanics.css`, `abilities.css`, and `supplemental.css` preserve cascade order while making feature-oriented styling discoverable. New styling should be added to the owning module rather than rebuilding a monolithic `styles.css`.

These boundaries are architectural, not line-count targets. A cohesive feature file may remain moderately large when splitting it would obscure behavior. New files are preferred when a change introduces a different ownership concern, state workflow, or reusable abstraction.

## PostgreSQL deployment

Character Sheet uses its own external PostgreSQL database, following the persistent-tool deployment pattern used by Rules Core. `CharacterSheetDbContext` remains the EF Core persistence model, and EF Core migrations are applied to PostgreSQL at application startup.

Production requires the externally supplied connection setting:

```text
ConnectionStrings__CharacterSheet=<PostgreSQL connection string>
```

The application does not silently fall back to SQLite or an application-local database file. The production Compose definition does not define a PostgreSQL service, mount `/data`, or own a database volume. It only consumes `ConnectionStrings__CharacterSheet` and joins the existing `dorks-and-dice-backend` network.

Deployment reads the connection configuration from the server-side environment file:

```text
/mnt/HDDs/www/dorks-and-dice-character-sheet/.env
```

The deployment fails before Compose if that file is missing. The repository contains only `.env.example` with non-production example values; real credentials are not committed.

The PostgreSQL database lifecycle is independent from the application container lifecycle. A normal Character Sheet redeployment may rebuild or recreate the application container but does not recreate, replace, or destroy the external database. `/ready` reports PostgreSQL connectivity as `postgresql-ready` or `postgresql-unavailable`.

CI and deployment smoke tests use disposable PostgreSQL 18 containers. Integration tests create isolated temporary databases on the disposable server so persistence, concurrency constraints, foreign-key behavior, canonical key storage, lifecycle cleanup, authorization, and builder workflows execute against the same database engine used in production.

## Deferred systems

This foundation persists base Ability inputs, rule selections, advancement occurrences, Inventory occurrences, and Notes, and it can present normalized Rules Core competency/check metadata. It still does not implement ability-score generation/provenance, effective/final Ability calculation, Ability modifiers, persisted competency ranks/training/class-skill state, Character capability derivation from Classes/Species/Feats, Character-fact-driven competency/check evaluation, maximum/derived hit-point calculation, temporary-hit-point state, equipment state or item-occurrence mechanics, movement state, spellcasting state/resources, carrying/encumbrance state, crafting progress/state, Prestige Class selection/prerequisites, multiclass prerequisites, generic level-up flow, Class/Subclass feature application, Campaign-specific mechanics context, optional Campaign modules, Rules-Core-backed Position authoring, Block Initiative integration, or cross-owner DM Character access.

Capability-gated saving throws, defenses, Base Attack Bonus, Grapple, nonlethal damage, and related 3.x mechanics are valid presentation shapes. Applicable Rules Core definitions remain visible with `-` while authoritative Character capability/contribution inputs are unavailable, and resolved values replace those dashes once the backend can evaluate them. Loot Tavern check/procedure definitions can likewise be presented from Rules Core, while Character-specific Harvesting/Crafting evaluation remains deferred until the required Character/runtime/source inputs exist.
