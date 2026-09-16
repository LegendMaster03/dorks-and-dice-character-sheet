import assert from "node:assert/strict";
import test from "node:test";
import {
    buildCharacterBuildBackendUrl,
    clearCharacterBuildChoice,
    loadCharacterBuild,
    setCharacterBuildChoice
} from "../.test-dist/builder-api.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const classEntryId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: `/tool-host/character-sheet/context?toolRoute=%2Fcharacters%2F${characterId}`,
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const build = {
    characterId,
    builderStatus: "BuildInProgress",
    readOnly: false,
    foundationalSelections: [],
    progressionEntries: []
};

test("builder API remains behind Character Sheet Tool Host authorization", () => {
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "raceSpecies"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/race-species`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "startingClass"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/starting-class`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "subclass", classEntryId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/classes/${classEntryId}/subclass`);
});

test("subclass builder route requires a Character-owned Class advancement entry", () => {
    assert.throws(
        () => buildCharacterBuildBackendUrl(environment, characterId, "subclass"),
        /Class advancement entry/);
});

test("loading builder state uses the coherent build resource", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push([String(input), init?.method ?? "GET"]);
        return Response.json(build);
    };

    await loadCharacterBuild(environment, characterId, fetcher);

    assert.deepEqual(calls, [[
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build`,
        "GET"
    ]]);
});

test("selection mutation sends only the stable concept key", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(build);
    };

    await setCharacterBuildChoice(environment, characterId, "raceSpecies", "race:elf", undefined, fetcher);
    await setCharacterBuildChoice(environment, characterId, "startingClass", "class:wizard", undefined, fetcher);
    await setCharacterBuildChoice(
        environment,
        characterId,
        "subclass",
        "subclass.wizard.evocation",
        classEntryId,
        fetcher);

    assert.deepEqual(JSON.parse(calls[0].body), { conceptKey: "race:elf" });
    assert.deepEqual(JSON.parse(calls[1].body), { conceptKey: "class:wizard" });
    assert.deepEqual(JSON.parse(calls[2].body), { conceptKey: "subclass.wizard.evocation" });
    assert.equal(calls[0].input.endsWith("/build/race-species"), true);
    assert.equal(calls[1].input.endsWith("/build/starting-class"), true);
    assert.equal(calls[2].input.endsWith(`/build/classes/${classEntryId}/subclass`), true);
    assert.equal(calls.some(call => String(call.body).includes("displayName")), false);
});

test("clearing a builder choice uses DELETE without rule content", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(build);
    };

    await clearCharacterBuildChoice(environment, characterId, "startingClass", undefined, fetcher);
    await clearCharacterBuildChoice(environment, characterId, "subclass", classEntryId, fetcher);

    assert.deepEqual(calls, [{
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/starting-class`,
        method: "DELETE",
        body: undefined
    }, {
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/classes/${classEntryId}/subclass`,
        method: "DELETE",
        body: undefined
    }]);
});
