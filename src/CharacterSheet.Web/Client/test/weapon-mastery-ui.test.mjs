import test from "node:test";
import assert from "node:assert/strict";
import { renderCharacterWorkspace } from "../.test-dist/ui/sheet.js";
import {
    renderFeaturesSection,
    resolveWeaponMasteries
} from "../.test-dist/features/features/features-section.js";

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

function byAttribute(root, name, value) {
    return walk(root).filter(node => node.getAttribute(name) === value);
}

function byTag(root, tagName) {
    return walk(root).filter(node => node.tagName === tagName.toUpperCase());
}

function visibleText(root) {
    return walk(root).map(node => node.textContent).filter(Boolean).join(" ");
}

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const character = {
    characterId,
    name: "Mastery Test",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: [],
    hasRichSheet: true,
    sheet: {
        schemaVersion: 1,
        builderStatus: "BuildInProgress",
        createdAt: "now",
        updatedAt: "now"
    }
};

const builder = {
    status: "ready",
    build: {
        characterId,
        builderStatus: "BuildInProgress",
        readOnly: false,
        foundationalSelections: [],
        baseAbilityScoreInputs: [],
        progressionEntries: [{
            id: "11111111-1111-1111-1111-111111111111",
            ordinal: 0,
            kind: "class",
            ruleConceptKey: "class:fighter",
            parentAdvancementEntryId: null,
            createdAt: "now",
            updatedAt: "now",
            level: 1
        }]
    },
    references: {
        species: { status: "none" },
        subspecies: { status: "none" },
        background: { status: "none" },
        deity: { status: "none" },
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

const routine = {
    status: "ready",
    state: {
        characterId,
        readOnly: false,
        currentHitPoints: null,
        deathSaves: { successes: 0, failures: 0 },
        inventoryItemOccurrences: [],
        notes: [],
        conditions: []
    },
    references: {},
    inventoryChooser: { kind: "closed" },
    spellChooser: { kind: "closed" },
    conditionChooser: { kind: "closed" },
    mutation: null
};

const guidedBuilder = {
    open: true,
    activeSection: "class",
    returnSheetMode: "view"
};

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
        setInspiration() { return Promise.resolve(true); },
        setProfile() {}, setAdvancementProgress() {},
        setCurrencyBalance() {}, removeCurrencyBalance() {},
        uploadArt() {}, setPortrait() {}, clearPortrait() {}, deleteArt() {},
        artContentUrl(assetId) { return `/art/${assetId}`; },
        setCurrentHitPoints() {}, setDeathSaves() {},
        addNote() {}, updateNote() {}, deleteNote() {},
        openInventoryChooser() {}, closeInventoryChooser() {}, searchInventory() {},
        addInventoryItem() {}, removeInventoryItem() {}
    },
    selectSection() {}, enterEditMode() {}, leaveEditMode() {},
    openGuidedBuilder() {}, closeGuidedBuilder() {}, selectGuidedBuilderSection() {}
};

const options = [
    { value: "item:longsword", displayName: "Longsword — Sap", conceptKey: "item:longsword" },
    { value: "item:greataxe", displayName: "Greataxe — Cleave", conceptKey: "item:greataxe" },
    { value: "item:shortsword", displayName: "Shortsword — Vex", conceptKey: "item:shortsword" }
];

const groupKey = "choice-group.class:fighter.weapon-mastery.0";

function masteryChoice(slot, state, selectedValue) {
    return {
        choiceKey: `choice.class:fighter.weapon-mastery.0.${slot}`,
        groupKey,
        displayName: `Fighter Weapon Mastery ${slot + 1} of 2`,
        kind: "weapon-mastery",
        state,
        options,
        selectedValue,
        sourceConceptKey: "class:fighter"
    };
}

function render(ruleChoices) {
    return renderCharacterWorkspace(
        character,
        builder,
        routine,
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        { ruleChoices },
        handlers);
}

test("Weapon Mastery sibling choices do not offer a weapon already selected in the group", () => {
    const rendered = render([
        masteryChoice(0, "resolved", "item:longsword"),
        masteryChoice(1, "choice-required", undefined)
    ]);

    const secondCard = byAttribute(
        rendered,
        "data-rule-choice-key",
        "choice.class:fighter.weapon-mastery.0.1")[0];
    assert.ok(secondCard);

    const select = byTag(secondCard, "select")[0];
    assert.ok(select);
    const values = select.children.map(option => option.value);
    assert.equal(values.includes("item:longsword"), false);
    assert.equal(values.includes("item:greataxe"), true);
    assert.equal(values.includes("item:shortsword"), true);
});

test("Weapon Mastery duplicate persisted choices surface a warning and require replacement", () => {
    const rendered = render([
        masteryChoice(0, "resolved", "item:longsword"),
        masteryChoice(1, "choice-required", "item:longsword")
    ]);

    const secondCard = byAttribute(
        rendered,
        "data-rule-choice-key",
        "choice.class:fighter.weapon-mastery.0.1")[0];
    assert.ok(secondCard);
    assert.match(visibleText(secondCard), /already selected in another choice/i);

    const select = byTag(secondCard, "select")[0];
    assert.ok(select);
    assert.equal(select.value, "");
    assert.equal(
        select.children.some(option => option.value === "item:longsword"),
        false);
});

test("source-unavailable Weapon Mastery choices do not expose futile selection controls", () => {
    const rendered = render([
        masteryChoice(0, "source-unavailable", undefined)
    ]);

    const card = byAttribute(
        rendered,
        "data-rule-choice-key",
        "choice.class:fighter.weapon-mastery.0.0")[0];
    assert.ok(card);
    assert.match(visibleText(card), /can not fully represent the legal options/i);
    assert.equal(byTag(card, "select").length, 0);
});

test("resolved Weapon Mastery normalization merges repeated weapon grants", () => {
    const first = masteryChoice(0, "resolved", "item:longsword");
    const duplicateFromAnotherGrant = {
        ...masteryChoice(1, "resolved", "item:longsword"),
        choiceKey: "choice.feat:weapon-master.weapon-mastery.0.0",
        groupKey: "choice-group.feat:weapon-master.weapon-mastery.0",
        displayName: "Weapon Master Weapon Mastery",
        sourceConceptKey: "feat:weapon-master"
    };

    const normalized = resolveWeaponMasteries([first, duplicateFromAnotherGrant]);
    assert.equal(normalized.length, 1);
    assert.equal(normalized[0].value, "item:longsword");
    assert.deepEqual(normalized[0].sourceConceptKeys, ["class:fighter", "feat:weapon-master"]);
});

test("resolved Weapon Mastery choices remain visible on the normal Features and Traits sheet", () => {
    const first = masteryChoice(0, "resolved", "item:longsword");
    const duplicateFromAnotherGrant = {
        ...masteryChoice(1, "resolved", "item:longsword"),
        choiceKey: "choice.feat:weapon-master.weapon-mastery.0.0",
        groupKey: "choice-group.feat:weapon-master.weapon-mastery.0",
        displayName: "Weapon Master Weapon Mastery",
        sourceConceptKey: "feat:weapon-master"
    };
    const rendered = renderFeaturesSection(
        builder,
        false,
        false,
        [],
        [
            first,
            duplicateFromAnotherGrant,
            masteryChoice(1, "choice-required", undefined)
        ],
        handlers.feats);

    assert.match(visibleText(rendered), /Weapon Mastery/);
    assert.match(visibleText(rendered), /Longsword — Sap/);
    assert.equal(byAttribute(rendered, "data-weapon-mastery", "item:longsword").length, 1);
    assert.match(visibleText(rendered), /class:fighter/);
    assert.match(visibleText(rendered), /feat:weapon-master/);
    assert.doesNotMatch(visibleText(rendered), /Greataxe — Cleave/);
});
