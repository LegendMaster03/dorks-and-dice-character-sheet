# Character Sheet architecture

## Ownership boundaries

The Dorks & Dice Site remains authoritative for canonical Character identity and lifecycle. It owns `CharacterId`, account ownership, Character name, active/archive state, permanent deletion, Campaign identity and membership, and Character-to-Campaign associations.

Rules Core is the rule-definition authority. It owns source provenance, normalization across editions, resolved rules, mechanical relationships, combined skills, Classes, Prestige Classes, Feats, race/species concepts, and future Subclass/campaign rule resolution.

Character Sheet owns Character decisions and state: which stable Rules Core concepts a Character selected, builder progress, advancement history, future calculated Character state, and future campaign-scoped Character module state. Character Sheet is not a second rules engine and does not copy resolved rule mechanics into its database.

`CharacterSheetRoot.CharacterId` is exactly the Site-issued `CharacterId`. Character Sheet does not persist a parallel identity, authoritative owner ID, Site Character name, lifecycle, or Campaign membership as a substitute for Site authorization.

## Tool Host Character authorization

The Site supplies Character Sheet an owner-only Character projection in the freshly redeemed Tool Host authentication context. Every rich-sheet and `/build` request authorizes the requested Site `CharacterId` before Character Sheet persistence is read or mutated. A local SQLite row is never sufficient authorization, and Campaign DM authority does not grant another player's Character in this contract.

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

`GET /api/rules` is used for searchable resolved catalog choices. `GET /api/rules/{conceptKey}` resolves a persisted reference for display. Rules Core applies its existing current-user/source-access filtering to those reads.

The builder intentionally uses global rules only. A Site Character can belong to multiple Campaigns, so Character Sheet does not arbitrarily select one Campaign's resolved rules. Campaign-specific resolution is deferred until the Character Sheet has an explicit Campaign-context selector/overlay.

For standalone ASP.NET development only, `RulesCore:DevelopmentBaseUrl` may expose an explicit direct Rules Core base URL to the frontend. The standalone shell emits that adapter only when the ASP.NET environment is `Development`; embedded mode ignores standalone adapter attributes and always uses the Site Tool Host path. Production authentication is not weakened for standalone convenience.

## Stable rule-reference semantics

Character Sheet persists Rules Core `RuleConcept.Key`, exposed as `ConceptKey` by the resolved catalog, as its stable rule reference. The resolved catalog also exposes `RuleConceptId`, display metadata, source IDs, and source revision IDs, but Character Sheet does not use display name, source-native ID, `SourceEntityRevisionId`, package revision, source JSON, or resolved mechanical `Document` as Character decision identity.

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

Builder choices are intentionally split between foundational selections and progression entries.

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

The first Starting Class is represented as the `Class` entry at ordinal `0` with no parent. Replacing the Starting Class updates that same Character-owned entry rather than appending duplicate first entries. The general model permits additional Class and Prestige Class entries later.

`ParentAdvancementEntryId` allows a future first-class Subclass advancement to reference its relevant Character Class advancement without placing Subclass fields on a Class row. Feat occurrences have their own Character-owned entry identities independently from the Rules Core feat concept, so future acquisition metadata can distinguish advancement-granted and free/flavor occurrences while preserving the same underlying Rules Core concept identity.

No total Character level is calculated from this immature progression model.

## Current `/build` API

Character build state is exposed as one coherent resource plus two selection resources:

```text
GET    /api/characters/{characterId}/build
PUT    /api/characters/{characterId}/build/race-species
DELETE /api/characters/{characterId}/build/race-species
PUT    /api/characters/{characterId}/build/starting-class
DELETE /api/characters/{characterId}/build/starting-class
```

A `PUT` body is only:

```json
{ "conceptKey": "<stable Rules Core ConceptKey>" }
```

Display names and mechanical JSON are neither accepted as Character Sheet identity nor required for persistence. The response contains Character Sheet-owned builder references/state, not copied Rules Core mechanics.

The existing rich-sheet bootstrap API remains:

```text
GET  /api/characters/{characterId}/sheet
POST /api/characters/{characterId}/sheet
```

An owned basic Site Character still uses `POST /sheet` to initialize rich state against the existing Site `CharacterId`; `/new` still creates the Site Character first and initializes exactly the returned canonical identity.

## Current builder UI

The first rules-backed Character Builder exposes only:

```text
<Character Name>

Character Builder

Race / Species
[current choice / Choose / Replace / Clear]

Starting Class
[current choice / Choose / Replace / Clear]

Build status: In progress
```

Race / Species queries the global Rules Core catalog with `entityType=race`. Starting Class queries it with `entityType=class`. `prestigeClass` is not offered as a Starting Class. `npcClass` is not offered because the current architecture does not establish NPC Classes as ordinary player Starting Classes.

The chooser models loading, search, empty results, Rules Core error, selection, replacement, clear, save error, and cancel explicitly. It displays Rules Core identification metadata such as resolved display name, edition/source/package metadata, but does not dump raw resolved JSON or reproduce source prose.

Build status remains `In progress` even after these two choices are selected. This slice does not invent a complete builder-step state machine.

## Subclass boundary

Rules Core source ingestion recognizes source-native 5e.tools `subclass`/`subclassFeature` material, but repository inspection for this slice did not establish a resolved first-class Subclass builder contract that supplies the parent-Class relationship Character Sheet needs. The generic resolved catalog is not a license for Character Sheet to parse nested/native class `Document` JSON and manufacture its own Subclass catalog.

Subclass selection is therefore intentionally absent. A later coordinated Rules Core slice should expose the normalized first-class Subclass relationship required by the builder; Character Sheet can then store it as a `Subclass` progression entry related to the appropriate `Class` entry.

## Durable Site lifecycle cleanup

Permanent Site deletion is delivered through the existing trusted lifecycle inbox/outbox contract. For `character.deleted`, deleting `CharacterSheetRoot` cascades to both foundational selections and advancement entries in the same local transaction before the lifecycle event is acknowledged. A missing local root is still successful.

`campaign.deleted` remains unrelated to these base/global Character selections and does not remove them. Future Campaign-scoped Character module state belongs behind the existing Campaign cleanup boundary.

## Embedded Module v2 frontend lifecycle

Production registration remains Embedded Module contract v2. The Site shell owns `#tool-root` and supplies host route/context attributes. Application-owned DOM uses the explicit application state/reducer/render lifecycle; `MutationObserver` is not used.

The reducer now models sheet loading, basic/rich/archived screens, builder loading/failure, saved rule-reference resolution, chooser open/search/catalog loading/catalog failure, saving, save failure, unresolved references, and read-only behavior. Chooser actions feed state and then the single render path replaces application-owned DOM.

Nested `/characters/{characterId}` routes and `/new` continue to use the host-provided Tool route/base path.

## SQLite deployment

EF Core migrations are applied at startup. Production Compose continues to use:

```text
ConnectionStrings__CharacterSheet=Data Source=/data/character-sheet.db
volume: dorks-and-dice-character-sheet-data -> /data
```

The builder tables are added to that same durable SQLite database. No production persistence path or volume contract changes in this feature.

## Deferred systems

This foundation does not implement ability score generation, skills/combined-skill UI, saving throws, hit points, armor class, attacks, equipment/inventory, spells/slots/points, Subclass UI, Prestige Class selection/prerequisites, multiclass prerequisites, Feat UI or grants, level-up UI beyond Starting Class representation, class-feature/proficiency calculations, Campaign-specific rules context, optional Campaign modules, Acquisitions Incorporated positions, Loot Tavern harvesting/crafting, Block Initiative integration, or cross-owner DM Character access.
