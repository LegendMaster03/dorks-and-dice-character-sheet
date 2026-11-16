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
