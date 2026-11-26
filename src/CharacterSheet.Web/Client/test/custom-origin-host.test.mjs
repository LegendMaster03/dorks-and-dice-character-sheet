import test from "node:test";
import assert from "node:assert/strict";
import { createCustomOriginHost } from "../.test-dist/features/origin/custom-workspace.js";
import { resolveMethodEditor } from "../.test-dist/features/guided/contracts.js";
import { newBiologicalProposal } from "../.test-dist/features/origin/biological/model.js";
import { emptyConstructDraft, packConstructDraft, unpackConstructDraft, addComponent }
    from "../.test-dist/features/origin/construct/model.js";
const id = "test-character";
const metadata = { rulesetId: "test", fingerprint: "current" };
const biological = { reference: { ruleId: "rule.creature-origination", methodId: "biological" },
    stage: null, support: "initial-preview" };
const construct = { reference: { ruleId: "rule.creature-origination", methodId: "construct-assembly" },
    stage: null, support: "assembly-preview" };
const view = proposal => ({ characterId: id, rulesetId: "test", fingerprint: "current",
    proposal, revision: 1, active: false });
test("production custom host registers the exact native methods and supplies the full biological envelope", () => {
    const proposal = newBiologicalProposal("initial-" + id);
    const preview = { previewOnly: true, fingerprint: "current", rulesetId: "test",
        contentChangedSinceRequest: false, allocation: { allowance: 123 }, result: {} };
    const state = { status: "ready", catalog: metadata, draft: view(proposal), preview, previewDraftRevision: 1 };
    const host = createCustomOriginHost({}, id, { current: () => state }, () => {});
    assert.ok(resolveMethodEditor(biological, host.methodEditors));
    assert.ok(resolveMethodEditor(construct, host.methodEditors));
    assert.equal(resolveMethodEditor({ ...biological, stage: "post-origination" }, host.methodEditors), null);
    const presets = [{ id: "species.dragonborn" }];
    const context = host.methodContext(biological, presets);
    assert.equal(context.kind, "biological");
    assert.equal(context.editor.preview.result, preview);
    assert.equal(context.editor.presets, presets);
    context.editor.navigate("inheritance");
    assert.equal(host.methodContext(biological, presets).editor.decisionId, "inheritance");
    state.previewDraftRevision = 0;
    assert.equal(host.methodContext(biological, presets).editor.preview, null);
});
test("construct input survives a canonical proposal JSON round trip with distinct component instances", () => {
    let draft = emptyConstructDraft("initial-" + id, 123);
    draft = addComponent(addComponent(draft, "first", "frame"), "second", "frame");
    const packed = JSON.parse(JSON.stringify(packConstructDraft(draft)));
    const restored = unpackConstructDraft(packed);
    assert.deepEqual(restored, draft);
    assert.equal(Object.hasOwn(restored.proposal, "constructAssembly"), false);
});
test("failed biological save retains the exact edit; stale revisions cannot be silently retried", async () => {
    const proposal = newBiologicalProposal("initial-" + id);
    const state = { status: "ready", catalog: metadata, draft: view(proposal) };
    let received;
    const workflow = { current: () => state, async saveCustomDraft(characterId, next, revision) {
        received = { characterId, next, revision }; throw new Error("transport failed");
    } };
    const host = createCustomOriginHost({}, id, workflow, () => {});
    const next = { ...proposal, contributors: [{ id: "one" }] };
    const context = host.methodContext(biological, []);
    await assert.rejects(context.editor.save(next), /transport failed/);
    assert.equal(received.next, next);
    assert.equal(host.hasPendingCustomEdit(), true);
    assert.equal(host.methodContext(biological, []).editor.draft.saveStatus, "failed");
    state.draft = { ...state.draft, revision: 2 };
    await assert.rejects(context.editor.retry(), /saved Origin changed/);
    assert.equal(host.methodContext(biological, []).editor.draft.saveStatus, "conflict");
});
