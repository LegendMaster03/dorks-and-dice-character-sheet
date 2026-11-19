"""Isolated three-service browser acceptance for the narrow Pillar 2 vertical slice.

Only disposable Site SQLite records, local PostgreSQL, three feature-head
binaries, and a locally trusted test certificate are used. Never point this
script at a deployed Site or a shared database.
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
                 "drow", "red", "human", "drow-mixed", "outsider", "membership-owner", "membership-dm"]:
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
        for name in ["direct", "mixed", "drow", "red", "human", "drow-mixed", "outsider"]:
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
            "ToolHost__BaseUrl": BASE, "SSL_CERT_FILE": CERT,
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
        chromium_path = os.environ.get("PILLAR2_CHROMIUM_PATH")
        browser = p.chromium.launch(
            executable_path=chromium_path, channel=None if chromium_path else "chrome")
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
        for name in ["direct", "mixed", "drow", "red", "human", "drow-mixed"]:
            request_on_page(owner, "browser-bootstrap-" + name,
                            API + "/characters/" + UUIDS[name] + "/sheet",
                            "POST", {}, 200)
        catalog = request_on_page(owner, "browser-delegated-native-catalog",
                                  API + "/native-rules/ruleset")
        expect_event("browser-catalog-fingerprint", bool(catalog["fingerprint"]),
                     {"fingerprint": catalog["fingerprint"]})

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

        expect_event("browser-orc-origin-resolved",
                     owner.locator('[data-guided-builder-section="species"]')
                     .get_attribute("data-guided-builder-status") == "resolved")
        orc_grants = state["savedState"]["profile"]["underlying"]["inherentGrants"]
        expect_event("browser-orc-native-characteristics",
                     len(orc_grants) == 9 and
                     all(grant["sourceCandidateIds"] for grant in orc_grants))

        def origin_url(name, action):
            return API + "/characters/" + UUIDS[name] + "/origin/" + action

        def open_origin(name):
            owner.goto(BASE + "/tools/character-sheet/characters/" + UUIDS[name])
            owner.locator('[data-character-sheet-state="rich-character"]').wait_for()
            owner.locator('[data-sheet-mode-control="guided"]').click()
            owner.locator('[data-guided-builder-section="species"]').click()

        def choose(trigger, category, name):
            trigger.click()
            chooser = owner.locator(f'[data-origin-chooser="{category}"]')
            chooser.get_by_role("button", name=name, exact=True).click()
            chooser.wait_for(state="hidden")

        def choose_direct(category, name):
            row = owner.locator(f'[data-origin-identity="{category}"]')
            choose(row.get_by_role("button", name="Choose"), category, name)

        charisma = owner.locator('[data-origin-choice] select').filter(
            has=owner.locator('option[value="charisma"]'))

        for fixture, species, ancestry in [
            ("drow", "Elf", "Drow"),
            ("red", "Dragonborn", "Red Dragonborn"),
        ]:
            open_origin(fixture)
            choose_direct("species", species)
            choose_direct("ancestry", ancestry)
            if fixture == "drow":
                charisma.select_option("charisma")
            owner.get_by_role("button", name="Activate Native Origin").click()
            owner.get_by_text("Origin construction is committed at revision 1",
                              exact=False).wait_for()
            open_origin(fixture)
            owner.get_by_text("Origin construction is committed at revision 1",
                              exact=False).wait_for()
            committed = request_on_page(owner, f"browser-{fixture}-origin-reload",
                                        origin_url(fixture, "state"))
            profile = committed["savedState"]["profile"]["underlying"]
            expect_event(f"browser-{fixture}-native-source",
                         profile["ancestryId"] == {
                             "drow": "ancestry.elf.drow",
                             "red": "ancestry.dragonborn.red"
                         }[fixture] and bool(profile["inherentGrants"]) and
                         all(grant["sourceCandidateIds"]
                             for grant in profile["inherentGrants"]),
                         {"ancestry": profile["ancestryId"],
                          "grants": profile["inherentGrants"]})

        open_origin("human")
        choose_direct("species", "Human")
        owner.get_by_text("Origin Feat selection awaits", exact=False).wait_for()
        expect_event("browser-human-origin-feat-deferred",
                     owner.locator('[data-guided-builder-section="species"]')
                     .get_attribute("data-guided-builder-status") == "incomplete")
        human = request_on_page(owner, "browser-human-origin-state",
                                origin_url("human", "state"))
        expect_event("browser-human-origin-uncommitted", human["savedState"] is None)

        def choose_source(slot, category, name):
            parent = owner.locator(f'[data-mixed-origin-parent="{slot}"]')
            choose(parent.get_by_role("button", name=f"Choose {category.title()} source"),
                   category, name)

        def select_offer(fixture, name):
            offer = owner.locator("[data-origin-native-candidate]").filter(
                has=owner.get_by_role("heading", name=name, exact=True))
            candidate_id = offer.get_attribute("data-origin-native-candidate")
            offer.get_by_role("button", name="Select characteristic for draft").click()
            offer.get_by_role("button", name="Remove draft selection").wait_for()
            request_on_page(owner, f"browser-{fixture}-selected-draft",
                                    origin_url(fixture, "draft"))
            return candidate_id

        def saved_selection(fixture, candidate_id, count):
            open_origin(fixture)
            draft = request_on_page(owner, f"browser-{fixture}-{count}-source-reload",
                                    origin_url(fixture, "draft"))
            proposal = draft["draft"]["proposal"]
            selection, = [entry for entry in proposal["manifestations"]
                          if entry["candidateId"] == candidate_id]
            expect_event(f"browser-{fixture}-{count}-source-retention",
                         len(proposal["contributors"]) == count
                         and draft["activeGrants"] is False,
                         {"candidateId": candidate_id, "selection": selection,
                          "contributors": proposal["contributors"]})
            return selection

        open_origin("mixed")
        owner.get_by_role("button", name="Mixed biological sources").click()
        choose_source("parent-one", "species", "Human")
        choose_source("parent-two", "species", "Elf")
        elf_id = select_offer("mixed", "Low-Light Vision")
        elf_selection = saved_selection("mixed", elf_id, 2)
        for count, next_species in [(3, "Orc"), (4, "Dragonborn")]:
            choose(owner.get_by_role("button", name="Add another native source (draft only)"),
                   "species", next_species)
            retained = saved_selection("mixed", elf_id, count)
            expect_event(f"browser-elf-exact-selection-{count}", retained == elf_selection)
        draft = request_on_page(owner, "browser-mixed-before-activation-denial",
                                origin_url("mixed", "draft"))
        request_on_page(owner, "browser-native-activation-refuses-mixed",
                        origin_url("mixed", "activate-native"), "POST",
                        {"expectedDraftRevision": draft["draft"]["revision"]}, expected=422)
        expect_event("browser-no-mixed-activation-control",
                     owner.get_by_role("button", name="Activate Native Origin").count() == 0)
        mixed_state = request_on_page(owner, "browser-mixed-origin-state",
                                      origin_url("mixed", "state"))
        expect_event("browser-mixed-origin-uncommitted",
                     mixed_state["savedState"] is None and not mixed_state.get("events"))

        open_origin("drow-mixed")
        owner.get_by_role("button", name="Mixed biological sources").click()
        choose_source("parent-one", "species", "Human")
        choose_source("parent-two", "ancestry", "Drow")
        drow_offer = owner.locator(
            '[data-origin-native-candidate="contributor:parent-two:ancestry.elf.drow:/traits/4"]')
        drow_id = drow_offer.get_attribute("data-origin-native-candidate")
        drow_offer.get_by_role("button", name="Select characteristic for draft").click()
        charisma.select_option("charisma")
        original = saved_selection("drow-mixed", drow_id, 2)
        choose_source("parent-one", "species", "Orc")
        retained = saved_selection("drow-mixed", drow_id, 2)
        expect_event("browser-drow-finite-choice-retained",
                     retained == original and
                     retained["choices"][0]["selectedOptionIds"] == ["charisma"])

        request_on_page(unrelated, "browser-cross-owner-draft-denied",
                        origin_url("mixed", "draft"), expected=404)
        request_on_page(dm, "browser-campaign-dm-owner-draft-denied",
                        origin_url("mixed", "draft"), expected=404)
        request_on_page(dm, "browser-deferred-mixed-review-unavailable",
                        origin_url("mixed", "mixed-review") + "?campaignId=" +
                        UUIDS["campaign"], expected=404)
        request_on_page(dm, "browser-deferred-mixed-activation-unavailable",
                        origin_url("mixed", "activate-mixed-approved"), "POST", {},
                        expected=404)
        dm.goto(BASE + "/tools/character-sheet/dm-origin-review/" +
                UUIDS["campaign"] + "/" + UUIDS["mixed"])
        expect_event("browser-deferred-dm-review-ui-unavailable",
                     dm.get_by_role("button", name="Commit DM-approved mixed Origin").count() == 0
                     and dm.locator("[data-dm-origin-review]").count() == 0)
        expect_event("browser-no-page-errors", not page_errors, {"exceptions": page_errors})
        browser.close()


def main():
    try:
        run_browser()
    except Exception as error:
        EVENTS.append({"name": "failure", "status": "FAIL",
                       "detail": type(error).__name__ + ": " + str(error)})
        raise
    finally:
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
