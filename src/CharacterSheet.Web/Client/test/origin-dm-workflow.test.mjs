import test from "node:test";
import assert from "node:assert/strict";
import { createOriginDmWorkflow } from "../.test-dist/features/origin/origin-dm-workflow.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const campaignId = "3e50b027-9c43-401f-bb31-ae4ae7409ce4";
const environment = {
    embedded: true, toolBasePath: "/tools/character-sheet",
    toolRoute: `/dm-origin-review/${campaignId}/${characterId}`,
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false
};
const candidates = Array.from({ length: 5 }, (_, index) => ({
    id: "candidate-" + index,
    contributorId: index % 2 ? "parent-two" : "parent-one",
    sourceId: index % 2 ? "species.elf" : "species.human",
    occurrencePath: index < 3 ? "/intrinsics/" + index : "/traits/" + index,
    kind: index < 3 ? "0" : "1",
    intrinsicSlotId: index < 3 ? ["creatureType", "size", "walkingSpeedFeet"][index] : null,
    offeredValue: index, requiresChoices: false
}));
const review = {
    status: "ready", draftRevision: 3, rulesetId: "first-party-001",
    fingerprint: "f".repeat(64), proposalId: "two-sources", candidates,
    previewOnly: true, activeGrants: false
};

test("DM review workflow cannot apply unreviewed, partial, or choice-dependent candidates", async () => {
    const calls = [];
    let notifications = 0;
    const workflow = createOriginDmWorkflow(environment,
        () => { notifications++; }, async (_url, init) => {
            calls.push({ method: init.method,
                body: init.body === undefined ? null : JSON.parse(init.body) });
            return init.method === "GET" ? Response.json(review) : Response.json({
                status: "applied", revision: 1, effectiveMechanicsReady: false
            });
        });
    await workflow.load(characterId, campaignId);
    assert.equal(workflow.current().status, "ready");
    workflow.toggleCandidate("unknown");
    assert.deepEqual(workflow.current().selectedCandidateIds, []);
    await workflow.apply(characterId, campaignId);
    assert.equal(calls.length, 1);
    for (const offer of candidates.slice(0, 4)) {
        workflow.toggleCandidate(offer.id);
    }
    await workflow.apply(characterId, campaignId);
    assert.equal(calls.length, 1);
    workflow.toggleCandidate(candidates[4].id);
    assert.equal(workflow.current().selectedCandidateIds.length, 5);
    await workflow.apply(characterId, "another-campaign");
    assert.equal(calls.length, 1);
    await workflow.apply(characterId, campaignId);
    assert.equal(calls.length, 2);
    assert.deepEqual(calls[1], {
        method: "POST",
        body: { campaignId, expectedDraftRevision: 3,
            candidateIds: candidates.map(x => x.id) }
    });
    assert.equal(workflow.current().status, "applied");
    assert.equal(workflow.current().committedRevision, 1);
    assert.equal(workflow.current().review, null);
    await workflow.apply(characterId, campaignId);
    assert.equal(calls.length, 2);
    assert.ok(notifications > 2);
});

test("DM review refresh clears selections and suppresses out-of-order responses", async () => {
    let resolveFirst;
    let readCalls = 0;
    const workflow = createOriginDmWorkflow(environment, () => {}, async () => {
        readCalls++;
        if (readCalls === 1) return await new Promise(resolve => { resolveFirst = resolve; });
        return Response.json({ ...review, draftRevision: 5 });
    });
    const first = workflow.load(characterId, campaignId);
    const second = workflow.load(characterId, campaignId);
    await second;
    resolveFirst(Response.json(review));
    await first;
    assert.equal(workflow.current().review.draftRevision, 5);
    workflow.toggleCandidate(candidates[0].id);
    assert.equal(workflow.current().selectedCandidateIds.length, 1);
    await workflow.load(characterId, campaignId);
    assert.equal(workflow.current().review.draftRevision, 5);
    assert.deepEqual(workflow.current().selectedCandidateIds, []);
});

test("DM review stays uncommitted when Site denies activation after the review", async () => {
    const workflow = createOriginDmWorkflow(environment, () => {}, async (_url, init) =>
        init.method === "GET"
            ? Response.json(review)
            : Response.json({ error: "not_found" }, { status: 404 }));
    await workflow.load(characterId, campaignId);
    for (const candidate of candidates) workflow.toggleCandidate(candidate.id);
    await workflow.apply(characterId, campaignId);
    assert.equal(workflow.current().status, "ready");
    assert.equal(workflow.current().committedRevision, undefined);
    assert.equal(workflow.current().busy, false);
    assert.match(workflow.current().message, /not_found/);
});

test("DM review never enables a choice-dependent candidate", async () => {
    const workflow = createOriginDmWorkflow(environment, () => {},
        async () => Response.json({ ...review, candidates: [
            { ...candidates[0], requiresChoices: true }, ...candidates.slice(1)
        ] }));
    await workflow.load(characterId, campaignId);
    workflow.toggleCandidate(candidates[0].id);
    assert.deepEqual(workflow.current().selectedCandidateIds, []);
});
