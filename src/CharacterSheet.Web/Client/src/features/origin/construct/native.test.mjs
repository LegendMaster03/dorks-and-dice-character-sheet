import test from "node:test";
import assert from "node:assert/strict";
import { loadConstructAllowance, loadConstructOptions } from "../../../../.construct-test-dist/features/origin/construct/native.js";

const fingerprint = "a".repeat(64);
const metadata = { contractVersion: 1, rulesetId: "first-party-001", fingerprint,
    capabilities: { nativeOptionSearch: true } };
const env = { embedded: true, contextUrl: "/tool-host/registrations/character-sheet/context" };
const envelope = { contractVersion: 1, rulesetId: metadata.rulesetId, fingerprint, previewOnly: true };
const method = { reference: { ruleId: "rule.creature-origination", methodId: "construct-assembly" },
    support: "assembly-preview" };
const respond = body => async () => new Response(JSON.stringify(body));
test("allowance is fingerprint-pinned native metadata, never a fallback constant", async () => {
    assert.equal(await loadConstructAllowance(env, metadata, respond({ ...envelope, methods: [
        { ...method, initialConstructionAllowance: 137 }] })), 137);
    assert.equal(await loadConstructAllowance(env, metadata, respond({ ...envelope, methods: [method] })), null);
    assert.equal(await loadConstructAllowance(env, metadata, respond({ ...envelope, methods: [
        { ...method, support: "reference-only", initialConstructionAllowance: 137 }] })), null);
    await assert.rejects(loadConstructAllowance(env, metadata, respond({ ...envelope, fingerprint: "b".repeat(64), methods: [method] })));
    await assert.rejects(loadConstructAllowance(env, metadata, respond({ ...envelope, methods: [
        { ...method, initialConstructionAllowance: -1 }] })));
});
test("component identity browsing retains only native role metadata", async () => {
    let called;
    const options = await loadConstructOptions(env, metadata, "construct-component", async (url, request) => {
        called = { url, request };
        return new Response(JSON.stringify({ ...envelope, category: "construct-component", total: 1, offset: 0,
            options: [{ id: "construct-component.frame", name: "Frame", kind: "construct-component",
                componentCategory: "structural", privateDefinition: { ignored: true } }] }));
    });
    assert.match(called.url, /\/api\/native-rules\/options\?/);
    assert.equal(called.request.credentials, "same-origin");
    assert.deepEqual(options, [{ id: "construct-component.frame", name: "Frame", componentCategory: "structural" }]);
    await assert.rejects(loadConstructOptions(env, metadata, "construct-component",
        respond({ ...envelope, category: "construct-component", total: 1, offset: 0,
            options: [{ id: "../working", name: "Unsafe", kind: "construct-component" }] })));
});
