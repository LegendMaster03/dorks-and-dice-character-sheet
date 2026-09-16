import assert from "node:assert/strict";
import test from "node:test";
import {
    getStoredChoiceConceptKey,
    loadingRuleReference,
    resolveStoredChoice
} from "../.test-dist/builder-rules.js";

const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/example",
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const build = {
    characterId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    builderStatus: "BuildInProgress",
    readOnly: false,
    foundationalSelections: [{
        id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        category: "raceSpecies",
        ruleConceptKey: "race:elf",
        createdAt: "now",
        updatedAt: "now"
    }],
    progressionEntries: [{
        id: "cccccccc-cccc-cccc-cccc-cccccccccccc",
        ordinal: 0,
        kind: "class",
        ruleConceptKey: "class:wizard",
        parentAdvancementEntryId: null,
        createdAt: "now",
        updatedAt: "now"
    }]
};

test("stored builder references are identified from Character Sheet-owned categories and progression", () => {
    assert.equal(getStoredChoiceConceptKey(build, "raceSpecies"), "race:elf");
    assert.equal(getStoredChoiceConceptKey(build, "startingClass"), "class:wizard");
    assert.deepEqual(loadingRuleReference(build, "raceSpecies"), {
        status: "loading",
        conceptKey: "race:elf"
    });
});

test("stored reference display is resolved through Rules Core rather than persisted display data", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "11111111-1111-1111-1111-111111111111",
        conceptKey: "race:elf",
        entityType: "race",
        displayName: "Elf",
        sourceEntityName: "Elf",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    });

    const resolved = await resolveStoredChoice(environment, build, "raceSpecies", fetcher);

    assert.equal(resolved.status, "resolved");
    assert.equal(resolved.rule.displayName, "Elf");
});

test("unavailable stored reference remains retained and is not substituted", async () => {
    const fetcher = async () => new Response(null, { status: 404 });

    const result = await resolveStoredChoice(environment, build, "startingClass", fetcher);

    assert.deepEqual(result, { status: "unavailable", conceptKey: "class:wizard" });
    assert.equal(build.progressionEntries[0].ruleConceptKey, "class:wizard");
});

test("a concept that resolves as the wrong entity type is unavailable rather than accepted by display name", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "11111111-1111-1111-1111-111111111111",
        conceptKey: "class:wizard",
        entityType: "prestigeClass",
        displayName: "Wizard",
        sourceEntityName: "Wizard",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "3.5e",
        editionDisplayName: "3.5e"
    });

    assert.deepEqual(
        await resolveStoredChoice(environment, build, "startingClass", fetcher),
        { status: "unavailable", conceptKey: "class:wizard" });
});

test("Rules Core technical failures produce an explicit reference error without clearing the key", async () => {
    const fetcher = async () => Response.json({ detail: "offline" }, { status: 503 });

    const result = await resolveStoredChoice(environment, build, "raceSpecies", fetcher);

    assert.equal(result.status, "error");
    assert.equal(result.conceptKey, "race:elf");
    assert.match(result.message, /offline/);
});
