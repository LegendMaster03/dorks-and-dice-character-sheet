import assert from "node:assert/strict";
import test from "node:test";
import {
    addCharacterNote,
    addInventoryItemOccurrence,
    buildCharacterStateBackendUrl,
    loadCharacterState,
    removeCharacterNote,
    removeInventoryItemOccurrence,
    setCharacterCurrentHitPoints,
    updateCharacterNote
} from "../.test-dist/character-state-api.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const noteId = "11111111-1111-1111-1111-111111111111";
const occurrenceId = "22222222-2222-2222-2222-222222222222";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: `/tool-host/character-sheet/context?toolRoute=%2Fcharacters%2F${characterId}`,
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const state = {
    characterId,
    readOnly: false,
    currentHitPoints: null,
    inventoryItemOccurrences: [],
    notes: []
};

test("routine-state API remains behind Character Sheet Tool Host authorization", () => {
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "health"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/health`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "inventory", occurrenceId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/inventory/${occurrenceId}`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "notes", noteId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/notes/${noteId}`);
});

test("routine-state loading uses the coherent state resource", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push([String(input), init?.method ?? "GET"]);
        return Response.json(state);
    };

    const result = await loadCharacterState(environment, characterId, fetcher);

    assert.equal(result.characterId, characterId);
    assert.deepEqual(calls, [[
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state`,
        "GET"
    ]]);
});

test("routine-state mutations send only Character-owned state inputs and return coherent state", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(state);
    };

    await setCharacterCurrentHitPoints(environment, characterId, -4, fetcher);
    await addInventoryItemOccurrence(environment, characterId, "item:rope", fetcher);
    await removeInventoryItemOccurrence(environment, characterId, occurrenceId, fetcher);
    await addCharacterNote(environment, characterId, "First note", fetcher);
    await updateCharacterNote(environment, characterId, noteId, "Updated note", fetcher);
    await removeCharacterNote(environment, characterId, noteId, fetcher);

    assert.deepEqual(JSON.parse(calls[0].body), { currentHitPoints: -4 });
    assert.equal(calls[0].method, "PUT");
    assert.deepEqual(JSON.parse(calls[1].body), { conceptKey: "item:rope" });
    assert.equal(calls[1].method, "POST");
    assert.equal(calls[2].method, "DELETE");
    assert.equal(calls[2].body, undefined);
    assert.deepEqual(JSON.parse(calls[3].body), { content: "First note" });
    assert.deepEqual(JSON.parse(calls[4].body), { content: "Updated note" });
    assert.equal(calls[5].method, "DELETE");
    assert.equal(calls.some(call => String(call.body).includes("displayName")), false);
});
