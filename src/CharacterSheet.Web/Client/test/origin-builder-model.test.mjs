import test from "node:test";
import assert from "node:assert/strict";
import {
    changeDirectOriginChoice,
    changeDirectOriginIdentity,
    changeMixedNativeParent,
    changeNativeOriginMode,
    createInitialOriginProposal,
    isEditableDirectNativeOrigin,
    isEditableMixedNativeOrigin,
    mixedNativeParentSources,
    canActivateDirectNativeOrigin,
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

test("native Human–Elf biological example persists only distinct source references", () => {
    const initial = createInitialOriginProposal(id);
    const mixed = changeNativeOriginMode(initial, "mixed");
    assert.equal(mixed.intent, 1);
    assert.deepEqual(mixed.contributors, []);
    assert.equal(isEditableMixedNativeOrigin(mixed), true);

    const human = changeMixedNativeParent(mixed, "parent-one", "species.human");
    const both = changeMixedNativeParent(human, "parent-two", "species.elf");
    assert.deepEqual(both.contributors, [
        { id: "parent-one", role: { kind: 0 }, source: {
            kind: 0, id: "species.human"
        } },
        { id: "parent-two", role: { kind: 0 }, source: {
            kind: 0, id: "species.elf"
        } }
    ]);
    assert.deepEqual(mixedNativeParentSources(both), [
        { slot: "parent-one", sourceId: "species.human" },
        { slot: "parent-two", sourceId: "species.elf" }
    ]);
    assert.equal(both.selectedSpeciesId, null);
    assert.equal(both.selectedAncestryId, null);
    assert.deepEqual(both.candidates, []);
    assert.deepEqual(both.manifestations, []);
    assert.equal(both.tableDecisions, undefined);
    assert.equal(both.existingConfiguration, undefined);
    assert.equal(both.history, undefined);
    assert.equal(both.contributors[0].manifestedProfile, undefined);
    assert.equal(isEditableMixedNativeOrigin(both), true);
    assert.equal(isEditableDirectNativeOrigin(both), false);
    assert.equal(canActivateDirectNativeOrigin(both, {
        previewOnly: true, result: { status: 4, issues: [] }
    }), false);
    assert.deepEqual(initial.contributors, []);
});

test("changing a mixed source invalidates its old evidence without granting inheritance", () => {
    const mixed = changeNativeOriginMode(createInitialOriginProposal(id), "mixed");
    const both = changeMixedNativeParent(changeMixedNativeParent(mixed,
        "parent-one", "species.human"), "parent-two", "species.elf");
    const updated = changeMixedNativeParent(both, "parent-two", "ancestry.elf.high");
    assert.deepEqual(mixedNativeParentSources(updated), [
        { slot: "parent-one", sourceId: "species.human" },
        { slot: "parent-two", sourceId: "ancestry.elf.high" }
    ]);
    const cleared = changeMixedNativeParent(updated, "parent-one", null);
    assert.equal(cleared.contributors.length, 1);
    assert.equal(cleared.contributors[0].id, "parent-two");
    assert.equal(both.contributors.length, 2);

    const reset = changeNativeOriginMode(cleared, "direct");
    assert.equal(reset.intent, 0);
    assert.equal(reset.id, mixed.id);
    assert.deepEqual(reset.contributors, []);
    assert.deepEqual(reset.candidates, []);
    assert.deepEqual(reset.manifestations, []);
});

test("basic mixed editor refuses claimed parent grants, rulings and invented options", () => {
    const mixed = changeNativeOriginMode(createInitialOriginProposal(id), "mixed");
    assert.throws(() => changeMixedNativeParent(mixed,
        "parent-one", "custom.unknown"), /Only native Species or Ancestry/);
    assert.throws(() => changeMixedNativeParent(mixed,
        "someone-else", "species.human"), /unrecognized biological source/);
    const withParent = changeMixedNativeParent(mixed, "parent-one", "species.human");
    const bad = [
        { ...withParent, tableDecisions: [{ id: "forged-dm" }] },
        { ...withParent, candidates: [{ id: "forged-grant" }] },
        { ...withParent, manifestations: [{ id: "forged-selection" }] },
        { ...withParent, processCreatureTypeId: "creature-type.humanoid" },
        { ...withParent, contributors: [...withParent.contributors, {
            id: "parent-two", role: { kind: 0 },
            source: { kind: 1, id: "forged-parent" },
            manifestedProfile: { inherentGrants: [{ id: "forged" }] }
        }] }
    ];
    for (const input of bad) {
        assert.equal(isEditableMixedNativeOrigin(input), false);
        assert.throws(() => changeMixedNativeParent(input,
            "parent-two", "species.elf"), /plain biological evidence draft/);
        assert.throws(() => changeNativeOriginMode(input, "direct"),
            /can not replace/);
    }
});

test("no arbitrary Origin becomes an editable direct or mixed biological draft", () => {
    const initial = createInitialOriginProposal(id);
    assert.equal(isEditableDirectNativeOrigin(initial), true);
    assert.equal(isEditableDirectNativeOrigin({
        ...initial, method: { ruleId: "rule.other", methodId: "rite" }
    }), false);
    assert.equal(isEditableDirectNativeOrigin({
        ...initial, contributors: [{ id: "claimed-parent" }]
    }), false);
    assert.equal(isEditableMixedNativeOrigin({
        ...changeNativeOriginMode(initial, "mixed"),
        inputRequirements: [{ role: { kind: 5 }, minimumCount: 1 }]
    }), false);
});
