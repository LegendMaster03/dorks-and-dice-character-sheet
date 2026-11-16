# Character Sheet — Native Innate Spell Ledger Persistence Boundary

**Date:** November 16, 2026  
**Repository:** `LegendMaster03/dorks-and-dice-character-sheet`  
**Branch:** `feature/innate-spell-ledger-persistence-20261116`  
**Rules Core source:** [draft PR #157](https://github.com/LegendMaster03/dorks-and-dice-rules-core/pull/157), branch `feature/pillar2-power-evaluation-prototype-20261114`

## Architectural placement

Rules Core's native `NativeOriginInnateSpellResourceLedger` calculates immutable, candidate transitions for already authorized innate spell grants. The public/private Rules Core hosts are **stateless** and do not own characters or an account database. Character Sheet already persists authorized native Origins in PostgreSQL and redeems Site character ownership and DM scope. The durable ledger therefore belongs with Character Sheet, *not* in a Rules Core host singleton or an arbitrary client-supplied snapshot.

This increment implements **the storage portion** of that boundary in Character Sheet. It does **not** add a browser-facing cast, recovery, bootstrap, or advancement endpoint. The full authenticated application service and native transition verification must be implemented before this persistence store may be used for actual character resources.

## Data model

`character_innate_spell_ledgers` stores one row per character, referencing `character_authorized_origins(CharacterId)`. It records the underlying native ruleset/fingerprint, Origin profile and proposal identifiers, the authoritative Origin row revision, the Origin spell ledger revision, the complete native ledger JSON snapshot, and timestamps.

`character_innate_spell_ledger_receipts` stores immutable event receipts. Both `(CharacterId, EventId)` and `(CharacterId, CommittedRevision)` have unique PostgreSQL constraints; each receipt records the Origin revision, actor GUID, verified authorization evidence identity, action, timestamp and optional exact capacity-decision ID JSON for audit. The native snapshot itself includes the level, projected source-course fingerprint and per-grant remaining free-use values.

The ledger revision begins at **0** on exactly one bootstrap and increments once on each verified native `Apply` or `Advance` candidate. A stored ledger may never be recreated merely to regain expended charges.

## Internal-only persistence contract

`ICharacterInnateSpellLedgerStore` exposes internal character-scoped reads, receipt reads and `CommitVerifiedAsync`, taking a `VerifiedInnateSpellLedgerPersistenceCommand` prepared by a trusted application evaluator, **never** by model binding a browser request. The store validates the candidate state envelope's native identities, expected revision, level, resources and JSON limits. This is only **structural** validation: it does not reproduce the rules calculation or grant permission to cast.

`PostgresCharacterInnateSpellLedgerStore` commits in a single PostgreSQL transaction using the established lock order:

1. Lock `character_sheet_roots` for the exact character.
2. Lock the committed `character_authorized_origins` row; require matching authoritative Origin revision, Ruleset, fingerprint and profile identity. A missing or replaced Origin blocks the write.
3. Lock the character's existing ledger row, if any, and check that the event ID was never committed.
4. For bootstrap, require no existing ledger, expected revision **null**, and native snapshot revision **0**. For updates, require an existing ledger, exact expected revision and next snapshot revision = expected + 1. Recheck origin identifiers and origin row revision.
5. Update under an explicit `WHERE CharacterId AND LedgerRevision AND OriginRevision` predicate or insert the first ledger. Insert an immutable event receipt in the same transaction. A unique-index conflict is rejected, not applied as a second event.

No state or receipt is published if the transaction aborts. A previously tracked EF Core instance is never used as an optimistic concurrency token; updates use `ExecuteUpdateAsync`. Deleting a character cascades its committed Origin, ledger and receipts.

## Missing trust and execution gates

- No public ledger write endpoint has been added. The store is registered in dependency injection for a future **internal-only trusted** application service.
- A future service must redeem the Site identity, confirm character ownership/editability, and obtain the current committed Origin. For a campaign DM decision, it must authenticate campaign role and exact decision evidence. A submitted client `ActorUserId`, `AuthorizationEvidenceId`, `NativeOriginProfile` or native ledger payload is **not** sufficient.
- The future service must re-evaluate or verify the Rules Core native candidate `Initialize`, `Apply` or `Advance` against the current catalog, exact saved Origin/revision, native character progression and stored ledger. The persistence store does not treat a structurally valid JSON claim as proof of entitlement.
- The same trusted operation must prove authority for a rest event, advancement, or spell-cast action. The Rules Core ledger presently has no executable spell-slot casting handoff, complete form applicability, or casting-action resolution.
- The Origin revision must remain unchanged between verification and commit. Under this implementation, a changed/replaced committed Origin blocks writes until an explicit reconciliation/migration is implemented. Existing spent charges must never be reinitialized by replacing the Origin.
- Audit evidence and decision IDs must be authoritative; a caller-provided receipt is not independently sufficient.

**Bottom line:** The PostgreSQL CAS and durable receipt mechanism can be tested and used internally, but this increment does **not** claim production-authenticated spell spending or an exposed API. That remains a separate and mandatory integration increment.

## Validation

`InnateSpellLedgerPersistenceTests` uses disposable PostgreSQL databases and the real EF Core migration. It tests atomic create-once bootstrap, persisted snapshot and audit reload, monotonic revision, event replay, competing writers, Origin revision/profile mismatch, invalid JSON/envelopes/receipts, and character deletion cascading. These are persistence correctness tests, not a test of native spellcasting calculation or Site ownership.

Keep the Character Sheet branch unmerged and un-deployed. Rules Core draft PR #157 remains unchanged and must be integrated by an authenticated application-level adapter before user-facing resource updates.


## Internal Site-authorized application coordinator — continuation (November 16)

The application layer now includes `CharacterInnateSpellLedgerService` as an **internal-only** owner/actor and CAS gate. It reuses the existing Site `ISiteCharacterAccessGateway`, obtains the authenticated acting account from `ICharacterInnateSpellActorSource`, reads the canonical committed `CharacterAuthorizedOriginState` and latest ledger, and forwards *server-held data only* to `ITrustedInnateSpellLedgerEvaluator`.

`SiteInnateSpellActorSource` extracts the actor GUID only from the already redeemed Site tool-host authentication context; it rejects incorrect tool slugs, Site modes, missing authentication and invalid account identifiers. Ownership must be authorized **before** either the committed Origin or ledger is read; inactive/archived characters can read their own stored ledger but can not mutate it. The coordinator requires exact previously saved origin identity and revision, an expected ledger revision, a whitelisted action kind, and a grant ID only for a free-cast spend. It rejects mismatching, absent, stale or malformed candidate snapshots and unapproved capacity decision IDs for nonadvancement actions.

The coordinator's `ApplyAsync` never accepts a caller's proposed `LedgerJson`, actor GUID, Origin profile, native entitlement maximum, authorization evidence, or table decision. A trusted evaluator must derive the complete candidate from the committed Origin, saved ledger and current native character projection. If an evaluator provides an authorized candidate, the coordinator validates its structural identity, computes the expected next revision, creates a fresh internal event ID, and submits the candidate to the already transactional PostgreSQL `CommitVerifiedAsync` boundary, translating storage conflicts into explicit status codes.

**Still not production-enabled for runtime actions:** The registered `NativeDirectInnateSpellLedgerEvaluator` can now calculate **only a conventional direct-Origin, first-party, initial revision-0 bootstrap candidate** using saved character-owned source and an authenticated delegated Rules Core request. It continues to return no candidate for spending, rests, advancement, mixed-Origin decisions or transformations. There is **no HTTP ledger mutation endpoint**, and no spell spending is enabled. This is not a default accept-all verifier or a trust bypass. A separate runtime event verifier and durable character-progression concurrency contract are still required.

`CharacterInnateSpellLedgerServiceTests` use an **explicit test-only evaluator stub** to verify that the application cannot read another owner's ledger or mutate an archived character, cannot substitute an actor, fails closed when native verification is unavailable, rejects changed saved Origin and stale ledger snapshots, declines malformed native candidate envelopes and unauthorized decision IDs, and does not claim success on CAS conflicts. The real PostgreSQL store integration tests continue to verify durability independently.

### Next necessary integration

Implement a production `ITrustedInnateSpellLedgerEvaluator` backed by Rules Core's actual `NativeOriginInnateSpellResourceLedger` logic and authentic current Class progression and active Origin source revisions. This requires an approved server-to-server calculation interface or another verifiable native execution boundary; an opaque JSON projection or a previously saved `previewOnly` result is not a substitute for such verification. Confirm spell/action/rest authorization and changing-capacity DM evidence. Only after that succeeds should an authenticated, narrowly scoped endpoint be considered. Keep both Rules Core PR #157 and Character Sheet PR #68 draft until those integration contracts are verified.


## Source-matched native direct bootstrap adapter — continuation (November 16)

The new `NativeDirectInnateSpellLedgerEvaluator` implements a **single supported action**, `Initialize`, for a conventional, previously committed **direct biological Origin**. It rejects an existing ledger, an unsupported action, missing Site actor identity, and missing/changed saved first-party Origin draft. The draft's revision must still match the authorized Origin's `SourceDraftRevision` and its ruleset ID must match. The saved draft fingerprint is a historical audit hint and **may be stale**, because initial first-party activation deliberately re-evaluates against current native content; the authorized Origin's **committed fingerprint** must exactly match Rules Core's live fingerprint at bootstrap. The committed Origin profile must identify the same proposal. If the original draft has been edited or replaced, this slice fails closed rather than reconstructing the old construction from current unrelated selections.

The adapter rereads Character Sheet's owner-scoped build and state, checks all six valid/unique ability score inputs, valid native Class occurrences and their recorded class levels, and rejects malformed/unmapped native choices. It forwards these **server-held** values (including valid saved hit point gains when available), the exact saved draft proposal and committed Origin profile, and the current native ruleset fingerprint through `INativeRulesCoreGateway.PreviewDirectInnateSpellLedgerBootstrapAsync`.

Rules Core [PR #157](https://github.com/LegendMaster03/dorks-and-dice-rules-core/pull/157) adds a **stateless authenticated delegated preview** at `/api/mechanics/v1/origins/innate-spells/ledger/initialize-preview`. The Core endpoint reconstructs direct initial construction against current first-party data and requires an exact full canonical match with the saved committed profile, a verified native total character level and proficiency bonus, and a bounded source-matched revision-0 free-cast ledger. Unrelated missing HP, AC and base movement inputs may leave the wider character projection partial without manufacturing free spell uses; all other issues block this preview.

The adapter requires current `contractVersion`, ruleset/fingerprint, `previewOnly` and source-match assertions, an actual `resolved` native result, the correct Origin profile/proposal IDs, and revision zero. The application coordinator independently validates the returned state envelope before its **internal-only** create-once PostgreSQL CAS. The native calculation result is an *input to* the trusted transaction, not a new source of permissions.

**Remaining safety gate:** No public mutation route, and no automatic initialization on GET or normal character load. A real migration/rebase and runtime event authorization, trusted Source Draft archival/provenance after future edits, and transactional stability of ability/class progression between evaluation and commit remain future work. The current storage lock ensures exact Origin + ledger revision but does not freeze *all separate character state tables* between computing a preview and writing it. Do not describe this as complete production spellcasting or a finished general Origin integration.

The expanded Character Sheet PostgreSQL integration suite uses a test-only native response to verify the original saved draft and owner-held Class/ability inputs reach the delegated preview, successfully bootstraps once through the transactional ledger, rejects a changed source draft, missing abilities, mismatched native catalog and source profile mismatch, and leaves all subsequent runtime spending disabled. Rules Core's independent Host HTTP test verifies actual first-party Origin source matching and native bootstrap calculation. Both branches remain draft and unmerged.


## Character aggregate mutation revision and native-preview CAS — continuation (November 16)

The previously identified **race between character build/state updates and native ledger storage** is now closed for normal Character Sheet aggregate mutations. Do not use `UpdatedAt` as an authority token: `CharacterSheetRoot.Touch` previously updated its timestamp only when the supplied `changedAt` was later; two writes with equal or older timestamps could change class/ability/choices without changing `UpdatedAt`.

### Source and storage contract

`CharacterSheetRoot.MutationRevision` is a monotonic `long` initialized at zero for existing migrated characters and increased on every aggregate `Touch`, **regardless of timestamp order**. The root EF Core mapping makes it a concurrency token and migration `20261116184500_AddCharacterMutationRevision` adds a nonnegative PostgreSQL column. A normal EF save that includes score, Class advancement, native choices, HP gains or other root state therefore updates the character-root revision and can not silently commit with a stale aggregate snapshot.

`ICharacterMutationRevisionReader` / `PostgresCharacterMutationRevisionReader` reads the authoritative root revision **before** any construction draft, build, state or remote native request is evaluated. `NativeDirectInnateSpellLedgerEvaluator` includes that exact pre-evaluation revision in the `TrustedInnateSpellLedgerCandidate`. The owner-authenticated `CharacterInnateSpellLedgerService` requires a valid, nonnegative revision, carries it in `VerifiedInnateSpellLedgerPersistenceCommand`, and reports `CharacterChanged` if the store rejects it.

`PostgresCharacterInnateSpellLedgerStore.CommitVerifiedAsync` now requires `ExpectedCharacterMutationRevision` for **all** commits. Its existing transaction obtains `character_sheet_roots FOR UPDATE` first, rejects a mismatch with `CharacterMutationConflict`, then locks the committed Origin and ledger, validates their identities and revisions, and inserts the new snapshot and receipt atomically. An interrupted or overlapping character mutation cannot publish a previously computed candidate. A correct native result calculated at an older revision is **not** retried or silently refreshed; another authenticated native evaluation is necessary.

This avoids holding PostgreSQL locks during the Rules Core HTTP request. It is a conservative aggregate-wide revision: unrelated changes may also invalidate a native candidate and require re-evaluation, but no stored free-use balance is fabricated or spent.

### Regression coverage and limits

New PostgreSQL tests verify that normal ability-score and Class mutations invalidate a candidate pinned to an earlier root revision, **including when both writes use a timestamp earlier than the recorded `UpdatedAt`**. They confirm no ledger or event receipt is published by a stale candidate, the revision advances monotonically, and a newly recomputed candidate with the matching revision can bootstrap. A separate integration test mutates the saved score or Class level from an independent DbContext while the fake delegated native HTTP response is pending; the coordinator must report `CharacterChanged` without committing a ledger. Unit tests reject candidates with missing/negative character revision evidence and map transaction-level character-revision conflicts to an explicit application result.

This contract covers **normal repository mutations that call the existing aggregate `Touch`** and honors their root EF Core concurrency token. Direct SQL updates outside the supported repository methods that bypass the aggregate and its root revision remain outside this guarantee and must not be introduced as alternate character progression write paths. It also does **not** establish authenticated casting/rest event permissions, native mixed-Origin authorization, transformation applicability, or a public ledger write API. No existing client receives a new mutation route; both PRs remain draft and unmerged.


### Guarded native resource SQL mutation — audit follow-up (November 16)

`PostgresCharacterStateStore.ApplyIntegerStateMutationsAsync` also supports a single **expected-current native resource** transition through an EF `ExecuteUpdateAsync` SQL predicate. That established fast path bypassed the normal aggregate `Touch` and previously changed the root `UpdatedAt` **only** if its event timestamp was later than the stored timestamp. Thus, even after adding `MutationRevision`, the existing direct SQL path was an unversioned write and could invalidate a native preview without invalidating its commit token.

The guarded branch now uses the same lock order as innate-ledger persistence: obtain a `character_sheet_roots FOR UPDATE` lock first, apply the conditional resource-row update, **increment `MutationRevision` exactly once in the same PostgreSQL transaction**, and update `UpdatedAt` only if the provided timestamp is later. Failed expected-current comparisons do not publish a resource value or root revision. This explicitly covers that existing non-`Touch` mutation path and ensures stale innate-spell candidates are refused by the separate locked ledger commit.

The PostgreSQL regression `GuardedResourceSqlUpdateAlsoInvalidatesPreviouslyEvaluatedLedger` covers a successful guarded value change with an **earlier** timestamp, a newly computed matching revision, the rejection of a previously calculated candidate and receipt-free failure on an incorrect expected-current resource. Other arbitrary or external direct SQL modifications must similarly participate in the root revision discipline before being considered authoritative.

The revision guard establishes **aggregate write consistency**, not complete ownership and runtime action authorization. Direct first-party bootstrap remains internal-only. No user-facing casting, Long Rest, advancement or mixed-Origin event path was enabled.
