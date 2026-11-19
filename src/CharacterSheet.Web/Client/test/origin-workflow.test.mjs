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

function candidateReviewPreview(proposal, fingerprint = currentFingerprint) {
    const candidates = [
        ["parent-one", "species.human", "/traits/0"],
        ["parent-two", "species.elf", "/traits/1"]
    ].map(([contributorId, sourceId, occurrencePath]) => ({
        id: "contributor:" + contributorId + ":" + sourceId + ":" + occurrencePath,
        contributorId,
        identity: {
            kind: 1, mechanicalDefinitionId: sourceId + "#trait",
            occurrence: { source: { kind: 0, id: sourceId }, occurrencePath }
        },
        selectionBasis: { eligibility: 0 },
        requiredChoices: [{
            id: "shade", timing: 0, minimum: 1, maximum: 2,
            finiteOptions: ["red", "blue", "green"]
        }]
    }));
    return {
        contractVersion: 1, rulesetId: "first-party-001",
        fingerprint, contentChangedSinceRequest: false,
        previewOnly: true,
        result: {
            status: 2, profile: null,
            issues: [{ code: "selection-eligibility-unresolved",
                path: "manifestations", severity: 1,
                detail: "Inheritance requires a trusted native rule." }],
            evaluatedProposal: {
                id: proposal.id, candidates,
                manifestations: structuredClone(proposal.manifestations)
            }
        }
    };
}

function candidateDraftBackend({ outdated = false, conflict = false,
    failNativePreview = false } = {}) {
    const requests = [], writes = [];
    let draft = makeDraft(1, outdated ? oldFingerprint : currentFingerprint);
    const fetch = async (url, init) => {
        const path = String(url);
        requests.push({ path, method: init?.method, body: init?.body });
        if (path.endsWith("/api/native-rules/ruleset")) return Response.json({
            contractVersion: 1, rulesetId: "first-party-001",
            schemaVersion: 1, fingerprint: currentFingerprint,
            capabilities: { nativeOptionSearch: true }
        });
        if (path.endsWith("/origin/state")) return Response.json({
            status: "ready", savedState: null, events: [],
            readOnly: false, effectiveMechanicsReady: false
        });
        if (path.endsWith("/origin/draft") && init.method === "GET") return Response.json({
            status: "ready", draft: structuredClone(draft),
            preview: failNativePreview ? null :
                candidateReviewPreview(draft.proposal,
                    outdated ? oldFingerprint : currentFingerprint),
            activeGrants: false
        });
        if (path.endsWith("/api/native-rules/origins/preview")) return failNativePreview
            ? Response.json({ error: "preview_unavailable" }, { status: 503 })
            : Response.json(candidateReviewPreview(draft.proposal));
        if (path.endsWith("/origin/draft") && init.method === "PUT") {
            const body = JSON.parse(init.body);
            writes.push(body);
            if (conflict) return Response.json({
                error: "origin_draft_revision_conflict"
            }, { status: 409 });
            assert.equal(body.expectedRevision, draft.revision);
            assert.equal(body.fingerprint, currentFingerprint);
            assert.deepEqual(body.proposal.candidates, []);
            assert.equal(body.proposal.tableDecisions, undefined);
            draft = { ...draft, revision: draft.revision + 1,
                fingerprint: currentFingerprint, proposal: structuredClone(body.proposal) };
            return Response.json({ status: "ready", draft: structuredClone(draft),
                preview: candidateReviewPreview(draft.proposal),
                activeGrants: false });
        }
        throw new Error("Unexpected request: " + path);
    };
    return { fetch, requests, writes, draft: () => draft };
}

test("native mixed selections and finite choices survive successive saves and reloads", async () => {
    const backend = candidateDraftBackend();
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        const selected = "contributor:parent-one:species.human:/traits/0";
        const second = "contributor:parent-two:species.elf:/traits/1";
        await workflow.toggleMixedCandidate(characterId, selected, true);
        assert.equal(workflow.current().draft.revision, 2);
        await workflow.toggleMixedCandidate(characterId, second, true);
        assert.equal(workflow.current().draft.proposal.manifestations.length, 2);
        await workflow.setFiniteChoice(characterId, selected,
            "shade", ["red", "blue"]);
        assert.equal(workflow.current().draft.revision, 4);
        assert.deepEqual(backend.writes[2].proposal.manifestations[0].choices, [{
            requirementId: "shade", selectedOptionIds: ["red", "blue"]
        }]);
        await workflow.load(characterId);
        assert.deepEqual(workflow.current().draft.proposal.manifestations,
            backend.draft().proposal.manifestations);
        assert.equal(workflow.current().preview.previewOnly, true);
        await workflow.activate(characterId);
        assert.equal(backend.requests.some(x => x.path.includes("activate")), false,
            "The owner can never activate a mixed draft through direct activation.");
        await workflow.toggleMixedCandidate(characterId, selected, false);
        assert.equal(workflow.current().draft.proposal.manifestations.length, 1);
        assert.deepEqual(backend.writes[3].proposal.manifestations.map(x => x.candidateId),
            [second]);
    });
});

test("native catalog drift blocks editing even when stale draft preview offers IDs", async () => {
    const backend = candidateDraftBackend({ outdated: true });
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        await workflow.toggleMixedCandidate(characterId,
            "contributor:parent-one:species.human:/traits/0", true);
        assert.equal(backend.writes.length, 0);
        await workflow.refreshDraft(characterId);
        assert.equal(backend.writes.length, 1);
        assert.equal(backend.writes[0].expectedRevision, 1);
        assert.equal(workflow.current().draft.fingerprint, currentFingerprint);
    });
});

test("mixed selection conflict retains the last loaded draft and requires reload", async () => {
    const backend = candidateDraftBackend({ conflict: true });
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        const old = structuredClone(workflow.current().draft);
        await workflow.toggleMixedCandidate(characterId,
            "contributor:parent-one:species.human:/traits/0", true);
        assert.match(workflow.current().message, /changed in another session/);
        assert.deepEqual(workflow.current().draft, old);
        assert.equal(workflow.current().busy, false);
        assert.equal(backend.writes.length, 1);
    });
});

test("native preview failure preserves draft but blocks characteristic edits", async () => {
    const backend = candidateDraftBackend({ failNativePreview: true });
    await withBackend(backend, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        assert.equal(workflow.current().status, "ready");
        assert.equal(workflow.current().preview, null);
        assert.match(workflow.current().message, /preview_unavailable|preview/);
        await workflow.toggleMixedCandidate(characterId,
            "contributor:parent-one:species.human:/traits/0", true);
        assert.equal(backend.writes.length, 0);
    });
});

test("conventional Species selection still uses the authorized direct activation path", async () => {
    let draft = null;
    let savedState = null;
    const calls = [];
    const fetch = async (url, init) => {
        const path = String(url);
        calls.push({ path, method: init?.method, body: init?.body });
        if (path.endsWith("/api/native-rules/ruleset")) return Response.json({
            contractVersion: 1, rulesetId: "first-party-001",
            fingerprint: currentFingerprint, schemaVersion: 1,
            capabilities: { nativeOptionSearch: true }
        });
        if (path.includes("/api/native-rules/options?")) {
            const params = new URL(path, "https://example.invalid").searchParams;
            assert.equal(params.get("category"), "species");
            assert.equal(params.get("fingerprint"), currentFingerprint);
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                fingerprint: currentFingerprint, category: "species",
                total: 1, offset: 0, limit: 100,
                options: [{ id: "species.human", name: "Human", kind: "species" }]
            });
        }
        if (path.endsWith("/origin/state")) return Response.json({
            status: "ready", savedState, events: [], readOnly: false,
            effectiveMechanicsReady: false
        });
        if (path.endsWith("/origin/draft") && init.method === "GET")
            return Response.json({ status: "ready", draft,
                activeGrants: false });
        if (path.endsWith("/origin/draft") && init.method === "PUT") {
            const body = JSON.parse(init.body);
            assert.equal(body.expectedRevision, 0);
            assert.equal(body.proposal.intent, 0);
            assert.equal(body.proposal.selectedSpeciesId, "species.human");
            assert.deepEqual(body.proposal.contributors, []);
            assert.deepEqual(body.proposal.candidates, []);
            draft = { characterId, rulesetId: body.rulesetId,
                fingerprint: body.fingerprint, revision: 1,
                updatedAt: "2026-11-19T00:00:00Z", active: false,
                proposal: structuredClone(body.proposal) };
            return Response.json({ status: "ready", draft,
                preview: { contractVersion: 1, rulesetId: "first-party-001",
                    fingerprint: currentFingerprint, previewOnly: true,
                    contentChangedSinceRequest: false, result: {
                        status: 4, issues: [], profile: null,
                        evaluatedProposal: { id: body.proposal.id,
                            candidates: [], manifestations: [] }
                    } },
                activeGrants: false });
        }
        if (path.endsWith("/origin/activate-native")) {
            const body = JSON.parse(init.body);
            assert.deepEqual(body, { expectedDraftRevision: 1 });
            savedState = { characterId, rulesetId: "first-party-001",
                fingerprint: currentFingerprint, revision: 1,
                sourceDraftRevision: 1, updatedAt: "2026-11-19T00:01:00Z",
                profile: { underlying: { speciesId: "species.human",
                    ancestryId: null } } };
            return Response.json({ status: "applied", revision: 1,
                effectiveMechanicsReady: false });
        }
        throw new Error("Unexpected direct request " + path);
    };
    await withBackend({ fetch }, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        await workflow.openChooser("species");
        assert.equal(workflow.current().chooser.kind, "open");
        await workflow.chooseOption(characterId, "species.human");
        assert.equal(workflow.current().draft.proposal.selectedSpeciesId,
            "species.human");
        assert.equal(workflow.current().savedState, null);
        await workflow.activate(characterId);
        assert.equal(workflow.current().savedState.profile.underlying.speciesId,
            "species.human");
        assert.equal(calls.filter(call =>
            call.path.endsWith("/origin/activate-native")).length, 1);
        assert.equal(calls.some(call => call.path.includes("activate-mixed")), false);
    });
});

test("an authorized-looking but stale direct preview cannot be activated", async () => {
    const direct = { id: "direct-stale", intent: 0,
        method: { ruleId: "rule.creature-origination", methodId: "biological" },
        contributors: [], candidates: [], manifestations: [],
        selectedSpeciesId: "species.human", selectedAncestryId: null };
    let activations = 0;
    const fetch = async (url, init) => {
        const path = String(url);
        if (path.endsWith("/api/native-rules/ruleset")) return Response.json({
            contractVersion: 1, rulesetId: "first-party-001",
            fingerprint: currentFingerprint, schemaVersion: 1,
            capabilities: { nativeOptionSearch: true }
        });
        if (path.endsWith("/origin/state")) return Response.json({
            status: "ready", savedState: null, events: [],
            readOnly: false, effectiveMechanicsReady: false
        });
        if (path.endsWith("/origin/draft") && init.method === "GET")
            return Response.json({ status: "ready", activeGrants: false,
                draft: { characterId, rulesetId: "first-party-001",
                    fingerprint: oldFingerprint, proposal: direct, revision: 2,
                    active: false, updatedAt: "2026-11-12T00:00:00Z" },
                preview: { contractVersion: 1, rulesetId: "first-party-001",
                    fingerprint: oldFingerprint, previewOnly: true,
                    contentChangedSinceRequest: true, result: {
                        status: 4, issues: [], evaluatedProposal: direct, profile: null
                    } } });
        if (path.endsWith("/origin/activate-native")) {
            activations++;
            return Response.json({ status: "applied", revision: 1,
                effectiveMechanicsReady: false });
        }
        throw new Error("Unexpected stale direct request " + path);
    };
    await withBackend({ fetch }, async () => {
        const workflow = createOriginWorkflow(environment, () => {});
        await workflow.load(characterId);
        await workflow.activate(characterId);
        assert.equal(activations, 0);
        assert.equal(workflow.current().draft.revision, 2);
    });
});
