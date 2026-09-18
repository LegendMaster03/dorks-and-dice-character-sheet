import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { renderCharacterWorkspace } from "../.test-dist/ui/sheet.js";

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
        this.type = "";
        this.disabled = false;
        this.value = "";
        this.placeholder = "";
        this.inputMode = "";
        this.autocomplete = "";
        this.rows = 0;
        this.maxLength = 0;
        this.href = "";
        this.target = "";
        this.rel = "";
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
    addEventListener() {}
    setCustomValidity() {}
    reportValidity() { return true; }
}
globalThis.document = { createElement: tagName => new FakeElement(tagName) };
globalThis.window = { confirm: () => true };

function walk(root) {
    const nodes = [root];
    for (const child of root.children) nodes.push(...walk(child));
    return nodes;
}
function byAttribute(root, name, value) { return walk(root).filter(node => node.getAttribute(name) === value); }
function byClass(root, className) { return walk(root).filter(node => node.className.split(/\s+/).includes(className)); }
function visibleText(root) { return walk(root).map(node => node.textContent).filter(Boolean).join(" "); }

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const character = {
    characterId,
    name: "Projection Test",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: [],
    hasRichSheet: true,
    sheet: { schemaVersion: 1, builderStatus: "BuildInProgress", createdAt: "now", updatedAt: "now" }
};
const builder = {
    status: "idle",
    build: null,
    references: {
        raceSpecies: { status: "none" },
        startingClass: { status: "none" },
        subclass: { status: "none" }
    },
    chooser: { kind: "closed" },
    saving: null,
    savingAbility: null,
    featReferences: {},
    featChooser: { kind: "closed" },
    savingFeat: null
};
const guidedBuilder = { open: false, activeSection: "species", returnSheetMode: "view" };
const handlers = {
    structural: {
        openChooser() {}, clearChoice() {}, submitChooserSearch() {}, closeChooser() {}, saveChoice() {},
        setBaseAbilityScore() {}, clearBaseAbilityScore() {}
    },
    feats: { openChooser() {}, closeChooser() {}, search() {}, add() {}, remove() {} },
    routine: {
        addNote() {}, updateNote() {}, deleteNote() {}, openInventoryChooser() {}, closeInventoryChooser() {},
        searchInventory() {}, addInventoryItem() {}, removeInventoryItem() {}
    },
    selectSection() {}, enterEditMode() {}, leaveEditMode() {}, openGuidedBuilder() {},
    closeGuidedBuilder() {}, selectGuidedBuilderSection() {}
};
const routine = (occurrences = [], references = {}) => ({
    status: "ready",
    state: { characterId, readOnly: true, inventoryItemOccurrences: occurrences, notes: [] },
    references,
    inventoryChooser: { kind: "closed" },
    mutation: null
});
const mechanical = (key, label, formattedValue, extra = {}) => ({
    key, label, effectiveValue: formattedValue, formattedValue, ...extra
});
const resolvedItem = (conceptKey, displayName) => ({
    status: "resolved",
    conceptKey,
    rule: {
        ruleConceptId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        conceptKey,
        entityType: "item",
        displayName,
        sourceEntityName: displayName,
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    }
});
function render(section, mechanics, currentRoutine = routine()) {
    return renderCharacterWorkspace(
        character, builder, currentRoutine, section, true, "view", guidedBuilder,
        null, mechanics, handlers
    );
}

test("workspace consumes supplied saving throws, competencies, combat, actions, movement, checks, and procedures", () => {
    const mechanics = {
        savingThrows: [mechanical("fort", "Fortitude", "+8")],
        competencies: { entries: [mechanical("listen", "Listen", "+7", { kind: "skill" })] },
        defenses: { primaryKey: "ac", values: [mechanical("ac", "Armor Class", "18")] },
        healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", current: 20, maximum: 30 }],
        combatFundamentals: [mechanical("bab", "Base Attack Bonus", "+6/+1")],
        movement: [mechanical("walk", "Walk", "30 ft.")],
        actions: [{ key: "sword", name: "Longsword", attackOrCheck: mechanical("attack", "Attack", "+8") }],
        checks: [{ key: "assessment", name: "Assessment", effectiveModifierOrResult: mechanical("check", "Modifier", "+5") }],
        procedures: [{ key: "field", name: "Field Procedure", components: [] }]
    };
    const rendered = render("actions", mechanics);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "fort").length, 1);
    assert.equal(byAttribute(rendered, "data-skill-id", "listen").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "ac").length, 1);
    assert.equal(byAttribute(rendered, "data-health-track-key", "hp").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "bab").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "walk").length, 1);
    assert.equal(byAttribute(rendered, "data-action-key", "sword").length, 1);
    assert.equal(byAttribute(rendered, "data-check-key", "assessment").length, 1);
    assert.equal(byAttribute(rendered, "data-procedure-key", "field").length, 1);
});

test("null mechanics projection renders honest unavailable states rather than manufactured values", () => {
    const rendered = render("actions", null);
    const text = visibleText(rendered);
    assert.match(text, /Saving throw mechanics are not available/);
    assert.match(text, /Resolved competencies are not available/);
    assert.match(text, /Combat mechanics are not available/);
    assert.match(text, /Movement mechanics are not available/);
    assert.match(text, /Resolved actions and attacks are not available/);
    assert.match(text, /Resolved checks and procedures are not available/);
    assert.doesNotMatch(text, /Fortitude|Armor Class 18|Base Attack Bonus \+6\/\+1/);
});

test("Inventory joins mechanics by stable occurrence identity and keeps duplicate concepts independent", () => {
    const occurrences = [
        { id: "item-one", ruleConceptKey: "item:rope", createdAt: "now" },
        { id: "item-two", ruleConceptKey: "item:rope", createdAt: "now" }
    ];
    const references = {
        "item-one": resolvedItem("item:rope", "Rope"),
        "item-two": resolvedItem("item:rope", "Rope")
    };
    const mechanics = {
        inventory: {
            itemOccurrences: [
                { occurrenceId: "item-one", facts: [{ key: "marker", label: "State", value: "First occurrence mechanics" }] },
                { occurrenceId: "item-two", facts: [{ key: "marker", label: "State", value: "Second occurrence mechanics" }] }
            ]
        }
    };
    const rendered = render("inventory", mechanics, routine(occurrences, references));
    const first = byAttribute(rendered, "data-inventory-occurrence-id", "item-one")[0];
    const second = byAttribute(rendered, "data-inventory-occurrence-id", "item-two")[0];
    assert.match(visibleText(first), /First occurrence mechanics/);
    assert.doesNotMatch(visibleText(first), /Second occurrence mechanics/);
    assert.match(visibleText(second), /Second occurrence mechanics/);
    assert.doesNotMatch(visibleText(second), /First occurrence mechanics/);
});

test("Inventory carrying, components, procedures, and crafting render only when supplied", () => {
    const populated = render("inventory", {
        inventory: {
            carrying: {
                carried: { key: "carried", label: "Carried", value: "42 lb." },
                thresholds: [{ key: "heavy", label: "Heavy", value: "67-100 lb." }]
            },
            components: [{ key: "component-a", label: "Recovered Component", quantity: "2" }],
            procedures: [{ key: "procedure-a", name: "General Procedure", components: [] }],
            crafting: [{ key: "craft-a", name: "Craft Item", progress: { key: "progress", label: "Progress", value: "1 / 3" } }]
        }
    });
    assert.equal(byClass(populated, "dd-inventory-mechanics").length, 1);
    assert.equal(byClass(populated, "dd-component-card").length, 1);
    assert.equal(byAttribute(populated, "data-procedure-key", "procedure-a").length, 1);
    assert.equal(byAttribute(populated, "data-crafting-key", "craft-a").length, 1);
    assert.match(visibleText(populated), /42 lb/);

    const sparse = render("inventory", { inventory: { itemOccurrences: [] } });
    assert.equal(byClass(sparse, "dd-component-card").length, 0);
    assert.equal(byClass(sparse, "dd-crafting-card").length, 0);
    assert.doesNotMatch(visibleText(sparse), /Carrying & Load/);
});

test("spellcasting profiles render only when supplied", () => {
    const supplied = render("spells", {
        spellcastingProfiles: [{
            key: "wizard",
            label: "Wizard Spellcasting",
            castingAbility: "Intelligence",
            saveDc: mechanical("dc", "Save DC", "16")
        }]
    });
    assert.equal(byAttribute(supplied, "data-spellcasting-profile-key", "wizard").length, 1);
    assert.match(visibleText(supplied), /Wizard Spellcasting/);

    const unavailable = render("spells", null);
    assert.match(visibleText(unavailable), /Resolved spellcasting profiles are not available/);
    assert.equal(byClass(unavailable, "dd-spellcasting-profile").length, 0);
});

test("production passes nullable advancement and mechanics projections without fixture substitution", async () => {
    const source = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
    assert.match(source, /state\.guidedBuilder,\s*null,\s*null,\s*\{/s);
});

test("generalized presentation contains no source-specific Loot Tavern formula or prose", async () => {
    const sources = await Promise.all([
        "character-mechanics.ts", "mechanics-components.ts", "procedure-components.ts",
        "source-attribution.ts", "sheet.ts"
    ].map(name => readFile(new URL(`../src/ui/${name}`, import.meta.url), "utf8")));
    assert.doesNotMatch(sources.join("\n"), /Loot Tavern|Harvest Assessment|Carving/i);
});

test("generalized mechanics styles use host semantic tokens and include mobile layout rules", async () => {
    const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
    assert.match(css, /\.dd-mechanic-value/);
    assert.match(css, /\.dd-source-attribution/);
    assert.match(css, /\.dd-procedure-card/);
    assert.match(css, /\.dd-crafting-card/);
    assert.match(css, /\.dd-spellcasting-profile/);
    assert.match(css, /@media \(max-width: 720px\)[\s\S]*\.dd-action-list/s);
    const marker = "/* Generalized Character mechanics presentation */";
    const start = css.indexOf(marker);
    assert.notEqual(start, -1);
    const mechanicsCss = css.slice(start);
    assert.match(mechanicsCss, /var\(--dd-sheet-/);
    assert.doesNotMatch(mechanicsCss, /#[0-9a-f]{3,8}\b|rgba?\s*\(|hsla?\s*\(/i);
});
