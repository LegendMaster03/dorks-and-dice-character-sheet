import test from "node:test";
import assert from "node:assert/strict";
import {
    changeDirectOriginChoice,
    changeDirectOriginIdentity,
    changeMixedNativeParent,
    changeMixedNativeCandidate,
    changeMixedNativeFiniteChoice,
    changeNativeOriginMode,
    createInitialOriginProposal,
    isEditableDirectNativeOrigin,
    isEditableMixedNativeOrigin,
    mixedNativeParentSources,
    mixedNativePreviewMatchesDraft,
    nextMixedNativeSourceSlot,
    originPreviewCandidates,
    requiredDraftOriginChoices,
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

function mixedNativeOfferPreview(proposal) {
    return {
        contractVersion: 1, rulesetId: "first-party-001",
        fingerprint: "a".repeat(64), contentChangedSinceRequest: false,
        previewOnly: true,
        result: {
            status: 2, profile: null, issues: [],
            evaluatedProposal: {
                id: proposal.id, intent: proposal.intent,
                method: structuredClone(proposal.method),
                contributors: structuredClone(proposal.contributors),
                manifestations: structuredClone(proposal.manifestations),
                candidates: [{
                    id: "contributor:parent-one:species.human:/traits/0",
                    contributorId: "parent-one",
                    identity: {
                        kind: 1, mechanicalDefinitionId: "trait.human.source",
                        occurrence: { source: { kind: 0, id: "species.human" },
                            occurrencePath: "/traits/0" }
                    },
                    selectionBasis: { eligibility: 0,
                        explanation: "Transmission requires native adjudication." },
                    offeredValue: { name: "Native characteristic", level: 3 },
                    offeredParameters: { tier: 3 },
                    requiredChoices: [{
                        id: "selection", timing: 0, minimum: 1, maximum: 2,
                        finiteOptions: ["red", "blue", "green"]
                    }],
                    unresolvedEvidence: ["Inheritance is unresolved."]
                }, {
                    id: "contributor:parent-two:species.elf:/walkingSpeedFeet",
                    contributorId: "parent-two",
                    identity: {
                        kind: 0, mechanicalDefinitionId: "species.elf#/walkingSpeedFeet",
                        occurrence: { source: { kind: 0, id: "species.elf" },
                            occurrencePath: "/walkingSpeedFeet" }
                    },
                    selectionBasis: { eligibility: 0 },
                    offeredValue: 30
                }].filter(c => proposal.contributors.some(x => x.id === c.contributorId))
            }
        }
    };
}

test("mixed draft selects multiple native offers by reference without copying grant payload", () => {
    const base = changeMixedNativeParent(changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human"), "parent-two", "species.elf");
    const preview = mixedNativeOfferPreview(base);
    const offers = originPreviewCandidates(preview, base);
    assert.equal(offers.length, 2);
    assert.equal(offers[0].selectedDraft, false);
    assert.equal(offers[0].offeredValue.level, 3);
    assert.equal(offers[0].offeredParameters.tier, 3);
    assert.deepEqual(offers[0].unresolvedEvidence, ["Inheritance is unresolved."]);
    let selected = changeMixedNativeCandidate(base, preview, offers[0].id, true);
    selected = changeMixedNativeCandidate(
        selected, mixedNativeOfferPreview(selected), offers[1].id, true);
    assert.deepEqual(selected.manifestations, [
        { id: "selection:" + offers[0].id, candidateId: offers[0].id },
        { id: "selection:" + offers[1].id, candidateId: offers[1].id }
    ]);
    assert.equal(isEditableMixedNativeOrigin(selected), true);
    assert.deepEqual(selected.candidates, []);
    assert.equal(selected.manifestations[0].selectedValue, undefined);
    assert.equal(selected.manifestations[0].selectedParameters, undefined);
    assert.equal(selected.manifestations[0].tableDecisionId, undefined);
    assert.equal(canActivateDirectNativeOrigin(selected, {
        previewOnly: true, result: { status: 4, issues: [] }
    }), false);
    assert.deepEqual(originPreviewCandidates(preview, selected), [],
        "An earlier preview is not valid after the draft gains selections.");
    const currentPreview = mixedNativeOfferPreview(selected);
    assert.equal(originPreviewCandidates(currentPreview, selected)[0].selectedDraft, true);
    const removed = changeMixedNativeCandidate(selected, currentPreview, offers[0].id, false);
    assert.deepEqual(removed.manifestations.map(x => x.candidateId), [offers[1].id]);
});

test("mixed finite choices persist, revise and obey current native offer boundaries", () => {
    const base = changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human");
    const preview = mixedNativeOfferPreview(base);
    const candidateId = preview.result.evaluatedProposal.candidates[0].id;
    const selected = changeMixedNativeCandidate(base, preview, candidateId, true);
    const selectedPreview = mixedNativeOfferPreview(selected);
    const required = requiredDraftOriginChoices(selectedPreview, selected);
    assert.equal(required.length, 1);
    assert.deepEqual(required[0].finiteOptions, ["red", "blue", "green"]);
    let updated = changeMixedNativeFiniteChoice(selected, selectedPreview, candidateId,
        "selection", ["red", "blue"]);
    assert.deepEqual(requiredDraftOriginChoices(mixedNativeOfferPreview(updated), updated)[0].selectedOptionIds,
        ["red", "blue"]);
    assert.equal(isEditableMixedNativeOrigin(structuredClone(updated)), true,
        "Persisted JSON must remain an editable draft.");
    const currentPreview = mixedNativeOfferPreview(updated);
    assert.throws(() => changeMixedNativeFiniteChoice(updated, currentPreview, candidateId,
        "selection", ["red", "blue", "green"]), /finite native offer/);
    assert.throws(() => changeMixedNativeFiniteChoice(updated, currentPreview, candidateId,
        "selection", ["red", "red"]), /finite native offer/);
    assert.throws(() => changeMixedNativeFiniteChoice(updated, currentPreview, candidateId,
        "selection", ["not-authored"]), /finite native offer/);
    updated = changeMixedNativeFiniteChoice(updated, currentPreview, candidateId,
        "selection", []);
    assert.deepEqual(updated.manifestations[0].choices, []);
    assert.equal(requiredDraftOriginChoices(mixedNativeOfferPreview(updated), updated)[0].selectedOptionIds.length, 0);
});

test("mixed native preview mismatch and forged selection payloads remain uneditable", () => {
    const base = changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human");
    const preview = mixedNativeOfferPreview(base);
    const candidateId = preview.result.evaluatedProposal.candidates[0].id;
    assert.throws(() => changeMixedNativeCandidate(base, preview,
        "browser-invented-candidate", true), /does not belong/);
    const wrongProposal = structuredClone(preview);
    wrongProposal.result.evaluatedProposal.id = "someone-else";
    assert.deepEqual(originPreviewCandidates(wrongProposal, base), []);
    assert.throws(() => changeMixedNativeCandidate(base, wrongProposal,
        candidateId, true), /does not belong/);
    const manifest = { id: "selection:" + candidateId, candidateId };
    for (const claim of [
        { ...manifest, selectedValue: { forged: true } },
        { ...manifest, selectedParameters: { madeUp: true } },
        { ...manifest, tableDecisionId: "forged" },
        { ...manifest, choices: [{ requirementId: "selection",
            selectedOptionIds: ["red"], catalogEligibilityEvidenceId: "fake" }] }
    ]) assert.equal(isEditableMixedNativeOrigin({
        ...base, manifestations: [claim]
    }), false);
});

test("additional biological draft source references do not impose a two-parent model", () => {
    const base = changeMixedNativeParent(changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human"), "parent-two", "species.elf");
    assert.equal(nextMixedNativeSourceSlot(base), "source-3");
    const third = changeMixedNativeParent(base, "source-3", "ancestry.elf.high");
    assert.equal(isEditableMixedNativeOrigin(third), true);
    assert.equal(nextMixedNativeSourceSlot(third), "source-4");
    assert.equal(mixedNativeParentSources(third).length, 3);
    assert.equal(third.contributors[2].manifestedProfile, undefined);
    const fourth = changeMixedNativeParent(third, "source-4", "species.orc");
    assert.equal(nextMixedNativeSourceSlot(fourth), null);
    assert.equal(fourth.contributors.length, 4);
    const cleared = changeMixedNativeParent(fourth, "source-3", null);
    assert.equal(nextMixedNativeSourceSlot(cleared), "source-3");
    assert.equal(cleared.contributors.length, 3);
    assert.equal(isEditableMixedNativeOrigin({
        ...fourth, contributors: [...fourth.contributors, {
            id: "source-5", role: { kind: 0 },
            source: { kind: 0, id: "species.human" }
        }]
    }), false);
});

test("ancestor Species candidate remains selectable from an Ancestry source chain", () => {
    const base = changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "ancestry.elf.high");
    const preview = mixedNativeOfferPreview(base);
    const candidateId = preview.result.evaluatedProposal.candidates[0].id;
    const selected = changeMixedNativeCandidate(base, preview, candidateId, true);
    assert.equal(selected.manifestations[0].candidateId, candidateId);
    const switched = changeMixedNativeParent(selected, "parent-one", "species.orc");
    assert.deepEqual(switched.manifestations, [],
        "Source revision must invalidate candidate selections and their choices.");
});

test("mixed native preview rejects mismatched source snapshots and malformed offer provenance", () => {
    const proposal = changeMixedNativeParent(changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human"), "parent-two", "species.elf");
    const preview = mixedNativeOfferPreview(proposal);
    const candidate = preview.result.evaluatedProposal.candidates[0].id;
    assert.equal(mixedNativePreviewMatchesDraft(preview, proposal), true);
    for (const altered of [
        p => p.result.evaluatedProposal.contributors[0].source.id = "species.orc",
        p => p.result.evaluatedProposal.contributors[0].role.kind = 4,
        p => p.result.evaluatedProposal.contributors.shift(),
        p => p.result.evaluatedProposal.intent = 0,
        p => p.result.evaluatedProposal.method.methodId = "magical",
        p => p.result.evaluatedProposal.id = "other-origin"
    ]) {
        const forged = structuredClone(preview);
        altered(forged);
        assert.equal(mixedNativePreviewMatchesDraft(forged, proposal), false);
        assert.deepEqual(originPreviewCandidates(forged, proposal), []);
        assert.throws(() => changeMixedNativeCandidate(proposal, forged, candidate, true),
            /does not belong/);
    }

    const malformed = [
        p => p.result.evaluatedProposal.candidates[0].id = "contributor:parent-two:species.human:/traits/0",
        p => p.result.evaluatedProposal.candidates[0].identity.occurrence.source.id = "species.unknown",
        p => p.result.evaluatedProposal.candidates[0].identity.occurrence.source.kind = 2,
        p => p.result.evaluatedProposal.candidates[0].identity.occurrence.occurrencePath = "/traits/99",
        p => p.result.evaluatedProposal.candidates[0].contributorId = "unrelated",
        p => p.result.evaluatedProposal.candidates.push(
            structuredClone(p.result.evaluatedProposal.candidates[0]))
    ];
    for (const altered of malformed) {
        const forged = structuredClone(preview);
        altered(forged);
        assert.deepEqual(originPreviewCandidates(forged, proposal), []);
        assert.throws(() => changeMixedNativeCandidate(proposal, forged, candidate, true),
            /does not belong/);
    }
});

test("native evaluated manifestation requests are not confused with preview grants", () => {
    const base = changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human");
    const raw = mixedNativeOfferPreview(base);
    const candidateId = raw.result.evaluatedProposal.candidates[0].id;
    const selected = changeMixedNativeCandidate(base, raw, candidateId, true);
    const evaluated = mixedNativeOfferPreview(selected);
    const unresolved = originPreviewCandidates(evaluated, selected)[0];
    assert.equal(unresolved.selectedDraft, true);
    assert.equal(unresolved.evaluatedManifestation, true);
    assert.equal(unresolved.manifestedPreview, false,
        "A persisted manifestation request was rejected by native eligibility.");
    evaluated.result.profile = { underlying: {
        inherentGrants: [{ id: "preview-only-grant", sourceCandidateIds: [candidateId] }]
    }, independentGrants: [] };
    const grantedPreview = originPreviewCandidates(evaluated, selected)[0];
    assert.equal(grantedPreview.manifestedPreview, true);
    assert.equal(grantedPreview.selectedDraft, true);
});

test("choosing the unchanged source preserves already saved draft choices", () => {
    const direct = changeDirectOriginChoice({
        ...createInitialOriginProposal(id), selectedSpeciesId: "species.human"
    }, "direct:species.human:/sizeChoice", "size", ["small"]);
    assert.strictEqual(changeDirectOriginIdentity(direct, "species", "species.human"),
        direct);
    const mixed = changeMixedNativeParent(changeNativeOriginMode(
        createInitialOriginProposal(id), "mixed"), "parent-one", "species.human");
    const preview = mixedNativeOfferPreview(mixed);
    const selected = changeMixedNativeCandidate(mixed, preview,
        preview.result.evaluatedProposal.candidates[0].id, true);
    assert.strictEqual(changeMixedNativeParent(selected, "parent-one", "species.human"),
        selected);
});

test("selected characteristic absent from current native offers can be removed without losing others", () => {
    const base = changeMixedNativeParent(changeMixedNativeParent(
        changeNativeOriginMode(createInitialOriginProposal(id), "mixed"),
        "parent-one", "species.human"), "parent-two", "species.elf");
    const original = mixedNativeOfferPreview(base);
    const [first, second] = original.result.evaluatedProposal.candidates.map(c => c.id);
    const one = changeMixedNativeCandidate(base, original, first, true);
    const both = changeMixedNativeCandidate(one, mixedNativeOfferPreview(one), second, true);
    assert.strictEqual(changeMixedNativeCandidate(both, mixedNativeOfferPreview(both),
        first, true), both, "Re-selecting an existing draft candidate must not lose choices.");
    const staleOffer = mixedNativeOfferPreview(both);
    staleOffer.result.evaluatedProposal.candidates =
        staleOffer.result.evaluatedProposal.candidates.filter(c => c.id !== first);
    assert.equal(mixedNativePreviewMatchesDraft(staleOffer, both), true,
        "A selected offer may legitimately become unavailable while its inert request persists.");
    assert.deepEqual(originPreviewCandidates(staleOffer, both).map(c => c.id), [second]);
    const revised = changeMixedNativeCandidate(both, staleOffer, first, false);
    assert.deepEqual(revised.manifestations.map(m => m.candidateId), [second]);
    assert.deepEqual(both.manifestations.map(m => m.candidateId), [first, second]);
    assert.throws(() => changeMixedNativeCandidate(revised,
        mixedNativeOfferPreview(revised), first, false), /not selected in the draft/);
    assert.throws(() => changeMixedNativeCandidate(revised,
        mixedNativeOfferPreview(revised), "unoffered-new-candidate", true),
    /does not belong/);
});

test("native evaluated choices must not add player selections or expose malformed offers", () => {
    const base = changeMixedNativeParent(changeNativeOriginMode(
        createInitialOriginProposal(id), "mixed"), "parent-one", "species.human");
    const original = mixedNativeOfferPreview(base);
    const offerId = original.result.evaluatedProposal.candidates[0].id;
    const selected = changeMixedNativeCandidate(base, original, offerId, true);
    const preview = mixedNativeOfferPreview(selected);
    const withChoice = changeMixedNativeFiniteChoice(selected, preview,
        offerId, "selection", ["red"]);
    assert.equal(requiredDraftOriginChoices(
        mixedNativeOfferPreview(withChoice), withChoice).length, 1);
    const injectedChoice = mixedNativeOfferPreview(withChoice);
    injectedChoice.result.evaluatedProposal.manifestations[0].choices.push({
        requirementId: "unselected", selectedOptionIds: ["invented"]
    });
    assert.equal(mixedNativePreviewMatchesDraft(injectedChoice, withChoice), false);
    assert.deepEqual(requiredDraftOriginChoices(injectedChoice, withChoice), []);

    const valid = mixedNativeOfferPreview(withChoice);
    const invalidOffers = [
        p => p.result.evaluatedProposal.candidates[0].id = "browser-substituted-id",
        p => p.result.evaluatedProposal.candidates[0].identity.occurrence.source.id = "species.orc",
        p => p.result.evaluatedProposal.candidates.push(
            structuredClone(p.result.evaluatedProposal.candidates[0]))
    ];
    for (const alter of invalidOffers) {
        const malformed = structuredClone(valid);
        alter(malformed);
        assert.deepEqual(originPreviewCandidates(malformed, withChoice), []);
        assert.deepEqual(requiredDraftOriginChoices(malformed, withChoice), [],
            "A choice may not survive when the corresponding native offer set is rejected.");
        assert.throws(() => changeMixedNativeFiniteChoice(withChoice,
            malformed, offerId, "selection", ["blue"]), /finite native offer/);
    }
});

test("basic contributor editor preserves unfamiliar or process-specific evidence", () => {
    const base = changeMixedNativeParent(changeNativeOriginMode(
        createInitialOriginProposal(id), "mixed"), "parent-one", "species.human");
    for (const extra of [
        { originProfileRevision: "trusted-server-only" },
        { role: { kind: 0, processRoleId: "process-specific-input" } },
        { source: { kind: 0, id: "species.human", otherEvidence: "rejected" } }
    ]) {
        const evidence = { ...base, contributors: [
            { ...base.contributors[0], ...extra }
        ] };
        assert.equal(isEditableMixedNativeOrigin(evidence), false);
        assert.throws(() => changeMixedNativeParent(evidence,
            "parent-one", "species.elf"), /plain biological evidence draft/);
        assert.throws(() => changeNativeOriginMode(evidence, "direct"),
            /can not replace/);
        assert.deepEqual(base.contributors[0].source.id, "species.human");
    }
});
