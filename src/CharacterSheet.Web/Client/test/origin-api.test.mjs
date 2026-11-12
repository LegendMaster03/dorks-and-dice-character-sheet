import test from "node:test";
import assert from "node:assert/strict";
import {
    activateNativeOrigin,
    loadOriginDraft,
    loadOriginState,
    originBackendUrl,
    previewOriginProposal,
    saveOriginDraft
} from "../.test-dist/features/origin/origin-api.js";
import { CharacterSheetApiError } from "../.test-dist/character-api.js";

const id = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${id}`,
    contextUrl: `/tool-host/character-sheet/context?toolRoute=%2Fcharacters%2F${id}`,
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const draft = {
    id: "initial-native",
    intent: "Direct",
    method: { ruleId: "rule.creature-origination", methodId: "biological" },
    contributors: [],
    candidates: [],
    manifestations: [],
    selectedSpeciesId: "species.orc"
};
const draftResponse = {
    status: "ready",
    draft: {
        characterId: id,
        rulesetId: "first-party-001",
        fingerprint: "some-content-hash",
        proposal: draft,
        revision: 1,
        updatedAt: "2026-11-12T00:00:00Z",
        active: false
    },
    preview: {
        contractVersion: 1,
        rulesetId: "first-party-001",
        fingerprint: "latest",
        previewOnly: true,
        contentChangedSinceRequest: true,
        result: { status: 4, issues: [] }
    },
    activeGrants: false
};

test("Origin endpoints stay behind the same Site Tool Host character boundary", () => {
    assert.equal(originBackendUrl(environment, id, "draft"),
        `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/draft`);
    assert.equal(originBackendUrl(environment, id, "state"),
        `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/state`);
    assert.equal(originBackendUrl(environment, id, "activate-native"),
        `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/activate-native`);
});

test("saving an Origin draft transmits only selected proposal and expected revision", async () => {
    const calls = [];
    const fetcher = async (path, init) => {
        calls.push({ path: String(path), method: init.method, credentials: init.credentials,
            cache: init.cache, body: JSON.parse(init.body) });
        return Response.json(draftResponse);
    };
    const saved = await saveOriginDraft(environment, id, "first-party-001",
        "stale-hash", draft, 0, fetcher);
    assert.equal(saved.draft.revision, 1);
    assert.deepEqual(calls, [{
        path: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/draft`,
        method: "PUT",
        credentials: "same-origin",
        cache: "no-store",
        body: {
            rulesetId: "first-party-001",
            fingerprint: "stale-hash",
            proposal: draft,
            expectedRevision: 0
        }
    }]);
    assert.equal(calls[0].body.proposal.history, undefined);
    assert.equal(calls[0].body.proposal.tableDecisions, undefined);
});

test("browser can not place prior profiles, choices proofs or DM rulings in a draft", async () => {
    let calls = 0;
    const fetcher = async () => { calls++; return Response.json(draftResponse); };
    for (const addition of [
        { existingConfiguration: { speciesId: "species.human" } },
        { history: [{ id: "construction:forged" }] },
        { tableDecisions: [{ id: "dm-forged" }] },
        { activeTemporaryTransformation: { id: "form-forged" } },
        { otherActiveTemporaryTransformations: [] }
    ]) {
        await assert.rejects(() => saveOriginDraft(environment, id, "first-party-001",
            null, { ...draft, ...addition }, 0, fetcher), /server-only state/);
    }
    await assert.rejects(() => saveOriginDraft(environment, id, "first-party-001",
        null, { ...draft, manifestations: [{
            id: "choice-1", candidateId: "direct:species.human:/traits/0",
            choices: [{
                requirementId: "selection",
                selectedOptionIds: ["skill.intimidation"],
                catalogEligibilityEvidenceId: "native-catalog:forged"
            }]
        }] }, 0, fetcher), /server-only state/);
    await assert.rejects(() => saveOriginDraft(environment, id, "first-party-001",
        null, draft, -1, fetcher), /invalid proposal or revision/);
    assert.equal(calls, 0);
});

test("draft GET preserves incomplete state and cannot activate grants", async () => {
    const called = [];
    const fetcher = async (path, init) => {
        called.push({ method: init.method, path: String(path) });
        return Response.json({ status: "ready", draft: null, activeGrants: false });
    };
    const result = await loadOriginDraft(environment, id, fetcher);
    assert.equal(result.draft, null);
    assert.equal(result.activeGrants, false);
    assert.deepEqual(called, [{
        method: "GET",
        path: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/draft`
    }]);
});

test("saved Origin is a read-only snapshot, not current cross-pillar mechanics", async () => {
    const state = {
        status: "ready",
        savedState: {
            characterId: id,
            rulesetId: "first-party-001",
            fingerprint: "latest",
            profile: { id: "origin:initial-native" },
            revision: 1,
            sourceDraftRevision: 1,
            updatedAt: "now"
        },
        events: [{ eventId: "construction:initial-native",
            committedRevision: 1, methodRuleId: "rule.creature-origination",
            methodId: "biological", createdAt: "now" }],
        readOnly: false,
        effectiveMechanicsReady: false
    };
    const loaded = await loadOriginState(environment, id, async () => Response.json(state));
    assert.equal(loaded.savedState.revision, 1);
    assert.equal(loaded.effectiveMechanicsReady, false);
    assert.equal(loaded.events.length, 1);
    await assert.rejects(() => loadOriginState(environment, id, async () =>
        Response.json({ ...state, effectiveMechanicsReady: true })),
    /invalid trust contract/);
});

test("initial activation sends only a saved-draft revision, not serialized authority", async () => {
    const calls = [];
    const fetcher = async (path, init) => {
        calls.push({ path: String(path), method: init.method, body: JSON.parse(init.body) });
        return Response.json({ status: "applied", revision: 1,
            effectiveMechanicsReady: false });
    };
    const applied = await activateNativeOrigin(environment, id, 2, fetcher);
    assert.equal(applied.revision, 1);
    assert.deepEqual(calls, [{
        path: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/activate-native`,
        method: "POST",
        body: { expectedDraftRevision: 2 }
    }]);
    await assert.rejects(() => activateNativeOrigin(environment, id, 0, fetcher),
        /existing saved draft revision/);
    assert.equal(calls.length, 1);
});

test("Origin API preserves server-side rejection and rejects untrusted success shapes", async () => {
    await assert.rejects(() => activateNativeOrigin(environment, id, 1,
        async () => Response.json({ error: "native_origin_not_authorized" }, { status: 422 })),
    error => error instanceof CharacterSheetApiError &&
        error.status === 422 && error.message === "native_origin_not_authorized");

    await assert.rejects(() => saveOriginDraft(environment, id, "first-party-001",
        null, draft, 0, async () => Response.json({
            ...draftResponse, activeGrants: true
        })), /invalid trust contract/);
    await assert.rejects(() => activateNativeOrigin(environment, id, 1,
        async () => Response.json({
            status: "applied", revision: 1, effectiveMechanicsReady: true
        })), /invalid trust contract/);
});

test("stateless preview retains native authority on the server", async () => {
    const calls = [];
    const fetcher = async (path, init) => {
        calls.push({ path: String(path), method: init.method, body: JSON.parse(init.body) });
        return Response.json({
            contractVersion: 1, rulesetId: "first-party-001",
            fingerprint: "f".repeat(64), contentChangedSinceRequest: true,
            previewOnly: true,
            result: { status: 1, profile: null, evaluatedProposal: draft,
                issues: [{ code: "choice-incomplete", path: "traits", detail: "Choice required." }] }
        });
    };
    const review = await previewOriginProposal(environment, "first-party-001",
        "old-native-fingerprint", draft, fetcher);
    assert.equal(review.previewOnly, true);
    assert.equal(review.result.status, 1);
    assert.deepEqual(calls, [{
        path: "/tool-host/character-sheet/api/upstream/api/native-rules/origins/preview",
        method: "POST",
        body: { rulesetId: "first-party-001", fingerprint: "old-native-fingerprint",
            proposal: draft }
    }]);
    await assert.rejects(() => previewOriginProposal(environment, "first-party-001",
        null, draft, async () => Response.json({ previewOnly: false,
            rulesetId: "first-party-001", fingerprint: "latest", result: { issues: [] } })),
    /stateless Origin preview/);
});
