import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createInitialState, reduceAppState } from "../.test-dist/app-state.js";
import {
    createCharacterHeaderModel,
    getChoiceActionPolicy,
    MECHANIC_PLACEHOLDERS,
    SHEET_SECTIONS,
    toRuleReferenceDisplay
} from "../.test-dist/ui/sheet-model.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const character = {
    characterId,
    name: "Sai Cithreth",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: ["11111111-1111-1111-1111-111111111111"],
    hasRichSheet: true,
    sheet: {
        schemaVersion: 1,
        builderStatus: "BuildInProgress",
        createdAt: "now",
        updatedAt: "now"
    }
};
const build = {
    characterId,
    builderStatus: "BuildInProgress",
    readOnly: false,
    foundationalSelections: [],
    progressionEntries: []
};
const resolved = (conceptKey, entityType, displayName) => ({
    status: "resolved",
    conceptKey,
    rule: {
        ruleConceptId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        conceptKey,
        entityType,
        displayName,
        sourceEntityName: displayName,
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    }
});

function builder(overrides = {}) {
    return {
        status: "ready",
        build,
        references: {
            raceSpecies: resolved("race:human", "race", "Human"),
            startingClass: resolved("class:wizard", "class", "Wizard"),
            subclass: resolved("subclass:wizard:evocation", "subclass", "School of Evocation")
        },
        chooser: { kind: "closed" },
        saving: null,
        ...overrides
    };
}

test("header model presents real Character name and resolved Race, Class, and Subclass", () => {
    const model = createCharacterHeaderModel(character, builder(), false);
    assert.equal(model.name, "Sai Cithreth");
    assert.equal(model.raceSpecies.value, "Human");
    assert.equal(model.startingClass.value, "Wizard");
    assert.equal(model.subclass.value, "School of Evocation");
    assert.equal(model.campaignContext, "1 Campaign association");
});

test("header does not present an uninitialized builder as real empty selections", () => {
    const idleBuilder = {
        ...builder(),
        status: "idle",
        build: null,
        references: {
            raceSpecies: { status: "none" },
            startingClass: { status: "none" },
            subclass: { status: "none" }
        }
    };
    const model = createCharacterHeaderModel(
        { ...character, hasRichSheet: false, sheet: null },
        idleBuilder,
        false);
    assert.equal(model.raceSpecies.value, "Digital sheet not initialized");
    assert.equal(model.startingClass.value, "Digital sheet not initialized");
    assert.equal(model.subclass.value, "Digital sheet not initialized");
});

test("unavailable persisted rule references remain visible instead of disappearing", () => {
    const reference = { status: "unavailable", conceptKey: "race:missing" };
    const display = toRuleReferenceDisplay(reference);
    assert.equal(display.value, "Unavailable saved selection");
    assert.equal(display.detail, "race:missing");
});

test("archived Character model is explicitly read-only", () => {
    const archived = { ...character, lifecycle: "Archived", archivedAt: "2026-09-16T02:00:00Z" };
    const model = createCharacterHeaderModel(archived, builder(), true);
    assert.equal(model.lifecycleLabel, "Archived");
    assert.equal(model.readOnly, true);
});

test("builder mutation policy removes editing affordances in read-only state", () => {
    const selected = resolved("class:wizard", "class", "Wizard");
    assert.deepEqual(getChoiceActionPolicy(selected, true, true, null), {
        canChoose: false,
        canClear: false,
        chooseLabel: "Replace"
    });
});

test("unfinished mechanics are explicit placeholders and contain no fabricated numeric values", () => {
    assert.ok(MECHANIC_PLACEHOLDERS.length >= 16);
    for (const placeholder of MECHANIC_PLACEHOLDERS) {
        assert.equal(Object.hasOwn(placeholder, "value"), false);
        assert.equal(/(^|\s)[+-]?\d+(?:\.\d+)?(?:\s|$)/.test(placeholder.message), false);
    }
});

test("primary sheet navigation establishes the requested scalable content regions", () => {
    assert.deepEqual(SHEET_SECTIONS.map(section => section.label), [
        "Actions",
        "Spells",
        "Inventory",
        "Features & Traits",
        "Notes"
    ]);
});

test("sheet section selection is explicit application state rather than DOM-only state", () => {
    let state = createInitialState({ kind: "character", characterId });
    assert.equal(state.activeSheetSection, "actions");
    state = reduceAppState(state, { type: "sheet-section-selected", section: "inventory" });
    assert.equal(state.activeSheetSection, "inventory");
});

const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
const appSource = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");

test("UI shell defines materially different tablet and mobile compositions", () => {
    assert.match(css, /@media \(max-width: 1099px\)/);
    assert.match(css, /@media \(max-width: 720px\)/);
    assert.match(css, /\.dd-sheet__workspace\s*\{[^}]*grid-template-columns:\s*1fr;/s);
    assert.match(css, /\.dd-core-stats__abilities\s*\{[^}]*grid-template-columns:\s*repeat\(2,/s);
});

test("responsive shell is driven by intentional placeholders rather than fabricated Character mechanics", () => {
    assert.match(sheetSource, /MECHANIC_PLACEHOLDERS/);
    assert.doesNotMatch(sheetSource, />?\s*(?:10|30|37)\s*(?:<|ft\.|HP|AC)/i);
});

test("embedded module loads its stylesheet from the same Tool Module asset subtree", () => {
    assert.match(appSource, /new URL\("\.\/app\.css", import\.meta\.url\)/);
    assert.match(appSource, /data-character-sheet-stylesheet/);
});
