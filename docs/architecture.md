# Character Sheet architecture

## Ownership boundaries

The Dorks & Dice Site remains authoritative for canonical Character identity and lifecycle. It owns `CharacterId`, account ownership, character name, active/archive state, permanent deletion, campaign membership and roles, and character-to-campaign associations. Character Sheet owns only rich digital-sheet state, builder progress, and future Character Sheet-specific selections attached to that Site identity. Rules Core remains authoritative for normalized rule definitions.

Character Sheet does not create a parallel character identifier and does not persist Site ownership as a substitute for authorization. `CharacterSheetRoot.CharacterId` is exactly the Site-generated `CharacterId` and is the physical database primary key. Character Sheet does not persist the authoritative Site character name, owner user ID, lifecycle, or campaign associations.

Future campaign-scoped module state may be identified conceptually as `(CharacterId, CampaignId, ModuleKey)`. Campaign IDs used for that purpose are contextual references; the Site remains authoritative for whether those associations exist.

## Tool Host Character authorization

Tool Host authentication contract version `1` now has an additive Character Sheet-specific `characters` projection. For Character Sheet, the Site supplies owner-only entries with this shape:

```text
characters[]
  id
  name
  status
  archivedAt
  campaignIds[]
```

`id` is the canonical Site `CharacterId`. `status` is mapped strictly from `Active` or `Archived`. `campaignIds` is the current set of active Site Character-to-campaign associations and can contain multiple campaign IDs.

`characters: []` means the authenticated account currently owns no Characters. A missing or `null` `characters` property means the required authorization projection is unavailable. Character Sheet does not treat those states as equivalent.

Every rich-sheet API request authorizes the requested `CharacterId` from the freshly redeemed Tool Host context before local persistence is read. A local SQLite record is never sufficient authorization. Campaign DM authority does not grant another player's Character because cross-owner DM sheet access is not part of this contract.

Hosted requests therefore distinguish:

- authenticated owner with an active Character;
- authenticated owner with an archived Character;
- Character not present in the owner's projection;
- missing required Character projection;
- unauthenticated direct backend access.

Frontend route visibility is not an authorization boundary.

## Rich Character Sheet persistence

Character Sheet has tool-owned durable persistence implemented with EF Core and SQLite. The database is independent of the Site database and does not change the Site's authority over Character identity or lifecycle.

The initial `character_sheet_roots` schema is deliberately small:

```text
CharacterId     TEXT primary key
SchemaVersion   INTEGER
BuilderStatus   TEXT
CreatedAt       TEXT
UpdatedAt       TEXT
```

The first builder status is `BuildInProgress`. It means that a digital-sheet build has begun; it does not attempt to model detailed character-building phases.

`ICharacterSheetStore.GetOrCreateAsync(CharacterId)` is idempotent. Repeated initialization of the same Site Character reuses the existing root rather than allocating another identity or row.

The application applies EF Core migrations during startup. Readiness checks verify that the configured SQLite database can be opened. Production Docker Compose mounts the named `dorks-and-dice-character-sheet-data` volume at `/data` and configures `ConnectionStrings:CharacterSheet` to use `/data/character-sheet.db`. Smoke tests use an isolated temporary `/data` filesystem and do not mount the production volume.

## Durable Site lifecycle cleanup

Permanent Site deletion is delivered to Character Sheet through a durable producer/consumer lifecycle contract. The Site is the producer and remains authoritative for whether a Character or Campaign has been permanently deleted. Character Sheet consumes the event only to remove Tool-owned dependent state; it does not independently infer canonical deletion from missing authorization projections or local records.

The Site writes a lifecycle outbox row in the same database transaction as canonical Character or Campaign deletion. An outbox persistence failure therefore rolls the canonical deletion back. After commit, a background dispatcher sends due events directly to the registered Character Sheet upstream endpoint:

```text
POST /api/lifecycle/events
X-Dorks-Tool-Lifecycle-Ticket: <opaque one-time ticket>
X-Dorks-Tool-Lifecycle-Introspection-Path: /tool-host/character-sheet/api/lifecycle/introspect
```

The delivery request does not trust an event body as the authoritative lifecycle payload. Character Sheet requires exactly one lifecycle ticket and one introspection-path header, requires the fixed Character Sheet lifecycle introspection path, and redeems the ticket against the Site:

```text
POST /tool-host/character-sheet/api/lifecycle/introspect
Authorization: Bearer <opaque one-time ticket>
```

Successful lifecycle introspection returns contract version `1` with the authoritative Tool-scoped event context:

```text
contractVersion
  1
toolSlug
  character-sheet
eventId
  <GUID>
eventType
  character.deleted | campaign.deleted
subjectId
  <canonical CharacterId or CampaignId>
occurredAt
  <Site deletion timestamp>
```

Lifecycle tickets are separate from ordinary hosted-user authentication. The context carries no user identity or ownership projection because permanent deletion cleanup is Site-authoritative system-to-system work, not a user-authorized Character Sheet action. Character Sheet validates the contract version, `character-sheet` Tool slug, non-empty `EventId` and `SubjectId`, and the supported event type before processing.

The Site keeps failed deliveries in its durable outbox and retries them. Character Sheet availability is therefore not part of the synchronous Site deletion transaction. A successful `2xx` response acknowledges delivery; a failed or unavailable receiver leaves the Site event pending for a later attempt. Character Sheet readiness also remains based on its own SQLite availability and does not require the Site lifecycle endpoint to be continuously reachable.

Character Sheet records successfully processed lifecycle events in `processed_lifecycle_events`, keyed by `EventId`. Cleanup and inbox insertion occur in the same local database transaction. If the transaction fails, neither the cleanup nor the inbox acknowledgment commits. If the same `EventId` is delivered again after a successful commit, Character Sheet returns the already-processed result without rerunning cleanup. This makes redelivery safe when the Site does not receive a previous acknowledgment.

For `character.deleted`, `SubjectId` is the canonical Site `CharacterId`. Character Sheet removes the matching `CharacterSheetRoot` if one exists and records the inbox event atomically. A missing local root is also successful because canonical deletion does not require Character Sheet-owned state to have existed. Future Character-owned tables must be added behind the same Character cleanup boundary so the lifecycle wire contract does not change as persistence grows.

For `campaign.deleted`, `SubjectId` is the canonical Site `CampaignId`. Character Sheet currently has no campaign-scoped persistent rows, so the implemented Campaign cleanup boundary is intentionally a no-op before the inbox event is recorded. Future state keyed by `(CharacterId, CampaignId, ModuleKey)` belongs behind this boundary. A Campaign lifecycle event must not delete `character_sheet_roots`, because the Site owns Character-to-Campaign association lifecycle separately from the Character's canonical existence.

## Character bootstrap API

The frontend uses a single bootstrap resource rather than separate persistence endpoints:

```text
GET  /api/characters/{characterId}/sheet
POST /api/characters/{characterId}/sheet
```

`GET` authorizes the Site Character first and then returns the transient Site projection together with whether a rich local root exists and its minimal builder state. It never creates a rich root.

`POST` performs the same authorization and idempotently initializes rich state only for an owned active Character. An archived Character returns an archived conflict and is not initialized. A missing/not-owned Character returns a generic unavailable response without revealing whether an orphaned local row exists. A missing Site projection fails closed, and an unauthenticated direct backend request is rejected.

Browser-to-tool backend traffic continues to pass through:

```text
/tool-host/character-sheet/api/upstream/{tool-backend-path}
```

The Site strips browser identity credentials and injects the one-time Tool Host authentication ticket and introspection path. Character Sheet redeems that context for each hosted request and does not make a second ownership query against the Site database or Site Character API.

## `/new` canonical identity allocation

Opening `/tools/character-sheet/new` does not allocate any identity or create any rich state. The initial UI contains only a Character name field and `Build Character` action.

On submit, the hosted browser performs this sequence:

1. `POST /characters/api` to the Site with the Character name.
2. Receive the Site-generated canonical `CharacterId`.
3. `POST` the Character Sheet bootstrap resource for exactly that returned `CharacterId` through the Tool Host upstream gateway.
4. Navigate to `/tools/character-sheet/characters/{characterId}` using the host-provided Tool base path.

The Character Sheet frontend does not generate a temporary Character ID. If Site Character creation succeeds but rich-sheet initialization fails, no compensating delete is attempted. The valid basic Site Character remains available for the existing-character upgrade flow.

Standalone `/new` remains runnable for development, but it does not invent production Site ownership or create a canonical Site Character outside Tool Host.

## Existing Character route

`/tools/character-sheet/characters/{characterId}` bootstraps through the Character Sheet backend.

For an owned active Character with a rich root, the UI uses the Site-projected name and reports that the digital build is in progress.

For an owned active basic Site Character with no rich root, `GET` leaves persistence untouched and the UI offers `Build Digital Sheet`. That action initializes the existing Site `CharacterId`; it does not call Site Character creation and does not create a second Character.

For an owned archived Character, any existing rich root remains preserved and readable as archived state. Ordinary initialization/editing is unavailable while archived. Restoration remains Site-owned; the Character Sheet UI directs the user to restore the Character through the Site.

For a Character that is not in the owner projection, the UI receives only a generic unavailable state. Local orphan data is not exposed.

## Embedded Module v2 frontend lifecycle

Production registration remains:

- display name: `Character Sheet`;
- slug: `character-sheet`;
- integration type: `Embedded Module`;
- integration contract version: `2`;
- Dorks & Dice Site mode only;
- anonymous access disabled.

The Site shell owns `#tool-root` and supplies `data-tool-base-path`, `data-tool-route`, and `data-tool-context-url`. The frontend derives Tool Host API paths and navigation from that host data rather than hard-coding the production hostname.

Application-owned DOM uses explicit application state, reducer actions, and render entry points. The workflow models loading, submitting, error, not-found, basic-character, rich-character, archived, and new-character states explicitly. It does not use `MutationObserver` for application-owned rendering.

## Rules Core and deferred mechanics

Rules Core remains the source of normalized rule definitions. This slice does not add species/race, ability scores, combined skills, classes, subclasses, prestige classes, feats, spells, inventory, equipment, Rules Core content queries, Loot Tavern harvesting/crafting, Acquisitions Incorporated positions, campaign module configuration, or cross-owner DM access.
