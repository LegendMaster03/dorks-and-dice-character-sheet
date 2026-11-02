import assert from "node:assert/strict";
import test from "node:test";
import { createConditionsWorkflow } from "../.test-dist/features/conditions/conditions-workflow.js";

const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/test",
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const fingerprint = "a".repeat(64);

function harness(deny = false) {
    const events = [];
    const requests = [];
    const app = { dispatch: action => events.push(action) };
    const routine = { mutate: async () => true };
    const fetcher = async input => {
        const path = new URL(String(input), "https://example.test");
        requests.push(path);
        assert.ok(path.pathname.includes("/api/native-rules/"),
            "Retired Rules Core path was requested");
        if (path.pathname.endsWith("/ruleset"))
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                schemaVersion: 1, fingerprint,
                capabilities: { nativeOptionSearch: true }
            });
        if (deny)
            return Response.json({ error: "ruleset_version_mismatch" }, { status: 409 });
        const category = path.searchParams.get("category");
        const options = category === "condition"
            ? [{ id: "condition.wet", name: "Wet", kind: "condition" }]
            : [{ id: "condition.diseased.2", name: "Disease (2)", kind: "leveled-condition" }];
        return Response.json({
            contractVersion: 1, rulesetId: "first-party-001", fingerprint,
            category, total: 1, offset: 0, limit: 100, options
        });
    };
    return { events, requests, app, routine, fetcher };
}

test("condition chooser searches native ordinary and leveled conditions without legacy API", async () => {
    const original = globalThis.fetch;
    const h = harness();
    globalThis.fetch = h.fetcher;
    try {
        const workflow = createConditionsWorkflow(h.app, h.routine, environment);
        await workflow.search(" Wet ");
        assert.deepEqual(h.requests.map(url => url.pathname.split("/").at(-1)),
            ["ruleset", "options", "options"]);
        assert.equal(h.requests[1].searchParams.get("category"), "condition");
        assert.equal(h.requests[2].searchParams.get("category"), "leveled-condition");
        assert.equal(h.requests[1].searchParams.get("q"), "Wet");
        const loaded = h.events.findLast(event => event.type === "condition-chooser-loaded");
        assert.ok(loaded);
        assert.deepEqual(loaded.results.map(item => item.id),
            ["condition.diseased.2", "condition.wet"]);
        assert.equal(loaded.results[0].kind, "leveled-condition");
    } finally {
        globalThis.fetch = original;
    }
});

test("native condition version conflict is surfaced, not treated as an empty list", async () => {
    const original = globalThis.fetch;
    const h = harness(true);
    globalThis.fetch = h.fetcher;
    try {
        const workflow = createConditionsWorkflow(h.app, h.routine, environment);
        await workflow.search("wet");
        const error = h.events.findLast(event => event.type === "condition-chooser-load-failed");
        assert.ok(error);
        assert.match(error.message, /HTTP 409|catalog changed/);
        assert.equal(h.events.some(event => event.type === "condition-chooser-loaded"), false);
    } finally {
        globalThis.fetch = original;
    }
});
