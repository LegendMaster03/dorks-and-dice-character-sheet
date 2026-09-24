import assert from "node:assert/strict";
import test from "node:test";
import {
    addCharacterNote,
    addInventoryItemOccurrence,
    buildCharacterStateBackendUrl,
    loadCharacterState,
    removeCharacterCurrencyBalance,
    removeCharacterHitPointGain,
    removeCharacterNote,
    removeCharacterRulesInput,
    removeInventoryItemOccurrence,
    setCharacterAdvancementProgress,
    setCharacterCurrencyBalance,
    setCharacterCurrentHitPoints,
    setCharacterDeathSaves,
    setCharacterHitPointGain,
    setCharacterProfile,
    setCharacterRulesInput,
    updateCharacterNote,
    updateInventoryItemOccurrence
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
    deathSaves: { successes: 0, failures: 0 },
    inventoryItemOccurrences: [],
    notes: [],
    conditions: []
};

test("routine-state API remains behind Character Sheet Tool Host authorization", () => {
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "progression"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/progression`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "currency"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/currency`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "profile"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/profile`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "health"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/health`);
    assert.equal(
        buildCharacterStateBackendUrl(environment, characterId, "death-saves"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/death-saves`);
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

test("profile mutation sends only Character-authored descriptive state", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(state);
    };
    const profile = {
        alignment: "Neutral",
        deity: "The Traveler",
        age: "34",
        height: null,
        weight: null,
        appearance: null,
        personalityTraits: "Curious",
        ideals: "Freedom",
        bonds: null,
        flaws: null,
        backstory: "History",
        alliesAndOrganizations: null,
        symbol: null
    };

    await setCharacterProfile(environment, characterId, profile, fetcher);

    assert.equal(calls[0].method, "PUT");
    assert.equal(
        calls[0].input,
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/profile`);
    assert.deepEqual(JSON.parse(calls[0].body), profile);
    assert.equal("size" in JSON.parse(calls[0].body), false);
    assert.equal("background" in JSON.parse(calls[0].body), false);
});


test("advancement progress mutation stays neutral instead of inventing XP semantics", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(state);
    };

    await setCharacterAdvancementProgress(environment, characterId, 1450, fetcher);
    await setCharacterAdvancementProgress(environment, characterId, null, fetcher);

    assert.equal(calls[0].method, "PUT");
    assert.equal(
        calls[0].input,
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/progression`);
    assert.deepEqual(JSON.parse(calls[0].body), { value: 1450 });
    assert.deepEqual(JSON.parse(calls[1].body), { value: null });
    assert.equal(String(calls[0].body).toLowerCase().includes("xp"), false);
});

test("currency mutations preserve arbitrary Character-owned denominations", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(state);
    };

    await setCharacterCurrencyBalance(
        environment,
        characterId,
        "campaign-scrip",
        -7,
        fetcher);
    await removeCharacterCurrencyBalance(
        environment,
        characterId,
        "campaign-scrip",
        fetcher);

    assert.equal(
        calls[0].input,
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/currency`);
    assert.equal(calls[0].method, "PUT");
    assert.deepEqual(JSON.parse(calls[0].body), { key: "campaign-scrip", amount: -7 });
    assert.equal(
        calls[1].input,
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/state/currency?key=campaign-scrip`);
    assert.equal(calls[1].method, "DELETE");
    assert.equal(calls[1].body, undefined);
});

test("routine-state mutations send only Character-owned state inputs and return coherent state", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(state);
    };

    await setCharacterCurrentHitPoints(environment, characterId, -4, fetcher);
    await setCharacterDeathSaves(environment, characterId, 2, 1, fetcher);
    await setCharacterRulesInput(
        environment,
        characterId,
        { kind: "choice", key: "spellcasting.resource-system", textValue: "spell-points" },
        fetcher);
    await removeCharacterRulesInput(
        environment,
        characterId,
        "choice",
        "spellcasting.resource-system",
        fetcher);
    await setCharacterHitPointGain(
        environment,
        characterId,
        occurrenceId,
        3,
        7,
        fetcher);
    await removeCharacterHitPointGain(
        environment,
        characterId,
        occurrenceId,
        3,
        fetcher);
    await addInventoryItemOccurrence(environment, characterId, "item:rope", fetcher);
    await updateInventoryItemOccurrence(
        environment,
        characterId,
        occurrenceId,
        {
            quantity: 3,
            isCarried: true,
            isEquipped: true,
            isAttuned: false,
            containerOccurrenceId: null
        },
        fetcher);
    await removeInventoryItemOccurrence(environment, characterId, occurrenceId, fetcher);
    await addCharacterNote(environment, characterId, "First note", fetcher);
    await updateCharacterNote(environment, characterId, noteId, "Updated note", fetcher);
    await removeCharacterNote(environment, characterId, noteId, fetcher);

    assert.deepEqual(JSON.parse(calls[0].body), { currentHitPoints: -4 });
    assert.equal(calls[0].method, "PUT");
    assert.deepEqual(JSON.parse(calls[1].body), { successes: 2, failures: 1 });
    assert.equal(calls[1].method, "PUT");
    assert.deepEqual(JSON.parse(calls[2].body), {
        kind: "choice",
        key: "spellcasting.resource-system",
        textValue: "spell-points"
    });
    assert.equal(calls[2].method, "PUT");
    assert.match(calls[3].input, /\/rules-inputs\/choice\?key=spellcasting\.resource-system$/);
    assert.equal(calls[3].method, "DELETE");
    assert.deepEqual(JSON.parse(calls[4].body), { hitDieValue: 7 });
    assert.equal(calls[4].input.endsWith(`/hit-point-gains/${occurrenceId}/3`), true);
    assert.equal(calls[4].method, "PUT");
    assert.equal(calls[5].method, "DELETE");
    assert.equal(calls[5].body, undefined);
    assert.deepEqual(JSON.parse(calls[6].body), { conceptKey: "item:rope" });
    assert.equal(calls[6].method, "POST");
    assert.deepEqual(JSON.parse(calls[7].body), {
        quantity: 3,
        isCarried: true,
        isEquipped: true,
        isAttuned: false,
        containerOccurrenceId: null
    });
    assert.equal(calls[7].method, "PUT");
    assert.equal(calls[8].method, "DELETE");
    assert.equal(calls[8].body, undefined);
    assert.deepEqual(JSON.parse(calls[9].body), { content: "First note" });
    assert.deepEqual(JSON.parse(calls[10].body), { content: "Updated note" });
    assert.equal(calls[11].method, "DELETE");
    assert.equal(calls.some(call => String(call.body).includes("displayName")), false);
});
