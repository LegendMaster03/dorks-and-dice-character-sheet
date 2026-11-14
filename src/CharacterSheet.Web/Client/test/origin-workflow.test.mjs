import test from "node:test";
import assert from "node:assert/strict";
import { createOriginWorkflow } from "../.test-dist/features/origin/origin-workflow.js";

const characterId = "54bb0d47-38d1-4ce4-9cbf-fcb8753e3140";
const oldFingerprint = "a".repeat(64);
const currentFingerprint = "b".repeat(64);
const environment = {
    embedded: true, toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false
};
const mixedProposal = {
    id: "refresh-two-source-example", intent: 1,
    method: { ruleId: "rule.creature-origination", methodId: "biological" },
    contributors: [
        { id: "parent-one", role: { kind: 0 },
            source: { kind: 0, id: "species.human" } },
        { id: "parent-two", role: { kind: 0 },
            source: { kind: 0, id: "species.elf" } }
    ],
    candidates: [], manifestations: []
};

function makeDraft(revision = 2, fingerprint = oldFingerprint) {
    return {
        characterId, rulesetId: "first-party-001", fingerprint,
        revision, updatedAt: "2026-11-12T18:00:00Z",
        active: false, proposal: structuredClone(mixedProposal)
    };
}

function preview(fingerprint, contentChangedSinceRequest) {
    return {
        contractVersion: 1, rulesetId: "first-party-001", fingerprint,
        contentChangedSinceRequest, previewOnly: true,
        result: { status: 1, evaluatedProposal: null, profile: null, issues: [] }
    };
}

function mockBackend({ denySave = false, readOnly = false, onDraftRead } = {}) {
    let draft = makeDraft();
    const writes = [];
    const calls = [];
    const fetch = async (url, init) => {
        const path = String(url);
        calls.push({ path, method: init?.method });
        if (path.endsWith("/api/native-rules/ruleset")) {
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                schemaVersion: 1, fingerprint: currentFingerprint,
                capabilities: {
                    characterProjection: true, resourceResolution: true,
                    nativeOptionSearch: true, advancementEligibility: "supported-subset",
                    conditionRollMode: "conditional-subset", campaignEffectiveRules: false
                }
            });
        }
        if (path.endsWith("/origin/state") && init.method === "GET") {
            return Response.json({
                status: "ready", savedState: null, events: [], readOnly,
                effectiveMechanicsReady: false
            });
        }
        if (path.endsWith("/origin/draft") && init.method === "GET") {
            onDraftRead?.(draft);
            return Response.json({
                status: "ready", draft: structuredClone(draft),
                preview: preview(currentFingerprint,
                    draft.fingerprint !== currentFingerprint),
                activeGrants: false
            });
        }
        if (path.endsWith("/origin/draft") && init.method === "PUT") {
            const body = JSON.parse(init.body);
            writes.push(body);
            if (denySave) {
                return Response.json({ error: "origin_draft_revision_conflict" },
                    { status: 409 });
            }
            assert.equal(body.expectedRevision, draft.revision);
            assert.equal(body.fingerprint, currentFingerprint);
            assert.deepEqual(body.proposal, mixedProposal);
            draft = makeDraft(draft.revision + 1, currentFingerprint);
            return Response.json({
                status: "ready", draft: structuredClone(draft),
                preview: preview(currentFingerprint, false),
                activeGrants: false
            });
        }
        throw new Error("Unexpected Origin request: " + init.method + " " + path);
    };
    return { fetch, writes, calls, draft: () => draft };
}

async function withBackend(backend, action) {
    const originalWindow = globalThis.window;
    const originalFetch = globalThis.fetch;
    globalThis.window = { fetch: backend.fetch };
    globalThis.fetch = backend.fetch;
    try {
        await action();
    } finally {
        globalThis.window = originalWindow;
        globalThis.fetch = originalFetch;
    }
}

test("owner refreshes an outdated mixed draft via a new native snapshot and revision", async () => {
    const backend = mockBackend();
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        assert.equal(workflow.current().draft.revision, 2);
        assert.equal(workflow.current().catalog.fingerprint, currentFingerprint);
        await workflow.refreshDraft(characterId);

        assert.equal(backend.writes.length, 1);
        assert.equal(backend.writes[0].expectedRevision, 2);
        assert.deepEqual(backend.writes[0].proposal, mixedProposal);
        assert.equal(workflow.current().draft.revision, 3);
        assert.equal(workflow.current().draft.fingerprint, currentFingerprint);
        assert.equal(workflow.current().preview.contentChangedSinceRequest, false);
        assert.equal(workflow.current().savedState, null);
        assert.deepEqual(workflow.current().parentSources.selected, {});
        assert.equal(backend.calls.some(x => x.path.includes("activate-mixed")), false);
        assert.equal(backend.calls.some(x => x.path.includes("evaluate-approved")), false);

        await workflow.refreshDraft(characterId);
        assert.equal(backend.writes.length, 1,
            "An already current draft is not needlessly rewritten.");
    });
});

test("re-review rereads the current draft before saving instead of overwriting another session", async () => {
    let reads = 0;
    const backend = mockBackend({
        denySave: true,
        onDraftRead: draft => {
            reads++;
            if (reads === 2) draft.revision = 4;
        }
    });
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        assert.equal(workflow.current().draft.revision, 2);
        await workflow.refreshDraft(characterId);
        assert.equal(reads, 2);
        assert.equal(backend.writes.length, 1);
        assert.equal(backend.writes[0].expectedRevision, 4);
        assert.equal(workflow.current().draft.revision, 4);
        assert.match(workflow.current().message, /changed in another session/);
        assert.equal(workflow.current().savedState, null);
    });
});

test("read-only Origin can not be re-reviewed or rewritten", async () => {
    const backend = mockBackend({ readOnly: true });
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        await workflow.refreshDraft(characterId);
        assert.equal(backend.writes.length, 0);
        assert.equal(workflow.current().draft.revision, 2);
    });
});
