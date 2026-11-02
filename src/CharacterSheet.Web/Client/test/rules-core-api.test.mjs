import assert from "node:assert/strict";
import test from "node:test";
import {
    RulesCoreApiError, loadHarvestingRules, resolveHarvestingTable, resolveHarvestingOutcome
} from "../.test-dist/rules-core-api.js";

test("retired harvesting operations fail closed without sending legacy requests", async () => {
    let calls = 0;
    const fetcher = async () => {
        calls += 1;
        throw new Error("Retired Rules Core endpoint was called.");
    };
    const environment = { embedded: true };
    const check = error => error instanceof RulesCoreApiError
        && error.status === 501
        && /native harvesting contract/.test(error.message);

    await assert.rejects(() => loadHarvestingRules(environment, fetcher), check);
    await assert.rejects(() => resolveHarvestingTable(
        environment, { creatureType: "dragon" }, null, fetcher), check);
    await assert.rejects(() => resolveHarvestingOutcome(
        environment, {
            table: { creatureType: "dragon" },
            assessmentResult: 15, carvingResult: 16,
            sameActor: true, harvestOrderComponentKeys: []
        }, null, fetcher), check);
    assert.equal(calls, 0);
});
