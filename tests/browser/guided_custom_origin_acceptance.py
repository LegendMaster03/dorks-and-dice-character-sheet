"""Current guided Origin UI acceptance using the isolated three-service fixture.

No production data or direct Core credentials. Draft mutations are browser UI
actions; authenticated GETs verify exact persisted choices and preview envelopes.
"""
import os
import re
from urllib.parse import urlencode
from playwright.sync_api import expect, sync_playwright
import pillar2_acceptance as fixture

BASE, API, OUT = fixture.BASE, fixture.API, fixture.OUT
event, request = fixture.expect_event, fixture.request_on_page


def run_browser():
    fixture.start_services()
    with sync_playwright() as p:
        executable = os.environ.get("PILLAR2_CHROMIUM_PATH")
        browser = p.chromium.launch(executable_path=executable,
                                    channel=None if executable else "chrome")
        context, page = fixture.login(browser, "owner")
        _, outsider = fixture.login(browser, "unrelated")
        errors = []
        page.on("pageerror", lambda error: errors.append(str(error)))
        page.on("pageerror", lambda error: print("Browser page error:", str(error), flush=True))
        page.set_default_timeout(45000)
        ids = fixture.UUIDS

        def url(name, suffix="draft"):
            return API + "/characters/" + ids[name] + "/origin/" + suffix

        for name in ("direct", "red", "mixed"):
            request(page, "bootstrap-" + name, API + "/characters/" + ids[name] + "/sheet",
                    "POST", {}, 200)
        catalog = request(page, "native-catalog", API + "/native-rules/ruleset")
        fingerprint = catalog["fingerprint"]
        methods = request(page, "native-methods", API + "/native-rules/origins/methods?" +
                          urlencode({"fingerprint": fingerprint}))["methods"]

        def open_origin(name):
            page.goto(BASE + "/tools/character-sheet/characters/" + ids[name])
            page.locator('[data-character-sheet-state="rich-character"]').wait_for()
            page.locator('[data-sheet-mode-control="guided"]').click()
            page.locator('[data-guided-builder-section="species"]').click()
            expect(page.get_by_role("heading", name="Character Origins", exact=True)).to_be_visible()

        def save(name, action):
            with page.expect_response(lambda response: response.request.method == "PUT" and
                                      response.url.lower().endswith(url(name).lower())) as pending:
                action()
            response = pending.value
            assert response.ok, response.text()
            result = response.json()
            assert result["activeGrants"] is False
            assert result["draft"]["revision"] > 0
            event(name + "-draft-save-" + str(result["draft"]["revision"]), True,
                  {"method": result["draft"]["proposal"]["method"], "revision": result["draft"]["revision"],
                   "preview": bool(result.get("preview")), "constructPreview": bool(result.get("constructPreview"))})
            return result

        def select_method(method_id):
            page.get_by_role("button", name="Construct a Custom Origin", exact=True).click()
            method = next(m for m in methods if m["reference"]["methodId"] == method_id)
            card = page.get_by_role("heading", name=method["name"], exact=True).locator("..")
            card.get_by_role("button", name="Configure", exact=True).click()

        def decision(label):
            page.get_by_role("navigation", name="Configuration decisions").get_by_role("button", name=label, exact=True).click()

        def snapshot(name):
            page.screenshot(path=str(OUT / (name + ".png")), full_page=True)

        # Default conventional workflow remains independent of custom construction.
        open_origin("direct")
        expect(page.get_by_role("button", name="Construct a Custom Origin")).to_be_visible()
        page.get_by_role("button", name="Orc", exact=True).click()
        direct = save("direct", lambda: page.get_by_role("button", name="Select preset").click())
        event("conventional-draft", direct["draft"]["proposal"]["selectedSpeciesId"] == "species.orc")
        snapshot("guided-conventional-desktop")

        open_origin("red")
        select_method("biological")
        save("red", lambda: page.get_by_role("button", name="Start biological draft").click())
        try:
            expect(page.get_by_label("Native biological contributor source")).to_be_visible(timeout=15000)
        except Exception:
            snapshot("guided-biological-post-save-failure")
            print("Post-save guided UI:", page.locator('[data-guided-builder]').inner_text()[:6000], flush=True)
            raise
        snapshot("guided-biological-initial-draft")
        for ancestry in ("ancestry.dragonborn.red", "ancestry.dragonborn.bronze"):
            page.get_by_label("Native biological contributor source").select_option(ancestry)
            result = save("red", lambda: page.get_by_role("button", name="Add contributor").click())
        event("two-native-biological-contributors", len(result["draft"]["proposal"]["contributors"]) == 2)
        event("native-biological-allocation", result["preview"].get("allocation") is not None)
        snapshot("guided-biological-contributors")
        decision("Inheritance & Expression")
        page.get_by_label("Search characteristics").fill("Creature Type")
        def offer(color):
            return page.get_by_role("heading", name=re.compile("Contributor.*" + color, re.I), level=4).locator("..").filter(
                has=page.get_by_role("button", name="Inherit instance", exact=True)).first
        inherited_red = save("red", lambda: offer("Red").get_by_role("button", name="Inherit instance", exact=True).click())
        inherited = save("red", lambda: offer("Bronze").get_by_role("button", name="Inherit instance", exact=True).click())
        old_ids = inherited_red["draft"]["proposal"]["biologicalTransmission"]["candidateIds"]
        bronze = [x for x in inherited["draft"]["proposal"]["biologicalTransmission"]["candidateIds"] if x not in old_ids]
        event("distinct-inherited-instances", len(bronze) == 1)
        bronze_card = page.get_by_role("heading", name=re.compile("Contributor.*Bronze", re.I), level=4).locator("..").filter(
            has=page.get_by_role("button", name="Remove inherited instance", exact=True))
        expressed = save("red", lambda: bronze_card.get_by_role("button", name="Request expression", exact=True).click())
        event("requested-expression", any(x["candidateId"] == bronze[0] for x in expressed["draft"]["proposal"]["manifestations"]))
        snapshot("guided-biological-inheritance")
        decision("Configuration")
        snapshot("guided-biological-configuration")
        decision("Contributors")
        page.get_by_label("Native biological contributor source").select_option("ancestry.dragonborn.blue")
        page.get_by_role("heading", name="Contributor 1", exact=True).locator("..").get_by_role(
            "button", name="Replace with selected source below").click()
        changed = save("red", lambda: page.get_by_role("button", name="Apply changes", exact=True).click())
        event("unaffected-contributor-retention", bronze[0] in changed["draft"]["proposal"]["biologicalTransmission"]["candidateIds"])
        open_origin("red")
        decision("Review")
        persisted = request(page, "biological-reload", url("red"))
        event("biological-reload-exact", persisted["draft"]["proposal"] == changed["draft"]["proposal"])
        snapshot("guided-biological-review-desktop")
        page.set_viewport_size({"width":390,"height":844})
        snapshot("guided-biological-review-mobile")
        page.set_viewport_size({"width":1440,"height":900})

        open_origin("mixed")
        select_method("construct-assembly")
        save("mixed", lambda: page.get_by_role("button", name="Start Construct draft").click())
        options = request(page, "native-construct-components", API + "/native-rules/options?" + urlencode({
            "fingerprint": fingerprint, "category":"construct-component","q":"","limit":100,"offset":0}))["options"]
        # Select actual published component names, not client-authored definitions.
        def component(key):
            return next(x for x in options if x["id"] == "construct-component." + key)
        # Specific native definitions from the documented reference, not an
        # arbitrary first match (which might be storage rather than generation).
        parts = [component("structural-frame"), component("power-source-housing"), component("locomotion-unit")]
        for part in parts:
            page.get_by_label("Search components").fill(part["name"])
            card = page.get_by_role("heading", name=part["name"], exact=True, level=3).locator("..").filter(
                has=page.get_by_role("button", name="Add component", exact=True))
            assembled = save("mixed", lambda: card.get_by_role("button", name="Add component").click())
        components = assembled["draft"]["proposal"]["constructAssembly"]["components"]
        event("three-native-components", len(components) == 3)
        event("native-construct-evaluation", assembled.get("constructPreview", {}).get("previewOnly") is True)
        snapshot("guided-construct-assembly")
        def configure(part, field, value, result):
            instance = next(x for x in result["draft"]["proposal"]["constructAssembly"]["components"]
                            if x["definitionId"] == part["id"])
            instance_id = instance["instanceId"]
            card = page.locator('[data-construct-instance="' + instance_id + '"]')
            card.get_by_role("button", name="Configure", exact=True).click()
            panel = page.locator('[data-construct-configuration="' + instance_id + '"]')
            selector = panel.get_by_label(field, exact=True)
            expect(selector).to_be_visible()
            previous = result["draft"]["proposal"]["constructAssembly"]["components"]
            updated = save("mixed", lambda: selector.select_option(value))
            after = updated["draft"]["proposal"]["constructAssembly"]["components"]
            event("unrelated-config-retention-" + field, [x for x in previous if x["instanceId"] != instance_id] ==
                  [x for x in after if x["instanceId"] != instance_id])
            return updated

        configured = configure(parts[0], "Construction variant", "medium", assembled)
        configured = configure(parts[0], "Material — Frame", "construct-material.steel", configured)
        snapshot("guided-construct-config-frame")
        configured = configure(parts[1], "Power source", "construct-power-source.mechanical-engine", configured)
        configured = configure(parts[1], "Material — Housing", "construct-material.wood", configured)
        snapshot("guided-construct-config-power")
        configured = configure(parts[2], "Material — Contact", "construct-material.steel", configured)
        event("construct-native-cost", configured["constructPreview"]["result"]["assembly"]["knownConstructionCost"] > 0)
        page.locator('[data-guided-builder-section="background"]').click()
        page.locator('[data-guided-builder-section="species"]').click()
        expect(page.get_by_role("navigation", name="Configuration decisions")).to_be_visible()
        event("construct-navigation-exact", request(page, "construct-navigation", url("mixed"))["draft"]["proposal"] ==
              configured["draft"]["proposal"])
        decision("Review")
        snapshot("guided-construct-review-desktop")
        open_origin("mixed")
        decision("Review")
        persisted = request(page, "construct-reload", url("mixed"))
        event("construct-reload-exact", persisted["draft"]["proposal"] == configured["draft"]["proposal"])
        page.set_viewport_size({"width":390,"height":844})
        snapshot("guided-construct-review-mobile")
        for name in ("red", "mixed"):
            state = request(page, name + "-state", url(name, "state"))
            event(name + "-unestablished", state["savedState"] is None)
            request(outsider, name + "-owner-boundary", url(name), expected=404)
        event("no-page-errors", not errors, errors)
        browser.close()


if __name__ == "__main__":
    # Reuse the fixture's cleanup and JSON evidence writer even on assertion failure.
    fixture.run_browser = run_browser
    fixture.main()
