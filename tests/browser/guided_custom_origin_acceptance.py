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

        for name in ("direct", "red", "mixed", "drow-mixed"):
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
        contributors = result["draft"]["proposal"]["contributors"]
        red_id, bronze_id = (c["id"] for c in contributors)
        def group(res, key):
            return next(g for g in res["preview"]["result"]["biologicalCombination"]["characteristics"]
                        if g["characteristicId"] == key)
        def candidate(res, key, contributor):
            return next(o["candidate"] for o in group(res, key)["offers"]
                        if o["candidate"]["contributorId"] == contributor)
        def open_gene(key):
            # Clear retained search and open the exact native group by its stable ID.
            page.get_by_label("Search characteristics").fill("")
            gene = page.locator('[data-biological-group="' + key + '"]')
            expect(gene).to_be_visible()
            if gene.get_attribute("open") is None:
                gene.locator("summary").first.click()
            return gene
        def gene_action(res, key, contributor, action, character="red"):
            gene = open_gene(key)
            cid = candidate(res, key, contributor)["id"]
            card = gene.locator('[data-biological-offer="' + cid + '"]')
            return save(character, lambda: card.get_by_role("button", name=action, exact=True).click())
        # A complete physical foundation is required before allocation is resolved.
        for key in ("creatureType", "size", "walkingSpeedFeet"):
            result = gene_action(result, key, red_id, "Inherit instance")
            result = gene_action(result, key, red_id, "Request expression")
        event("biological-foundation-authorized", result["preview"]["result"]["isMechanicallyAuthorized"] is True)
        base_allocation = result["preview"]["allocation"]
        event("biological-foundation-allocation", base_allocation["status"] == "within-allowance")
        key = "trait.breath-weapon"
        breath = group(result, key)
        event("native-breath-discovery", set(breath["presentation"]["recombinableParameters"]) == {"damageType", "area"})
        red_breath = candidate(result, key, red_id)
        bronze_breath = candidate(result, key, bronze_id)
        event("distinct-breath-configurations",
              red_breath["offeredParameters"]["damageType"] == "fire" and
              bronze_breath["offeredParameters"]["damageType"] == "lightning" and
              red_breath["offeredParameters"]["area"] != bronze_breath["offeredParameters"]["area"])
        result = gene_action(result, key, red_id, "Inherit instance")
        result = gene_action(result, key, bronze_id, "Inherit instance")
        event("latent-breath-no-charge", result["preview"]["allocation"]["committedLoad"] == base_allocation["committedLoad"] and
              not group(result, key)["expressions"])
        genes = result["preview"]["result"]["biologicalCombination"]["inheritedInstances"]
        event("two-latent-breath-instances", sum(g["isLatent"] for g in genes
              if g["gene"]["sourceCandidateId"] in [red_breath["id"], bronze_breath["id"]]) == 2)
        snapshot("guided-biological-inheritance")
        decision("Configuration")
        open_gene(key)
        panel = page.locator('[data-biological-composition="' + key + '"]')
        panel.locator("summary").first.click()
        panel.get_by_label("Base expression source", exact=True).select_option(red_breath["id"])
        panel.get_by_label("Damage Type source", exact=True).select_option(red_breath["id"])
        panel.get_by_label("Area source", exact=True).select_option(bronze_breath["id"])
        result = save("red", lambda: panel.get_by_role("button", name="Apply combined expression").click())
        effective = group(result, key)["expressions"]
        event("one-native-recombined-breath", len(effective) == 1 and
              effective[0]["parameters"]["damageType"] == "fire" and
              effective[0]["parameters"]["area"] == bronze_breath["offeredParameters"]["area"] and
              effective[0]["parameterSources"]["damageType"] == red_breath["id"] and
              effective[0]["parameterSources"]["area"] == bronze_breath["id"])
        event("recombined-native-allocation", result["preview"]["allocation"]["status"] == "within-allowance" and
              result["preview"]["allocation"]["committedLoad"] > base_allocation["committedLoad"])
        snapshot("guided-biological-configuration")
        open_origin("red")
        decision("Configuration")
        open_gene(key)
        expect(page.get_by_label("Area source", exact=True)).to_have_value(bronze_breath["id"])
        event("recombined-configuration-reload", request(page, "recombined-reload", url("red"))["draft"]["proposal"] ==
              result["draft"]["proposal"])
        decision("Contributors")
        page.get_by_label("Native biological contributor source").select_option("ancestry.dragonborn.blue")
        page.get_by_role("heading", name="Contributor 1", exact=True).locator("..").get_by_role(
            "button", name="Replace with selected source below").click()
        changed = save("red", lambda: page.get_by_role("button", name="Apply changes", exact=True).click())
        event("unaffected-contributor-retention", bronze_breath["id"] in changed["draft"]["proposal"]["biologicalTransmission"]["candidateIds"])
        event("affected-composition-cleared", not changed["draft"]["proposal"].get("relationships") and
              not changed["draft"]["proposal"].get("resolutions"))
        open_origin("red")
        decision("Review")
        persisted = request(page, "biological-reload", url("red"))
        event("biological-reload-exact", persisted["draft"]["proposal"] == changed["draft"]["proposal"])
        snapshot("guided-biological-review-desktop")
        page.set_viewport_size({"width":390,"height":844})
        snapshot("guided-biological-review-mobile")
        page.set_viewport_size({"width":1440,"height":900})

        # Real finite choice persistence, transport failure and linked native
        # expression (no catalog/feat authority is manufactured).
        open_origin("drow-mixed")
        select_method("biological")
        save("drow-mixed", lambda: page.get_by_role("button", name="Start biological draft").click())
        page.get_by_label("Native biological contributor source").select_option("ancestry.elf.drow")
        drow = save("drow-mixed", lambda: page.get_by_role("button", name="Add contributor").click())
        donor = drow["draft"]["proposal"]["contributors"][0]["id"]
        decision("Inheritance & Expression")
        for key in ("creatureType", "size", "walkingSpeedFeet"):
            drow = gene_action(drow, key, donor, "Inherit instance", "drow-mixed")
            drow = gene_action(drow, key, donor, "Request expression", "drow-mixed")
        ability = candidate(drow, "spellcastingAbility", donor)
        drow = gene_action(drow, "spellcastingAbility", donor, "Inherit instance", "drow-mixed")
        decision("Configuration")
        ability_group = open_gene("spellcastingAbility")
        wisdom = ability_group.get_by_label("Wisdom", exact=True)
        expect(wisdom).to_be_visible()
        failed_requests = []
        def abort_save(route):
            if route.request.method == "PUT":
                failed_requests.append(route.request.post_data_json)
                route.abort()
            else:
                route.continue_()
        page.route("**/origin/draft", abort_save)
        wisdom.check()
        expect(page.get_by_role("button", name="Retry draft save", exact=True)).to_be_enabled()
        event("finite-choice-save-failure-retained", len(failed_requests) == 1)
        event("failed-save-did-not-change-server-draft", request(page, "failed-save-read", url("drow-mixed"))["draft"]["proposal"] ==
              drow["draft"]["proposal"])
        page.unroute("**/origin/draft", abort_save)
        drow = save("drow-mixed", lambda: page.get_by_role("button", name="Retry draft save", exact=True).click())
        event("retry-exact-pending-finite-choice", drow["draft"]["proposal"] == failed_requests[0]["proposal"])
        latent = drow["draft"]["proposal"]["biologicalTransmission"]["latentConfigurations"]
        event("latent-finite-choice-persisted", any(c["candidateId"] == ability["id"] and
              c["choices"][0]["selectedOptionIds"] == ["wisdom"] for c in latent))
        decision("Inheritance & Expression")
        spell_key = "trait.faerie-fire"
        drow = gene_action(drow, spell_key, donor, "Inherit instance", "drow-mixed")
        drow = gene_action(drow, spell_key, donor, "Request expression", "drow-mixed")
        event("linked-spell-not-granted-without-expression", not group(drow, spell_key)["expressions"])
        drow = gene_action(drow, "spellcastingAbility", donor, "Request expression", "drow-mixed")
        linked = group(drow, spell_key)["expressions"]
        event("linked-spell-evaluates-source-ability", len(linked) == 1 and linked[0]["parameters"]["spellcastingAbility"] == "wisdom")
        open_origin("drow-mixed")
        decision("Configuration")
        ability_group = open_gene("spellcastingAbility")
        expect(ability_group.get_by_label("Wisdom", exact=True)).to_be_checked()
        event("finite-choice-reload-exact", request(page, "finite-choice-reload", url("drow-mixed"))["draft"]["proposal"] ==
              drow["draft"]["proposal"])
        snapshot("guided-biological-linked-finite-choice")

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
        for name in ("red", "mixed", "drow-mixed"):
            state = request(page, name + "-state", url(name, "state"))
            event(name + "-unestablished", state["savedState"] is None)
            request(outsider, name + "-owner-boundary", url(name), expected=404)
        event("no-page-errors", not errors, errors)
        browser.close()


if __name__ == "__main__":
    # Reuse the fixture's cleanup and JSON evidence writer even on assertion failure.
    fixture.run_browser = run_browser
    fixture.main()
