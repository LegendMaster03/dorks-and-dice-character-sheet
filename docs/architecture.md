# Character Sheet foundation architecture

## Ownership boundaries

The Dorks & Dice Site is authoritative for canonical Character identity and lifecycle. It owns `CharacterId`, account ownership, character name, active/archive state, permanent deletion, campaign membership and authorization, and character-to-campaign associations. Character Sheet owns only the rich digital sheet and tool-specific state attached to that Site identity. Rules Core owns normalized rule definitions.

Character Sheet must never create a parallel character identifier. `CharacterSheetRoot.CharacterId` is the Site-generated `CharacterId`, and `ICharacterSheetStore` is intentionally keyed by that identifier. There is no physical persistence implementation in this slice because no datastore is required to establish the boundary.

Future campaign-scoped module state is identified conceptually as `(CharacterId, CampaignId, ModuleKey)`. `CampaignModuleStateKey` represents that key without treating campaign membership or association as Character Sheet-owned data.

## Site lifecycle model

An active Site Character can exist without a rich Character Sheet record. Opening that Character later must create or load rich state against the existing Site `CharacterId`; the tool must not create a replacement identity.

Archived Site Characters retain Character Sheet-owned rich state but do not permit ordinary editing. `SiteCharacterProjection` represents this as a transient Site-authoritative projection, not persisted ownership data. A Character can have multiple active campaign associations at the same time, and the projection therefore models campaign IDs as a collection rather than a single campaign.

Permanent Site deletion will eventually require durable cleanup of Character Sheet-owned state. This repository deliberately does not implement a synchronous deletion callback. The required integration is a durable Site lifecycle-delivery mechanism that can be retried and acknowledged.

## Site authorization dependency

The current Tool Host authentication context establishes the signed-in user, global roles, and campaign memberships, but it does not establish authoritative access to an arbitrary `CharacterId` or expose the Character active/archive state. Character Sheet therefore does not expose character-data backend endpoints in this foundation slice and does not persist `OwnerUserId` as a substitute.

`ISiteCharacterAccessGateway` marks the future application boundary. A later coordinated Site change must provide an authoritative Character access projection that the tool backend can consume before rich character read/write APIs are enabled.

The existing Site Character APIs under `/characters/api` remain Site-owned lifecycle APIs. Character Sheet does not reimplement character creation identity, naming, archive/restore, campaign linking, or permanent deletion.

## Embedded Module v2 frontend

Production registration is:

- display name: `Character Sheet`
- slug: `character-sheet`
- integration type: `Embedded Module`
- integration contract version: `2`
- Site mode: Dorks & Dice only
- anonymous access: disabled

The Site shell owns `#tool-root` and supplies `data-tool-base-path`, `data-tool-route`, and `data-tool-context-url`. The frontend reads those values when embedded. Standalone development uses `window.location.pathname` instead.

Supported application routes in this slice are `/new` and `/characters/{characterId}`. They intentionally render development placeholders only. Invalid routes are represented explicitly instead of being silently treated as a valid Character.

Application-owned DOM uses explicit state, dispatch, and render entry points in `render-lifecycle.ts`. No `MutationObserver` is used for application-owned state or rendering.

## Tool Host authentication

Browser-to-tool backend traffic is expected to pass through `/tool-host/character-sheet/api/upstream/{tool-backend-path}`. The Site strips browser identity credentials and injects the one-time `X-Dorks-Tool-Auth-Ticket` plus `X-Dorks-Tool-Auth-Introspection-Path` headers. Character Sheet redeems the ticket against the fixed `/tool-host/character-sheet/api/introspect` path using the configured `ToolHost:BaseUrl` and only then creates a request principal.

Standalone requests that do not contain Tool Host authentication headers do not require the Site or a separate login/session system.

## Rules Core boundary

Rules Core is the source of normalized rule definitions. Character Sheet will eventually store selections or references to stable Rules Core identities and resolve rule details through Rules Core. It must not recreate normalized skills, classes, feats, spells, or other rule definitions as a second rules database.

The already-combined 3e/3.5e/5e/5.5e skill model remains a Rules Core concern. Prestige class must eventually be a distinct concept from class, subclass, and feat across both systems, but prestige-class semantics are not part of this slice.

## Deliberate non-goals

This foundation does not implement the character builder, species/races, classes, subclasses, prestige classes, feats, skills, spells, inventory, Loot Tavern harvesting/crafting, Acquisitions Incorporated positions, campaign rule configuration, DM sheet access, a complete persistence schema, durable deletion delivery, or the Site-side Character authorization extension.
