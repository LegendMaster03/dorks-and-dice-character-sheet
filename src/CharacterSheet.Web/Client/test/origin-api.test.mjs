import test from "node:test";
import assert from "node:assert/strict";
import {
    activateNativeOrigin,
    loadOriginDraft,
    loadOriginState,
    loadOwnerOriginParentOptions,
    inspectOwnerVisibleParentOrigin,
    reviewOwnerVisibleParentNativeOrigin,
    reviewOwnerVisibleParentSourceAlignment,
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

test("owned parent inspection exposes only stored identity, not inherited grants", async () => {
    const parentId = "9c97ec43-39bd-45d9-9264-64387d0f6cb6";
    const seen = [];
    const expected = {
        status: "ready",
        snapshot: {
            parentCharacterId: parentId,
            rulesetId: "first-party-001",
            fingerprint: "native-1",
            revision: 2,
            snapshotUpdatedAt: "2026-11-12T00:00:00Z",
            underlying: {
                speciesId: "species.human",
                ancestryId: null,
                profileId: "original"
            },
            current: {
                speciesId: "species.werewolf",
                ancestryId: null,
                profileId: "shifted"
            }
        },
        inspectionOnly: true,
        inheritanceAuthorized: false,
        effectiveMechanicsReady: false
    };
    const reply = await inspectOwnerVisibleParentOrigin(environment, id, parentId,
        async (url, init) => {
            seen.push({ url: String(url), method: init.method,
                credentials: init.credentials, cache: init.cache,
                body: init.body });
            return Response.json(expected);
        });
    assert.deepEqual(reply, expected);
    assert.deepEqual(seen, [{
        url: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/parent-snapshot/${parentId}`,
        method: "GET",
        credentials: "same-origin",
        cache: "no-store",
        body: undefined
    }]);

    // A malicious or malfunctioning endpoint must not silently elevate a
    // stored snapshot into a verified inheritance or effective-mechanics claim.
    for (const alternate of [
        { ...expected, inheritanceAuthorized: true },
        { ...expected, inspectionOnly: false },
        { ...expected, effectiveMechanicsReady: true },
        { ...expected, snapshot: { ...expected.snapshot, parentCharacterId: id } },
        { ...expected, snapshot: { ...expected.snapshot,
            current: { ...expected.snapshot.current, inherentGrants: [] } } }
    ]) {
        await assert.rejects(() => inspectOwnerVisibleParentOrigin(
            environment, id, parentId, async () => Response.json(alternate)),
        /invalid trust contract/);
    }
});

test("parent snapshot inspection rejects malformed IDs and inaccessible parents", async () => {
    let calls = 0;
    await assert.rejects(() => inspectOwnerVisibleParentOrigin(environment, id,
        "../secrets", async () => { calls++; return Response.json({}); }),
    /Site Character ID/);
    assert.equal(calls, 0);

    await assert.rejects(() => inspectOwnerVisibleParentOrigin(
        environment, id, "9c97ec43-39bd-45d9-9264-64387d0f6cb6",
        async () => Response.json({ error: "not_found" }, { status: 404 })),
    error => error instanceof CharacterSheetApiError && error.status === 404);
});

test("native parent Origin review is inspection-only, not an inheritance grant", async () => {
    const parentId = "9c97ec43-39bd-45d9-9264-64387d0f6cb6";
    const expected = {
        status: "ready",
        review: {
            parentCharacterId: parentId, rulesetId: "first-party-001",
            storedRevision: 1, latestNativeFingerprint: "native-current",
            verifiedProposalId: "parent-creation-1"
        },
        inspectedOriginMatchesLatestNativeCatalog: true,
        inspectionOnly: true,
        inheritanceAuthorized: false,
        effectiveMechanicsReady: false
    };
    const calls = [];
    const result = await reviewOwnerVisibleParentNativeOrigin(
        environment, id, parentId, async (url, options) => {
            calls.push({ url: String(url), method: options.method,
                credentials: options.credentials, body: options.body });
            return Response.json(expected);
        });
    assert.deepEqual(result, expected);
    assert.deepEqual(calls, [{
        url: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/parent-native-review/${parentId}`,
        method: "GET", credentials: "same-origin", body: undefined
    }]);
    assert.equal(result.inheritanceAuthorized, false);

    for (const changed of [
        { ...expected, inheritanceAuthorized: true },
        { ...expected, inspectedOriginMatchesLatestNativeCatalog: false },
        { ...expected, inspectionOnly: false },
        { ...expected, effectiveMechanicsReady: true },
        { ...expected, review: { ...expected.review, parentCharacterId: id } },
        { ...expected, review: { ...expected.review,
            grants: [{ id: "fabricated-origin-grant" }] } },
        { ...expected, review: { ...expected.review,
            profile: { underlying: { speciesId: "species.human" } } } }
    ]) {
        await assert.rejects(() => reviewOwnerVisibleParentNativeOrigin(
            environment, id, parentId, async () => Response.json(changed)),
        /invalid trust contract/);
    }
});

test("native parent Origin review never accepts a path traversal or inaccessible parent", async () => {
    let attempts = 0;
    await assert.rejects(() => reviewOwnerVisibleParentNativeOrigin(
        environment, id, "../private", async () => {
            attempts++;
            return Response.json({});
        }), /Site Character ID/);
    assert.equal(attempts, 0);
    await assert.rejects(() => reviewOwnerVisibleParentNativeOrigin(
        environment, id, "9c97ec43-39bd-45d9-9264-64387d0f6cb6",
        async () => Response.json({ error: "not_found" }, { status: 404 })),
    err => err instanceof CharacterSheetApiError && err.status === 404);
});

test("owner parent-source alignment carries only proposed IDs and draft revision", async () => {
    const parentOne = "e85ec742-4991-47ef-8994-c7bb08df99ca";
    const parentTwo = "72b0b0f1-8a0b-4f40-9d15-a99c843db8c3";
    const bindings = [
        { contributorId: "parent-one", parentCharacterId: parentOne },
        { contributorId: "parent-two", parentCharacterId: parentTwo }
    ];
    const calls = [];
    const response = {
        status: "ready", draftRevision: 3,
        matches: [
            { ...bindings[0], nativeSourceId: "species.orc", parentOriginRevision: 1 },
            { ...bindings[1], nativeSourceId: "ancestry.high-elf", parentOriginRevision: 1 }
        ],
        inspectionOnly: true,
        relationshipVerified: false,
        inheritanceAuthorized: false,
        activeGrants: false,
        effectiveMechanicsReady: false
    };
    const fetcher = async (url, init) => {
        calls.push({ url: String(url), method: init.method,
            credentials: init.credentials, cache: init.cache, body: JSON.parse(init.body) });
        return Response.json(response);
    };
    const reviewed = await reviewOwnerVisibleParentSourceAlignment(
        environment, id, 3, bindings, fetcher);
    assert.equal(reviewed.matches.length, 2);
    assert.equal(reviewed.relationshipVerified, false);
    assert.deepEqual(calls, [{
        url: `/tool-host/character-sheet/api/upstream/api/characters/${id}/origin/parent-source-alignment-review`,
        method: "POST", credentials: "same-origin", cache: "no-store",
        body: { expectedDraftRevision: 3, bindings }
    }]);
    assert.equal(calls[0].body.parentProfile, undefined);
    assert.equal(calls[0].body.manifestations, undefined);
    assert.equal(calls[0].body.relationships, undefined);
});

test("owner parent-source alignment rejects forged grants, relation claims and ambiguous IDs", async () => {
    const parent = "e85ec742-4991-47ef-8994-c7bb08df99ca";
    const binding = [{ contributorId: "parent-one", parentCharacterId: parent }];
    const baseline = {
        status: "ready", draftRevision: 2,
        matches: [{ ...binding[0], nativeSourceId: "species.orc",
            parentOriginRevision: 1 }],
        inspectionOnly: true, relationshipVerified: false,
        inheritanceAuthorized: false, activeGrants: false,
        effectiveMechanicsReady: false
    };
    let calls = 0;
    const fetcher = async () => { calls++; return Response.json(baseline); };
    for (const bad of [
        [{ contributorId: "parent-one", parentCharacterId: id }],
        [binding[0], binding[0]],
        [{ contributorId: "parent-one", parentCharacterId: "not-a-guid" }],
        [{ contributorId: "", parentCharacterId: parent }]
    ]) {
        await assert.rejects(() => reviewOwnerVisibleParentSourceAlignment(
            environment, id, 2, bad, fetcher), /valid, distinct Site Character IDs/);
    }
    assert.equal(calls, 0);
    for (const extra of [
        { inheritanceAuthorized: true },
        { relationshipVerified: true },
        { activeGrants: true },
        { verifiedSources: [{ underlying: { inherentGrants: [] } }] },
        { matches: [{ ...baseline.matches[0], inherentGrants: [] }] },
        { matches: [{ ...baseline.matches[0], parentCharacterId: id }] },
        { draftRevision: 999 }
    ]) {
        await assert.rejects(() => reviewOwnerVisibleParentSourceAlignment(
            environment, id, 2, binding,
            async () => Response.json({ ...baseline, ...extra })),
        /invalid inspection-only trust contract/);
    }
    await assert.rejects(() => reviewOwnerVisibleParentSourceAlignment(
        environment, id, 2, binding,
        async () => Response.json({ error: "parent_origin_not_natively_verified" },
            { status: 409 })),
    error => error instanceof CharacterSheetApiError &&
        error.status === 409 &&
        error.message === "parent_origin_not_natively_verified");
});

test("Site owner characters are narrow name-based parent options, not sheet data", async () => {
    const parent = "e85ec742-4991-47ef-8994-c7bb08df99ca";
    const calls = [];
    const options = await loadOwnerOriginParentOptions(environment,
        async (url, init) => {
            calls.push({ url: String(url), method: init.method,
                credentials: init.credentials, cache: init.cache });
            return Response.json([{
                id: parent, name: "Orc Parent", status: "Archived",
                archivedAt: "2026-11-01T00:00:00Z",
                campaigns: [{ campaignId: "private", campaignName: "Other Campaign" }]
            }]);
        });
    assert.deepEqual(calls, [{
        url: "/characters/api?includeArchived=true", method: "GET",
        credentials: "same-origin", cache: "no-store"
    }]);
    assert.deepEqual(options, [{ id: parent, name: "Orc Parent", archived: true }]);
    assert.equal(options[0].campaigns, undefined);
    assert.equal(options[0].archivedAt, undefined);
});

test("Site owner character options reject untrusted or unavailable responses", async () => {
    const parent = "e85ec742-4991-47ef-8994-c7bb08df99ca";
    const rows = [{ id: parent, name: "Owner Parent", status: "Active" }];
    let calls = 0;
    await assert.rejects(() => loadOwnerOriginParentOptions(
        { ...environment, embedded: false }, async () => {
            calls++; return Response.json(rows);
        }), /authenticated Dorks & Dice Site/);
    assert.equal(calls, 0);
    for (const invalid of [
        { characters: rows },
        [{ id: "../escape", name: "X", status: "Active" }],
        [{ id: parent, name: "", status: "Active" }],
        [{ id: parent, name: "X", status: "NotOwned" }],
        [rows[0], rows[0]]
    ]) {
        await assert.rejects(() => loadOwnerOriginParentOptions(
            environment, async () => Response.json(invalid)),
        /invalid owner identity contract/);
    }
    await assert.rejects(() => loadOwnerOriginParentOptions(
        environment, async () =>
            Response.json({ error: "unauthorized" }, { status: 401 })),
    error => error instanceof CharacterSheetApiError && error.status === 401);
});
