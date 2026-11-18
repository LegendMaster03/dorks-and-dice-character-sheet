# Pillar 2 — In-Flight DM Authorization Consistency (S04)

**Status:** OPEN DESIGN DECISION — neither policy nor a production authorization
protocol is approved. This document does not authorize a Site refactor, claim
atomic revocation, or change the current mixed-Origin gameplay permissions.

## Source-derived boundary (2026-11-17)

In `CharacterOriginMixedActivationService.ApplyAsync` the operation:

1. calls `ISiteOriginDmAccessGateway.CheckAsync` on the exact campaign and character;
2. reads the target Sheet and Origin draft;
3. runs native Core preview and delegated scoped mixed evaluation;
4. checks resulting profile, projection and source authority;
5. sends `VerifiedOriginPersistenceCommand` to
   `PostgresCharacterAuthorizedOriginStore.CommitVerifiedAsync`.

The ordinary `ToolHostOriginDmAccessGateway.CheckAsync` reads the
`ToolHostAuthenticationContext` already stored on the current request by
`HostedToolAuthenticationMiddleware`. **Calling it again in that same request
does not re-introspect Site** or guarantee that a previously valid campaign DM
and character association remain valid.

Site's one-use authentication ticket is redeemed at middleware entry. The
issued delegate capability is short-lived (30 seconds, up to 32 uses), with
fresh Site role projection for each *delegated target* request. That fresh role
check in Rules Core can reject some early revocations. It does not establish
the target character's association at the eventual Sheet PostgreSQL commit,
and no live check occurs after successful Core evaluation.

Sheet's Origin store commits snapshot and event atomically **with respect to
the Sheet PostgreSQL transaction**, including row locks, draft revision
conflicts and duplicate event protection. Site role and character-link records
live in a different data store. The Sheet transaction does not lock those
records or obtain a Site commit-time authorization lease.

## Deterministic race case to execute in a separate isolated fixture

1. Use a genuine Site login for a DM and an active Human/Elf target in a
   campaign. Save a revision-pinned native draft as the target owner.
2. Submit mixed activation. Hold the request after successful delegated Rules
   Core evaluation but **before** the Sheet `CommitVerifiedAsync` call. A
   controlled gateway barrier in an integration test, or an isolated network
   response barrier between Core and Sheet, provides a deterministic pause.
3. From Site, revoke the DM role or archive/unlink the target. Commit that
   Site state change, verify Site now denies a *fresh* review request.
4. Release the original activation. Record HTTP status, Origin revision,
   immutable event receipt, exact actor/campaign/character and whether the
   state actually persisted.
5. Repeat at barriers before delegated Core evaluation and during persistence,
   and with concurrent draft editing, duplicate activation and Site timeout.

The prior independent review **did not execute** the exact live Site
case 2–4. A new deterministic **service-level** characterization now executes
the important interleaving against real native Rules Core HTTP and PostgreSQL,
with a controllable Site-authority test double:

`S04CurrentBehaviorCharacterization_RevocationAfterCoreApprovalStillCommits`

Its `HttpNativeGateway.afterEvaluation` barrier revokes the test DM scope
immediately after a successful native evaluation and before the Sheet commit.
The result is **Applied**, an immutable audit receipt and Origin revision 1,
despite the test authority reporting revoked. [Pinned Core/Sheet run
44165389916](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44165389916)
**PASS 15/15** at Sheet `78241906c8537c694c66fd7777d9f6a9d887c330`
and Core `23919c3ce00825f2da6204f218786957e9f1f1c2`.

**This passing characterization is evidence of the security gap, not a passing
S04 acceptance check.**

## New: genuine three-service S04 reproduction (2026-11-18)

The [isolated Chrome/Site/Sheet/Core run 44254000314](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44254000314)
**PASSED its test assertions, explicitly classifying S04 as OPEN-GAP**.
Actual checked-out implementation revisions were:

- Site `e8d0cb7d89de71aa337293b8f36d22bac98687c3`;
- Rules Core `23919c3ce00825f2da6204f218786957e9f1f1c2`;
- Character Sheet `c51bcaadf87c5e3c88772aeaac02a4e1754eacf5`.

[Captured screenshots, sanitized service logs and 29 structured results](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44254000314/artifacts/14405112176)
show all supported browser cases passed before the race, then:

1. Genuine Site DM review returned HTTP 200 for an active Human/Elf
   character and a revision-pinned draft with five native offers.
2. The original DM activation reached a successful **real native Core**
   evaluation (HTTP 200). A localhost-only test relay between Site and
   Core held that response **before Site and Character Sheet received it**.
3. The fixture changed the DM's role to `player` in the **actual Site
   SQLite membership-role table**, committing the change.
4. A new Site-authenticated DM review returned HTTP **404**; the
   original character was still uncommitted at the pause.
5. The held Core response was released. The original activation
   returned HTTP **200** and the owner reloaded Origin revision **1**.

The test did not alter Site, Core or Sheet production authorization code.
The relay is strictly a disposable test mechanism. It does not depend on a
constructed Site authority projection.

**Security interpretation:** A fresh Site request correctly denies the
revoked DM, but a preexisting request can commit after the role change.
That observed behavior is neither an approved bounded-authorization
contract nor the immediate-revocation guarantee. **S04 remains BLOCKED.**
Do not infer atomicity from the Sheet PostgreSQL transaction or from
the workflow's green status. The prior simulated-scope proof and this
real-Site reproduction are now separate evidence.

As a further live assertion, the next browser test revision also checks
the Origin event receipt before and after release; its result must be
verified before claiming that specific live audit assertion passed.

## Policies requiring an explicit owner decision

### A. Approval authorization is a bounded, durable snapshot

Site signs/issues a server-only approval for the exact actor, campaign,
target, native draft revision/fingerprint and explicit selected native offers.
Its issuance is serialized with current Site membership/link checks.
The resulting authorization has a short validity interval and is consumed
once by the Sheet's atomic Origin event commit.

Under **this** policy, role revocation invalidates **new** approvals, but an
already issued valid approval may still finish until expiry. Such a commit is
not characterized as an unauthorized race, **provided the approved policy
explicitly allows that window**. Audit the issuance time, bounded expiry and
one-use approval identity. A mere existing delegation capability is not
an exact Origin authorization token.

### B. Revocation takes precedence until final commit

Site must provide a serialized, revocable per-target authorization reservation
or lock whose grant and revocation operations share a single ordering. Sheet
keeps that reservation through its PostgreSQL commit and completes/releases
it afterward. Fencing, bounded lease lifetime, retries, process failures,
Site availability, and ambiguity when Sheet commits but acknowledgement
fails must all be defined and tested. A single late recheck alone **does
not** provide this atomic guarantee.

This is a coordinated Site/Sheet protocol; neither of the two existing
databases can unilaterally supply a cross-database transaction.

## Recommendation for implementation planning

Do not add a second call to the cached `ToolHostOriginDmAccessGateway` and
label S04 resolved. First decide whether the system's consistency contract
is (A) bounded authorization at decision issuance or (B) immediate revocation
up to a serialized commit point. Both are defensible if deliberately specified,
but have different security and user-facing consequences.

Until a policy is approved, the final acceptance matrix must record
**S04 INCONCLUSIVE/BLOCKED**. Site role checks between *distinct* HTTP
requests are already covered; they are not evidence of an in-flight guarantee.
No Source, Site, Core or Sheet runtime behavior is changed by this document.

## Separate browser acceptance

The Sheet feature branch adds `.github/workflows/pillar2-isolated-browser-acceptance.yml`
and `tests/browser/pillar2_acceptance.py` to run actual Site, Character
Sheet, Rules Core, and Chrome with entirely disposable identity, content,
PostgreSQL, and TLS fixtures.

**Verified browser runs:** [initial 44164142897](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44164142897)
**PASS** and [expanded UI 44165230706](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44165230706)
**PASS**, with exact expanded run source identities:
- Sheet `85ae7e234905b868c6d8c79449df4303f69edbd9`
- Core `23919c3ce00825f2da6204f218786957e9f1f1c2`
- Site `e8d0cb7d89de71aa337293b8f36d22bac98687c3`

The expanded browser assertions cover actual Site password login and
private cookies; the embedded Character Sheet creation form; the native
Species chooser selecting Orc, the UI `Activate Native Origin` button and
reload that displays the committed state; delegated native catalog; the
Human/Elf draft and DM-only five checkbox selections; role-demotion denial
on a fresh DM request; restored DM approval; committed mixed Origin shown
in the owner-facing setup UI; persisted owner reload, unrelated-account
denial and browser script-exception checks. Screenshots, logs and structured
results are in [the expanded run artifact](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44165230706/artifacts/14365781356).
This artifact excludes disposable databases and TLS keys/certificates.

This is a **PASS only for the scripted supported browser subset**, not all
Species presets, unimplemented mechanics, or a visual accessibility audit.
A subsequent [three-service race workflow](https://github.com/LegendMaster03/dorks-and-dice-character-sheet/actions/runs/44254000314)
**does now execute** the suspended Core-response/real Site-role-revocation
case above. That additional coverage **does not approve either S04
consistency policy**. Keep the feature PRs draft and unmerged until an
explicit authorization consistency decision and corresponding enforcement
tests are completed.
