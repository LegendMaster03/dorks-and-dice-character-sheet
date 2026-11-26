// Standalone until Workstream A adds this to the shared test command.
import test from "node:test";
import assert from "node:assert/strict";
import { newBiologicalProposal, addNativeContributor, evaluateBiological,
    setInherited, setExpression, setFiniteChoice, removeContributor, allocationView }
    from "../src/features/origin/biological/model.ts";

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
