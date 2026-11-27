import test from "node:test";
import assert from "node:assert/strict";
import { newBiologicalProposal, addNativeContributor, evaluateBiological,
    setInherited, setExpression, setFiniteChoice, removeContributor, allocationView,
    composableOffers, setComposition, clearComposition, supportsBiologicalDraft,
    setInheritedInteraction, removeInheritedInteraction, groupAllocationCharges }
    from "../.test-dist/features/origin/biological/model.js";

const preset = (id, name) => ({ id, name, kind: "ancestry", parentId: "species.dragonborn",
    selectable: true, defaultUpbringingId: null, acquisitionMethodIds: [], ancestrySelectionRequired: false });
const red = preset("ancestry.red-dragonborn", "Red Dragonborn");
const bronze = preset("ancestry.bronze-dragonborn", "Bronze Dragonborn");
const offer = (id, contributorId, sourceId, damage) => ({
    candidate: { id, contributorId,
        identity: { occurrence: { source: { id: sourceId }, occurrencePath: "/traits/0" },
            mechanicalDefinitionId: "trait.breath-weapon", kind: "Trait" },
        selectionBasis: { eligibility: "Eligible" }, offeredParameters: { damage },
        requiredChoices: [{ id: "choice:" + id, timing: "Creation", minimum: 1,
            maximum: 1, finiteOptions: ["one", "two"] }] }
});
const redOffer = offer("red-breath", "red", red.id, "Fire");
const bronzeOffer = offer("bronze-breath", "bronze", bronze.id, "Lightning");
const proposal = () => addNativeContributor(addNativeContributor(
    newBiologicalProposal("origin"), "red", red), "bronze", bronze);
function evaluation(p, genes = [], allocation = { status: "within-allowance", allowance: 17, committedLoad: 3 }) {
    return evaluateBiological(p, { previewOnly: true, result: {
        evaluatedProposal: { id: p.id }, issues: [],
        biologicalCombination: { proposalId: p.id, explicitTransmissionPlan: true,
            characteristics: [{ kind: "Trait", characteristicId: "trait.breath-weapon",
                offers: [redOffer, bronzeOffer], expressions: [] }],
            inheritedInstances: genes.map(([id, grants]) => ({ gene: { sourceCandidateId: id }, expressionGrantIds: grants })) }
    }, allocation }, true);
}
test("same kind Red and Bronze offers preserve distinct configurations and exact contributor identities", () => {
    const e = evaluation(proposal());
    assert.equal(e.groups.length, 1);
    assert.deepEqual(e.groups[0].offers.map(o => [o.id, o.parameters.damage]), [
        ["red-breath", "Fire"], ["bronze-breath", "Lightning"]]);
    assert.ok(e.groups[0].offers.every(o => o.state === "available"));
});
test("inherited instances may be entirely latent and expression comes only from native evidence", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    p = setInherited(p, evaluation(p), "bronze-breath", true);
    const e = evaluation(p, [["red-breath", []], ["bronze-breath", []]],
        { status: "within-allowance", allowance: 17, committedLoad: 0 });
    assert.deepEqual(e.groups[0].offers.map(o => o.state), ["latent", "latent"]);
    assert.equal(e.allocation.used, "0");
    p = setExpression(p, e, "red-breath", true);
    assert.equal(evaluation(p, [["red-breath", []], ["bronze-breath", []]]).groups[0].offers[0].state, "latent");
    assert.deepEqual(evaluation(p, [["red-breath", ["grant"]], ["bronze-breath", []]]).groups[0].offers.map(o => o.state), ["expressed", "latent"]);
});
test("moving between latent and requested expression retains the exact configured choice", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    p = setFiniteChoice(p, evaluation(p), "red-breath", "choice:red-breath", ["one"]);
    p = setExpression(p, evaluation(p), "red-breath", true);
    assert.deepEqual(p.manifestations[0].choices[0].selectedOptionIds, ["one"]);
    p = setExpression(p, evaluation(p), "red-breath", false);
    assert.deepEqual(p.biologicalTransmission.latentConfigurations[0].choices[0].selectedOptionIds, ["one"]);
});
test("changing one contributor retains another exact occurrence and its choices", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    p = setInherited(p, evaluation(p), "bronze-breath", true);
    p = setFiniteChoice(p, evaluation(p), "bronze-breath", "choice:bronze-breath", ["two"]);
    p = setExpression(p, evaluation(p), "bronze-breath", true);
    const changed = removeContributor(p, evaluation(p), "red");
    assert.deepEqual(changed.proposal.biologicalTransmission.candidateIds, ["bronze-breath"]);
    assert.deepEqual(changed.proposal.manifestations[0].choices[0].selectedOptionIds, ["two"]);
    assert.equal(changed.change.reconsider.length, 1);
});
test("native allocation uses the returned total, allowance and status; no offer sums", () => {
    assert.deepEqual([allocationView({status:"over-allowance", allowance:17, committedLoad:20}).status,
        allocationView({status:"over-allowance", allowance:17, committedLoad:20}).remaining], ["over", "-3"]);
    assert.equal(allocationView({status:"unresolved", allowance:17, committedLoad:null}).used, undefined);
    assert.equal(allocationView(null).status, "unavailable");
});
test("stale and non-preview evaluations cannot drive edits", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    const e = evaluateBiological(p, { previewOnly: true }, false);
    assert.equal(e.current, false);
    assert.throws(() => setInherited(p, e, "red-breath", true));
    assert.throws(() => removeContributor(p, e, "red"));
});
test("no four-source limit is inferred from the prior editor", () => {
    let p = proposal();
    for (let i = 0; i < 5; i++) p = addNativeContributor(p, "extra-" + i, red);
    assert.equal(p.contributors.length, 7);
    assert.throws(() => addNativeContributor(p, "red", red));
    assert.throws(() => addNativeContributor(p, "restricted", {...red, selectable: false}));
});

test("canonical native composition is source-bound, persisted, and removed on affected contributor change", () => {
    let p = proposal();
    const plain = [redOffer, bronzeOffer].map((o, i) => ({ candidate: {
        ...o.candidate, requiredChoices: [], offeredParameters: {
            damageType: i ? "lightning" : "fire", area: {shape: i ? "cone" : "line"}
        }
    }}));
    const evalFor = (draft, metadata = true) => evaluateBiological(draft, { previewOnly: true, result: {
        evaluatedProposal: { id: draft.id }, issues: [], biologicalCombination: {
            proposalId: draft.id, explicitTransmissionPlan: true,
            maxInheritedInstancesPerKind: 2, maxExpressedInstancesPerKind: 1,
            characteristics: [{ kind: "Trait", characteristicId: "trait.breath-weapon",
                offers: plain, expressions: [], ...(metadata ? { presentation: {
                    name: "Breath Weapon", recombinableParameters: ["damageType", "area"]
                }} : {}) }], inheritedInstances: []
        }
    }}, true);
    p = setInherited(p, evalFor(p), "red-breath", true);
    p = setInherited(p, evalFor(p), "bronze-breath", true);
    assert.equal(evalFor(p).maxInherited, 2);
    assert.equal(composableOffers(evalFor(p, false).groups[0]).length, 0);
    assert.throws(() => setComposition(p, evalFor(p, false), "trait.breath-weapon", "red-breath", {
        damageType: "red-breath", area: "bronze-breath"
    }));
    p = setComposition(p, evalFor(p), "trait.breath-weapon", "red-breath", {
        damageType: "red-breath", area: "bronze-breath"
    });
    assert.deepEqual(p.resolutions[0].replacementParameters, { damageType: "fire", area: { shape: "cone" } });
    assert.deepEqual(p.resolutions[0].replacementParameterSources, { damageType: "red-breath", area: "bronze-breath" });
    assert.equal(p.manifestations.length, 2);
    assert.equal(supportsBiologicalDraft(JSON.parse(JSON.stringify(p))), true);
    assert.equal(supportsBiologicalDraft({...p, resolutions:[{...p.resolutions[0], basis:{adjudication:3}}]}), false);
    const cleared = clearComposition(p, "trait.breath-weapon");
    assert.deepEqual(cleared.biologicalTransmission, p.biologicalTransmission);
    assert.equal(cleared.manifestations.length, 0);
    assert.equal(cleared.relationships.length, 0);
    const changed = removeContributor(p, evalFor(p), "red");
    assert.deepEqual(changed.proposal.biologicalTransmission.candidateIds, ["bronze-breath"]);
    assert.equal(changed.proposal.relationships.length, 0);
    assert.equal(changed.proposal.resolutions.length, 0);
    assert.ok(changed.change.reconsider.some(x => x.includes("Combined expression")));
});

test("inherited interactions require the native exact configured inputs and do not invent grants", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    p = setInherited(p, evaluation(p), "bronze-breath", true);
    const interaction = { ruleOwnerId: "rule.test", interactionId: "test-interaction",
        resultTraitId: "trait.test", resultName: "Test result",
        inputs: [{count: 2, candidateIds: ["red-breath", "bronze-breath"]}] };
    const native = (raw, configured = ["red-breath", "bronze-breath"]) => evaluateBiological(p, { previewOnly: true, result: {
        evaluatedProposal: { id: p.id }, issues: [], biologicalCombination: {
            proposalId: p.id, explicitTransmissionPlan: true, interactionOptions: raw,
            characteristics: [{ kind: "Trait", characteristicId: "trait.breath-weapon",
                offers: [redOffer, bronzeOffer], expressions: [] }],
            inheritedInstances: configured.map(id => ({ gene: { sourceCandidateId: id }, expressionGrantIds: [] }))
        }
    }}, true);
    assert.equal(native([interaction], ["red-breath"]).interactionOptions, undefined);
    assert.equal(native([{...interaction, ruleOwnerId:"table-decision:forged"}]).interactionOptions, undefined);
    assert.throws(() => setInheritedInteraction(p, native([]), "rule.test", "test-interaction", [["red-breath","bronze-breath"]]));
    assert.throws(() => setInheritedInteraction(p, native([interaction]), "rule.test", "test-interaction", [["red-breath","red-breath"]]));
    const selected = setInheritedInteraction(p, native([interaction]), "rule.test", "test-interaction", [["red-breath","bronze-breath"]]);
    assert.equal(selected.biologicalTransmission.combinations.length, 1);
    assert.equal(selected.manifestations.length, 0);
    assert.deepEqual(selected.biologicalTransmission.candidateIds, p.biologicalTransmission.candidateIds);
    assert.equal(supportsBiologicalDraft(JSON.parse(JSON.stringify(selected))), true);
    assert.deepEqual(removeInheritedInteraction(selected, "rule.test", "test-interaction").biologicalTransmission, p.biologicalTransmission);
});

test("recombined source instances display one native shared charge rather than one charge per donor", () => {
    let p = proposal();
    p = setInherited(p, evaluation(p), "red-breath", true);
    p = setInherited(p, evaluation(p), "bronze-breath", true);
    const base = evaluation(p, [["red-breath", ["grant"]], ["bronze-breath", ["grant"]]]);
    const group = {...base.groups[0], expressions: [{id:"grant"}]};
    const charge = {priceId:"breath", load:3, grantIds:["grant"]};
    const e = {...base, groups:[group], charges:[charge, charge]};
    assert.deepEqual(groupAllocationCharges(e, group), [{load:3, shared:true}]);
    assert.equal(e.allocation.used, "3");
    assert.deepEqual(groupAllocationCharges({...e,current:false},group), []);
});
