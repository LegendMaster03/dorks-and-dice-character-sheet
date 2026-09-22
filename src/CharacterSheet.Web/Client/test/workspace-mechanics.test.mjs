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
        this.tabIndex = 0;
        this.listeners = new Map();
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }
    dispatchEvent(event) {
        for (const listener of this.listeners.get(event.type) ?? []) listener(event);
        return true;
    }
    focus() { this.focused = true; }
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
function byTag(root, tagName) { return walk(root).filter(node => node.tagName === tagName.toUpperCase()); }
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
        setCurrentHitPoints() {},
        addNote() {}, updateNote() {}, deleteNote() {}, openInventoryChooser() {}, closeInventoryChooser() {},
        searchInventory() {}, addInventoryItem() {}, removeInventoryItem() {}
    },
    selectSection() {}, enterEditMode() {}, leaveEditMode() {}, openGuidedBuilder() {},
    closeGuidedBuilder() {}, selectGuidedBuilderSection() {}
};
const routine = (occurrences = [], references = {}, readOnly = true, currentHitPoints = null) => ({
    status: "ready",
    state: { characterId, readOnly, currentHitPoints, inventoryItemOccurrences: occurrences, notes: [] },
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

test("primary sheet sections expose tab and tabpanel semantics", () => {
    const rendered = render("actions", null);
    const nav = byClass(rendered, "dd-primary-nav")[0];
    assert.ok(nav);
    assert.equal(nav.getAttribute("role"), "tablist");

    const tabs = byClass(nav, "dd-primary-nav__button");
    assert.equal(tabs.length, 5);
    const selected = tabs.filter(tab => tab.getAttribute("aria-selected") === "true");
    assert.equal(selected.length, 1);
    assert.equal(selected[0].id, "dd-sheet-tab-actions");
    assert.equal(selected[0].getAttribute("role"), "tab");

    const panel = byAttribute(rendered, "data-sheet-section", "actions")[0];
    assert.ok(panel);
    assert.equal(panel.getAttribute("role"), "tabpanel");
    assert.equal(panel.getAttribute("aria-labelledby"), "dd-sheet-tab-actions");
});

test("primary sheet tabs use roving tabindex and horizontal arrow-key selection", () => {
    let selectedSection = null;
    const keyboardHandlers = {
        ...handlers,
        selectSection(section) { selectedSection = section; }
    };
    const rendered = renderCharacterWorkspace(
        character, builder, routine(), "actions", true, "view", guidedBuilder,
        null, null, keyboardHandlers
    );
    const nav = byClass(rendered, "dd-primary-nav")[0];
    const tabs = byClass(nav, "dd-primary-nav__button");

    assert.equal(nav.getAttribute("aria-orientation"), "horizontal");
    assert.equal(tabs[0].tabIndex, 0);
    assert.deepEqual(tabs.slice(1).map(tab => tab.tabIndex), [-1, -1, -1, -1]);
    assert.equal(tabs[3].textContent, "Features & Traits");
    assert.equal(tabs[3].getAttribute("data-sheet-section-tab"), "features");

    let prevented = false;
    nav.dispatchEvent({
        type: "keydown",
        key: "ArrowRight",
        target: tabs[0],
        preventDefault() { prevented = true; }
    });
    assert.equal(prevented, true);
    assert.equal(selectedSection, "spells");

    nav.dispatchEvent({
        type: "keydown",
        key: "End",
        target: tabs[0],
        preventDefault() {}
    });
    assert.equal(selectedSection, "notes");
});

test("section-tab CSS preserves long labels and delegates narrow overflow to the strip", async () => {
    const css = (await Promise.all([
        "../src/styles/foundation.css",
        "../src/styles/builder.css",
        "../src/styles/advancement.css",
        "../src/styles/mechanics.css",
        "../src/styles/abilities.css",
        "../src/styles/supplemental.css"
    ].map(path => readFile(new URL(path, import.meta.url), "utf8")))).join("\n");
    const navBlock = css.match(/\.dd-primary-nav\s*\{[\s\S]*?\n\}/)?.[0] ?? "";
    const buttonBlock = css.match(/\.dd-primary-nav__button\s*\{[\s\S]*?\n\}/)?.[0] ?? "";

    assert.match(navBlock, /overflow-x:\s*auto/);
    assert.match(navBlock, /scrollbar-width:\s*thin/);
    assert.match(buttonBlock, /flex:\s*1 0 auto/);
    assert.match(buttonBlock, /min-width:\s*max-content/);
    assert.doesNotMatch(buttonBlock, /min-width:\s*0/);
    assert.match(buttonBlock, /border-inline-end:/);
});

test("player-facing setup copy avoids architecture-first terminology", async () => {
    const sources = await Promise.all([
        "../src/app.ts",
        "../src/ui/sheet.ts",
        "../src/ui/builder.ts",
        "../src/ui/sheet-model.ts",
        "../src/ui/primary-content.ts",
        "../src/features/features/features-section.ts",
        "../src/features/inventory/inventory-section.ts",
        "../src/features/notes/notes-section.ts"
    ].map(path => readFile(new URL(path, import.meta.url), "utf8")));
    const copy = sources.join("\n");

    assert.doesNotMatch(copy, /Site-owned Character|digital build state|canonical Character identity/i);
    assert.doesNotMatch(copy, /backend-supported base Ability Score|current backend can determine/i);
    assert.doesNotMatch(copy, /normalized ordinary-Character feature\/effect consumer contract/i);
    assert.doesNotMatch(copy, /Character-owned|Rules Core (?:catalog|Feat|item)|No (?:Feat|item) occurrences/i);
});

test("Ability cards pair effective score, modifier, and matching Ability save", () => {
    const rendered = render("actions", {
        abilityValues: [
            mechanical("strength", "Strength", "18", {
                relatedValues: [
                    { key: "modifier", label: "Modifier", effectiveValue: "+4", formattedValue: "+4" }
                ]
            })
        ],
        savingThrows: [
            mechanical("save.strength", "Strength Save", "+6", { governingAbility: "strength" })
        ]
    });

    const strength = byAttribute(rendered, "data-ability-key", "strength")[0];
    assert.ok(strength);
    assert.equal(byClass(strength, "dd-split-stat").length, 1);
    assert.equal(byAttribute(strength, "data-ability-modifier", "strength").length, 1);
    assert.equal(byAttribute(strength, "data-ability-save", "strength").length, 1);
    assert.equal(byAttribute(strength, "data-saving-throw-key", "save.strength").length, 1);
    assert.match(visibleText(strength), /Strength/);
    assert.match(visibleText(strength), /Modifier\s+\+4/);
    assert.match(visibleText(strength), /Save\s+\+6/);
});

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
    assert.match(visibleText(rendered), /Current\s+20/);
    assert.match(visibleText(rendered), /Max\s+30/);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "bab").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "walk").length, 1);
    assert.equal(byAttribute(rendered, "data-action-key", "sword").length, 1);
    assert.equal(byAttribute(rendered, "data-check-key", "assessment").length, 1);
    assert.equal(byAttribute(rendered, "data-procedure-key", "field").length, 1);
});

test("rest actions live in the top control bar rather than the Hit Points card", () => {
    const editableBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [],
            progressionEntries: []
        }
    };
    const activeRoutine = routine([], {}, false, 10);
    const rendered = renderCharacterWorkspace(
        character,
        editableBuilder,
        activeRoutine,
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        { healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", maximum: 20 }] },
        {
            ...handlers,
            routine: {
                ...handlers.routine,
                rest() {}
            }
        }
    );

    const modeBar = byClass(rendered, "dd-sheet-mode-bar")[0];
    const healthCard = byClass(rendered, "dd-health-quick")[0];
    assert.ok(modeBar);
    assert.ok(healthCard);
    assert.equal(byAttribute(modeBar, "data-rest-action", "short").length, 1);
    assert.equal(byAttribute(modeBar, "data-rest-action", "long").length, 1);
    assert.equal(byAttribute(healthCard, "data-rest-action", "short").length, 0);
    assert.equal(byAttribute(healthCard, "data-rest-action", "long").length, 0);
});

test("Character-owned current HP overrides mechanics presentation and is editable for active Characters", () => {
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        routine([], {}, false, -2),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        {
            healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", current: 99, maximum: 30 }]
        },
        handlers
    );

    const card = byClass(rendered, "dd-health-quick")[0];
    assert.ok(card);
    assert.match(visibleText(card), /Current\s+-2/);
    assert.doesNotMatch(visibleText(card), /Current\s+99/);
    assert.equal(byAttribute(card, "data-health-editor", "true").length, 1);
});

test("top-row Hit Points card keeps current, max, temporary, and nonlethal values dense and distinct", () => {
    const rendered = render("actions", {
        healthTracks: [
            { key: "hp", label: "Hit Points", role: "hit-points", current: 21, maximum: 30 },
            { key: "temp", label: "Temporary HP", role: "temporary-hit-points", current: 5 },
            { key: "nonlethal", label: "Nonlethal Damage", role: "nonlethal-damage", current: 3 }
        ]
    });
    const card = byClass(rendered, "dd-health-quick")[0];
    assert.ok(card);
    assert.match(visibleText(card), /Current\s+21/);
    assert.match(visibleText(card), /Max\s+30/);
    assert.match(visibleText(card), /Temporary HP\s+5/);
    assert.match(visibleText(card), /Nonlethal Damage\s+3/);
    assert.equal(byAttribute(card, "data-health-track-key", "hp").length, 2);
    assert.equal(byAttribute(card, "data-health-track-key", "temp").length, 1);
    assert.equal(byAttribute(card, "data-health-track-key", "nonlethal").length, 1);
});

test("workspace promotes Armor Class beside Movement and Initiative and removes the AC trio from Defense", () => {
    const rendered = render("actions", {
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac", "Armor Class", "18"),
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "15"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5 / magic"),
                mechanical("defense.spell-resistance", "Spell Resistance", "17")
            ]
        },
        combatFundamentals: [mechanical("combat.initiative", "Initiative", "+4")]
    });

    const acCard = byAttribute(rendered, "data-armor-class-card", "true")[0];
    assert.ok(acCard);
    assert.match(visibleText(acCard), /Armor Class/);
    assert.match(visibleText(acCard), /Touch AC/);
    assert.match(visibleText(acCard), /Flat-Footed AC/);
    assert.match(visibleText(acCard), /18/);
    assert.match(visibleText(acCard), /13/);
    assert.match(visibleText(acCard), /15/);

    const defense = byClass(rendered, "dd-defense-card")[0];
    assert.ok(defense);
    assert.doesNotMatch(visibleText(defense), /Armor Class|Touch AC|Flat-Footed/);
    assert.match(visibleText(defense), /Damage Reduction/);
    assert.match(visibleText(defense), /Spell Resistance/);
});

test("Defense and Combat share a compact summary row above Skills", () => {
    const rendered = render("actions", null);
    const summaries = byClass(rendered, "dd-mechanics-summary-grid");
    assert.equal(summaries.length, 1);
    assert.equal(byClass(summaries[0], "dd-defense-card").length, 1);
    assert.equal(byClass(summaries[0], "dd-combat-fundamentals-card").length, 1);
    assert.equal(byClass(rendered, "dd-skills-card").length, 1);
});

test("wide shell keeps top statistics full-width and moves persistent facts beneath Skills", () => {
    const rendered = render("actions", null);
    const dashboard = byClass(rendered, "dd-sheet__dashboard")[0];
    const skills = byClass(rendered, "dd-sheet__skills")[0];
    const topRow = byClass(rendered, "dd-sheet__top-row")[0];
    const support = byClass(rendered, "dd-sheet__support")[0];
    const mechanicsColumn = byClass(rendered, "dd-sheet__mechanics")[0];
    const main = byClass(rendered, "dd-sheet__main")[0];
    assert.ok(dashboard);
    assert.ok(skills);
    assert.ok(topRow);
    assert.ok(support);
    assert.ok(mechanicsColumn);
    assert.ok(main);
    assert.equal(byClass(topRow, "dd-health-quick").length, 1);
    assert.equal(walk(skills).includes(support), true);
    assert.equal(walk(skills).includes(mechanicsColumn), true);
    assert.equal(walk(skills).includes(main), false);
    assert.equal(byClass(rendered, "dd-saving-throws-card").length, 1);
    assert.equal(byClass(rendered, "dd-defense-card").length, 1);
    assert.equal(byClass(rendered, "dd-combat-fundamentals-card").length, 1);
});

test("support scaffolds do not invent edition-specific passive values or training categories", () => {
    const rendered = render("actions", null);
    const text = visibleText(rendered);
    assert.match(text, /Passive Values/);
    assert.match(text, /Proficiencies & Training/);
    assert.doesNotMatch(text, /Passive Values\s+Perception|Passive Values\s+Investigation|Proficiencies & Training\s+Armor/);
    assert.equal(byAttribute(rendered, "data-support-scaffold-state", "unavailable").length, 2);
});

test("null mechanics projection keeps the normal sheet structure and uses neutral dashes", () => {
    const rendered = render("actions", null);
    const text = visibleText(rendered);
    assert.equal(byClass(rendered, "dd-skill-row--placeholder").length, 1);
    assert.doesNotMatch(text, /Saving throw mechanics are not available|Resolved competencies are not available|Combat mechanics are not available|Movement mechanics are not available|Resolved checks and procedures are not available|Resolved actions and attacks are not available/);
    assert.equal(byAttribute(rendered, "data-action-state", "unavailable").length, 1);

    for (const label of [
        "Armor Class",
        "Touch AC",
        "Flat-Footed AC",
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

    assert.equal(byAttribute(rendered, "data-support-scaffold-state", "unavailable").length, 2);
    assert.equal(byAttribute(rendered, "data-support-scaffold-key", "passive-perception").length, 0);
    assert.equal(byAttribute(rendered, "data-support-scaffold-key", "armor-training").length, 0);
    assert.equal(byAttribute(rendered, "data-sheet-scaffold-key", "armor-class").length, 1);
    assert.equal(byClass(rendered, "dd-health-quick").length, 1);
});

test("Inventory and Notes empty states remain visible and task-oriented", () => {
    const inventory = render("inventory", null);
    assert.match(visibleText(inventory), /No items have been added/);
    assert.equal(byClass(inventory, "dd-inventory-item").length, 0);

    const notes = render("notes", null);
    assert.match(visibleText(notes), /No notes have been added/);
    assert.equal(byClass(notes, "dd-note").length, 0);
});

test("active Character exposes edit and guided setup entry points", () => {
    const editableBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [],
            progressionEntries: []
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        editableBuilder,
        routine([], {}, false),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        null,
        handlers
    );

    assert.match(visibleText(rendered), /Edit Character/);
    assert.match(visibleText(rendered), /Guided Setup/);
    assert.equal(byAttribute(rendered, "data-sheet-mode-control", "edit").length, 1);
    assert.equal(byAttribute(rendered, "data-sheet-mode-control", "guided").length, 1);
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
    assert.equal(byAttribute(unavailable, "data-spellcasting-state", "unavailable").length, 1);
    assert.doesNotMatch(visibleText(unavailable), /Resolved spellcasting profiles are not available/);
    assert.equal(byClass(unavailable, "dd-spellcasting-profile").length, 0);
});

test("production consumes backend advancement and mechanics projections instead of hard-coded nulls", async () => {
    const source = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
    assert.match(source, /state\.presentation\.status === "ready" \? state\.presentation\.advancement : null/);
    assert.match(source, /state\.presentation\.status === "ready" \? state\.presentation\.mechanics : null/);
    assert.doesNotMatch(source, /state\.guidedBuilder,\s*null,\s*null,\s*\{/s);
});

test("production refreshes presentation after structural, Ability, Feat, and Inventory mutations", async () => {
    const sources = await Promise.all([
        "../src/core/application/presentation-workflow.ts",
        "../src/features/inventory/inventory-workflow.ts",
        "../src/features/features/feat-workflow.ts",
        "../src/features/advancement/advancement-workflow.ts",
        "../src/features/abilities/ability-workflow.ts"
    ].map(path => readFile(new URL(path, import.meta.url), "utf8")));
    const [presentation, inventory, feats, advancement, abilities] = sources;
    assert.match(presentation, /loadCharacterPresentation/);
    assert.match(inventory, /async add[\s\S]*presentation\.load\(characterId\)/);
    assert.match(inventory, /async remove[\s\S]*presentation\.load\(characterId\)/);
    assert.match(feats, /async function mutate[\s\S]*presentation\.load\(characterId\)/);
    assert.match(advancement, /async function mutate[\s\S]*presentation\.load\(characterId\)/);
    assert.match(abilities, /async function persist[\s\S]*presentation\.load\(characterId\)/);
    assert.doesNotMatch(sources.join("\n"), /calculateAbilityModifier|score\s*-\s*10/i);
});

test("generalized presentation contains no source-specific Loot Tavern formula or prose", async () => {
    const sources = await Promise.all([
        "character-mechanics.ts", "mechanics-components.ts", "procedure-components.ts",
        "source-attribution.ts", "sheet.ts"
    ].map(name => readFile(new URL(`../src/ui/${name}`, import.meta.url), "utf8")));
    assert.doesNotMatch(sources.join("\n"), /Loot Tavern|Harvest Assessment|Carving/i);
});

test("generalized mechanics styles use host semantic tokens and include mobile layout rules", async () => {
    const css = (await Promise.all([
    "../src/styles/foundation.css",
    "../src/styles/builder.css",
    "../src/styles/advancement.css",
    "../src/styles/mechanics.css",
    "../src/styles/abilities.css",
    "../src/styles/supplemental.css"
].map(path => readFile(new URL(path, import.meta.url), "utf8")))).join("\n");
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


test("effective Ability projection is separate from Character-owned base Ability input", () => {
    const configuredBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [{ abilityKey: "strength", score: 12 }],
            progressionEntries: []
        }
    };
    const mechanics = {
        abilityValues: [{
            key: "strength",
            label: "Strength",
            effectiveValue: 18,
            formattedValue: "18",
            relatedValues: [{ key: "modifier", label: "Modifier", effectiveValue: 4, formattedValue: "+4" }],
            breakdown: [{ key: "enhancement", label: "Enhancement", effectiveValue: 6, formattedValue: "+6" }],
            sourceAttributions: [{ key: "module", label: "Rules module" }]
        }]
    };
    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        routine([], {}, false),
        "actions",
        false,
        "edit",
        guidedBuilder,
        null,
        mechanics,
        handlers
    );
    const strength = byAttribute(rendered, "data-ability-key", "strength")[0];
    assert.equal(strength.getAttribute("data-effective-ability-state"), "resolved");
    assert.match(visibleText(strength), /18/);
    assert.match(visibleText(strength), /Base input: 12/);
    assert.match(visibleText(strength), /Modifier \+4/);
    const modifier = byAttribute(strength, "data-ability-modifier", "strength");
    assert.equal(modifier.length, 1);
    assert.match(visibleText(modifier[0]), /Modifier \+4/);
    assert.match(visibleText(strength), /Enhancement \+6/);
    assert.match(visibleText(strength), /Rules module/);

    const editor = byTag(strength, "input")[0];
    assert.ok(editor);
    assert.equal(editor.value, "12");
    assert.notEqual(editor.value, "18");
});

test("absent effective Ability mechanics preserve base input and honest modifier unavailability", () => {
    const configuredBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [{ abilityKey: "strength", score: 12 }],
            progressionEntries: []
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        routine([], {}, false),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        { abilityValues: [] },
        handlers
    );
    const strength = byAttribute(rendered, "data-ability-key", "strength")[0];
    assert.equal(strength.getAttribute("data-effective-ability-state"), "unavailable");
    assert.match(visibleText(strength), /12/);
    assert.match(visibleText(strength), /Base Score/);
    assert.match(visibleText(strength), /Modifier -/);
    const modifier = byAttribute(strength, "data-ability-modifier", "strength");
    assert.equal(modifier.length, 1);
    assert.match(visibleText(modifier[0]), /Modifier -/);
});

test("backend-supplied Ability values outside the six structural keys use the generic fallback presentation", () => {
    const rendered = render("actions", {
        abilityValues: [mechanical("honor", "Honor", "14", {
            relatedValues: [{ key: "modifier", label: "Modifier", effectiveValue: 2, formattedValue: "+2" }]
        })]
    });
    const extra = byAttribute(rendered, "data-additional-ability-key", "honor")[0];
    assert.ok(extra);
    assert.match(visibleText(extra), /Honor/);
    assert.match(visibleText(extra), /14/);
    assert.match(visibleText(extra), /Modifier/);
});

test("top-level Character mechanics attribution renders once as a projection-wide rules-module surface", () => {
    const rendered = render("actions", {
        sourceAttributions: [{
            key: "module",
            label: "External rules module",
            detail: "Applies across several Character mechanics",
            officialUrl: "https://example.test/module"
        }]
    });
    const surfaces = byClass(rendered, "dd-character-mechanics-sources");
    assert.equal(surfaces.length, 1);
    assert.match(visibleText(surfaces[0]), /Rules modules/);
    assert.match(visibleText(surfaces[0]), /Sources \(1\)/);
    assert.match(visibleText(surfaces[0]), /External rules module/);
    assert.equal(byTag(surfaces[0], "details").length, 1);
    assert.equal(byTag(surfaces[0], "a").length, 1);
});

test("production Ability presentation contains no D&D Ability modifier formula", async () => {
    const sources = await Promise.all([
        "sheet.ts",
        "character-mechanics.ts",
        "mechanics-components.ts"
    ].map(name => readFile(new URL(`../src/ui/${name}`, import.meta.url), "utf8")));
    const production = sources.join("\n");
    assert.doesNotMatch(production, /score\s*-\s*10|Math\.floor\s*\([^\n]*-\s*10|calculateAbilityModifier/i);
});

test("legacy combat placeholder renderer is removed in favor of generalized mechanic cards", async () => {
    const source = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    const coreStatsSource = await readFile(new URL("../src/ui/core-stats.ts", import.meta.url), "utf8");
    assert.doesNotMatch(source, /function renderCombatSummary\s*\(/);
    assert.match(source, /renderDefenseMechanicsCard\(mechanics\)/);
    assert.match(source, /renderCombatFundamentalsCard\(mechanics\)/);
    assert.match(coreStatsSource, /renderHealthQuickCard\(mechanics, healthControl\)/);
    assert.match(source, /renderSavingThrowsCard\(mechanics\?\.savingThrows\)/);
});


test("backend-supplied Proficiency Bonus is promoted into the top stat strip without duplication", () => {
    const rendered = render("actions", {
        combatFundamentals: [mechanical("proficiency-bonus", "Proficiency Bonus", "+3")]
    });
    const resolved = byAttribute(rendered, "data-mechanic-key", "proficiency-bonus");
    const card = byClass(rendered, "dd-stat--proficiency")[0];
    assert.equal(resolved.length, 1);
    assert.ok(card);
    assert.equal(walk(card).includes(resolved[0]), true);
    assert.match(visibleText(card), /Proficiency Bonus/);
    assert.match(visibleText(card), /\+3/);
    assert.equal(byAttribute(rendered, "data-unimplemented-mechanic", "proficiency").length, 0);
    assert.doesNotMatch(visibleText(rendered), /Proficiency Bonus\s+Not (?:configured|yet configured)/i);
});

test("missing Proficiency Bonus keeps the reference slot but does not invent a numeric value", () => {
    const rendered = render("actions", { combatFundamentals: [] });
    const card = byClass(rendered, "dd-stat--proficiency")[0];
    assert.ok(card);
    assert.match(visibleText(card), /Proficiency Bonus/);
    assert.match(visibleText(card), /-/);
    assert.equal(card.getAttribute("data-proficiency-bonus-state"), "unavailable");
    assert.equal(byAttribute(rendered, "data-unimplemented-mechanic", "proficiency").length, 0);
});


test("workspace renders specialized, composite, independent, and unconfigured competencies from backend data", () => {
    const mechanics = {
        competencies: {
            entries: [
                mechanical("skill.stealth", "Stealth", "-", { kind: "skill" }),
                mechanical("skill.hide", "Hide", "-", { kind: "skill", governingAbility: "dexterity" }),
                mechanical("skill.move-silently", "Move Silently", "-", { kind: "skill" }),
                mechanical("skill.knowledge-planes", "Knowledge (the planes)", "-", {
                    kind: "skill",
                    family: "Knowledge",
                    specialty: "the planes",
                    supportsRanks: true,
                    supportsClassSkillState: true,
                    supportsTrainingState: true,
                    trainedOnly: true,
                    armorCheckPenalty: { applies: false }
                }),
                mechanical("skill.perception", "Perception", "-", { kind: "skill" }),
                mechanical("skill.listen", "Listen", "-", { kind: "skill" }),
                mechanical("skill.spot", "Spot", "-", { kind: "skill" })
            ],
            relationships: [{
                parentKey: "skill.stealth",
                componentKeys: ["skill.hide", "skill.move-silently"]
            }]
        }
    };

    const rendered = render("actions", mechanics);
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.stealth")[0].getAttribute("data-skill-role"), "parent");
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.hide")[0].getAttribute("data-skill-role"), "component");
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.move-silently")[0].getAttribute("data-skill-role"), "component");

    const specialtyRow = byAttribute(rendered, "data-skill-id", "skill.knowledge-planes")[0];
    const specialty = byAttribute(rendered, "data-skill-disclosure", "skill.knowledge-planes")[0];
    assert.ok(specialty);
    assert.match(visibleText(specialtyRow), /Knowledge \(the planes\)/);
    assert.match(visibleText(specialty), /Family\s+Knowledge/);
    assert.match(visibleText(specialty), /Specialty\s+the planes/);
    assert.match(visibleText(specialty), /Ranks\s+-/);
    assert.match(visibleText(specialty), /Training\s+-/);
    assert.match(visibleText(specialty), /Class skill\s+-/);
    assert.doesNotMatch(visibleText(specialty), /\b0\b/);

    for (const key of ["skill.perception", "skill.listen", "skill.spot"]) {
        assert.equal(byAttribute(rendered, "data-skill-id", key)[0].getAttribute("data-skill-role"), "standalone");
    }
});

test("workspace renders backend-supplied 3.x saving throws, defenses, combat, and nonlethal state without formulas", () => {
    const mechanics = {
        savingThrows: [
            mechanical("save.fortitude", "Fortitude Save", "+7"),
            mechanical("save.reflex", "Reflex Save", "+5"),
            mechanical("save.will", "Will Save", "+6")
        ],
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "17"),
                mechanical("defense.spell-resistance", "Spell Resistance", "18"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5/magic")
            ]
        },
        combatFundamentals: [
            mechanical("combat.initiative", "Initiative", "+4"),
            mechanical("combat.base-attack-bonus", "Base Attack Bonus", "+6/+1"),
            mechanical("combat.grapple", "Grapple", "+9")
        ],
        healthTracks: [{
            key: "resource.nonlethal-damage",
            label: "Nonlethal Damage",
            role: "nonlethal-damage",
            current: 4
        }]
    };

    const rendered = render("actions", mechanics);
    for (const key of [
        "save.fortitude",
        "save.reflex",
        "save.will",
        "defense.ac.touch",
        "defense.ac.flat-footed",
        "defense.spell-resistance",
        "defense.damage-reduction",
        "combat.initiative",
        "combat.base-attack-bonus",
        "combat.grapple"
    ]) {
        assert.equal(byAttribute(rendered, "data-mechanic-key", key).length, 1, key);
    }
    const nonlethal = byAttribute(rendered, "data-health-quick-field", "nonlethal");
    assert.equal(nonlethal.length, 1);
    assert.match(visibleText(nonlethal[0]), /Nonlethal Damage/);
    assert.match(visibleText(nonlethal[0]), /4/);
    const initiative = byAttribute(rendered, "data-mechanic-key", "combat.initiative")[0];
    assert.ok(initiative);
    assert.ok(byClass(rendered, "dd-stat--initiative").some(node => walk(node).includes(initiative)));
    assert.match(visibleText(rendered), /Touch AC/);
    assert.match(visibleText(rendered), /Base Attack Bonus/);
    assert.match(visibleText(rendered), /Nonlethal Damage/);
});
