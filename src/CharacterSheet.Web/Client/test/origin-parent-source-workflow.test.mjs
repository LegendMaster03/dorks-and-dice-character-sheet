import test from "node:test";
import assert from "node:assert/strict";
import { createOriginParentSourceInspectionWorkflow } from
    "../.test-dist/features/origin/origin-parent-source-workflow.js";

const environment = {
    embedded: true, toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/some-child", contextUrl: null,
    standaloneDevelopment: false
};
const childId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const parentOne = "e85ec742-4991-47ef-8994-c7bb08df99ca";
const parentTwo = "72b0b0f1-8a0b-4f40-9d15-a99c843db8c3";
const sources = [
    { contributorId: "parent-one", sourceId: "species.human" },
    { contributorId: "parent-two", sourceId: "species.elf" }
];
const options = [
    { id: childId, name: "Child", archived: false },
    { id: parentOne, name: "Parent One", archived: false },
    { id: parentTwo, name: "Parent Two", archived: true }
];
function accepted(bindings, revision) {
    return {
        status: "ready", draftRevision: revision,
        matches: bindings.map((binding, index) => ({
            ...binding, nativeSourceId: sources[index].sourceId,
            parentOriginRevision: 1
        })),
        inspectionOnly: true, relationshipVerified: false,
        inheritanceAuthorized: false, activeGrants: false,
        effectiveMechanicsReady: false
    };
}

test("owner parent inspection is temporary, source-qualified and read-only", async () => {
    const calls = [];
    const notifications = [];
    const flow = createOriginParentSourceInspectionWorkflow(
        environment, () => notifications.push(true), {
            loadOptions: async () => options,
            review: async (_environment, characterId, revision, bindings) => {
                calls.push({ characterId, revision, bindings });
                return accepted(bindings, revision);
            }
        });
    flow.prepare(childId, 4, sources);
    assert.equal(flow.current().result, null);
    await flow.loadOptions();
    assert.deepEqual(flow.current().options.map(candidate => candidate.id),
        [parentOne, parentTwo], "the child can not select itself");
    flow.select("parent-one", parentOne);
    flow.select("parent-two", parentTwo);
    await flow.review();
    assert.equal(calls.length, 1);
    assert.equal(calls[0].characterId, childId);
    assert.equal(calls[0].revision, 4);
    assert.deepEqual(calls[0].bindings, [
        { contributorId: "parent-one", parentCharacterId: parentOne },
        { contributorId: "parent-two", parentCharacterId: parentTwo }
    ]);
    assert.equal(flow.current().result.matches[1].nativeSourceId, "species.elf");
    assert.equal(flow.current().result.relationshipVerified, false);
    assert.ok(notifications.length > 0);
    flow.select("parent-two", "");
    assert.equal(flow.current().result, null,
        "changing a proposed parent immediately invalidates the prior review");
    await flow.review();
    assert.equal(calls.length, 1,
        "incomplete source bindings must never call the server");
    assert.match(flow.current().message, /Select a different owned Character/);
});

test("duplicate parent choices and unknown characters are refused client-side", async () => {
    let calls = 0;
    const flow = createOriginParentSourceInspectionWorkflow(environment, () => {}, {
        loadOptions: async () => options,
        review: async (_e, _id, revision, bindings) => {
            calls++; return accepted(bindings, revision);
        }
    });
    flow.prepare(childId, 2, sources);
    await flow.loadOptions();
    flow.select("parent-one", parentOne);
    flow.select("parent-two", parentOne);
    assert.equal(flow.current().selected["parent-two"], undefined);
    assert.match(flow.current().message, /different Character/);
    flow.select("parent-two", childId);
    assert.equal(flow.current().selected["parent-two"], undefined);
    flow.select("unrecognized-contributor", parentTwo);
    assert.equal(flow.current().selected["unrecognized-contributor"], undefined);
    await flow.review();
    assert.equal(calls, 0);
});

test("late native review can not overwrite a reloaded or edited draft", async () => {
    let release;
    const barrier = new Promise(resolve => { release = resolve; });
    const flow = createOriginParentSourceInspectionWorkflow(environment, () => {}, {
        loadOptions: async () => options,
        review: async (_e, _id, revision, bindings) => {
            await barrier;
            return accepted(bindings, revision);
        }
    });
    flow.prepare(childId, 1, sources);
    await flow.loadOptions();
    flow.select("parent-one", parentOne);
    flow.select("parent-two", parentTwo);
    const pending = flow.review();
    assert.equal(flow.current().busy, true);
    flow.prepare(childId, 2, sources);
    release();
    await pending;
    assert.equal(flow.current().result, null);
    assert.equal(flow.current().busy, false);
    assert.equal(flow.current().optionsStatus, "idle",
        "draft revision changes discard every previous parent selection");
    assert.deepEqual(flow.current().selected, {});
});

test("late owner list response and native source mismatches fail closed", async () => {
    let finish;
    const delayedOptions = new Promise(resolve => { finish = resolve; });
    const flow = createOriginParentSourceInspectionWorkflow(environment, () => {}, {
        loadOptions: async () => delayedOptions,
        review: async (_e, _id, revision, bindings) => ({
            ...accepted(bindings, revision),
            matches: accepted(bindings, revision).matches.map(match =>
                ({ ...match, nativeSourceId: "species.forged" }))
        })
    });
    flow.prepare(childId, 1, sources);
    const pending = flow.loadOptions();
    flow.reset();
    finish(options);
    await pending;
    assert.equal(flow.current().optionsStatus, "idle");
    assert.equal(flow.current().options.length, 0);

    const mismatchFlow = createOriginParentSourceInspectionWorkflow(
        environment, () => {}, {
            loadOptions: async () => options,
            review: async (_e, _id, revision, bindings) => ({
                ...accepted(bindings, revision),
                matches: accepted(bindings, revision).matches.map(match =>
                    ({ ...match, nativeSourceId: "species.forged" }))
            })
        });
    mismatchFlow.prepare(childId, 1, sources);
    await mismatchFlow.loadOptions();
    mismatchFlow.select("parent-one", parentOne);
    mismatchFlow.select("parent-two", parentTwo);
    await mismatchFlow.review();
    assert.equal(mismatchFlow.current().result, null);
    assert.match(mismatchFlow.current().message, /does not match this saved Origin draft/);
});
