import test from "node:test";
import assert from "node:assert/strict";
import {
    changeDirectOriginChoice,
    changeDirectOriginIdentity,
    createInitialOriginProposal,
    hasLegacySpeciesSelections,
    isAuthorizedInitialNativeOrigin,
    requiredDirectOriginChoices,
    savedUnderlyingOriginIdentity
} from "../.test-dist/features/origin/origin-builder-model.js";

const id = "6e32c099-9b23-4664-9f13-ad86e88f9958";

test("direct Origin starts as an inert initial construction, not an active Species grant", () => {
    const proposal = createInitialOriginProposal(id);
    assert.equal(proposal.id, `initial-${id}`);
    assert.equal(proposal.intent, 0);
    assert.deepEqual(proposal.method, {
        ruleId: "rule.creature-origination", methodId: "biological"
    });
    assert.deepEqual(proposal.manifestations, []);
    assert.equal(proposal.selectedSpeciesId, null);
    assert.equal(proposal.selectedAncestryId, null);
    assert.equal(proposal.existingConfiguration, undefined);
    assert.equal(proposal.tableDecisions, undefined);
});

test("changing the underlying Species or Ancestry invalidates stale source choices", () => {
    const proposal = {
        ...createInitialOriginProposal(id),
        selectedSpeciesId: "species.human",
        selectedAncestryId: "ancestry.elf.high",
        manifestations: [{ id: "old", candidateId: "old-source",
            choices: [{ requirementId: "size", selectedOptionIds: ["medium"] }] }]
    };
    const speciesChanged = changeDirectOriginIdentity(proposal, "species", "species.orc");
    assert.equal(speciesChanged.selectedSpeciesId, "species.orc");
    assert.equal(speciesChanged.selectedAncestryId, null);
    assert.deepEqual(speciesChanged.manifestations, []);
    assert.deepEqual(proposal.manifestations.length, 1);

    const ancestryChanged = changeDirectOriginIdentity(proposal, "ancestry", null);
    assert.equal(ancestryChanged.selectedSpeciesId, "species.human");
    assert.equal(ancestryChanged.selectedAncestryId, null);
    assert.deepEqual(ancestryChanged.manifestations, []);
    assert.throws(() => changeDirectOriginIdentity(
        { ...proposal, intent: "Mixed" }, "species", "species.orc"), /separately authorized/);
});

test("finite and native Skill selections record only source-local choices", () => {
    let proposal = changeDirectOriginIdentity(createInitialOriginProposal(id),
        "species", "species.human");
    proposal = changeDirectOriginChoice(proposal, "candidate:size", "size", ["medium"]);
    proposal = changeDirectOriginChoice(proposal, "candidate:skill", "selection",
        ["skill.intimidation"]);
    assert.equal(proposal.manifestations.length, 2);
    assert.equal(proposal.manifestations[1].choices[0].catalogEligibilityEvidenceId, undefined);
    proposal = changeDirectOriginChoice(proposal, "candidate:size", "size", []);
    assert.equal(proposal.manifestations.length, 1);
    assert.equal(proposal.manifestations[0].candidateId, "candidate:skill");
    assert.deepEqual(proposal.manifestations[0].choices[0].selectedOptionIds, ["skill.intimidation"]);
});

test("creation choices come from evaluated source candidates but selection remains in raw draft", () => {
    const proposal = changeDirectOriginChoice({
        ...createInitialOriginProposal(id), selectedSpeciesId: "species.human"
    }, "candidate:skill", "selection", ["skill.intimidation"]);
    const preview = {
        contractVersion: 1, rulesetId: "first-party-001",
        fingerprint: "catalog-hash", previewOnly: true,
        contentChangedSinceRequest: false,
        result: {
            status: 1, issues: [{ code: "choice-incomplete", detail: "Feat remains", path: "traits/1" }],
            profile: { id: "origin:initial" },
            evaluatedProposal: {
                candidates: [
                    { id: "candidate:size", contributorId: null, identity: {
                        occurrence: { source: { id: "species.human" } } },
                      requiredChoices: [{
                          id: "size", timing: 0, minimum: 1, maximum: 1,
                          finiteOptions: ["small", "medium"]
                      }] },
                    { id: "candidate:skill", contributorId: null, identity: {
                        occurrence: { source: { id: "species.human" } } },
                      requiredChoices: [{
                          id: "selection", timing: 0, minimum: 1, maximum: 1,
                          optionCatalogId: "any-native-skill"
                      }] },
                    { id: "candidate:feat", contributorId: null, identity: {
                        occurrence: { source: { id: "species.human" } } },
                      requiredChoices: [{
                          id: "selection", timing: 0, minimum: 1, maximum: 1,
                          optionCatalogId: "feat:origin"
                      }] }
                ]
            }
        }
    };
    const choices = requiredDirectOriginChoices(preview, proposal);
    assert.equal(choices.length, 3);
    assert.deepEqual(choices.find(c => c.candidateId === "candidate:skill").selectedOptionIds,
        ["skill.intimidation"]);
    assert.deepEqual(choices.find(c => c.candidateId === "candidate:size").finiteOptions,
        ["small", "medium"]);
    assert.equal(choices.find(c => c.candidateId === "candidate:feat").optionCatalogId,
        "feat:origin");
    assert.equal(isAuthorizedInitialNativeOrigin(preview), false);
    assert.equal(isAuthorizedInitialNativeOrigin({
        ...preview, result: { ...preview.result, status: 4, issues: [] }
    }), true);
    assert.equal(isAuthorizedInitialNativeOrigin({
        ...preview, result: { ...preview.result, status: 4 }
    }), false);
});

test("legacy Species/Subspecies records remain explicit reconciliation conflicts", () => {
    assert.equal(hasLegacySpeciesSelections([{ category: "Species" }]), true);
    assert.equal(hasLegacySpeciesSelections([{ category: "raceSpecies" }]), true);
    assert.equal(hasLegacySpeciesSelections([{ category: "Background" }]), false);
});

test("header identity comes from committed Underlying, not Current or draft", () => {
    const committed = savedUnderlyingOriginIdentity({
        underlying: { speciesId: "species.elf", ancestryId: "ancestry.elf.high" },
        current: { speciesId: "species.werewolf", ancestryId: null }
    });
    assert.deepEqual(committed, {
        speciesId: "species.elf", ancestryId: "ancestry.elf.high"
    });
    assert.equal(savedUnderlyingOriginIdentity({ current: { speciesId: "species.werewolf" } }), null);
    assert.equal(savedUnderlyingOriginIdentity(null), null);
});
