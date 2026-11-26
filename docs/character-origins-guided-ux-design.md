# Character Origins and Life Experiences — Guided Creator UX

**Status:** UX direction approved, November 25, 2026. Implementation not started by this design pass. The lifecycle and server authorization checkpoint below must be resolved before production activation; approval of the UX is not approval of new gameplay rules or a security bypass.

**Design baselines:** Character Sheet `c3a5e27d95e70307901e85ee46a3faa59f069899`; Rules Core `0baa466b7fc8f55e5d675b886f76e95f9a4b551f`. Recheck the actual integration target before coding.

## 1. Objectives and boundaries

Replace the provisional Origin editor in the existing guided creator and redesign the existing Background page as Life Experiences. Preserve the guided creator, site shell, normal Character Sheet, and ordinary inventory. Conventional characters must not be forced through custom origination. Each executable origination method owns its interaction sequence; share presentation primitives, not a compulsory generic form or a separate rules engine.

Rules Core owns native discovery, available methods, hierarchy, eligibility, provenance, reconciliation, configuration, prices, allocation and projection. Character Sheet owns authorized player interaction, persistence, host verification and committed state. The client never mints grants or calculates an authoritative mechanical result. Read-only previews, draft choices, confirmed creation configurations, and established character mechanics are distinct.

No new balance policies, acquisition methods, rule content, or changes to the Class/Ability Scores workflows beyond shared integration are authorized here. Do not implement reference-only methods as if executable. No duplicate legacy selection or inventory state.

## 2. Guided navigation and shell

Proposed approved UX order:

1. Class
2. Character Origins
3. Life Experiences
4. Ability Scores
5. Other Choices
6. Equipment
7. Review

Retain the existing route/state identifier `species` where necessary for compatibility; migrate or map the existing Background route rather than introduce parallel mutable sources. Existing navigation is nonblocking: a player may move between steps while creating a character, and can return to an incomplete section. Section completion reflects actual persisted, current native validation and the applicable creation lifecycle, not merely the presence of an ID.

Secondary navigation is local to the current method. It permits direct jumps and inline editing where possible. Keep global navigation, current section, save status, and next/back accessible. A persistent preview uses relevant method-specific results and a compact shared character summary (Class, Origin, Upbringing, Background, abilities, HP, AC, movement, PB, training, inventory). Clearly mark provisional/unavailable values. Do not present unverified preview mechanics as active.

Desktop: browser/navigation, working area, and contextual preview where useful; do not force three columns on simple screens. Tablet: working area plus browser, preview via secondary panel. Mobile: one primary decision at a time, persistent allocation/status and accessible preview, full-width component/characteristic editor; tables reflow to cards without lost data. Use existing theme and component conventions. Keyboard navigation, visible focus, semantic controls, accessible labels/status, responsive touch targets and no color-only states.

## 3. Conventional Species/Ancestry browser — default entry

The Origin step opens directly on a searchable hierarchical Species/Ancestry browser. A prominent **Construct a Custom Origin** action opens method selection. No obligatory preliminary choice for standard characters.

```text
Character Origins                         [Construct a Custom Origin]
[Search Species and Ancestries]
Species / ancestry tree        | Selected ancestry details
  Dragonborn >                 | Wood Elf
  Dwarf >                      | Creature type · Size · Movement
  Elf v                        | Inherent characteristics (expandable)
    Drow                       | Required choices (if applicable)
    High Elf                   | Suggested Upbringing (not selected)
    Wood Elf                   | [Select / Confirm]
  ...
[Back]                         Draft status                  [Continue]
```

- Use native preset hierarchy including nonselectable intermediate nodes, deeper ancestry groups, explicit required ancestry selection, acquisition restrictions and eligibility. Nonselectable nodes remain browsable; do not make them selectable. Search exposes matching ancestry paths.
- Detail view shows native description, physical statistics, inherent characteristics, required choices, level-dependent unlocks and provenance. Display a default Upbringing only as a suggestion; do not grant learned benefits as biological traits.
- Selecting a conventional preset populates its native configuration and asks only for required choices; do not require contributors, individual gene selection or allocation editing. Do not make price a primary comparison axis. Native validation and applicable allocation gates still apply.
- The conventional path and custom paths use the same canonical underlying Origin. Preserve valid choices when switching selections; confirm destructive changes. Clearly explain unavailable or restricted presets.

## 4. Custom origination entry

Discover methods from the active ruleset. Show executable initial methods as choices with plain-language descriptions. Biological and Construct Assembly currently have native preview support; preview availability alone is not permission to finalize. Reference-only methods may be described as unavailable; do not offer inert editor controls. Post-origination methods (e.g. Rite of Rebirth) belong to authorized later acquisition/reconstruction workflows, not ordinary initial creation. Creature-type defaults are not exclusive permissions.

Register method-specific editors via a shared shell/adapter contract. Additional origination methods can introduce independent screens without changing the underlying engine. Unsupported/custom draft evidence must remain intact and read-only rather than being silently rewritten by a basic editor.

## 5. Biological construction

Navigation: Contributors → Inheritance & Expression → Configuration → Review. Configuration is also accessible directly from a gene/characteristic card.

### Contributors

Use repeatable contributor cards with searchable native Species/Ancestry sources, or verified existing character profiles where available. Show source type, identity, selected ancestry and available characteristics. Add/change/remove contributors according to native method constraints, not an assumed biological parent count or the existing four-slot editor limit. Native exemplars are not authenticated individuals. Existing character relationships and revision-pinned current underlying profiles require host verification; ownership inspection alone does not establish parentage or eligibility.

When contributors change, preserve selections/configurations tied to unchanged exact source occurrences; show affected and newly available offers. Never erase unrelated choices or silently substitute source IDs. When native evidence cannot establish retention, preserve the draft evidence and require review.

### Inheritance and expression

Organize canonical gene kinds/intrinsic slots into browsable groups (physical characteristics, senses, defenses, active abilities, innate magic, other). Use actual native metadata where available, generic fallback where not. Group the source-specific offers by canonical identity; show name, source, configuration, eligibility, description, progression, restrictions and evaluated cost. Do not collapse distinct donor instances merely because names match.

```text
Inheritance & Expression                 [Allocation used / allowance]
Breath Weapon                             [2 inherited / native limit]
Contributor        Offered configuration    Inherit     State
Red Dragonborn     Fire                     [x]         Expressed
Bronze Dragonborn  Lightning                [x]         Latent
Expression: (o) Fire  ( ) Lightning  ( ) No expression
[Configure / reconcile if offered]       Resulting characteristic ...
```

Available, inherited, expressed and latent are different:
- Available offers are not automatically inherited or granted.
- Player explicitly selects inherited source instances in the native transmission plan.
- Expression is a native manifestation request; effective grants are the evaluated, reconciled dependency-closed result.
- Latency is derived from inherited instances not contributing to the effective grant set. Do not store an independent latent flag or grant mechanics for latent genes.
- Current first-party default is at most two inherited and one expressed per canonical kind; read native policy and scoped overrides rather than hardcode these limits. Do not assume a global gene count, primary Species, random probabilities or dominance.
- Recombination, multiple-source reconciliation, linked parameters, required choice configuration (including latent configuration), and dependency resolution must use native permitted decisions. A verified parent's persistent choices cannot be arbitrarily reopened.
- Show unavailable or conflicting offers with actionable reasons. Use explicit not-inherited/latent/expressed labels; support zero expressed instances where allowed. The Breath Weapon example demonstrates interaction, not a prescribed legal complete character.

### Allocation

Persistent compact indicator: used / allowed / remaining, with unresolved and over-allowance states. Display evaluated per-characteristic costs and details when provided. First-party trial allowance at the design baseline is 18, pricing revision v14; both are host/native data, not frontend constants. Costs apply to effective grants, not merely selected offers; latent genes incur no creation points. Account for configured values, linked abilities, consolidation and other native pricing results. No frontend authoritative sum or assumption of zero price for unknown traits.

Allow provisional over-budget editing where supported; block authorization/finalization until valid or a verified scoped exception applies. Explain discrepancies between offer and effective cost. Preserve allocations across navigation and reload. Do not reapply the initial creation allowance to subsequent gameplay acquisitions.

## 6. Construct Assembly

Separate workspace: Assembly → Configuration → Review. Treat this as an equipment/vehicle-style component configuration system, not a biological gene selector.

```text
Construct Assembly                [Load starting configuration, if offered]
Construction allowance [used / cap]   Rated power [demand / supply]
Installed components              | Assembly preview
  Structural frame [Configure]    | Size, creature type, movement
  Power source     [Configure]    | Recipe/material quotation
  Locomotion       [Configure]    | Network and operating state
  Sensory systems  [Configure]    | Inherent/operating capabilities
  ...                             | Missing requirements / conflicts
  [+ Add component]               |
[Back]                          Draft status                  [Review]
```

- Browse components by native categories/roles; support multiple exact component instances, variant, material, design, enhancement, recipe options, and network relationships according to native configuration discovery. Do not assume fixed humanoid slots or a one-component-per-role limit. Optional anatomy illustration is decorative, not the model.
- Configuration panel exposes only applicable native options and consequences. Changing one component reevaluates dependency closure, affected network connections, demand, available capacities, quoted materials/cost and effective/operating characteristics; retain unaffected instance choices.
- Show construction allowance separately from rated power. The current reference allowance is 200, not a lifetime cap; read the native rule. Display ingredient bill, recipe stages, known/unknown cost, retained mass bounds and other supported physical properties. Do not invent defense, movement, load, salvage, repair, crafting time or material mechanics.
- Distinguish installed from operating capabilities. Rated output is not verified fuel or current supply. Show disconnected, insufficient, unresolved and disabled states separately; do not silently fix the network or grant runtime abilities.
- Optional starting assemblies: documented Standard Medium (163 points, 7 demand / 8 supply), Small (147), Lightweight (187), Armored (187). These are reference quotations, not published selectable presets unless native discovery makes them available. Reevaluate any loaded template against current rules. Never hardcode a playable preset from a documentation fixture.
- Separate initial assembly from later installation, repair, power/fuel transactions and operating changes. The latter require established-character authority and appropriate gameplay workflows; do not implement them through the creation editor.

## 7. Life Experiences

Replace the current Background workspace with independent Upbringing and Background selection, a combined benefit preview and required-choice editor. Navigation: Upbringing → Background → Benefits → Review, with direct editing.

```text
Life Experiences
[Upbringing: Wood Elf / Change] [Background: Guild Merchant / Change]
Combined benefits               | Required choices / validation
  Benefit             Source   | Cultural skill
  Elvish              Upbring. | Commercial skill
  Persuasion          Backgr.  | Trade language
  ...                          | Professional benefit > nested choice
Starting equipment → Equipment step
```

- Browse both native catalogs using reusable rule browser and detail view. Upbringing parent chain is cultural, not biological; select one package, resolve its inherited benefits once. Suggested Upbringing from Origin is optional, never automatically awarded. No forced age boundary between Upbringing and Background. Show description, optional narrative prompts, authored benefits and restrictions.
- Combined preview groups Skill training, Competency ranks/proficiencies, equipment qualifications, languages, contextual bonuses, learned features/spells and other supported grants. Distinguish learned/inherent, fixed/selected, rank/proficiency, permanent/contextual, candidate/earned. Retain source/benefit provenance.
- Use recursive choice controls for finite, counted, catalog-based, restricted and nested branches. Example: Guild Merchant professional benefit selects Navigation rank OR an additional trade language; selecting language reveals the restricted language chooser, not both benefits. Existing baseline Trade Language remains independent. Navigation rank is not tool ownership or vehicle proficiency.
- Native duplicate Skill proficiency requires an eligible alternative when the applicable resolver authorizes it; do not convert to ranks. Competencies use actual rank/breadth policy; weapon overlap is binary; duplicate specific-tool proficiency can require a distinct eligible replacement. Preserve all source occurrences and replacement evidence. Distinguish local pair conflicts from whole-character conflicts with Class or other sources. Keep Skill families, composites and nested independently trainable skills/competencies accessible; do not flatten or auto-train children.
- Route starting equipment choices and entitlements into the existing Equipment step with source provenance. Granted items become ordinary inventory; do not duplicate, overwrite or remove owned items by changing a creation choice. Unclaimed entitlements may be reevaluated. Verified inventory resolution must prevent repeated awards.
- Deity selection from the old Background page must not disappear; audit its current use and retain it in an appropriate guided section (e.g. Other Choices) unless existing native rules provide a better owner. Exact placement is an implementation design checkpoint, not a new Life Experiences benefit.

## 8. Common validation, editing and review

Store draft revisions and selections through the authorized application. Autosave/debounce as appropriate, indicate saving/saved/error truthfully, protect in-flight changes, ignore stale responses, preserve local pending edits on transport failure, allow retry. Compare-and-swap conflicts require reload/reconciliation, not silent overwrite. Ruleset/fingerprint changes trigger re-evaluation and explicit review of affected choices. Internal IDs, raw JSON, revision numbers and fingerprints are not normal player content; keep diagnostics available to appropriate developers/operators.

Validation issues have a severity, affected decision, readable explanation and a route to resolution. Distinguish incomplete, conflicting, over allowance, authorization required, unsupported, rules unavailable and valid. Invalid or partial drafts can be explored and persisted where permitted; no unauthorized grants. A DM exception request is contextual and only enabled for an implemented authorization type, exact campaign/character/configuration scope, with explicit reason and decision status. Recheck scope/revisions on change and commit; denial/revocation cannot be bypassed. No blanket approval or unverified client authority.

Change review: preserve valid exact-source selections, show what remains, what must be reconsidered and what becomes available; ask for confirmation only for meaningful loss/destructive changes. Apply to contributor, ancestry, component, Upbringing, Background and Class changes. Do not re-award equipment or reset spent resources.

Common Origin review: physical stats, inherent/expressed characteristics, required choices, validation, edit links; expandable method sections for sources, latent genes, allocation, installed components, operating/power state, construction quotation as applicable. Clearly distinguish proposed vs established mechanics. Life Experiences review shows source-resolved benefits, unresolved training choices and equipment intentions. Global Review links back to incomplete sections and reports whole-character verification separately from local completion.

### Lifecycle checkpoint — blocking integration decision

Desired UX: Draft (editable) → Confirmed creation configuration (validated, can return to draft during creation) → Established character (changes follow gameplay rules). Existing activation service commits an Origin and prevents replacement; it is not safe to treat that write as reversible merely because a player is in Guided Setup.

Audit Site character lifecycle, Character Sheet state, authorization, grants, starting inventory and advancement before choosing implementation. If an existing authoritative creation-finalization boundary supports this, reuse it; otherwise design and obtain approval for an explicit boundary. Finalization must verify current draft revisions, ruleset, permissions, all required sources/choices, scoped approvals and applicable inventory transaction requirements. Avoid partial/duplicate activation; establish safe recovery/idempotence. Do not add a UI-only unlock, weaken committed Origin invariants, or silently turn existing characters into drafts. Existing legacy Species/Subspecies selections require explicit reconciliation; never grant via legacy fallback. Do not force new players through a Rite merely to edit a pre-finalization draft.

## 9. Integration checkpoints and ownership

Source references at the design baselines:
- Character Sheet: [guided renderer](../src/CharacterSheet.Web/Client/src/ui/sheet.ts), [section state](../src/CharacterSheet.Web/Client/src/ui/sheet-model.ts), [existing Origin editor](../src/CharacterSheet.Web/Client/src/features/origin/origin-builder.ts), [Origin API adapter](../src/CharacterSheet.Web/Client/src/features/origin/origin-api.ts), [server endpoints](../src/CharacterSheet.Web/CharacterOriginDraftEndpoints.cs), [prior construction workstream](pillar2-origin-construction-ui-20261119.md).
- Rules Core: [native mechanics API](https://github.com/LegendMaster03/dorks-and-dice-rules-core/blob/0baa466b7fc8f55e5d675b886f76e95f9a4b551f/docs/native-mechanics-api.md), [discovery endpoints](https://github.com/LegendMaster03/dorks-and-dice-rules-core/blob/0baa466b7fc8f55e5d675b886f76e95f9a4b551f/src/RulesCore.Host/NativeOriginDiscoveryEndpoints.cs), [construction endpoints](https://github.com/LegendMaster03/dorks-and-dice-rules-core/blob/0baa466b7fc8f55e5d675b886f76e95f9a4b551f/src/RulesCore.Host/NativeOriginApiEndpoints.cs).

Inventory the current API shapes on implementation heads, including delegated method/preset/choice-catalog/component configuration discovery; initial and construct previews; verified biological parent/choice/allocation evaluation; Life Experiences preview/verified plan; training/effective-state baseline and effective-state projection. Reuse current services and trust boundaries, add typed adapters and host providers as necessary. The older Character Sheet draft API and narrow approved mixed activation do not establish generalized activation. Browser inspection alone does not grant authority. Do not expose private catalog payloads to compensate for missing presentation metadata. If a native contract is insufficient, document the precise gap and coordinate an extension, without reimplementing mechanics in TypeScript.

## 10. Workstreams and sequencing

A. Shared shell, responsive navigation, reusable hierarchy/rule detail, recursive choice controls, preview/status, conflict resolution, draft integration and lifecycle audit. Establish typed shared interfaces and test fixtures before parallel changes. Scope shared Class/Abilities adoption to compatibility and future extension.
B. Conventional Origin browser, preset detail, native required choices and review. Verify a normal creation end-to-end.
C. Biological construction: contributor management, inheritance/expression, reconciliation/configuration, allocation and authorized persistence.
D. Construct assembly: native component discovery, instance configuration, quotation, power/operating preview, review and authorized persistence.
E. Life Experiences: Upbringing/Background browsing, nested benefits, training, duplicate handling, Equipment and whole-character projection integration.

B–E may proceed in parallel after shared contracts are agreed; coordinate file ownership, integration branches and cross-workstream tests. Do not merge main without explicit approval. Final integrated review includes self-review, useful comments, automated tests, actual authenticated browser acceptance against pinned Core/Site/Sheet builds, and subsequent merge authorization.

## 11. Acceptance criteria

| Area | Minimum acceptance evidence |
| --- | --- |
| Navigation | Default conventional path; custom selection accessible; existing guided steps usable; direct jumps and return preserve draft; no duplicate legacy editors |
| Conventional | Nested selectable/nonselectable ancestry hierarchy; restricted choices not bypassable; fixed grants and required choices correctly previewed; suggested Upbringing not awarded |
| Biological | Distinct source offers, inherited/latent/expressed states, native capacities; Red/Bronze differing Breath Weapon configurations; latent not granted/charged; changed contributor retains valid other choices; verified parent transmission and stale source rejection; allocation valid/over/unresolved and scoped exceptions |
| Construct | Standard reference and other applicable assemblies verified against native quotes; variant/material/configuration; add/remove exact instance; incomplete network/overload; installed vs operating; no fabricated mechanics or inventory grants |
| Life Experiences | Independent Upbringing and Background; inherited cultural benefits once; Guild Merchant nested alternatives; restricted catalog and duplicate resolution; hierarchical skills/competencies; cross-Class conflicts and starting items become ordinary inventory once |
| Persistence | Reload, partial drafts, concurrent edits, revision conflict, stale fingerprint, network failure, late responses, preserved selections; existing legacy and read-only characters remain safe |
| Authority | No preview treated as grant; authenticated owner/DM and campaign boundaries; approval revocation/staleness fails closed; established state not mutable through creation UI; legitimate finalization produces correct persisted mechanics |
| Responsive/accessibility | Desktop/tablet/mobile screenshots and actual interaction; keyboard/focus/labels; no hidden controls or unmanageable horizontal scrolling |
| Regression | Existing Class, Ability Scores, Other Choices, Equipment, Review and normal Character Sheet workflows remain usable; no duplicate grants/ledger resets |

Browser acceptance must cover representative conventional, mixed and construct characters through creation, persistence, review, authorization and resulting sheet; Life Experiences must be checked with each path as appropriate. Test actual authorized integration, not merely a mocked preview. Report unsupported paths and blocked tests explicitly; do not label an unavailable case as passed.

## 12. Design limitations

Wireframes are structural reference designs, not a fixed pixel specification; use the existing design system. Numeric examples are baseline trial/reference values, not new balancing decisions or hardcoded UI inputs. The creation lifecycle, generalized host authorization, actual native presentation metadata coverage, and publication of optional construct templates remain implementation checkpoints. No application code or live browser behavior was changed or verified by this documentation pass.
