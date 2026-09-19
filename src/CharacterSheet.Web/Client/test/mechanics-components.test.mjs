import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
    renderActionsPresentation,
    renderCombatMechanicsSummary,
    renderSavingThrowsCard
} from "../.test-dist/ui/mechanics-components.js";
import { renderChecksAndProceduresPresentation, renderProcedure } from "../.test-dist/ui/procedure-components.js";
import { getSafeExternalSourceUrl, renderSourceAttributions } from "../.test-dist/ui/source-attribution.js";

class FakeStyle {
    values = new Map();
    setProperty(name, value) { this.values.set(name, String(value)); }
    getPropertyValue(name) { return this.values.get(name) ?? ""; }
}

class FakeElement {
    constructor(tagName) {
        this.tagName = String(tagName).toUpperCase();
        this.className = "";
        this.textContent = "";
        this.children = [];
        this.attributes = new Map();
        this.style = new FakeStyle();
        this.id = "";
        this.href = "";
        this.target = "";
        this.rel = "";
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
}

globalThis.document = { createElement: tagName => new FakeElement(tagName) };

function walk(root) {
    const nodes = [root];
    for (const child of root.children) nodes.push(...walk(child));
    return nodes;
}
function byAttribute(root, name, value) { return walk(root).filter(node => node.getAttribute(name) === value); }
function byTag(root, tagName) { return walk(root).filter(node => node.tagName === tagName.toUpperCase()); }
function byClass(root, className) { return walk(root).filter(node => node.className.split(/\s+/).includes(className)); }
function visibleText(root) { return walk(root).map(node => node.textContent).filter(Boolean).join(" "); }
const mechanical = (key, label, formattedValue, extra = {}) => ({ key, label, effectiveValue: formattedValue, formattedValue, ...extra });

test("saving throw renderer accepts arbitrary three-save and six-save projections", () => {
    const three = renderSavingThrowsCard([
        mechanical("fort", "Fortitude", "+8"),
        mechanical("ref", "Reflex", "+5"),
        mechanical("will", "Will", "+7")
    ]);
    const six = renderSavingThrowsCard([
        "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"
    ].map((label, index) => mechanical(`save-${index}`, label, `+${index}`)));
    assert.equal(byAttribute(three, "data-mechanic-key", "fort").length, 1);
    assert.equal(byClass(three, "dd-saving-throw").length, 3);
    assert.equal(byClass(six, "dd-saving-throw").length, 6);
});

test("saving throw breakdown is progressive disclosure with arbitrary contribution labels", () => {
    const card = renderSavingThrowsCard([
        mechanical("fort", "Fortitude", "+9", {
            breakdown: [
                mechanical("progression", "Progression", "+5"),
                mechanical("ability", "Constitution", "+3"),
                mechanical("item", "Resistance item", "+1")
            ]
        })
    ]);
    assert.equal(byTag(card, "details").length, 1);
    assert.match(visibleText(card), /Progression/);
    assert.match(visibleText(card), /Resistance item/);
});

test("combat summary renders only supplied defenses and supports optional DR and spell resistance", () => {
    const primaryOnly = renderCombatMechanicsSummary({
        defenses: { primaryKey: "ac", values: [mechanical("ac", "Armor Class", "18")] }
    });
    assert.match(visibleText(primaryOnly), /Armor Class/);
    assert.doesNotMatch(visibleText(primaryOnly), /Touch|Flat-Footed/);

    const rich = renderCombatMechanicsSummary({
        defenses: {
            primaryKey: "ac",
            values: [
                mechanical("touch", "Touch", "13"),
                mechanical("ac", "Armor Class", "18"),
                mechanical("flat", "Flat-Footed", "15"),
                mechanical("dr", "Damage Reduction", "5 / magic"),
                mechanical("sr", "Spell Resistance", "17")
            ]
        }
    });
    assert.match(visibleText(rich), /Armor Class/);
    assert.match(visibleText(rich), /Touch/);
    assert.match(visibleText(rich), /Flat-Footed/);
    assert.match(visibleText(rich), /Damage Reduction/);
    assert.match(visibleText(rich), /Spell Resistance/);
});

test("combat summary keeps HP, temporary HP, and nonlethal damage independent", () => {
    const rendered = renderCombatMechanicsSummary({
        healthTracks: [
            { key: "hp", label: "Hit Points", role: "hit-points", current: 20, maximum: 30 },
            { key: "temp", label: "Temporary HP", role: "temporary-hit-points", current: 4 },
            { key: "nonlethal", label: "Nonlethal Damage", role: "nonlethal-damage", current: 6 }
        ]
    });
    assert.equal(byAttribute(rendered, "data-health-track-role", "temporary-hit-points").length, 1);
    assert.equal(byAttribute(rendered, "data-health-track-role", "nonlethal-damage").length, 1);
});

test("BAB, maneuver value, and Proficiency Bonus can coexist without equivalence logic", () => {
    const rendered = renderCombatMechanicsSummary({
        combatFundamentals: [
            mechanical("bab", "Base Attack Bonus", "+6/+1"),
            mechanical("grapple", "Grapple", "+10"),
            mechanical("proficiency", "Proficiency Bonus", "+3")
        ]
    });
    assert.match(visibleText(rendered), /Base Attack Bonus/);
    assert.match(visibleText(rendered), /Grapple/);
    assert.match(visibleText(rendered), /Proficiency Bonus/);
});

test("minimal attacks stay sparse while rich attacks expose optional critical, range, and ammunition fields", () => {
    const minimal = renderActionsPresentation([{ key: "simple", name: "Strike", damage: "1d8" }]);
    assert.doesNotMatch(visibleText(minimal), /Critical|Ammunition|Reach/);
    const rich = renderActionsPresentation([{
        key: "rich", name: "Longbow", actionType: "Attack",
        attackOrCheck: mechanical("attack", "Attack", "+8"),
        damage: "1d8+3", damageType: "piercing",
        criticalRange: "19-20", criticalMultiplier: "x3",
        range: "100 ft.", ammunition: "20 arrows"
    }]);
    assert.match(visibleText(rich), /Critical/);
    assert.match(visibleText(rich), /19-20 \/ x3/);
    assert.match(visibleText(rich), /Ammunition/);
});

test("procedure renderer accepts arbitrary component counts and displays only backend-supplied combined result", () => {
    const procedure = renderProcedure({
        key: "procedure", name: "Field Procedure",
        components: [0, 1, 2].map(index => ({
            key: `check-${index}`,
            name: `Step ${index + 1}`,
            effectiveModifierOrResult: mechanical(`mod-${index}`, "Modifier", `+${index + 2}`)
        })),
        result: { key: "result", label: "Outcome", value: "Backend result" }
    });
    assert.equal(byClass(procedure, "dd-check-card").length, 3);
    assert.match(visibleText(procedure), /Backend result/);
});

test("supplemental rule checks are collapsed while required creator credit remains visible", () => {
    const attribution = {
        key: "fixture-public",
        label: "Fixture Public Rules",
        detail: "Rules by Fixture Publisher · 5e · 2026-09-19",
        officialUrl: "https://example.test/public-rules",
        linkLabel: "Official rules",
        presentationRequired: true
    };
    const rendered = renderChecksAndProceduresPresentation(
        [
            { key: "core", name: "Core Check" },
            { key: "external", name: "External Check", supplemental: true, sourceAttributions: [attribution] }
        ],
        [{
            key: "external-procedure",
            name: "External Procedure",
            components: [],
            supplemental: true,
            sourceAttributions: [attribution]
        }]
    );

    const disclosure = byClass(rendered, "dd-check-procedure-presentation__supplemental")[0];
    const credit = byClass(rendered, "dd-check-procedure-presentation__supplemental-credit")[0];
    assert.ok(disclosure);
    assert.equal(disclosure.tagName, "DETAILS");
    assert.ok(credit);
    assert.equal(walk(disclosure).includes(credit), false);
    assert.match(visibleText(disclosure), /External Check/);
    assert.match(visibleText(disclosure), /External Procedure/);
    assert.doesNotMatch(visibleText(disclosure), /Core Check/);
    assert.match(visibleText(credit), /Fixture Public Rules/);
    assert.match(visibleText(credit), /Rules by Fixture Publisher/);
    const links = byTag(credit, "a");
    assert.equal(links.length, 1);
    assert.equal(links[0].href, "https://example.test/public-rules");
    assert.equal(links[0].textContent, "Official rules");
});

test("source attribution renders the official link supplied by data and has no source-specific assumption", async () => {
    const rendered = renderSourceAttributions([{
        key: "external", label: "External rules publisher",
        officialUrl: "https://example.test/official", linkLabel: "Official rules"
    }]);
    const links = byTag(rendered, "a");
    assert.equal(links.length, 1);
    assert.equal(links[0].href, "https://example.test/official");
    assert.equal(links[0].textContent, "Official rules");

    const genericSource = await readFile(new URL("../src/ui/source-attribution.ts", import.meta.url), "utf8");
    const mechanicsSource = await readFile(new URL("../src/ui/mechanics-components.ts", import.meta.url), "utf8");
    const procedureSource = await readFile(new URL("../src/ui/procedure-components.ts", import.meta.url), "utf8");
    assert.doesNotMatch(genericSource + mechanicsSource + procedureSource, /Loot Tavern|Harvest Assessment|Carving/i);
});


test("health track uses the standard mechanic value label and value classes", () => {
    const rendered = renderCombatMechanicsSummary({
        healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", current: 7, maximum: 12 }]
    });
    assert.equal(byClass(rendered, "dd-mechanic-value__label").length, 1);
    assert.equal(byClass(rendered, "dd-mechanic-value__value").length, 1);
    assert.equal(byClass(rendered, "dd-mechanic-value-_label").length, 0);
    assert.equal(byClass(rendered, "dd-mechanic-value-_value").length, 0);
});

test("source attribution only links valid HTTPS external URLs and retains invalid attribution text", () => {
    assert.equal(getSafeExternalSourceUrl("https://example.test/rules"), "https://example.test/rules");
    assert.equal(getSafeExternalSourceUrl("http://example.test/rules"), null);
    assert.equal(getSafeExternalSourceUrl("javascript:alert(1)"), null);
    assert.equal(getSafeExternalSourceUrl("not a url"), null);

    const rendered = renderSourceAttributions([
        { key: "good", label: "Safe source", officialUrl: "https://example.test/rules" },
        { key: "script", label: "Unsafe source", detail: "Attribution remains", officialUrl: "javascript:alert(1)" },
        { key: "bad", label: "Malformed source", officialUrl: "not a url" }
    ]);
    assert.equal(byTag(rendered, "a").length, 1);
    assert.match(visibleText(rendered), /Safe source/);
    assert.match(visibleText(rendered), /Unsafe source/);
    assert.match(visibleText(rendered), /Attribution remains/);
    assert.match(visibleText(rendered), /Malformed source/);
});
