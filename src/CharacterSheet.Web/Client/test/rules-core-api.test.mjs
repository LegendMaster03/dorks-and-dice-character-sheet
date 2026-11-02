import assert from "node:assert/strict";
import test from "node:test";
import {
    RulesCoreApiError, loadHarvestingRules, resolveHarvestingTable, resolveHarvestingOutcome
} from "../.test-dist/rules-core-api.js";

const fingerprint = "a".repeat(64);
const environment = {
    embedded: true, contextUrl: "/tool-host/registrations/character-sheet/context"
};
const metadata = {
    contractVersion: 1, rulesetId: "first-party-001", fingerprint,
    capabilities: { harvestingResolution: "type-monster-manual-edits" }
};
const source = {
    workKey: "loot-tavern.harvesting-crafting-lite",
    workDisplayName: "Harvesting & Crafting Lite",
    provider: "Loot Tavern", gameEdition: "5e",
    releaseKind: "public-release", publicationDate: "2024-07-03",
    referenceUri: "https://example.test/loot-tavern"
};
const procedure = { assessment: {}, carving: {}, helpers: {} };
const catalog = {
    contractVersion: 1, rulesetId: "first-party-001", fingerprint,
    source, procedure, creatureTypes: [
        { key: "dragon", displayName: "Dragon", competencyKey: "competency.survival",
          competencyDisplayName: "Survival", defaultComponents: [] }
    ]
};
const table = {
    source, creatureType: "dragon", creatureTypeDisplayName: "Dragon",
    competencyKey: "competency.survival", competencyDisplayName: "Survival",
    components: [{ key: "scale", displayName: "Scale", componentDc: 5,
        quantity: null, origin: "base" }],
    creatureConceptKey: null, creatureDisplayName: null, creatureSize: null,
    creatureOverridesApplied: false, manualEditsApplied: false
};

function simulated(fetches, capability = metadata.capabilities.harvestingResolution) {
    return async (url, options) => {
        const path = String(url);
        fetches.push({ path, options });
        if (path.endsWith("/api/native-rules/ruleset"))
            return Response.json({
                ...metadata, capabilities: { harvestingResolution: capability }
            });
        if (path.includes("/api/native-rules/harvesting?"))
            return Response.json(catalog);
        if (path.endsWith("/api/native-rules/harvesting/resolve"))
            return Response.json(table);
        if (path.endsWith("/api/native-rules/harvesting/outcome"))
            return Response.json({
                table, assessmentRollMode: "normal", carvingRollMode: "normal",
                creatureSize: "Large", assessmentResult: 12, carvingResult: 15,
                helperCount: 0, helperContribution: 0, harvestingResult: 27,
                components: [{ ...table.components[0], harvestDc: 5, awarded: true }]
            });
        throw Error("Unexpected route: " + path);
    };
}

test("native Harvesting catalog uses the authenticated Sheet gateway and pinned content", async () => {
    const calls = [];
    const response = await loadHarvestingRules(environment, simulated(calls));
    assert.equal(response.creatureTypes[0].key, "dragon");
    assert.equal(calls.length, 2);
    assert.ok(calls.every(c => c.path.includes("/api/upstream/api/native-rules/")));
    assert.match(calls[1].path, /fingerprint=a{64}/);
    assert.equal(calls[0].options.credentials, "same-origin");
});

test("native Harvesting table and outcome preserve edits, helpers, and ordered components", async () => {
    const calls = [];
    const fetcher = simulated(calls);
    const edits = { upsertComponents: [
        { key: "scale", componentDc: 10, quantity: 2 }
    ] };
    const resolved = await resolveHarvestingTable(environment,
        { creatureType: "dragon", manualEdits: edits }, null, fetcher);
    assert.equal(resolved.components[0].key, "scale");
    const outcome = await resolveHarvestingOutcome(environment, {
        table: { creatureType: "dragon", manualEdits: edits },
        assessmentResult: 12, carvingResult: 15, sameActor: false,
        harvestOrderComponentKeys: ["scale"], creatureSize: "Large",
        helpers: [{ proficiencyBonus: 3, isProficient: true }]
    }, null, fetcher);
    assert.equal(outcome.harvestingResult, 27);
    assert.equal(outcome.components[0].awarded, true);
    const tableRequest = JSON.parse(calls[1].options.body);
    const outcomeRequest = JSON.parse(calls[3].options.body);
    assert.equal(tableRequest.fingerprint, fingerprint);
    assert.deepEqual(tableRequest.manualEdits, edits);
    assert.equal(outcomeRequest.table.rulesetId, "first-party-001");
    assert.deepEqual(outcomeRequest.table.manualEdits, edits);
    assert.equal(outcomeRequest.helpers[0].proficiencyBonus, 3);
});

test("unavailable native contract and campaign-specific rules do not silently fall back", async () => {
    const calls = [];
    await assert.rejects(
        () => loadHarvestingRules(environment, simulated(calls, "unavailable")),
        error => error instanceof RulesCoreApiError && error.status === 503);
    const fetcher = simulated(calls);
    const before = calls.length;
    await assert.rejects(() => resolveHarvestingTable(environment,
        { creatureType: "dragon" }, "00000000-0000-0000-0000-000000000001", fetcher),
        error => error instanceof RulesCoreApiError && error.status === 501);
    assert.equal(calls.length, before);
});

test("retired Harvesting endpoints are never requested", async () => {
    const calls = [];
    await resolveHarvestingTable(environment, { creatureType: "dragon" },
        null, simulated(calls));
    assert.ok(calls.every(x => !x.path.includes("/api/rules/")));
});
