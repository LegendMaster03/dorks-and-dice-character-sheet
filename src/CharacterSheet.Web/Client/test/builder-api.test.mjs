import assert from "node:assert/strict";
import test from "node:test";
import {
    addCharacterFeatOccurrence,
    buildCharacterAbilityScoreBackendUrl,
    buildCharacterFeatBackendUrl,
    buildCharacterBuildBackendUrl,
    clearCharacterBaseAbilityScore,
    clearCharacterBuildChoice,
    loadCharacterBuild,
    removeCharacterFeatOccurrence,
    setCharacterBaseAbilityScore,
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
    baseAbilityScoreInputs: [],
    progressionEntries: []
};

test("builder API remains behind Character Sheet Tool Host authorization", () => {
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "species"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/species`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "subspecies"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/subspecies`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "background"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/background`);
    assert.equal(
        buildCharacterBuildBackendUrl(environment, characterId, "deity"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/deity`);
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

    await setCharacterBuildChoice(environment, characterId, "species", "species:elf", undefined, fetcher);
    await setCharacterBuildChoice(environment, characterId, "subspecies", "subspecies:high-elf", undefined, fetcher);
    await setCharacterBuildChoice(environment, characterId, "background", "background:sage", undefined, fetcher);
    await setCharacterBuildChoice(environment, characterId, "deity", "deity:pelor", undefined, fetcher);
    await setCharacterBuildChoice(environment, characterId, "startingClass", "class:wizard", undefined, fetcher);
    await setCharacterBuildChoice(
        environment,
        characterId,
        "subclass",
        "subclass.wizard.evocation",
        classEntryId,
        fetcher);

    assert.deepEqual(JSON.parse(calls[0].body), { conceptKey: "species:elf" });
    assert.deepEqual(JSON.parse(calls[1].body), { conceptKey: "subspecies:high-elf" });
    assert.deepEqual(JSON.parse(calls[2].body), { conceptKey: "background:sage" });
    assert.deepEqual(JSON.parse(calls[3].body), { conceptKey: "deity:pelor" });
    assert.deepEqual(JSON.parse(calls[4].body), { conceptKey: "class:wizard" });
    assert.deepEqual(JSON.parse(calls[5].body), { conceptKey: "subclass.wizard.evocation" });
    assert.equal(calls[0].input.endsWith("/build/species"), true);
    assert.equal(calls[1].input.endsWith("/build/subspecies"), true);
    assert.equal(calls[2].input.endsWith("/build/background"), true);
    assert.equal(calls[3].input.endsWith("/build/deity"), true);
    assert.equal(calls[4].input.endsWith("/build/starting-class"), true);
    assert.equal(calls[5].input.endsWith(`/build/classes/${classEntryId}/subclass`), true);
    assert.equal(calls.some(call => String(call.body).includes("displayName")), false);
});

test("clearing a builder choice uses DELETE without rule content", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(build);
    };

    await clearCharacterBuildChoice(environment, characterId, "species", undefined, fetcher);
    await clearCharacterBuildChoice(environment, characterId, "subspecies", undefined, fetcher);
    await clearCharacterBuildChoice(environment, characterId, "startingClass", undefined, fetcher);
    await clearCharacterBuildChoice(environment, characterId, "subclass", classEntryId, fetcher);

    assert.deepEqual(calls, [{
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/species`,
        method: "DELETE",
        body: undefined
    }, {
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/subspecies`,
        method: "DELETE",
        body: undefined
    }, {
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/starting-class`,
        method: "DELETE",
        body: undefined
    }, {
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/classes/${classEntryId}/subclass`,
        method: "DELETE",
        body: undefined
    }]);
});

test("ability score API uses the stable Character-owned build route", () => {
    assert.equal(
        buildCharacterAbilityScoreBackendUrl(environment, characterId, "strength"),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/ability-scores/strength`);
});

test("base ability score set and replacement send only the integer input and return coherent build state", async () => {
    const calls = [];
    const firstBuild = {
        ...build,
        baseAbilityScoreInputs: [{
            id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            abilityKey: "strength",
            score: 15,
            createdAt: "now",
            updatedAt: "now"
        }]
    };
    const replacementBuild = {
        ...firstBuild,
        baseAbilityScoreInputs: [{
            ...firstBuild.baseAbilityScoreInputs[0],
            score: 18,
            updatedAt: "later"
        }]
    };
    const responses = [firstBuild, replacementBuild];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(responses.shift());
    };

    const first = await setCharacterBaseAbilityScore(environment, characterId, "strength", 15, fetcher);
    const replacement = await setCharacterBaseAbilityScore(environment, characterId, "strength", 18, fetcher);

    assert.deepEqual(JSON.parse(calls[0].body), { score: 15 });
    assert.deepEqual(JSON.parse(calls[1].body), { score: 18 });
    assert.equal(calls[0].method, "PUT");
    assert.equal(calls[1].method, "PUT");
    assert.equal(calls[0].input.endsWith("/build/ability-scores/strength"), true);
    assert.equal(first.baseAbilityScoreInputs[0].score, 15);
    assert.equal(replacement.baseAbilityScoreInputs[0].score, 18);
    assert.equal(Object.hasOwn(replacement.baseAbilityScoreInputs[0], "effectiveScore"), false);
});

test("clearing a base ability score uses DELETE and the coherent returned build", async () => {
    const calls = [];
    const clearedBuild = { ...build, baseAbilityScoreInputs: [] };
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(clearedBuild);
    };

    const result = await clearCharacterBaseAbilityScore(environment, characterId, "charisma", fetcher);

    assert.deepEqual(calls, [{
        input: `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/ability-scores/charisma`,
        method: "DELETE",
        body: undefined
    }]);
    assert.deepEqual(result.baseAbilityScoreInputs, []);
});


test("Feat occurrence API uses stable build occurrence routes and concept identity", async () => {
    const featId = "dddddddd-dddd-dddd-dddd-dddddddddddd";
    assert.equal(
        buildCharacterFeatBackendUrl(environment, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/feats`);
    assert.equal(
        buildCharacterFeatBackendUrl(environment, characterId, featId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/build/feats/${featId}`);

    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method, body: init?.body });
        return Response.json(build);
    };

    await addCharacterFeatOccurrence(environment, characterId, "feat:alert", fetcher);
    await removeCharacterFeatOccurrence(environment, characterId, featId, fetcher);

    assert.equal(calls[0].method, "POST");
    assert.deepEqual(JSON.parse(calls[0].body), { conceptKey: "feat:alert" });
    assert.equal(calls[1].method, "DELETE");
    assert.equal(calls[1].body, undefined);
    assert.equal(String(calls[0].body).includes("displayName"), false);
});
