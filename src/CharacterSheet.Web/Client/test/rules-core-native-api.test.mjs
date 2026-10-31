import assert from "node:assert/strict";
import test from "node:test";
import {
    loadNativeRuleset, searchNativeOptions, evaluateNativeAdvancement, projectNativeCharacter
} from "../.test-dist/rules-core-native-api.js";

const fingerprint = "a".repeat(64);
const metadata = {
    contractVersion: 1, rulesetId: "first-party-001", schemaVersion: 1, fingerprint,
    capabilities: { nativeOptionSearch: true, characterProjection: true, resourceResolution: true,
        advancementEligibility: "supported-subset", conditionRollMode: "conditional-subset",
        campaignEffectiveRules: false }
};
const env = { embedded: true };
const character = {
    rulesetId: metadata.rulesetId, fingerprint, abilityScores: {
        strength: 10, dexterity: 14, constitution: 12,
        intelligence: 10, wisdom: 10, charisma: 16
    },
    advancements: [{ classId: "class.warlock", level: 2, occurrenceId: "a-valid-occurrence" }],
    selectedOptions: ["pact.warlock.chain"]
};
const reply = (value, status = 200) => async () =>
    new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json" } });

// The local compilation stub matches the upstream buildRulesCoreUrl path shape.
test("ruleset metadata uses the existing delegated Site route", async () => {
    let observed;
    const result = await loadNativeRuleset(env, async (url, options) => {
        observed = { url, options };
        return new Response(JSON.stringify(metadata));
    });
    assert.deepEqual(result, metadata);
    assert.equal(observed.url,
        "/tool-host/registrations/rules-core/api/upstream/api/mechanics/v1/ruleset");
    assert.equal(observed.options.credentials, "same-origin");
});

test("option search passes the snapshot and returns only allowed summary fields", async () => {
    let called;
    const expected = { contractVersion: 1, rulesetId: metadata.rulesetId,
        fingerprint, category: "pact", total: 1, limit: 8, offset: 0,
        options: [{ id: "pact.warlock.chain", name: "Chain", kind: "pact",
            sourcePath: "/private/path", definition: { hidden: true } }] };
    const page = await searchNativeOptions(env, metadata, "pact", "Chain", 8, 0,
        async (url, options) => {
            called = { url, options };
            return new Response(JSON.stringify(expected));
        });
    assert.deepEqual(page.options, [{ id: "pact.warlock.chain", name: "Chain", kind: "pact" }]);
    assert.match(called.url, /fingerprint=a{64}/);
    assert.match(called.url, /category=pact/);
    assert.match(called.url, /q=Chain/);
});

test("stale catalog, unauthorized response, and suspicious options fail closed", async () => {
    await assert.rejects(searchNativeOptions(env, metadata, "pact", "", 10, 0,
        reply({ ...metadata, category: "pact", options: [], total: 0, fingerprint: "b".repeat(64) })),
        /different native snapshot/);
    await assert.rejects(searchNativeOptions(env, metadata, "source", "", 10, 0,
        reply({})), /Invalid native option/);
    await assert.rejects(searchNativeOptions(env, metadata, "pact", "", 101, 0,
        reply({})), /Invalid native option/);
    await assert.rejects(searchNativeOptions(env, metadata, "pact", "", 10, 0,
        reply({}, 403)), /not authorized/);
    await assert.rejects(searchNativeOptions(env, metadata, "pact", "", 10, 0,
        reply({}, 409)), /catalog changed/);
    await assert.rejects(searchNativeOptions(env, metadata, "pact", "", 10, 0,
        reply({ contractVersion: 1, rulesetId: metadata.rulesetId, fingerprint,
            category: "pact", total: 1, options: [{ id: "../working", name: "unsafe", kind: "pact" }] })),
        /invalid native option identity/);
});

test("eligibility submits native character state and preserves unresolved", async () => {
    let payload;
    const state = { contractVersion: 1, rulesetId: metadata.rulesetId, fingerprint,
        candidateId: "invocation.chain-gift", candidateKind: "invocation",
        status: "unresolved", eligible: null,
        requirements: [{ key: "cantrip", status: "unresolved", detail: "unknown" }] };
    const result = await evaluateNativeAdvancement(env, metadata,
        "invocation.chain-gift", character, async (_url, options) => {
            payload = JSON.parse(options.body);
            return new Response(JSON.stringify(state));
        });
    assert.equal(payload.character.selectedOptions[0], "pact.warlock.chain");
    assert.equal(payload.fingerprint, fingerprint);
    assert.equal(result.eligible, null);
    await assert.rejects(evaluateNativeAdvancement(env, metadata,
        "rule:legacy", character, reply(state)), /Native candidate ID is invalid/);
    await assert.rejects(evaluateNativeAdvancement(env, metadata,
        "invocation.chain-gift", { ...character, advancements: [{
            ...character.advancements[0], classId: "rule:warlock"
        }] }, reply(state)), /native class IDs/);
    await assert.rejects(evaluateNativeAdvancement(env, metadata,
        "invocation.chain-gift", character,
        reply({ ...state, eligible: true })), /inconsistent eligibility/);
});

test("character projection preserves unsupported numerical values and source identity", async () => {
    const expected = { contractVersion: 1, rulesetId: metadata.rulesetId, fingerprint,
        status: "partial", values: {
            "ac.touch": { status: "unsupported", value: null, reason: "Typed defenses required" },
            "ability.dexterity.modifier": { status: "resolved", value: 2, reason: null }
        }, featureIds: [], resources: [], requiredChoices: [], issues: [{
            code: "defense-unresolved", path: "ac.touch", detail: "unknown"
        }] };
    const result = await projectNativeCharacter(env, metadata, character, reply(expected));
    assert.equal(result.values["ac.touch"].value, null);
    assert.equal(result.values["ac.touch"].status, "unsupported");
    assert.equal(result.status, "partial");
    await assert.rejects(projectNativeCharacter(env, metadata,
        { ...character, fingerprint: "b".repeat(64) }, reply(expected)), /different Rules Core snapshot/);
    await assert.rejects(projectNativeCharacter(env, metadata, character,
        reply({ ...expected, fingerprint: "c".repeat(64) })), /different native snapshot/);
});
