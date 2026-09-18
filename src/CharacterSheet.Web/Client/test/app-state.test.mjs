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
    baseAbilityScoreInputs: [],
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
    assert.deepEqual(state.builder.references.subclass, { status: "none" });

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
        editionDisplayName: "5e",
        relationships: []
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

test("subclass reference is loaded from its Character-owned parented advancement entry", () => {
    const subclassBuild = {
        ...build,
        progressionEntries: [
            ...build.progressionEntries,
            {
                id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
                ordinal: null,
                kind: "subclass",
                ruleConceptKey: "subclass.wizard.evocation",
                parentAdvancementEntryId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                createdAt: "now",
                updatedAt: "now"
            }
        ]
    };

    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build: subclassBuild });
    assert.deepEqual(state.builder.references.subclass, {
        status: "loading",
        conceptKey: "subclass.wizard.evocation"
    });
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

test("ability mutation state replaces the coherent build for set, replace, and clear", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });

    state = reduceAppState(state, { type: "ability-save-started", abilityKey: "strength" });
    assert.equal(state.builder.savingAbility, "strength");

    const setBuild = {
        ...build,
        baseAbilityScoreInputs: [{
            id: "ffffffff-ffff-ffff-ffff-ffffffffffff",
            abilityKey: "strength",
            score: 15,
            createdAt: "now",
            updatedAt: "now"
        }]
    };
    state = reduceAppState(state, { type: "ability-saved", build: setBuild });
    assert.equal(state.builder.savingAbility, null);
    assert.equal(state.builder.build.baseAbilityScoreInputs[0].score, 15);

    const replacementBuild = {
        ...setBuild,
        baseAbilityScoreInputs: [{
            ...setBuild.baseAbilityScoreInputs[0],
            score: 18,
            updatedAt: "later"
        }]
    };
    state = reduceAppState(state, { type: "ability-save-started", abilityKey: "strength" });
    state = reduceAppState(state, { type: "ability-saved", build: replacementBuild });
    assert.equal(state.builder.build.baseAbilityScoreInputs[0].score, 18);

    const clearedBuild = { ...replacementBuild, baseAbilityScoreInputs: [] };
    state = reduceAppState(state, { type: "ability-save-started", abilityKey: "strength" });
    state = reduceAppState(state, { type: "ability-saved", build: clearedBuild });
    assert.deepEqual(state.builder.build.baseAbilityScoreInputs, []);
});

test("ability mutation failure is attached to the attempted ability", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });
    state = reduceAppState(state, { type: "ability-save-started", abilityKey: "wisdom" });
    state = reduceAppState(state, {
        type: "ability-save-failed",
        abilityKey: "wisdom",
        message: "save failed"
    });

    assert.equal(state.builder.savingAbility, null);
    assert.deepEqual(state.builder.abilitySaveError, {
        abilityKey: "wisdom",
        message: "save failed"
    });
});


test("rich Character defaults to View mode and structural editing is explicit", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });

    assert.equal(state.sheetMode, "view");
    assert.equal(state.guidedBuilder.open, false);

    state = reduceAppState(state, { type: "sheet-edit-entered" });
    assert.equal(state.sheetMode, "edit");

    state = reduceAppState(state, { type: "sheet-edit-exited" });
    assert.equal(state.sheetMode, "view");
});

test("archived and backend read-only Characters can not enter Edit Mode or Guided Builder", () => {
    let archivedState = createInitialState({ kind: "character", characterId });
    archivedState = reduceAppState(archivedState, {
        type: "character-loaded",
        character: { ...rich, lifecycle: "Archived", archivedAt: "2026-09-18T00:00:00Z" }
    });
    archivedState = reduceAppState(archivedState, { type: "builder-loaded", build });
    archivedState = reduceAppState(archivedState, { type: "sheet-edit-entered" });
    archivedState = reduceAppState(archivedState, { type: "guided-builder-opened" });
    assert.equal(archivedState.sheetMode, "view");
    assert.equal(archivedState.guidedBuilder.open, false);

    let readOnlyState = createInitialState({ kind: "character", characterId });
    readOnlyState = reduceAppState(readOnlyState, { type: "character-loaded", character: rich });
    readOnlyState = reduceAppState(readOnlyState, {
        type: "builder-loaded",
        build: { ...build, readOnly: true }
    });
    readOnlyState = reduceAppState(readOnlyState, { type: "sheet-edit-entered" });
    readOnlyState = reduceAppState(readOnlyState, { type: "guided-builder-opened" });
    assert.equal(readOnlyState.sheetMode, "view");
    assert.equal(readOnlyState.guidedBuilder.open, false);
});

test("Guided Builder is optional, directly navigable, and does not replace Character state", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });

    assert.equal(state.screen.kind, "rich-character");
    assert.equal(state.guidedBuilder.open, false);

    state = reduceAppState(state, { type: "guided-builder-opened" });
    assert.equal(state.guidedBuilder.open, true);
    assert.equal(state.screen.kind, "rich-character");
    assert.equal(state.builder.build, build);

    state = reduceAppState(state, {
        type: "guided-builder-section-selected",
        section: "abilities"
    });
    assert.equal(state.guidedBuilder.activeSection, "abilities");
    assert.equal(state.screen.kind, "rich-character");

    state = reduceAppState(state, { type: "guided-builder-closed" });
    assert.equal(state.guidedBuilder.open, false);
    assert.equal(state.sheetMode, "view");
    assert.equal(state.screen.kind, "rich-character");
});

test("incomplete build configuration never prevents normal rich Character rendering", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, {
        type: "builder-loaded",
        build: {
            ...build,
            foundationalSelections: [],
            progressionEntries: [],
            baseAbilityScoreInputs: []
        }
    });

    assert.equal(state.screen.kind, "rich-character");
    assert.equal(state.sheetMode, "view");
    assert.equal(state.guidedBuilder.open, false);
});


test("routine state loads independently from Character build state", () => {
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "builder-loaded", build });
    state = reduceAppState(state, { type: "routine-load-started" });
    assert.equal(state.builder.status, "ready");
    assert.equal(state.routine.status, "loading");

    state = reduceAppState(state, { type: "routine-load-failed", message: "state unavailable" });
    assert.equal(state.builder.status, "ready");
    assert.equal(state.routine.status, "error");
    assert.equal(state.routine.message, "state unavailable");
});

test("routine state preserves duplicate inventory occurrences and protects stale reference resolution", () => {
    const occurrenceA = "33333333-3333-3333-3333-333333333333";
    const occurrenceB = "44444444-4444-4444-4444-444444444444";
    const routine = {
        characterId,
        readOnly: false,
        inventoryItemOccurrences: [
            { id: occurrenceA, ruleConceptKey: "item:rope", createdAt: "now" },
            { id: occurrenceB, ruleConceptKey: "item:rope", createdAt: "later" }
        ],
        notes: []
    };
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "character-loaded", character: rich });
    state = reduceAppState(state, { type: "routine-loaded", state: routine });

    assert.equal(state.routine.state.inventoryItemOccurrences.length, 2);
    assert.deepEqual(state.routine.references[occurrenceA], { status: "loading", conceptKey: "item:rope" });
    assert.deepEqual(state.routine.references[occurrenceB], { status: "loading", conceptKey: "item:rope" });

    state = reduceAppState(state, {
        type: "routine-reference-resolved",
        occurrenceId: occurrenceA,
        conceptKey: "item:old",
        reference: { status: "unavailable", conceptKey: "item:old" }
    });
    assert.deepEqual(state.routine.references[occurrenceA], { status: "loading", conceptKey: "item:rope" });
});

test("routine mutation state is explicit and coherent response replacement clears pending state", () => {
    const routine = {
        characterId,
        readOnly: false,
        inventoryItemOccurrences: [],
        notes: []
    };
    const withNote = {
        ...routine,
        notes: [{
            id: "55555555-5555-5555-5555-555555555555",
            content: "Remember this",
            createdAt: "now",
            updatedAt: "now"
        }]
    };
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "routine-loaded", state: routine });
    state = reduceAppState(state, { type: "routine-mutation-started", kind: "note-add" });
    assert.deepEqual(state.routine.mutation, { kind: "note-add", entryId: undefined });

    state = reduceAppState(state, { type: "routine-mutation-succeeded", state: withNote });
    assert.equal(state.routine.mutation, null);
    assert.equal(state.routine.state.notes[0].content, "Remember this");

    state = reduceAppState(state, { type: "routine-mutation-started", kind: "note-update", entryId: withNote.notes[0].id });
    state = reduceAppState(state, { type: "routine-mutation-failed", message: "save failed" });
    assert.equal(state.routine.mutation, null);
    assert.equal(state.routine.mutationError, "save failed");
});


test("inventory chooser request state ignores stale search results", () => {
    const routine = {
        characterId,
        readOnly: false,
        inventoryItemOccurrences: [],
        notes: []
    };
    let state = createInitialState({ kind: "character", characterId });
    state = reduceAppState(state, { type: "routine-loaded", state: routine });
    state = reduceAppState(state, { type: "inventory-chooser-opened" });
    state = reduceAppState(state, { type: "inventory-chooser-load-started", query: "rope" });
    state = reduceAppState(state, { type: "inventory-chooser-query-changed", query: "sword" });
    state = reduceAppState(state, {
        type: "inventory-chooser-loaded",
        query: "rope",
        results: [{ conceptKey: "item:rope", entityType: "item", displayName: "Rope" }]
    });
    assert.equal(state.routine.inventoryChooser.status, "loading");
    assert.equal(state.routine.inventoryChooser.query, "sword");
});
