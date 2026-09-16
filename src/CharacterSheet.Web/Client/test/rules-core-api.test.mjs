import assert from "node:assert/strict";
import test from "node:test";
import {
    buildRulesCoreUrl,
    resolveRuleConcept,
    RulesCoreApiError,
    searchResolvedRules
} from "../.test-dist/rules-core-api.js";

const embedded = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/example",
    contextUrl: "/tool-host/character-sheet/context?ticket=character-sheet-only",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};

const rule = {
    ruleConceptId: "11111111-1111-1111-1111-111111111111",
    conceptKey: "class:arcane-archer",
    entityType: "class",
    displayName: "Arcane Archer",
    effectiveDecisionKind: "select",
    hasCampaignOverride: false,
    sourceEntityId: "22222222-2222-2222-2222-222222222222",
    sourceEntityRevisionId: "33333333-3333-3333-3333-333333333333",
    sourceRevisionNumber: 1,
    sourceEntityName: "Arcane Archer",
    sourceCode: "SRD",
    packageKey: "fixture",
    packageDisplayName: "Fixture",
    editionKey: "3.5e",
    editionDisplayName: "3.5e"
};

test("embedded Rules Core URLs use the Rules Core Tool Host route rather than Character Sheet context", () => {
    assert.equal(
        buildRulesCoreUrl(embedded, "/api/rules"),
        "/tool-host/rules-core/api/upstream/api/rules");
    assert.equal(buildRulesCoreUrl(embedded, "/api/rules").includes("character-sheet"), false);
});

test("class catalog requests use Rules Core and encode search", async () => {
    const calls = [];
    const fetcher = async (input, init) => {
        calls.push({ input: String(input), method: init?.method ?? "GET" });
        return Response.json({ scope: "global", campaignId: null, revisionNumber: 1, publishedAt: "now", rules: [rule] });
    };

    const result = await searchResolvedRules(embedded, "class", "Arcane Archer & Mage", fetcher);

    assert.equal(result.rules[0].conceptKey, rule.conceptKey);
    assert.deepEqual(calls, [{
        input: "/tool-host/rules-core/api/upstream/api/rules?entityType=class&q=Arcane+Archer+%26+Mage&limit=200",
        method: "GET"
    }]);
});

test("race catalog requests use the race entity type", async () => {
    let requested = "";
    const fetcher = async input => {
        requested = String(input);
        return Response.json({ scope: "global", campaignId: null, revisionNumber: 1, publishedAt: "now", rules: [] });
    };

    await searchResolvedRules(embedded, "race", "elf", fetcher);

    assert.equal(requested.includes("entityType=race"), true);
    assert.equal(requested.includes("q=elf"), true);
});

test("Rules Core failures are explicit", async () => {
    const fetcher = async () => Response.json({ detail: "catalog offline" }, { status: 503 });

    await assert.rejects(
        () => searchResolvedRules(embedded, "class", "", fetcher),
        error => error instanceof RulesCoreApiError
            && error.status === 503
            && error.message === "catalog offline");
});

test("stable concept resolution encodes the concept key and treats 404 as unavailable", async () => {
    const calls = [];
    const fetcher = async input => {
        calls.push(String(input));
        return new Response(null, { status: 404 });
    };

    const result = await resolveRuleConcept(embedded, "class:key/with slash", fetcher);

    assert.equal(result, null);
    assert.equal(
        calls[0],
        "/tool-host/rules-core/api/upstream/api/rules/class%3Akey%2Fwith%20slash");
});

test("standalone Rules Core adapter is development-only", () => {
    const productionStandalone = {
        ...embedded,
        embedded: false,
        contextUrl: null,
        rulesCoreDevelopmentBaseUrl: "http://localhost:5188"
    };
    assert.throws(
        () => buildRulesCoreUrl(productionStandalone, "/api/rules"),
        /development-only/);

    const developmentStandalone = {
        ...productionStandalone,
        standaloneDevelopment: true
    };
    assert.equal(
        buildRulesCoreUrl(developmentStandalone, "/api/rules"),
        "http://localhost:5188/api/rules");
});
