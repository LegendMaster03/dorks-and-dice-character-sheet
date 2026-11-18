"""Isolated three-service browser acceptance for the narrow Pillar 2 vertical slice.

Only disposable Site SQLite records, local PostgreSQL, three feature-head
binaries, and a locally trusted test certificate are used. Never point this
script at a deployed Site or a shared database. Browser observations do not
establish commit-time atomic Site authorization (S04).
"""
import json
import os
import sqlite3
import ssl
import subprocess
import sys
import time
import urllib.error
import urllib.request
import uuid
import hashlib
import struct
import base64
import secrets
from pathlib import Path
from playwright.sync_api import sync_playwright
from s04_site_delegation_barrier import S04SiteDelegationBarrier

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".pillar2-browser"
OUT.mkdir(exist_ok=True)
BASE = "https://localhost:5157"
API = "/tool-host/character-sheet/api/upstream/api"
CERT = str(OUT / "localhost.crt")
PROCS = []
LOGS = []
EVENTS = []
UUIDS = {}
PASSWORD = "LocalReview123!"  # Disposable fixture account only.
S04_BARRIER = None


def launch(name, command, settings=None, cwd=None):
    log = open(OUT / (name + ".log"), "w", encoding="utf-8")
    LOGS.append(log)
    proc = subprocess.Popen(command, cwd=cwd or ROOT,
                            env=dict(os.environ, **(settings or {})),
                            stdout=log, stderr=subprocess.STDOUT)
    PROCS.append(proc)
    return proc


def wait_ready(url):
    for _ in range(120):
        for proc in PROCS:
            if proc.poll() is not None:
                raise RuntimeError("Fixture process exited before ready: " +
                                   str(proc.args))
        try:
            with urllib.request.urlopen(
                    url, context=ssl._create_unverified_context(), timeout=2) as r:
                if r.status < 500:
                    return
        except Exception:
            pass
        time.sleep(0.5)
    raise RuntimeError("Disposable service did not become ready: " + url)


def seed_site():
    """Use only the disposable Site schema created by its actual startup."""
    run_key = os.environ.get("PILLAR2_RUN", "local")
    for name in ["owner", "dm", "unrelated", "campaign", "direct", "mixed",
                 "race", "outsider", "membership-owner", "membership-dm"]:
        UUIDS[name] = str(uuid.uuid5(
            uuid.NAMESPACE_DNS, "pillar2-browser-" + run_key + "-" + name)).upper()
    now = "2026-11-17 14:10:00+00:00"
    with sqlite3.connect(OUT / "site-identity.db") as db:
        for user in ["owner", "dm", "unrelated"]:
            salt = secrets.token_bytes(16)
            key = hashlib.pbkdf2_hmac("sha512", PASSWORD.encode(), salt, 100000, 32)
            password_hash = base64.b64encode(
                b"\x01" + struct.pack(">III", 2, 100000, 16) + salt + key).decode()
            email = user + "@review.test"
            db.execute(
                "INSERT INTO AspNetUsers "
                "(Id,AccountKind,DisplayName,CreatedAt,UserName,NormalizedUserName,"
                "Email,NormalizedEmail,EmailConfirmed,PasswordHash,SecurityStamp,"
                "ConcurrencyStamp,PhoneNumberConfirmed,TwoFactorEnabled,"
                "LockoutEnabled,AccessFailedCount) "
                "VALUES (?,0,?,?,?,?,?,?,1,?,?,?,0,0,1,0)",
                (UUIDS[user], user, now, email, email.upper(),
                 email, email.upper(), password_hash,
                 str(uuid.uuid4()), str(uuid.uuid4())))
    with sqlite3.connect(OUT / "site-content.db") as db:
        for key, port in [("character-sheet", 5158), ("rules-core", 5159)]:
            is_application = key == "character-sheet"
            fields = {
                "tool_id": str(uuid.uuid5(uuid.NAMESPACE_DNS, "browser-" + key)),
                "tool_key": key,
                "tool_kind": 0 if is_application else 1,
                "tool_slug": key if is_application else None,
                "tool_display_name": key,
                "tool_integration_type": 0 if is_application else None,
                "tool_integration_contract_version": 2 if is_application else None,
                "tool_upstream_base_url": f"http://127.0.0.1:{port}",
                "tool_frontend_entry_point": "/app.js" if is_application else None,
                "tool_health_path": "/health",
                "tool_modes": '["dorks-and-dice"]',
                "tool_delegation_targets": '["rules-core"]' if is_application else "[]",
                "tool_allow_anonymous": 0,
                "tool_enabled": 1,
                "tool_created_at": now,
                "tool_updated_at": now,
            }
            cols = ",".join(fields)
            db.execute(
                "INSERT INTO tool_registration (" + cols + ") VALUES (" +
                ",".join("?" for _ in fields) + ")", tuple(fields.values()))
        db.execute(
            "INSERT INTO dd_campaign "
            "(Id,Name,CreatedByUserId,Status,CreatedAt,UpdatedAt) "
            "VALUES (?,?,?,0,?,?)",
            (UUIDS["campaign"], "Browser Review Campaign", UUIDS["dm"], now, now))
        for name in ["direct", "mixed", "race", "outsider"]:
            owner = "unrelated" if name == "outsider" else "owner"
            db.execute(
                "INSERT INTO dd_character "
                "(Id,OwnerUserId,Name,Status,CreatedAt,UpdatedAt) "
                "VALUES (?,?,?,0,?,?)",
                (UUIDS[name], UUIDS[owner], "Browser " + name, now, now))
            if name != "outsider":
                db.execute(
                    "INSERT INTO dd_campaign_character "
                    "(Id,CampaignId,CharacterId,Status,ConnectedAt) "
                    "VALUES (?,?,?,0,?)",
                    (str(uuid.uuid4()), UUIDS["campaign"], UUIDS[name], now))
        for name in ["owner", "dm"]:
            member = UUIDS["membership-" + name]
            db.execute(
                "INSERT INTO dd_campaign_membership "
                "(Id,CampaignId,UserId,Status,JoinedAt) VALUES (?,?,?,0,?)",
                (member, UUIDS["campaign"], UUIDS[name], now))
            db.execute(
                "INSERT INTO dd_campaign_membership_role "
                "(Id,CampaignMembershipId,Role,GrantedAt,GrantedByUserId) "
                "VALUES (?,?,?,?,?)",
                (str(uuid.uuid4()), member,
                 "dm" if name == "dm" else "player", now, UUIDS["dm"]))


def set_dm_role(role):
    with sqlite3.connect(OUT / "site-content.db") as db:
        db.execute("UPDATE dd_campaign_membership_role SET Role=? "
                   "WHERE CampaignMembershipId=?",
                   (role, UUIDS["membership-dm"]))


def expect_event(name, predicate, evidence=None):
    if not predicate:
        raise AssertionError("Browser acceptance failed: " + name +
                             (" / " + str(evidence)[:500] if evidence else ""))
    EVENTS.append({"name": name, "status": "PASS", "detail": evidence})


def login(browser, user):
    context = browser.new_context(ignore_https_errors=True, viewport={
        "width": 1440, "height": 900})
    page = context.new_page()
    page.goto(BASE + "/account/login", wait_until="domcontentloaded")
    page.locator('input[name="Email"]').fill(user + "@review.test")
    page.locator('input[name="Password"]').fill(PASSWORD)
    page.locator('form button[type="submit"]').first.click()
    # Site sets a host-only auth cookie on ordinary password login; the
    # browser keeps it internally and it is never copied into the report.
    expect_event("site-login-" + user,
                 any(c["name"] == "__Host-dorks-and-dice.auth"
                     for c in context.cookies()))
    return context, page


def request_on_page(page, name, path, method="GET", body=None, expected=200):
    result = page.evaluate(
        """async ({path, method, body}) => {
            const response = await fetch(path, {
                method, credentials: "same-origin",
                headers: {"Accept": "application/json",
                    ...(body === null ? {} : {"Content-Type": "application/json"})},
                ...(body === null ? {} : {body: JSON.stringify(body)})
            });
            const raw = await response.text();
            let data; try { data = JSON.parse(raw); } catch { data = raw; }
            return {status: response.status, body: data};
        }""", {"path": path, "method": method, "body": body})
    expect_event(name, result["status"] == expected,
                 {"status": result["status"]})
    return result["body"]


def run_browser():
    global S04_BARRIER
    # All outbound Sheet -> Site ticket/delegation requests go through a
    # localhost-only pass-through. The response barrier is armed only for the
    # separate S04 reproduction after normal browser acceptance succeeds.
    S04_BARRIER = S04SiteDelegationBarrier()
    S04_BARRIER.start()
    launch("site",
           ["dotnet", str(ROOT / "site/dorks-and-dice-site/bin/Release/net10.0/dorks-and-dice-site.dll")],
           {"ASPNETCORE_ENVIRONMENT": "Development",
            "ConnectionStrings__ContentDatabaseLocal": f"Data Source={OUT}/site-content.db",
            "ConnectionStrings__ContentDatabaseExternal": f"Data Source={OUT}/site-content.db",
            "ConnectionStrings__IdentityDatabase": f"Data Source={OUT}/site-identity.db",
            "DorksAndDiceStorage__ContentSource": "Local",
            "SiteHosting__Modes__dorks-and-dice__Domains__0": "localhost",
            "SiteHosting__Modes__dorks-and-dice__CanonicalHost": "localhost",
            "SiteHosting__TrustedPreviewHosts__0": "unused-review.test",
            "GameServers__Minecraft__Host": "127.0.0.1",
            "GameServers__Minecraft__Port": "65534",
            "ASPNETCORE_Kestrel__Certificates__Default__Path":
                str(OUT / "localhost.pfx"),
            "ASPNETCORE_Kestrel__Certificates__Default__Password":
                "local-review-only",
            "ASPNETCORE_URLS": BASE, "SSL_CERT_FILE": CERT},
           ROOT / "site/dorks-and-dice-site")
    wait_ready(BASE + "/account/login")
    seed_site()
    launch("sheet",
           ["dotnet", str(ROOT / "src/CharacterSheet.Web/bin/Release/net10.0/CharacterSheet.Web.dll")],
           {"ASPNETCORE_URLS": "http://127.0.0.1:5158",
            "ToolHost__BaseUrl": "http://127.0.0.1:5160", "SSL_CERT_FILE": CERT,
            "ConnectionStrings__CharacterSheet":
                "Host=127.0.0.1;Port=55433;Database=postgres;"
                "Username=agent;Password=fixture-only"},
           ROOT / "src/CharacterSheet.Web")
    wait_ready("http://127.0.0.1:5158/health")
    launch("core",
           ["dotnet", str(ROOT / "rules-core/src/RulesCore.Host/bin/Release/net10.0/RulesCore.Host.dll")],
           {"ASPNETCORE_URLS": "http://127.0.0.1:5159",
            "ToolHost__BaseUrl": BASE, "SSL_CERT_FILE": CERT,
            "Rulesets__ContentRoot": str(ROOT / "rules-core/data/rulesets")},
           ROOT / "rules-core")
    wait_ready("http://127.0.0.1:5159/ready")

    with sync_playwright() as p:
        browser = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox"])
        owner_context, owner = login(browser, "owner")
        dm_context, dm = login(browser, "dm")
        unrelated_context, unrelated = login(browser, "unrelated")
        page_errors = []
        owner.on("pageerror", lambda error: page_errors.append(str(error)))
        dm.on("pageerror", lambda error: page_errors.append(str(error)))

        # Real browser navigation and form submission through the embedded
        # Site/Tool shell, not a direct HTTP-only substitute.
        owner.goto(BASE + "/tools/character-sheet/new")
        owner.locator('[data-character-sheet-state="new-character"]').wait_for(timeout=30000)
        owner.locator('input[name="characterName"]').fill("Chromium Created Hero")
        owner.get_by_role("button", name="Build Character").click()
        owner.locator('[data-character-sheet-state="rich-character"]').wait_for(timeout=45000)
        expect_event("browser-new-character-form", "characters/" in owner.url,
                     {"urlPath": owner.url.split("localhost:5157")[-1]})
        owner.screenshot(path=str(OUT / "browser-new-character.png"), full_page=True)

        # Set up previously seeded Site Characters through the same browser
        # credentials. These API calls are fixture setup, not assertions that
        # every builder control has been exercised.
        for name in ["direct", "mixed", "race"]:
            request_on_page(owner, "browser-bootstrap-" + name,
                            API + "/characters/" + UUIDS[name] + "/sheet",
                            "POST", {}, 200)
        catalog = request_on_page(owner, "browser-delegated-native-catalog",
                                  API + "/native-rules/ruleset")
        fp = catalog["fingerprint"]

        # Exercise the actual owner Species chooser, native option search,
        # save-draft workflow and native activation button. These previously
        # used browser-authenticated fetch fixture setup only.
        owner.goto(BASE + "/tools/character-sheet/characters/" + UUIDS["direct"])
        owner.locator('[data-character-sheet-state="rich-character"]').wait_for(timeout=30000)
        owner.locator('[data-sheet-mode-control="guided"]').click()
        owner.locator('[data-guided-builder-section="species"]').click()
        identity = owner.locator('[data-origin-identity="species"]')
        identity.get_by_role("button", name="Choose").click()
        owner.locator('[data-origin-chooser="species"]').wait_for(timeout=30000)
        owner.get_by_role("button", name="Orc", exact=True).click()
        identity.get_by_text("species.orc").wait_for(timeout=30000)
        owner.get_by_role("button", name="Activate Native Origin").click()
        owner.get_by_text("Origin construction is committed at revision 1",
                          exact=False).wait_for(timeout=30000)
        expect_event("browser-owner-direct-chooser-and-activation", True)
        owner.screenshot(path=str(OUT / "browser-direct-origin-ui.png"),
                         full_page=True)
        owner.reload()
        owner.locator('[data-character-sheet-state="rich-character"]').wait_for(timeout=30000)
        owner.locator('[data-sheet-mode-control="guided"]').click()
        owner.locator('[data-guided-builder-section="species"]').click()
        owner.get_by_text("Origin construction is committed at revision 1",
                          exact=False).wait_for(timeout=30000)
        owner.screenshot(path=str(OUT / "browser-direct-reloaded.png"), full_page=True)
        state = request_on_page(owner, "browser-direct-reload",
                                API + "/characters/" + UUIDS["direct"] + "/origin/state")
        expect_event("browser-direct-persisted",
                     state["savedState"]["revision"] == 1)

        mixed = {
            "id": "browser-mixed", "intent": 1,
            "method": {"ruleId": "rule.creature-origination",
                       "methodId": "biological"},
            "contributors": [
                {"id": "parent-one", "role": {"kind": 0},
                 "source": {"kind": 0, "id": "species.human"}},
                {"id": "parent-two", "role": {"kind": 0},
                 "source": {"kind": 0, "id": "species.elf"}}
            ],
            "candidates": [], "manifestations": []
        }
        request_on_page(owner, "browser-save-mixed", API + "/characters/" +
                        UUIDS["mixed"] + "/origin/draft", "PUT",
                        {"rulesetId": "first-party-001", "fingerprint": fp,
                         "proposal": mixed, "expectedRevision": 0})
        review_url = (BASE + "/tools/character-sheet/dm-origin-review/" +
                      UUIDS["campaign"] + "/" + UUIDS["mixed"])
        dm.goto(review_url)
        dm.locator('[data-dm-origin-review]').wait_for(timeout=30000)
        dm.get_by_text("Selected offers: 0 of 5").wait_for(timeout=30000)

        # A DM who has been demoted since the initial loaded page must not
        # receive offers on a new navigation.
        set_dm_role("player")
        dm.reload()
        dm.get_by_role("button", name="Retry DM review").wait_for(timeout=30000)
        expect_event("browser-revoked-dm-review-denied", True)
        set_dm_role("dm")
        dm.reload()
        dm.get_by_text("Selected offers: 0 of 5").wait_for(timeout=30000)
        for source, path in [
            ("species.elf", "/creatureType"),
            ("species.elf", "/size"),
            ("species.elf", "/walkingSpeedFeet"),
            ("species.elf", "/traits/0"),
            ("species.human", "/traits/2"),
        ]:
            dm.get_by_role("checkbox", name=f"Select {source} {path}").check()
        dm.get_by_text("Selected offers: 5 of 5").wait_for(timeout=15000)
        dm.screenshot(path=str(OUT / "browser-dm-selections.png"), full_page=True)
        dm.get_by_role("button", name="Commit DM-approved mixed Origin").click()
        dm.get_by_text("Origin construction committed at revision 1",
                       exact=False).wait_for(timeout=45000)
        dm.screenshot(path=str(OUT / "browser-mixed-committed.png"), full_page=True)

        owner.goto(BASE + "/tools/character-sheet/characters/" + UUIDS["mixed"])
        owner.locator('[data-character-sheet-state="rich-character"]').wait_for(timeout=30000)
        persisted = request_on_page(owner, "browser-mixed-owner-reload",
                                    API + "/characters/" + UUIDS["mixed"] + "/origin/state")
        expect_event("browser-mixed-persisted",
                     persisted["savedState"]["revision"] == 1 and
                     persisted["savedState"]["profile"]["underlying"]["speciesId"]
                     == "custom.human-elf")
        owner.locator('[data-sheet-mode-control="guided"]').click()
        owner.locator('[data-guided-builder-section="species"]').click()
        owner.get_by_text("Origin construction is committed at revision 1",
                          exact=False).wait_for(timeout=30000)
        expect_event("browser-owner-mixed-committed-presentation", True)
        owner.screenshot(path=str(OUT / "browser-mixed-owner.png"), full_page=True)
        denied = request_on_page(unrelated, "browser-unrelated-owner-denied",
                                 API + "/characters/" + UUIDS["mixed"] +
                                 "/origin/draft", expected=404)
        # S04 characterization with the actual Site source of authority.
        # Hold the response AFTER real Site -> Rules Core approval evaluation,
        # but BEFORE the Sheet receives it and persists the Origin. Revoke
        # the real Site DM role in SQLite while the original HTTP request
        # is still suspended. This is NOT a test that the behavior is safe.
        race_proposal = dict(mixed, id="browser-s04-race")
        race_saved = request_on_page(
            owner, "s04-owner-save-race-draft",
            API + "/characters/" + UUIDS["race"] + "/origin/draft",
            "PUT", {"rulesetId": "first-party-001", "fingerprint": fp,
                    "proposal": race_proposal, "expectedRevision": 0})
        race_revision = race_saved["draft"]["revision"]
        review_path = (API + "/characters/" + UUIDS["race"] +
                       "/origin/mixed-review?campaignId=" + UUIDS["campaign"])
        offers = request_on_page(dm, "s04-genuine-site-dm-review", review_path)
        wanted = {
            ("species.elf", "/creatureType"),
            ("species.elf", "/size"),
            ("species.elf", "/walkingSpeedFeet"),
            ("species.elf", "/traits/0"),
            ("species.human", "/traits/2"),
        }
        selected = [candidate["id"] for candidate in offers["candidates"]
                    if (candidate["sourceId"], candidate["occurrencePath"]) in wanted]
        expect_event("s04-five-native-offers-verified", len(selected) == 5)

        activation_path = (API + "/characters/" + UUIDS["race"] +
                           "/origin/activate-mixed-approved")
        activation_command = {
            "campaignId": UUIDS["campaign"],
            "expectedDraftRevision": race_revision,
            "candidateIds": selected,
        }
        S04_BARRIER.arm()
        dm.evaluate("""({path, body}) => {
            window.__s04RaceResult = null;
            fetch(path, {
                method: "POST",
                credentials: "same-origin",
                headers: {"Content-Type": "application/json",
                          "Accept": "application/json"},
                body: JSON.stringify(body)
            }).then(async response => {
                window.__s04RaceResult = {
                    status: response.status,
                    text: (await response.text()).slice(0, 350)
                };
            }).catch(error => {
                window.__s04RaceResult = {status: 0,
                                         text: String(error).slice(0, 350)};
            });
        }""", {"path": activation_path, "body": activation_command})
        try:
            expect_event("s04-real-core-evaluation-held",
                         S04_BARRIER.reached.wait(timeout=22),
                         {"siteDelegatedEvaluationStatus": S04_BARRIER.status})
            # No browser-visible tokens or synthetic Site permissions are
            # used to revoke this membership. It is the actual Site store.
            set_dm_role("player")
            request_on_page(dm, "s04-fresh-site-denies-revoked-dm",
                            review_path, expected=404)
            before_release = request_on_page(
                owner, "s04-no-commit-while-core-response-held",
                API + "/characters/" + UUIDS["race"] + "/origin/state")
            expect_event("s04-still-uncommitted-at-revocation",
                         before_release["savedState"] is None)
        finally:
            # Always release the suspended request; never strand a runner.
            S04_BARRIER.release.set()

        dm.wait_for_function("window.__s04RaceResult !== null", timeout=40000)
        race_result = dm.evaluate("window.__s04RaceResult")
        expect_event("s04-original-request-status-after-site-revocation",
                     race_result["status"] == 200,
                     {"httpStatus": race_result["status"]})
        after = request_on_page(owner, "s04-owner-reload-after-revocation",
                                API + "/characters/" + UUIDS["race"] + "/origin/state")
        expect_event("s04-confirm-current-commit-after-site-revocation",
                     after["savedState"] is not None and
                     after["savedState"]["revision"] == 1)
        # Passing here is a confirmed Site/Sheet/Core security RACE
        # CHARACTERIZATION. S04 acceptance remains blocked, and policy is
        # intentionally not changed by this test.
        EVENTS.append({
            "name": "s04-security-acceptance",
            "status": "OPEN-GAP",
            "detail": "Real Site DM revoked after native evaluation; original "
                      "activation still committed at revision 1"
        })

        expect_event("browser-no-script-exceptions", not page_errors,
                     {"exceptions": page_errors})
        browser.close()


def main():
    try:
        run_browser()
    except Exception as error:
        EVENTS.append({"name": "failure", "status": "FAIL",
                       "detail": type(error).__name__ + ": " + str(error)})
        raise
    finally:
        if S04_BARRIER is not None:
            S04_BARRIER.close()
        (OUT / "result.json").write_text(json.dumps(EVENTS, indent=2))
        # Terminate independent disposable processes, not system services.
        for proc in reversed(PROCS):
            proc.terminate()
        for proc in PROCS:
            try:
                proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
        for log in LOGS:
            log.close()


if __name__ == "__main__":
    main()
