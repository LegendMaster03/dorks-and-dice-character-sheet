import test from "node:test";
import assert from "node:assert/strict";
import { createInitialState, reduceAppState } from "../.test-dist/app-state.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const basic = {
    characterId,
    name: "Basic Hero",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: [],
    hasRichSheet: false,
    sheet: null
};
const rich = {
    ...basic,
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
    foundationalSelections: [{
        id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        category: "raceSpecies",
        ruleConceptKey: "race:elf",
        createdAt: "now",
        updatedAt: "now"
    }],
    progressionEntries: [{
        id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        ordinal: 0,
        kind: "class",
        ruleConceptKey: "class:wizard",
        parentAdvancementEntryId: null,
        createdAt: "now",
        updatedAt: "now"
    }]
};

test("new route starts ready without allocating identity", () => {
    const state = createInitialState({ kind: "new" });
    assert.deepEqual(state.screen, { kind: "new-character", status: "ready" });
    assert.equal(state.builder.status, "idle");
});

test("character route models loading, basic, submitting, and error states explicitly", () => {
    let state = createInitialState({ kind: "character", characterId });
    assert.equal(state.screen.kind, "loading");

    state = reduceAppState(state, { type: "character-loaded", character: basic });
    assert.equal(state.screen.kind, "basic-character");

    state = reduceAppState(state, { type: "sheet-submit-started", character: basic });
    assert.equal(state.screen.kind, "submitting");

    state = reduceAppState(state, { type: "load-failed", message: "failed" });
    assert.deepEqual(state.screen, { kind: "error", message: "failed" });
});

test("rich builder state models Rules Core resolution, chooser loading, search, and saving", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-load-started" });
    assert.equal(state.builder.status, "loading");

    state = reduceAppState(state, { type: "builder-loaded", build });
    assert.equal(state.builder.status, "ready");
    assert.deepEqual(state.builder.references.raceSpecies, { status: "loading", conceptKey: "race:elf" });
    assert.deepEqual(state.builder.references.startingClass, { status: "loading", conceptKey: "class:wizard" });

    state = reduceAppState(state, {
        type: "rule-reference-resolved",
        target: "raceSpecies",
        conceptKey: "race:elf",
        reference: { status: "unavailable", conceptKey: "race:elf" }
    });
    assert.equal(state.builder.references.raceSpecies.status, "unavailable");

    state = reduceAppState(state, { type: "chooser-opened", target: "startingClass" });
    state = reduceAppState(state, {
        type: "chooser-load-started",
        target: "startingClass",
        query: "wiz"
    });
    assert.equal(state.builder.chooser.status, "loading");
    assert.equal(state.builder.chooser.query, "wiz");

    const result = {
        ruleConceptId: "cccccccc-cccc-cccc-cccc-cccccccccccc",
        conceptKey: "class:wizard",
        entityType: "class",
        displayName: "Wizard",
        effectiveDecisionKind: "select",
        hasCampaignOverride: false,
        sourceEntityId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        sourceEntityRevisionId: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        sourceRevisionNumber: 1,
        sourceEntityName: "Wizard",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    };
    state = reduceAppState(state, {
        type: "chooser-loaded",
        target: "startingClass",
        query: "wiz",
        results: [result]
    });
    assert.equal(state.builder.chooser.status, "ready");
    assert.equal(state.builder.chooser.results[0].displayName, "Wizard");

    state = reduceAppState(state, { type: "selection-save-started", target: "startingClass" });
    assert.equal(state.builder.saving, "startingClass");
    state = reduceAppState(state, { type: "selection-save-failed", message: "save failed" });
    assert.equal(state.builder.saving, null);
    assert.equal(state.builder.saveError, "save failed");
});

test("stale Rules Core resolution can not overwrite a replaced stored concept", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });
    const replacement = {
        ...build,
        foundationalSelections: [{
            ...build.foundationalSelections[0],
            ruleConceptKey: "race:dwarf"
        }]
    };
    state = reduceAppState(state, { type: "selection-saved", build: replacement });
    state = reduceAppState(state, {
        type: "rule-reference-resolved",
        target: "raceSpecies",
        conceptKey: "race:elf",
        reference: { status: "unavailable", conceptKey: "race:elf" }
    });

    assert.deepEqual(state.builder.references.raceSpecies, {
        status: "loading",
        conceptKey: "race:dwarf"
    });
});

test("archived and rich Character screens remain distinct", () => {
    const initial = createInitialState({ kind: "character", characterId });
    const archived = reduceAppState(initial, {
        type: "character-loaded",
        character: { ...rich, lifecycle: "Archived", archivedAt: "2026-09-16T02:00:00Z" }
    });
    assert.equal(archived.screen.kind, "archived");

    const activeRich = reduceAppState(initial, {
        type: "character-loaded",
        character: rich
    });
    assert.equal(activeRich.screen.kind, "rich-character");
});
