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

test("new route starts ready without allocating identity", () => {
    const state = createInitialState({ kind: "new" });
    assert.deepEqual(state.screen, { kind: "new-character", status: "ready" });
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

test("archived and rich Character states remain distinct", () => {
    const initial = createInitialState({ kind: "character", characterId });
    const archived = reduceAppState(initial, {
        type: "character-loaded",
        character: { ...basic, lifecycle: "Archived", archivedAt: "2026-09-16T02:00:00Z" }
    });
    assert.equal(archived.screen.kind, "archived");

    const rich = reduceAppState(initial, {
        type: "character-loaded",
        character: { ...basic, hasRichSheet: true, sheet: { schemaVersion: 1, builderStatus: "BuildInProgress", createdAt: "now", updatedAt: "now" } }
    });
    assert.equal(rich.screen.kind, "rich-character");
});
