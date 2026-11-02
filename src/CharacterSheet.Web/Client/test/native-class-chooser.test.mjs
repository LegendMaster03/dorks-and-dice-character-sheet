import assert from "node:assert/strict";
import test from "node:test";
import { createAdvancementWorkflow } from "../.test-dist/features/advancement/advancement-workflow.js";

const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/example",
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const fingerprint = "a".repeat(64);

function setup() {
    const events = [];
    const calls = [];
    const app = { dispatch: action => events.push(action) };
    const buildState = {
        current: () => ({
            build: { progressionEntries: [], foundationalSelections: [] }
        }),
        resolveReferences: async () => {},
        currentStartingClassId: () => "test"
    };
    const presentation = { load: async () => {} };
    const fetcher = async input => {
        const url = new URL(String(input), "https://example.test");
        calls.push(url);
        assert.match(url.pathname, /\/api\/native-rules\//,
            "Old Rules Core catalog endpoint was requested");
        if (url.pathname.endsWith("/ruleset"))
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                schemaVersion: 1, fingerprint,
                capabilities: { nativeOptionSearch: true }
            });
        assert.equal(url.searchParams.get("category"), "class");
        return Response.json({
            contractVersion: 1, rulesetId: "first-party-001", fingerprint,
            category: "class", total: 1, offset: 0, limit: 100,
            options: [{ id: "class.warlock", name: "Warlock", kind: "class" }]
        });
    };
    return {
        events, calls, fetcher,
        workflow: createAdvancementWorkflow(app, buildState, presentation, environment)
    };
}

test("starting Class chooser searches native IDs without calling the retired catalog", async () => {
    const old = globalThis.fetch;
    const h = setup();
    globalThis.fetch = h.fetcher;
    try {
        await h.workflow.search("startingClass", "warlock");
        assert.equal(h.calls.length, 2);
        assert.equal(h.calls[1].searchParams.get("q"), "warlock");
        const loaded = h.events.findLast(x => x.type === "chooser-loaded");
        assert.ok(loaded);
        assert.deepEqual(loaded.results, [{
            id: "class.warlock", name: "Warlock", kind: "class"
        }]);
    } finally {
        globalThis.fetch = old;
    }
});

test("unsupported builder selections do not silently retry the legacy API", async () => {
    const old = globalThis.fetch;
    const h = setup();
    globalThis.fetch = h.fetcher;
    try {
        await h.workflow.search("species", "elf");
        assert.equal(h.calls.length, 0);
        const failed = h.events.findLast(x => x.type === "chooser-load-failed");
        assert.ok(failed);
        assert.match(failed.message, /not available through the native Rules Core catalog/);
    } finally {
        globalThis.fetch = old;
    }
});

test("Class advancement candidate search returns native IDs", async () => {
    const old = globalThis.fetch;
    const h = setup();
    globalThis.fetch = h.fetcher;
    try {
        await h.workflow.searchCandidates("class", "warlock");
        const state = h.workflow.current();
        assert.equal(state.chooser.kind, "open");
        assert.deepEqual(state.chooser.results.map(x => x.id), ["class.warlock"]);
        assert.equal(h.calls.length, 2);
    } finally {
        globalThis.fetch = old;
    }
});

test("stale candidate searches do not replace newer search results", async () => {
    const oldFetch = globalThis.fetch;
    const h = setup();
    let completeSlow;
    let slowStarted;
    const started = new Promise(resolve => { slowStarted = resolve; });
    globalThis.fetch = async input => {
        const url = new URL(String(input), "https://example.test");
        if (url.pathname.endsWith("/ruleset")) return h.fetcher(input);
        if (url.searchParams.get("q") === "slow") {
            slowStarted();
            return new Promise(resolve => { completeSlow = resolve; });
        }
        return Response.json({
            contractVersion: 1, rulesetId: "first-party-001", fingerprint,
            category: "class", total: 1, offset: 0, limit: 100,
            options: [{ id: "class.fighter", name: "Fighter", kind: "class" }]
        });
    };
    try {
        const slow = h.workflow.searchCandidates("class", "slow");
        await started;
        await h.workflow.searchCandidates("class", "fast");
        completeSlow(Response.json({
            contractVersion: 1, rulesetId: "first-party-001", fingerprint,
            category: "class", total: 1, offset: 0, limit: 100,
            options: [{ id: "class.warlock", name: "Warlock", kind: "class" }]
        }));
        await slow;
        const chooser = h.workflow.current().chooser;
        assert.equal(chooser.kind, "open");
        assert.equal(chooser.query, "fast");
        assert.deepEqual(chooser.results.map(option => option.id), ["class.fighter"]);
    } finally {
        globalThis.fetch = oldFetch;
    }
});

test("stale advancement previews do not override newer choices or reopen closed workspace", async () => {
    const oldWindow = globalThis.window;
    const h = setup();
    const pending = [];
    globalThis.window = {
        fetch: async (_input, options) => new Promise(resolve => {
            pending.push({ request: JSON.parse(options.body), resolve });
        })
    };
    try {
        const slow = h.workflow.selectCandidate("character", "class", "class.warlock");
        const fast = h.workflow.selectCandidate("character", "class", "class.fighter");
        assert.equal(pending.length, 2);
        pending[1].resolve(Response.json({
            classConceptKey: "class.fighter", canApply: false, status: "choice-required"
        }));
        await fast;
        pending[0].resolve(Response.json({
            classConceptKey: "class.warlock", canApply: true, status: "ready"
        }));
        await slow;
        assert.equal(h.workflow.current().plan.classConceptKey, "class.fighter");

        const late = h.workflow.selectCandidate("character", "class", "class.warlock");
        assert.equal(pending.length, 3);
        h.workflow.closeAdvancement();
        pending[2].resolve(Response.json({
            classConceptKey: "class.warlock", canApply: true, status: "ready"
        }));
        await late;
        assert.equal(h.workflow.current().open, false);
        assert.equal(h.workflow.current().plan, null);
    } finally {
        if (oldWindow === undefined) delete globalThis.window;
        else globalThis.window = oldWindow;
    }
});
