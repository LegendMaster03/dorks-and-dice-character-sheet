import test from "node:test";
import assert from "node:assert/strict";
import {
    buildCharacterRouteUrl,
    buildCharacterSheetBackendUrl,
    createNewCharacterAndSheet,
    initializeCharacterSheet,
    RichSheetInitializationError
} from "../.test-dist/character-api.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/new",
    contextUrl: "/tool-host/character-sheet/context?toolRoute=%2Fnew"
};

const sheet = {
    characterId,
    name: "Canonical Hero",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: [],
    hasRichSheet: true,
    sheet: {
        schemaVersion: 1,
        builderStatus: "BuildInProgress",
        createdAt: "2026-09-16T02:30:00Z",
        updatedAt: "2026-09-16T02:30:00Z"
    }
};

test("host URLs use Tool Host context and base-path information", () => {
    assert.equal(
        buildCharacterSheetBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/sheet`);
    assert.equal(
        buildCharacterRouteUrl(environment, characterId),
        `/tools/character-sheet/characters/${characterId}`);
});

test("nested Character routes ignore Tool Host context query while deriving upstream API base", () => {
    const nestedEnvironment = {
        ...environment,
        toolRoute: `/characters/${characterId}`,
        contextUrl: `/tool-host/character-sheet/context?toolRoute=%2Fcharacters%2F${characterId}`
    };

    assert.equal(
        buildCharacterSheetBackendUrl(nestedEnvironment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/sheet`);
});

test("new character workflow allocates identity at the Site before initializing the returned CharacterId", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method ?? "GET", body: init?.body });
        if (calls.length === 1) {
            return Response.json({ id: characterId, name: "Canonical Hero", status: "Active" }, { status: 201 });
        }
        return Response.json(sheet);
    };

    const result = await createNewCharacterAndSheet(environment, "Canonical Hero", fetcher);

    assert.equal(result, characterId);
    assert.deepEqual(calls.map(call => [call.input, call.method]), [
        ["/characters/api", "POST"],
        [`/tool-host/character-sheet/api/upstream/api/characters/${characterId}/sheet`, "POST"]
    ]);
    assert.deepEqual(JSON.parse(calls[0].body), { name: "Canonical Hero" });
});

test("rich-sheet initialization failure preserves the Site Character and never compensates with deletion", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method ?? "GET" });
        if (calls.length === 1) {
            return Response.json({ id: characterId }, { status: 201 });
        }
        return Response.json({ error: "temporary failure" }, { status: 503 });
    };

    await assert.rejects(
        () => createNewCharacterAndSheet(environment, "Canonical Hero", fetcher),
        error => error instanceof RichSheetInitializationError && error.characterId === characterId);
    assert.equal(calls.some(call => call.method === "DELETE"), false);
});

test("existing basic Character initialization calls only Character Sheet backend", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method ?? "GET" });
        return Response.json(sheet);
    };

    await initializeCharacterSheet(environment, characterId, fetcher);

    assert.deepEqual(calls, [{
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/sheet`,
        method: "POST"
    }]);
});
