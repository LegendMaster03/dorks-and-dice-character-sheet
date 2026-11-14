import test from "node:test";
import assert from "node:assert/strict";
import {
    reviewDmMixedOrigin,
    activateDmApprovedMixedOrigin
} from "../.test-dist/features/origin/origin-dm-api.js";
import { CharacterSheetApiError } from "../.test-dist/character-api.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const campaignId = "3e50b027-9c43-401f-bb31-ae4ae7409ce4";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: `/tool-host/character-sheet/context?toolRoute=%2Fcharacters%2F${characterId}`,
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const prefix = `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/origin/`;
const offer = {
    id: "source-elf:trait-low-light",
    contributorId: "parent-two",
    sourceId: "species.elf",
    occurrencePath: "/traits/0",
    kind: "1",
    intrinsicSlotId: null,
    offeredValue: { name: "Low-Light Vision" },
    requiresChoices: false
};
const review = {
    status: "ready", draftRevision: 4, rulesetId: "first-party-001",
    fingerprint: "f".repeat(64), proposalId: "initial-mixed",
    candidates: [offer], previewOnly: true, activeGrants: false
};

test("DM review requests only exact campaign and character-scoped offer metadata", async () => {
    const calls = [];
    const result = await reviewDmMixedOrigin(environment, characterId, campaignId,
        async (path, init) => {
            calls.push({ path: String(path), method: init.method, credentials: init.credentials,
                cache: init.cache, body: init.body });
            return Response.json(review);
        });
    assert.equal(result.draftRevision, 4);
    assert.equal(result.candidates[0].sourceId, "species.elf");
    assert.deepEqual(calls, [{
        path: prefix + `mixed-review?campaignId=${campaignId}`,
        method: "GET", credentials: "same-origin", cache: "no-store",
        body: undefined
    }]);
});

test("DM review fails closed on malformed scopes and authority-bearing responses", async () => {
    let calls = 0;
    const fetcher = async () => { calls++; return Response.json(review); };
    for (const bad of ["", "not-a-guid", "../escaping", "00000000-0000-0000-0000-000000000000"]) {
        await assert.rejects(() => reviewDmMixedOrigin(environment, characterId,
            bad, fetcher), /valid Site UUID/);
    }
    await assert.rejects(() => reviewDmMixedOrigin(environment, "../escaping",
        campaignId, fetcher), /valid Site UUID/);
    assert.equal(calls, 0);

    for (const bad of [
        { ...review, activeGrants: true },
        { ...review, previewOnly: false },
        { ...review, profile: { id: "forged" } },
        { ...review, approvedDecisions: [{ id: "forged" }] },
        { ...review, draftRevision: 0 },
        { ...review, candidates: [offer, offer] },
        { ...review, candidates: [{ ...offer, authorization: { kind: "DM" } }] },
        { ...review, candidates: [{ ...offer, sourceId: "species.orc" }] },
        { ...review, candidates: [{ ...offer, occurrencePath: "" }] }
    ]) {
        await assert.rejects(() => reviewDmMixedOrigin(environment, characterId,
            campaignId, async () => Response.json(bad)), /contract/);
    }
});

test("DM activation submits only reviewed IDs and revision, never grants or DM evidence", async () => {
    const calls = [];
    const selected = [offer.id, "source-human:creature-type"];
    const result = await activateDmApprovedMixedOrigin(
        environment, characterId, campaignId, 4, selected,
        async (path, init) => {
            calls.push({ path: String(path), method: init.method,
                body: JSON.parse(init.body), credentials: init.credentials,
                cache: init.cache });
            return Response.json({
                status: "applied", revision: 1, effectiveMechanicsReady: false
            });
        });
    assert.equal(result.revision, 1);
    assert.deepEqual(calls, [{
        path: prefix + "activate-mixed-approved", method: "POST",
        credentials: "same-origin", cache: "no-store",
        body: { campaignId, expectedDraftRevision: 4, candidateIds: selected }
    }]);
    assert.equal(calls[0].body.profile, undefined);
    assert.equal(calls[0].body.approvedDecisions, undefined);
});

test("DM activation never sends duplicate, stale, or malformed offers", async () => {
    let calls = 0;
    const fetcher = async () => { calls++; return Response.json({
        status: "applied", revision: 1, effectiveMechanicsReady: false }); };
    for (const [revision, selected] of [
        [0, [offer.id]], [2.5, [offer.id]], [2, []],
        [2, [offer.id, offer.id]], [2, [" "]]
    ]) {
        await assert.rejects(() => activateDmApprovedMixedOrigin(
            environment, characterId, campaignId, revision, selected, fetcher),
        /distinct, reviewed offer selection/);
    }
    assert.equal(calls, 0);
    await assert.rejects(() => activateDmApprovedMixedOrigin(
        environment, characterId, "forged", 4, [offer.id], fetcher),
    /valid Site UUID/);
    assert.equal(calls, 0);
});

test("DM activation preserves server rejection and refuses overclaimed success", async () => {
    await assert.rejects(() => activateDmApprovedMixedOrigin(
        environment, characterId, campaignId, 4, [offer.id],
        async () => Response.json({ error: "origin_draft_revision_conflict" },
            { status: 409 })),
    error => error instanceof CharacterSheetApiError &&
        error.status === 409 && error.message === "origin_draft_revision_conflict");
    await assert.rejects(() => activateDmApprovedMixedOrigin(
        environment, characterId, campaignId, 4, [offer.id],
        async () => Response.json({ status: "applied", revision: 1,
            effectiveMechanicsReady: true })),
    /invalid trust contract/);
    await assert.rejects(() => reviewDmMixedOrigin(
        environment, characterId, campaignId,
        async () => Response.json({ error: "not_found" }, { status: 404 })),
    error => error instanceof CharacterSheetApiError && error.status === 404);
});
