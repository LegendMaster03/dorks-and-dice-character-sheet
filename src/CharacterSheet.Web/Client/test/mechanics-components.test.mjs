import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
    adjustCurrentHitPoints,
    findInitiativeValue,
    renderActionsPresentation,
    renderArmorClassQuickCard,
    renderCombatMechanicsSummary,
    renderDefenseMechanicsCard,
    renderHealthMechanicsCard,
    renderMovementValues,
    renderQuickMechanicalValue,
    renderRecoveryControls,
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

test("Armor Class quick card promotes primary AC and keeps touch and flat-footed as subordinate variants", () => {
    const rendered = renderArmorClassQuickCard({
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac", "Armor Class", "18"),
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "15"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5 / magic")
            ]
        }
    });
    assert.equal(rendered.getAttribute("data-armor-class-card"), "true");
    assert.equal(byClass(rendered, "dd-split-stat").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "defense.ac").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "defense.ac.touch").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "defense.ac.flat-footed").length, 1);
    assert.match(visibleText(rendered), /Armor Class/);
    assert.match(visibleText(rendered), /Touch AC/);
    assert.match(visibleText(rendered), /Flat-Footed AC/);
    assert.match(visibleText(rendered), /18/);
    assert.match(visibleText(rendered), /13/);
    assert.match(visibleText(rendered), /15/);
});

test("Defense card excludes the promoted AC trio while retaining other defensive mechanics", () => {
    const rendered = renderDefenseMechanicsCard({
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac", "Armor Class", "18"),
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "15"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5 / magic"),
                mechanical("defense.spell-resistance", "Spell Resistance", "17")
            ]
        }
    });
    assert.doesNotMatch(visibleText(rendered), /Armor Class|Touch AC|Flat-Footed/);
    assert.match(visibleText(rendered), /Damage Reduction/);
    assert.match(visibleText(rendered), /Spell Resistance/);
});

test("combat summary preserves named defense surfaces when values are missing", () => {
    const primaryOnly = renderCombatMechanicsSummary({
        defenses: { primaryKey: "ac", values: [mechanical("ac", "Armor Class", "18")] }
    });
    const text = visibleText(primaryOnly);
    assert.match(text, /Armor Class/);
    assert.match(text, /Touch Armor Class/);
    assert.match(text, /Flat-Footed Armor Class/);
    assert.match(text, /Damage Reduction/);
    assert.match(text, /Spell Resistance/);
    assert.equal(byAttribute(primaryOnly, "data-sheet-scaffold-key", "touch-armor-class").length, 1);
});

test("null mechanics projection retains the full named combat scaffold with neutral dashes", () => {
    const rendered = renderCombatMechanicsSummary(null);
    const text = visibleText(rendered);
    assert.equal(rendered.getAttribute("data-combat-mechanics-state"), "unavailable");
    for (const label of [
        "Armor Class",
        "Touch Armor Class",
        "Flat-Footed Armor Class",
        "Damage Reduction",
        "Spell Resistance",
        "Fortitude Save",
        "Reflex Save",
        "Will Save",
        "Hit Points",
        "Nonlethal Damage",
        "Base Attack Bonus",
        "Grapple Modifier"
    ]) {
        assert.ok(text.includes(label), label);
    }
    assert.ok(byClass(rendered, "dd-mechanic-value--scaffold").length >= 12);
});

test("combat summary groups defenses and saving throws using compact 3.x-style relationships", () => {
    const rendered = renderCombatMechanicsSummary({
        defenses: {
            primaryKey: "ac",
            values: [
                mechanical("ac", "Armor Class", "18"),
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "15"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5 / magic"),
                mechanical("defense.spell-resistance", "Spell Resistance", "17")
            ]
        },
        savingThrows: [
            mechanical("save.fortitude", "Fortitude Save", "+8"),
            mechanical("save.reflex", "Reflex Save", "+5"),
            mechanical("save.will", "Will Save", "+7")
        ]
    });
    assert.equal(byAttribute(rendered, "data-combat-group", "defense").length, 1);
    assert.equal(byAttribute(rendered, "data-combat-group", "saves").length, 1);
    assert.equal(byClass(rendered, "dd-saving-throw").length, 3);
});

test("movement presentation gives all five standard speeds fixed in-card slots", () => {
    const rendered = renderMovementValues([
        mechanical("movement.fly", "Fly", "60 ft."),
        mechanical("movement.walk", "Walk", "30 ft."),
        mechanical("movement.swim", "Swim", "20 ft."),
        mechanical("movement.climb", "Climb", "15 ft."),
        mechanical("movement.burrow", "Burrow", "10 ft.")
    ]);

    assert.equal(rendered.getAttribute("data-movement-state"), "resolved");
    const primary = byAttribute(rendered, "data-movement-primary", "movement.walk");
    assert.equal(primary.length, 1);
    assert.match(visibleText(primary[0]), /Walk\s+30 ft\./);
    assert.equal(byAttribute(rendered, "data-movement-mode", "burrow").length, 1);
    assert.equal(byAttribute(rendered, "data-movement-mode", "climb").length, 1);
    assert.equal(byAttribute(rendered, "data-movement-mode", "fly").length, 1);
    assert.equal(byAttribute(rendered, "data-movement-mode", "swim").length, 1);
    assert.match(visibleText(rendered), /Burrow\s+10 ft\./);
    assert.match(visibleText(rendered), /Climb\s+15 ft\./);
    assert.match(visibleText(rendered), /Fly\s+60 ft\./);
    assert.match(visibleText(rendered), /Swim\s+20 ft\./);
    assert.equal(byTag(rendered, "details").length, 0);
});

test("movement presentation keeps unresolved standard speeds visible without inventing values", () => {
    const rendered = renderMovementValues([
        mechanical("movement.fly", "Fly", "40 ft.")
    ]);

    assert.equal(byAttribute(rendered, "data-movement-primary", "movement.walk").length, 1);
    for (const mode of ["burrow", "climb", "fly", "swim"]) {
        assert.equal(byAttribute(rendered, "data-movement-mode", mode).length, 1);
    }
    assert.match(visibleText(rendered), /Walk\s+-/);
    assert.match(visibleText(rendered), /Burrow\s+-/);
    assert.match(visibleText(rendered), /Climb\s+-/);
    assert.match(visibleText(rendered), /Fly\s+40 ft\./);
    assert.match(visibleText(rendered), /Swim\s+-/);
});

test("nonstandard movement modes remain available in an additional-movement disclosure", () => {
    const rendered = renderMovementValues([
        mechanical("movement.walk", "Walk", "30 ft."),
        mechanical("movement.phase", "Phase", "15 ft."),
        mechanical("movement.glide", "Glide", "45 ft.")
    ]);

    const details = byTag(rendered, "details");
    assert.equal(details.length, 1);
    assert.match(visibleText(details[0]), /Additional movement/);
    assert.equal(byAttribute(details[0], "data-mechanic-key", "movement.phase").length, 1);
    assert.equal(byAttribute(details[0], "data-mechanic-key", "movement.glide").length, 1);
    assert.match(visibleText(details[0]), /Phase\s+15 ft\./);
    assert.match(visibleText(details[0]), /Glide\s+45 ft\./);
    assert.equal(byAttribute(details[0], "data-movement-mode", "burrow").length, 0);
});
test("hit point adjustment reuses the combat tracker behavior without imposing a 5e zero floor", () => {
    assert.equal(adjustCurrentHitPoints(12, 30, 5, -1), 7);
    assert.equal(adjustCurrentHitPoints(5, 30, 12, -1), -7);
    assert.equal(adjustCurrentHitPoints(27, 30, 8, 1), 30);
    assert.equal(adjustCurrentHitPoints(null, 30, 5, -1), null);
});

test("editable Hit Points card exposes direct and modifier controls while keeping max read-only", () => {
    const saved = [];
    const rendered = renderHealthMechanicsCard({
        healthTracks: [
            { key: "hp", label: "Hit Points", role: "hit-points", maximum: 30 }
        ]
    }, {
        currentHitPoints: 12,
        onSetCurrentHitPoints: value => saved.push(value)
    });

    assert.equal(byAttribute(rendered, "data-health-editor", "true").length, 1);
    assert.match(visibleText(rendered), /Current\s+12/);
    assert.match(visibleText(rendered), /Maximum\s+30/);
    assert.match(visibleText(rendered), /Adjust HP/);
    assert.match(visibleText(rendered), /Max HP\s+30/);

    const inputs = byClass(rendered, "dd-health-editor__input");
    const buttons = action => byAttribute(rendered, "data-health-action", action);
    inputs[1].value = "15";
    buttons("subtract")[0].onclick();
    assert.equal(saved.at(-1), -3);

    inputs[1].value = "50";
    buttons("add")[0].onclick();
    assert.equal(saved.at(-1), 30);

    inputs[0].value = "-8";
    buttons("set")[0].onclick();
    assert.equal(saved.at(-1), -8);
});

test("recovery controls render nothing when Rules Core supplies no procedures", () => {
    assert.equal(renderRecoveryControls(undefined, false, false, undefined), null);
    assert.equal(renderRecoveryControls([], false, false, undefined), null);
});

test("recovery controls use Rules Core procedure identity, labels, roles, and applicability", () => {
    const calls = [];
    const rendered = renderRecoveryControls([
        {
            procedureKey: "recovery.short.fixture",
            displayName: "Take a Breather",
            presentationRole: "short-rest",
            applicabilityState: "applicable"
        },
        {
            procedureKey: "recovery.long.fixture",
            displayName: "Full Recovery",
            presentationRole: "long-rest",
            applicabilityState: "not-applicable"
        }
    ], false, false, key => calls.push(key));
    assert.ok(rendered);

    const shortRest = byAttribute(rendered, "data-recovery-procedure", "recovery.short.fixture")[0];
    const longRest = byAttribute(rendered, "data-recovery-procedure", "recovery.long.fixture")[0];
    assert.ok(shortRest);
    assert.ok(longRest);
    assert.equal(shortRest.textContent, "Take a Breather");
    assert.equal(longRest.textContent, "Full Recovery");
    assert.equal(shortRest.getAttribute("data-rest-action"), "short");
    assert.equal(longRest.getAttribute("data-rest-action"), "long");
    assert.equal(shortRest.disabled, false);
    assert.equal(longRest.disabled, true);

    shortRest.onclick();
    assert.deepEqual(calls, ["recovery.short.fixture"]);
});

test("read-only Hit Points card omits mutation controls", () => {
    const rendered = renderHealthMechanicsCard(null, {
        currentHitPoints: 10,
        readOnly: true,
        onSetCurrentHitPoints() {}
    });
    assert.equal(byAttribute(rendered, "data-health-editor", "true").length, 0);
    assert.match(visibleText(rendered), /Current\s+10/);
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

test("Initiative is promoted to the quick-stat surface and omitted from the lower combat group", () => {
    const values = [
        mechanical("combat.base-attack-bonus", "Base Attack Bonus", "+6/+1"),
        mechanical("combat.initiative", "Initiative", "+4")
    ];
    const initiative = findInitiativeValue(values);
    assert.ok(initiative);
    assert.equal(initiative.key, "combat.initiative");

    const quick = renderQuickMechanicalValue(initiative);
    assert.equal(byAttribute(quick, "data-mechanic-key", "combat.initiative").length, 1);
    assert.match(visibleText(quick), /\+4/);

    const summary = renderCombatMechanicsSummary({ combatFundamentals: values });
    assert.match(visibleText(summary), /Base Attack Bonus/);
    assert.doesNotMatch(visibleText(summary), /Initiative/);
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

test("supplemental rules use a source-identified compact disclosure with one creator credit", () => {
    const attribution = {
        key: "fixture-public",
        label: "Fixture Public Rules",
        detail: "Rules by Fixture Publisher · 5e · 2026-09-19",
        officialUrl: "https://example.test/public-rules",
        linkLabel: "Official rules",
        presentationRequired: true
    };
    const sharedCheck = {
        key: "external",
        name: "External Check",
        supplemental: true,
        sourceAttributions: [attribution]
    };
    const rendered = renderChecksAndProceduresPresentation(
        [
            { key: "core", name: "Core Check" },
            sharedCheck
        ],
        [{
            key: "external-procedure",
            name: "External Procedure",
            components: [sharedCheck],
            supplemental: true,
            sourceAttributions: [attribution]
        }]
    );

    const disclosure = byClass(rendered, "dd-check-procedure-presentation__supplemental")[0];
    const credit = byClass(rendered, "dd-check-procedure-presentation__supplemental-credit")[0];
    assert.ok(disclosure);
    assert.equal(disclosure.tagName, "DETAILS");
    assert.ok(credit);
    assert.equal(walk(disclosure).includes(credit), true);
    assert.match(
        visibleText(disclosure),
        /Fixture Public Rules · Fixture Publisher — checks & procedures/);
    assert.match(visibleText(disclosure), /External Procedure/);
    assert.doesNotMatch(visibleText(disclosure), /Core Check/);

    const externalChecks = byAttribute(disclosure, "data-check-key", "external");
    assert.equal(externalChecks.length, 1);
    assert.equal(byClass(disclosure, "dd-check-card--compact").length, 1);
    assert.equal(byClass(disclosure, "dd-procedure-card--compact").length, 1);

    assert.match(visibleText(credit), /Fixture Public Rules/);
    assert.match(visibleText(credit), /Rules by Fixture Publisher/);
    const allSourceBlocks = byClass(disclosure, "dd-source-attributions");
    assert.equal(allSourceBlocks.length, 1);
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
    const hp = byAttribute(rendered, "data-health-track-key", "hp")[0];
    assert.ok(hp);
    assert.equal(byClass(hp, "dd-mechanic-value__label").length, 1);
    assert.equal(byClass(hp, "dd-mechanic-value__value").length, 1);
    assert.equal(byClass(hp, "dd-mechanic-value-_label").length, 0);
    assert.equal(byClass(hp, "dd-mechanic-value-_value").length, 0);
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
