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
    savingAdvancementLevel: null,
    featReferences: {},
    featChooser: { kind: "closed" },
    savingFeat: null
};
const guidedBuilder = { open: false, activeSection: "species", returnSheetMode: "view" };
const handlers = {
    structural: {
        openChooser() {}, clearChoice() {}, submitChooserSearch() {}, closeChooser() {}, saveChoice() {},
        setAdvancementLevel() {}, setBaseAbilityScore() {}, clearBaseAbilityScore() {}
    },
    feats: { openChooser() {}, closeChooser() {}, search() {}, add() {}, remove() {} },
    spells: { openChooser() {}, closeChooser() {}, search() {}, add() {}, remove() {} },
    rules: {
        setChoice() {}, clearChoice() {}, setResource() {},
        setHitPointGain() {}, clearHitPointGain() {}
    },
    routine: {
        setProfile() {},
        setCurrencyBalance() {},
        removeCurrencyBalance() {},
        setCurrentHitPoints() {},
        setDeathSaves() {},
        addNote() {}, updateNote() {}, deleteNote() {}, openInventoryChooser() {}, closeInventoryChooser() {},
        searchInventory() {}, addInventoryItem() {}, removeInventoryItem() {}
    },
    selectSection() {}, enterEditMode() {}, leaveEditMode() {}, openGuidedBuilder() {},
    closeGuidedBuilder() {}, selectGuidedBuilderSection() {}
};
const routine = (
    occurrences = [],
    references = {},
    readOnly = true,
    currentHitPoints = null,
    deathSaves = { successes: 0, failures: 0 }
) => ({
    status: "ready",
    state: {
        characterId,
        readOnly,
        currentHitPoints,
        deathSaves,
        inventoryItemOccurrences: occurrences,
        notes: [],
        conditions: []
    },
    references,
    inventoryChooser: { kind: "closed" },
    spellChooser: { kind: "closed" },
    conditionChooser: { kind: "closed" },
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
    assert.equal(tabs.length, 6);
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
    assert.deepEqual(tabs.slice(1).map(tab => tab.tabIndex), [-1, -1, -1, -1, -1]);
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
    assert.match(css, /@container character-stage \(min-width: 54rem\)[\s\S]*?\.dd-primary-nav\s*\{[^}]*overflow-x:\s*visible;/s);
    assert.match(css, /@container character-stage \(min-width: 54rem\)[\s\S]*?\.dd-primary-nav__button\s*\{[^}]*flex:\s*1 1 0;[^}]*min-width:\s*0;/s);
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

test("3.x Fortitude, Reflex, and Will remain independent from six Ability saves", () => {
    const rendered = render("actions", {
        savingThrows: [
            mechanical("save.dexterity", "Dexterity Save", "+4", { governingAbility: "dexterity" }),
            mechanical("save.constitution", "Constitution Save", "+3", { governingAbility: "constitution" }),
            mechanical("save.wisdom", "Wisdom Save", "+5", { governingAbility: "wisdom" }),
            mechanical("save.fortitude", "Fortitude Save", "+8"),
            mechanical("save.reflex", "Reflex Save", "+5"),
            mechanical("save.will", "Will Save", "+7")
        ]
    });

    const constitution = byAttribute(rendered, "data-ability-key", "constitution")[0];
    const dexterity = byAttribute(rendered, "data-ability-key", "dexterity")[0];
    const wisdom = byAttribute(rendered, "data-ability-key", "wisdom")[0];
    const saveCard = byClass(rendered, "dd-saving-throws-card")[0];

    assert.match(visibleText(constitution), /Save\s+\+3/);
    assert.doesNotMatch(visibleText(constitution), /Fortitude|Fort Save/);
    assert.match(visibleText(dexterity), /Save\s+\+4/);
    assert.doesNotMatch(visibleText(dexterity), /Reflex|Ref Save/);
    assert.match(visibleText(wisdom), /Save\s+\+5/);
    assert.doesNotMatch(visibleText(wisdom), /Will Save\s+\+7/);

    assert.ok(saveCard);
    assert.match(visibleText(saveCard), /Fortitude Save\s+\+8/);
    assert.match(visibleText(saveCard), /Reflex Save\s+\+5/);
    assert.match(visibleText(saveCard), /Will Save\s+\+7/);
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

test("Rules Core recovery actions live in the top control bar rather than the Hit Points card", () => {
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
    const recoveryCalls = [];
    const rendered = renderCharacterWorkspace(
        character,
        editableBuilder,
        activeRoutine,
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        {
            healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", maximum: 20 }],
            recoveryProcedures: [
                {
                    procedureKey: "recovery.short.fixture",
                    displayName: "Short Rest",
                    presentationRole: "short-rest",
                    applicabilityState: "applicable"
                },
                {
                    procedureKey: "recovery.long.fixture",
                    displayName: "Long Rest",
                    presentationRole: "long-rest",
                    applicabilityState: "applicable"
                }
            ]
        },
        {
            ...handlers,
            routine: {
                ...handlers.routine,
                recover(key) { recoveryCalls.push(key); }
            }
        }
    );

    const modeBar = byClass(rendered, "dd-sheet-mode-bar")[0];
    const healthCard = byClass(rendered, "dd-health-quick")[0];
    assert.ok(modeBar);
    assert.ok(healthCard);
    const shortRest = byAttribute(modeBar, "data-recovery-procedure", "recovery.short.fixture")[0];
    const longRest = byAttribute(modeBar, "data-recovery-procedure", "recovery.long.fixture")[0];
    assert.ok(shortRest);
    assert.ok(longRest);
    assert.equal(byAttribute(healthCard, "data-recovery-procedure", "recovery.short.fixture").length, 0);
    shortRest.onclick();
    longRest.onclick();
    assert.deepEqual(recoveryCalls, ["recovery.short.fixture", "recovery.long.fixture"]);
});

test("sheet does not invent rest buttons when Rules Core supplies no recovery procedures", () => {
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
        routine([], {}, false, 10),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        { healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", maximum: 20 }] },
        handlers
    );
    const modeBar = byClass(rendered, "dd-sheet-mode-bar")[0];
    assert.equal(byClass(modeBar, "dd-rest-button").length, 0);
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

test("top strip reserves Inspiration and renders a supplied resource without inventing state", () => {
    const empty = render("actions", null);
    const emptyCard = byClass(empty, "dd-stat--inspiration")[0];
    assert.ok(emptyCard);
    assert.equal(emptyCard.getAttribute("data-inspiration-state"), "unavailable");
    assert.match(visibleText(emptyCard), /Inspiration\s+-/);

    const supplied = render("actions", {
        inspiration: mechanical("resource.heroic-inspiration", "Heroic Inspiration", "1")
    });
    const card = byClass(supplied, "dd-stat--inspiration")[0];
    assert.equal(card.getAttribute("data-inspiration-state"), "resolved");
    assert.equal(byAttribute(card, "data-mechanic-key", "resource.heroic-inspiration").length, 1);
    assert.match(visibleText(card), /Inspiration\s+1/);
});

test("top-row Hit Points card keeps current, max, temporary, and nonlethal values dense and distinct", () => {
    const rendered = render("actions", {
        healthTracks: [
            { key: "hp", label: "Hit Points", role: "hit-points", current: 21, maximum: 30 },
            { key: "temp", label: "Temporary HP", role: "temporary-hit-points", current: 5 },
            { key: "nonlethal", label: "Nonlethal Damage", role: "nonlethal-damage", current: 3 },
            { key: "resource.hit-dice", label: "Hit Dice", role: "hit-dice", current: 2, maximum: 4 }
        ]
    });
    const card = byClass(rendered, "dd-health-quick")[0];
    assert.ok(card);
    assert.match(visibleText(card), /Current\s+21/);
    assert.match(visibleText(card), /Max\s+30/);
    assert.match(visibleText(card), /Temporary HP\s+5/);
    assert.match(visibleText(card), /Nonlethal Damage\s+3/);
    assert.match(visibleText(card), /Hit Dice\s+2 \/ 4/);
    assert.match(visibleText(card), /Death Saves\s+S 0 • F 0/);
    assert.equal(byAttribute(card, "data-health-track-key", "hp").length, 2);
    assert.equal(byAttribute(card, "data-health-track-key", "temp").length, 1);
    assert.equal(byAttribute(card, "data-health-track-key", "nonlethal").length, 1);
});

test("top-row Hit Points tracker exposes D&D Beyond-style Heal and Damage controls while retaining direct-set access", () => {
    const saved = [];
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        routine([], {}, false, 18),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        {
            healthTracks: [
                { key: "hp", label: "Hit Points", role: "hit-points", current: 99, maximum: 30 },
                { key: "temp", label: "Temporary HP", role: "temporary-hit-points", current: 4 },
                { key: "nonlethal", label: "Nonlethal Damage", role: "nonlethal-damage", current: 2 }
            ]
        },
        {
            ...handlers,
            routine: {
                ...handlers.routine,
                setCurrentHitPoints(value) { saved.push(value); }
            }
        }
    );

    const card = byClass(rendered, "dd-health-quick")[0];
    assert.ok(card);
    const input = byClass(card, "dd-health-quick__adjust-input")[0];
    const damage = byAttribute(card, "data-health-action", "damage")[0];
    const heal = byAttribute(card, "data-health-action", "heal")[0];
    const directSetter = byAttribute(card, "data-health-direct-setter", "true")[0];
    assert.ok(input);
    assert.ok(damage);
    assert.ok(heal);
    assert.ok(directSetter);

    input.value = "5";
    damage.onclick();
    assert.equal(saved.at(-1), 13);

    input.value = "20";
    heal.onclick();
    assert.equal(saved.at(-1), 30);

    assert.match(visibleText(card), /Temporary HP\s+4/);
    assert.match(visibleText(card), /Nonlethal Damage\s+2/);
});

test("Death Saves remain visible and editable through persisted runtime handlers", () => {
    const saved = [];
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        routine([], {}, false, 18, { successes: 1, failures: 2 }),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        {
            healthTracks: [
                { key: "hp", label: "Hit Points", role: "hit-points", maximum: 30 },
                { key: "resource.hit-dice", label: "Hit Dice", role: "hit-dice", current: 3, maximum: 5 }
            ]
        },
        {
            ...handlers,
            routine: {
                ...handlers.routine,
                setDeathSaves(successes, failures) { saved.push([successes, failures]); }
            }
        }
    );

    const card = byClass(rendered, "dd-health-quick")[0];
    assert.match(visibleText(card), /Hit Dice\s+3 \/ 5/);
    assert.match(visibleText(card), /Death Saves\s+S 1 • F 2/);
    assert.equal(byAttribute(card, "data-death-saves-editor", "true").length, 1);

    byAttribute(card, "data-death-save-action", "success-increment")[0].onclick();
    assert.deepEqual(saved.at(-1), [2, 2]);

    byAttribute(card, "data-death-save-action", "failure-decrement")[0].onclick();
    assert.deepEqual(saved.at(-1), [1, 1]);

    byAttribute(card, "data-death-save-action", "reset")[0].onclick();
    assert.deepEqual(saved.at(-1), [0, 0]);
});

test("workspace promotes Armor Class beside Initiative and keeps all non-AC defenses in one combat-band card", () => {
    const rendered = render("actions", {
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac", "Armor Class", "18", {
                    breakdown: [
                        mechanical("armor", "Armor", "6"),
                        mechanical("dexterity", "Dexterity", "2")
                    ]
                }),
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "15"),
                mechanical("defense.miss-chance", "Miss Chance", "20%"),
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
    assert.match(visibleText(acCard), /Details/);
    assert.match(visibleText(acCard), /Armor\s+6/);
    assert.match(visibleText(acCard), /Dexterity\s+2/);

    const defense = byClass(rendered, "dd-combat-band__defenses")[0];
    assert.ok(defense);
    assert.doesNotMatch(visibleText(defense), /Armor Class|Touch AC|Flat-Footed/);
    assert.match(visibleText(defense), /Resistances/);
    assert.match(visibleText(defense), /Immunities/);
    assert.match(visibleText(defense), /Vulnerabilities/);
    assert.match(visibleText(defense), /Damage Reduction\s+5 \/ magic/);
    assert.match(visibleText(defense), /Spell Resistance\s+17/);
    assert.match(visibleText(defense), /More defenses/);
    assert.match(visibleText(defense), /Miss Chance\s+20%/);
    assert.equal(byClass(rendered, "dd-defense-card").length, 0);
});

test("Defense and combat fundamentals stay above the tabs instead of trailing Skills", () => {
    const rendered = render("actions", null);
    const skills = byClass(rendered, "dd-sheet__skills")[0];
    const band = byClass(rendered, "dd-combat-band")[0];
    assert.ok(skills);
    assert.ok(band);
    assert.equal(byClass(rendered, "dd-mechanics-summary-grid").length, 0);
    assert.equal(byClass(rendered, "dd-defense-card").length, 0);
    assert.equal(byClass(rendered, "dd-combat-fundamentals-card").length, 0);
    assert.match(visibleText(band), /Base Attack Bonus/);
    assert.match(visibleText(band), /Grapple Modifier/);
    assert.match(visibleText(band), /Damage Reduction/);
    assert.match(visibleText(band), /Spell Resistance/);
});

test("wide shell uses a Beyond-style reference rail beside Skills and the primary stage", () => {
    const rendered = render("actions", null);
    const dashboard = byClass(rendered, "dd-sheet__dashboard")[0];
    const rail = byClass(rendered, "dd-sheet__reference-rail")[0];
    const skills = byClass(rendered, "dd-sheet__skills")[0];
    const topRow = byClass(rendered, "dd-sheet__top-row")[0];
    const main = byClass(rendered, "dd-sheet__main")[0];
    assert.ok(dashboard);
    assert.ok(rail);
    assert.ok(skills);
    assert.ok(topRow);
    assert.ok(main);
    assert.equal(byClass(topRow, "dd-health-quick").length, 1);
    assert.equal(walk(rail).includes(byClass(rendered, "dd-saving-throws-card")[0]), true);
    assert.equal(walk(rail).includes(byClass(rendered, "dd-senses-summary-card")[0]), true);
    assert.equal(walk(skills).includes(main), false);
    assert.equal(byClass(rendered, "dd-defense-card").length, 0);
    assert.equal(byClass(rendered, "dd-combat-fundamentals-card").length, 0);
    assert.equal(byClass(rendered, "dd-combat-band__defenses").length, 1);
    assert.equal(byClass(rendered, "dd-combat-band__initiative").length, 1);
});

test("support surfaces stay neutral when unavailable and consume supplied values without edition guesses", () => {
    const empty = render("actions", null);
    const emptyText = visibleText(empty);
    assert.match(emptyText, /Senses/);
    assert.match(emptyText, /Passive Values/);
    assert.match(emptyText, /Additional Senses/);
    assert.match(emptyText, /Proficiencies & Training/);
    assert.doesNotMatch(emptyText, /Passive Values\s+Perception|Proficiencies & Training\s+Armor/);
    assert.equal(byAttribute(empty, "data-support-scaffold-state", "unavailable").length, 2);

    const supplied = render("actions", {
        passiveValues: [mechanical("passive.awareness", "Awareness", "17")],
        senses: [mechanical("sense.darkvision", "Darkvision", "60 ft.")],
        training: [mechanical("training.armor.light", "Light Armor", "Proficient")],
        competencies: { entries: [
            mechanical("tool.alchemist", "Alchemist's Supplies", "-", {
                kind: "tool", training: "Proficient"
            })
        ] }
    });
    assert.match(visibleText(supplied), /Awareness\s+17/);
    assert.match(visibleText(supplied), /Darkvision\s+60 ft\./);
    assert.match(visibleText(supplied), /Light Armor\s+Proficient/);
    assert.match(visibleText(supplied), /Alchemist's Supplies\s+Proficient/);
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

test("Details renders Rules Core character metadata generically with calculation detail", () => {
    const rendered = render("details", {
        characterMetadata: [{
            key: "character.size-category",
            label: "Size",
            effectiveValue: "Medium",
            sourceAttributions: [{
                key: "fixture",
                label: "Fixture Rules"
            }]
        }]
    });
    const size = byAttribute(rendered, "data-mechanic-key", "character.size-category")[0];

    assert.ok(size);
    assert.match(visibleText(size), /Size\s+Medium/);
    assert.match(visibleText(size), /Details/);
});

test("Details renders Character-authored profile without inventing rule-derived identity", () => {
    const currentRoutine = routine([], {}, true);
    currentRoutine.state.profile = {
        alignment: "Neutral",
        deity: "The Traveler",
        age: "34",
        height: "5 ft. 11 in.",
        weight: "180 lb.",
        appearance: "Scarred",
        personalityTraits: "Curious",
        ideals: "Freedom",
        bonds: "Old company",
        flaws: "Impatient",
        backstory: "A long-form history.",
        alliesAndOrganizations: "Cartographers Guild",
        symbol: "Compass rose",
        createdAt: "now",
        updatedAt: "now"
    };

    const rendered = render("details", null, currentRoutine);
    const text = visibleText(rendered);

    assert.match(text, /Alignment\s+Neutral/);
    assert.match(text, /Deity\s+The Traveler/);
    assert.match(text, /Backstory\s+A long-form history/);
    assert.doesNotMatch(text, /Background|Size|Player Name|Campaign/);
});

test("Details editing delegates one complete Character-authored profile mutation", () => {
    let savedProfile = null;
    const editableRoutine = routine([], {}, false);
    const profileHandlers = {
        ...handlers,
        routine: {
            ...handlers.routine,
            setProfile(input) { savedProfile = input; }
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        editableRoutine,
        "details",
        false,
        "view",
        guidedBuilder,
        null,
        null,
        profileHandlers
    );

    const alignment = byAttribute(rendered, "data-profile-field", "alignment")[0];
    const deity = byAttribute(rendered, "data-profile-field", "deity")[0];
    assert.ok(alignment);
    assert.ok(deity);
    alignment.value = "Chaotic good";
    deity.value = "The Traveler";

    const form = byClass(rendered, "dd-profile__form")[0];
    form.dispatchEvent({ type: "submit", preventDefault() {} });

    assert.ok(savedProfile);
    assert.equal(savedProfile.alignment, "Chaotic good");
    assert.equal(savedProfile.deity, "The Traveler");
    assert.equal(savedProfile.backstory, null);
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

test("Known Spells renders Character-owned spell concepts and delegates add/remove without prepared state", () => {
    let removed = null;
    const knownSpellId = "known-spell-input";
    const currentRoutine = routine([], {
        [knownSpellId]: resolvedItem("spell.magic-missile", "Magic Missile")
    }, false);
    currentRoutine.state.rulesInputs = [{
        id: knownSpellId,
        kind: "knownSpell",
        key: "spell.magic-missile",
        integerValue: null,
        booleanValue: null,
        textValue: null,
        createdAt: "now",
        updatedAt: "now"
    }];
    currentRoutine.references[knownSpellId] = {
        status: "resolved",
        conceptKey: "spell.magic-missile",
        rule: {
            ...resolvedItem("spell.magic-missile", "Magic Missile").rule,
            entityType: "spell"
        }
    };

    const spellHandlers = {
        ...handlers,
        spells: {
            ...handlers.spells,
            remove(conceptKey) { removed = conceptKey; }
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        currentRoutine,
        "spells",
        false,
        "view",
        guidedBuilder,
        null,
        { spellcastingProfiles: [] },
        spellHandlers
    );

    const spell = byAttribute(rendered, "data-known-spell-key", "spell.magic-missile")[0];
    assert.ok(spell);
    assert.match(visibleText(spell), /Magic Missile/);
    assert.doesNotMatch(visibleText(rendered), /Prepared Spells/i);
    const remove = byTag(spell, "button").find(button => button.textContent === "Remove");
    assert.ok(remove);
    remove.dispatchEvent({ type: "click" });
    assert.equal(removed, "spell.magic-missile");
});

test("spellcasting profiles keep independent resource systems and runtime resources", () => {
    const supplied = render("spells", {
        spellcastingProfiles: [{
            key: "wizard",
            label: "Wizard Spellcasting",
            castingAbility: "Intelligence",
            resourceSystem: { key: "resource-system", label: "Resource System", value: "spell-points" },
            resources: [{
                key: "resource.spell-points",
                label: "Spell Points",
                state: "resolved",
                current: 8,
                maximum: 14
            }],
            saveDc: mechanical("dc", "Save DC", "16")
        }, {
            key: "warlock",
            label: "Warlock Pact Magic",
            castingAbility: "Charisma",
            resourceSystem: { key: "resource-system", label: "Resource System", value: "pact-magic" },
            resources: [{
                key: "resource.pact-slot.warlock.level-3",
                label: "Pact Slots",
                state: "resolved",
                current: 1,
                maximum: 2
            }]
        }]
    });
    assert.equal(byAttribute(supplied, "data-spellcasting-profile-key", "wizard").length, 1);
    assert.equal(byAttribute(supplied, "data-spellcasting-profile-key", "warlock").length, 1);
    assert.match(visibleText(supplied), /Wizard Spellcasting/);
    assert.match(visibleText(supplied), /Spell Points 8 \/ 14/);
    assert.match(visibleText(supplied), /Warlock Pact Magic/);
    assert.match(visibleText(supplied), /Pact Slots 1 \/ 2/);

    const unavailable = render("spells", null);
    assert.equal(byAttribute(unavailable, "data-spellcasting-state", "unavailable").length, 1);
    assert.equal(byClass(unavailable, "dd-spellcasting-profile").length, 0);
});

test("spellcasting resource editor persists the backend resource key and current value", () => {
    let saved = null;
    const resourceHandlers = {
        ...handlers,
        rules: {
            ...handlers.rules,
            setResource(resourceKey, currentValue) { saved = [resourceKey, currentValue]; }
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        builder,
        routine([], {}, false),
        "spells",
        false,
        "view",
        guidedBuilder,
        null,
        {
            spellcastingProfiles: [{
                key: "wizard",
                label: "Wizard Spellcasting",
                resourceSystem: { key: "resource-system", label: "Resource System", value: "spell-points" },
                resources: [{
                    key: "resource.spell-points",
                    label: "Spell Points",
                    state: "resolved",
                    current: 8,
                    maximum: 14
                }]
            }]
        },
        resourceHandlers
    );

    const resource = byAttribute(rendered, "data-spellcasting-resource-key", "resource.spell-points")[0];
    assert.ok(resource);
    const input = byTag(resource, "input")[0];
    input.value = "6";
    const save = byTag(resource, "button").find(button => button.textContent === "Set");
    assert.ok(save);
    save.dispatchEvent({ type: "click" });
    assert.deepEqual(saved, ["resource.spell-points", 6]);
});

test("guided advancement exposes one raw hit-die outcome per owned Class level", () => {
    let saved = null;
    const hpHandlers = {
        ...handlers,
        rules: {
            ...handlers.rules,
            setHitPointGain(occurrenceId, classLevel, hitDieValue) {
                saved = [occurrenceId, classLevel, hitDieValue];
            }
        }
    };
    const configuredBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [],
            progressionEntries: [{
                id: "fighter-entry",
                ordinal: 0,
                kind: "class",
                ruleConceptKey: "class.fighter",
                parentAdvancementEntryId: null,
                createdAt: "now",
                updatedAt: "now",
                level: 2
            }]
        }
    };
    const openGuided = { open: true, activeSection: "advancement", returnSheetMode: "view" };
    const currentRoutine = routine([], {}, false);
    currentRoutine.state.hitPointGains = [{
        id: "gain-one",
        advancementOccurrenceId: "fighter-entry",
        classLevel: 1,
        hitDieValue: 10,
        createdAt: "now",
        updatedAt: "now"
    }];

    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        currentRoutine,
        "actions",
        false,
        "view",
        openGuided,
        {
            occurrences: [{
                occurrenceId: "fighter-entry",
                conceptKey: "class.fighter",
                kind: "class",
                displayName: "Fighter",
                progression: { label: "Level", value: 2, formattedValue: "Level 2" }
            }]
        },
        {},
        hpHandlers
    );

    assert.equal(byAttribute(rendered, "data-hit-point-gain-key", "fighter-entry:1").length, 1);
    const levelTwo = byAttribute(rendered, "data-hit-point-gain-key", "fighter-entry:2")[0];
    assert.ok(levelTwo);
    const input = byTag(levelTwo, "input")[0];
    input.value = "7";
    const save = byTag(levelTwo, "button").find(button => button.textContent === "Save");
    assert.ok(save);
    save.dispatchEvent({ type: "click" });
    assert.deepEqual(saved, ["fighter-entry", 2, 7]);
});

test("Features & Traits renders Rules Core-granted features without replacing Character-owned Feats", () => {
    const supplied = render("features", {
        features: [{
            key: "feature.second-wind",
            label: "Second Wind",
            kind: "class-feature",
            state: "resolved",
            sourceConceptKey: "class.fighter",
            grantingSourceKind: "class",
            acquisitionLevel: 1,
            effects: [{ key: "effect", label: "Resource", value: "Second Wind use" }]
        }]
    });
    assert.equal(byAttribute(supplied, "data-feature-key", "feature.second-wind").length, 1);
    assert.match(visibleText(supplied), /Second Wind/);
    assert.match(visibleText(supplied), /Level 1/);
    assert.match(visibleText(supplied), /class\.fighter/);
    assert.match(visibleText(supplied), /Resource: Second Wind use/);
});

test("Guided Setup renders Rules Core choices and delegates the selected value without interpreting it", () => {
    let saved = null;
    const choiceHandlers = {
        ...handlers,
        rules: {
            ...handlers.rules,
            setChoice(choiceKey, value) { saved = [choiceKey, value]; }
        }
    };
    const configuredBuilder = {
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
    const openGuided = { open: true, activeSection: "review", returnSheetMode: "view" };
    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        routine([], {}, false),
        "actions",
        false,
        "view",
        openGuided,
        null,
        {
            ruleChoices: [{
                choiceKey: "spellcasting.resource-system",
                groupKey: "spellcasting",
                displayName: "Spellcasting Resource System",
                kind: "single-select",
                state: "choice-required",
                options: [
                    { value: "spell-slots", displayName: "Spell Slots" },
                    { value: "spell-points", displayName: "Spell Points" }
                ]
            }],
            projectionConflicts: [{
                conflictKey: "conflict.fixture",
                kind: "fixture",
                message: "Choose one resource system.",
                relatedMechanicKeys: [],
                relatedConceptKeys: []
            }]
        },
        choiceHandlers
    );

    const choice = byAttribute(rendered, "data-rule-choice-key", "spellcasting.resource-system")[0];
    assert.ok(choice);
    assert.match(visibleText(choice), /Spellcasting Resource System/);
    assert.match(visibleText(choice), /Spell Slots/);
    assert.match(visibleText(choice), /Spell Points/);
    assert.equal(byAttribute(rendered, "data-rule-conflict-key", "conflict.fixture").length, 1);

    const select = byTag(choice, "select")[0];
    select.value = "spell-points";
    const choose = byTag(choice, "button").find(button => button.textContent === "Choose");
    assert.ok(choose);
    choose.dispatchEvent({ type: "click" });
    assert.deepEqual(saved, ["spellcasting.resource-system", "spell-points"]);
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
    assert.doesNotMatch(source, /renderDefenseMechanicsCard\(mechanics\)/);
    assert.doesNotMatch(source, /renderCombatFundamentalsCard\(mechanics\)/);
    assert.match(source, /renderCombatSummaryBand\(/);
    assert.match(coreStatsSource, /renderHealthQuickCard\(mechanics, healthControl\)/);
    assert.doesNotMatch(source, /renderSavingThrowsCard\(mechanics\?\.savingThrows\)/);
    assert.match(source, /renderSavingThrowsCard\(detachedSavingThrows, true\)/);
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
                mechanical("skill.craft", "Craft", "-", {
                    kind: "skill",
                    family: "Craft",
                    isFamily: true
                }),
                mechanical("skill.craft-alchemy", "Craft (Alchemy)", "-", {
                    kind: "specialized-skill",
                    family: "Craft",
                    specialty: "alchemy",
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

    const family = byAttribute(rendered, "data-skill-family", "skill.craft")[0];
    const specialtyRow = byAttribute(rendered, "data-skill-id", "skill.craft-alchemy")[0];
    const specialty = byAttribute(rendered, "data-skill-disclosure", "skill.craft-alchemy")[0];
    assert.ok(family);
    assert.ok(specialty);
    assert.match(visibleText(family), /Craft/);
    assert.match(visibleText(specialtyRow), /Craft \(Alchemy\)/);
    assert.match(visibleText(specialty), /Family\s+Craft/);
    assert.match(visibleText(specialty), /Specialty\s+alchemy/);
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
    assert.match(visibleText(rendered), /Grapple/);
    assert.match(visibleText(rendered), /Damage Reduction/);
    assert.match(visibleText(rendered), /Spell Resistance/);
    assert.match(visibleText(rendered), /Nonlethal Damage/);

    const defensesCard = byClass(rendered, "dd-combat-band__defenses")[0];
    assert.ok(defensesCard);
    assert.match(visibleText(defensesCard), /Resistances/);
    assert.match(visibleText(defensesCard), /Immunities/);
    assert.match(visibleText(defensesCard), /Vulnerabilities/);
    assert.match(visibleText(defensesCard), /Damage Reduction/);
    assert.match(visibleText(defensesCard), /Spell Resistance/);

    const initiativeCard = byClass(rendered, "dd-combat-band__initiative")[0];
    assert.match(visibleText(initiativeCard), /Base Attack Bonus/);
    assert.match(visibleText(initiativeCard), /Grapple/);
    assert.equal(byClass(rendered, "dd-defense-card").length, 0);
    assert.equal(byClass(rendered, "dd-combat-fundamentals-card").length, 0);
});


test("Inventory currency stays denomination-agnostic and rejects unsafe browser integers", () => {
    const calls = [];
    const currentRoutine = routine([], {}, false);
    currentRoutine.state.currencyBalances = [{
        id: "33333333-3333-3333-3333-333333333333",
        currencyKey: "campaign-scrip",
        amount: -7,
        createdAt: "now",
        updatedAt: "now"
    }];

    const rendered = renderCharacterWorkspace(
        character,
        builder,
        currentRoutine,
        "inventory",
        false,
        "view",
        guidedBuilder,
        null,
        null,
        {
            ...handlers,
            routine: {
                ...handlers.routine,
                setCurrencyBalance(key, amount) { calls.push(["set", key, amount]); },
                removeCurrencyBalance(key) { calls.push(["remove", key]); }
            }
        }
    );

    const section = byClass(rendered, "dd-inventory-currency")[0];
    assert.ok(section);
    assert.match(visibleText(section), /campaign-scrip\s+-7/);
    assert.doesNotMatch(visibleText(section), /Copper|Silver|Electrum|Gold|Platinum/);

    const existing = byAttribute(section, "data-currency-key", "campaign-scrip")[0];
    const existingInput = byTag(existing, "input")[0];
    const existingButtons = byTag(existing, "button");
    existingInput.value = "42";
    existingButtons.find(button => button.textContent === "Save").dispatchEvent({ type: "click" });
    existingButtons.find(button => button.textContent === "Remove").dispatchEvent({ type: "click" });

    const form = byTag(section, "form")[0];
    const inputs = byTag(form, "input");
    inputs[0].value = "gp";
    inputs[1].value = String(Number.MAX_SAFE_INTEGER + 1);
    form.dispatchEvent({ type: "submit", preventDefault() {} });
    assert.deepEqual(calls, [
        ["set", "campaign-scrip", 42],
        ["remove", "campaign-scrip"]
    ]);

    inputs[0].value = "campaign-scrip";
    inputs[1].value = "-125";
    form.dispatchEvent({ type: "submit", preventDefault() {} });
    assert.deepEqual(calls.at(-1), ["set", "campaign-scrip", -125]);

    const readonly = renderCharacterWorkspace(
        character,
        builder,
        currentRoutine,
        "inventory",
        true,
        "view",
        guidedBuilder,
        null,
        null,
        handlers
    );
    const readonlySection = byClass(readonly, "dd-inventory-currency")[0];
    assert.match(visibleText(readonlySection), /campaign-scrip\s+-7/);
    assert.equal(byTag(readonlySection, "form").length, 0);
});
