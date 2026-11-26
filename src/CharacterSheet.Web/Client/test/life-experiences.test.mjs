import test from "node:test";
import assert from "node:assert/strict";
import { emptyExperienceDraft, changeExperienceSource, changeExperienceChoice, choiceTree,
    toNativeProposal, replayExperienceProposal, reconsiderChoices, removeReviewedChoice,
    decodeExperiencePreview } from "../.test-dist/features/life-experiences/model.js";
import { evaluateExperienceDraft, loadExperienceTitleCatalog } from "../.test-dist/features/life-experiences/api.js";

const fingerprint = "a".repeat(64);
const metadata = { contractVersion: 1, schemaVersion: 1, rulesetId: "first-party-001", fingerprint,
    capabilities: { nativeOptionSearch: true, characterProjection: true, resourceResolution: true,
        advancementEligibility: "supported-subset", conditionRollMode: "conditional-subset", campaignEffectiveRules: false } };
const env = { embedded: true, contextUrl: "/tool-host/registrations/character-sheet/context" };
const source = (id, kind, parentId, selectable = true) => ({ id, kind, parentId, selectable,
    label: id, detail: { title: id } });
const catalog = { ...metadata, hierarchyComplete: true, labels: {}, nodes: [
    source("upbringing.elf.general", "upbringing", null, false),
    source("upbringing.elf.wood", "upbringing", "upbringing.elf.general"),
    source("upbringing.elf.drow", "upbringing", "upbringing.elf.general"),
    source("upbringing.street-raised", "upbringing", null),
    source("background.guild-merchant", "background", null),
    source("background.spy", "background", null)] };
const cultural = "upbringing.elf.general/cultural-training";
const baseLanguage = "background.guild-merchant/guild-merchant-trade-language";
const outer = "background.guild-merchant/guild-merchant-professional-choice";
const extra = outer + "/additional-trade-language";
const primary = { key: outer, kind: "benefit-option", count: 1, optionIds: ["navigation", "additional-trade-language"] };
const language = key => ({ key, kind: "language", count: 1, optionIds: ["language.common", "language.elvish"],
    contextConstraint: "learned-through-commercial-dealings" });
const training = { key: cultural, kind: "skill", count: 1, optionIds: ["skill.perception", "skill.survival"] };
const pair = { ...emptyExperienceDraft(), upbringingId: "upbringing.elf.wood", backgroundId: "background.guild-merchant" };
const result = (extra = {}) => ({ status: "needs-choices", sources: [], choices: [], selectedPaths: [],
    candidateGrants: [], candidateEquipment: null, equipmentReplacements: [], startingItems: [],
    currency: [], issues: [], earnedProjection: null, ...extra });
const view = proposal => ({ revision: 7, rulesetId: metadata.rulesetId, fingerprint, proposal, saveStatus: "saved", readOnly: false });
const reply = value => new Response(JSON.stringify(value), { status: 200 });
const envelope = result => ({ ...metadata, previewOnly: true, committed: false, result });

test("independent source selection never selects an upbringing from biological ancestry", () => {
    const next = changeExperienceSource(emptyExperienceDraft(), "background", "background.guild-merchant", catalog).draft;
    assert.equal(next.upbringingId, null);
    assert.equal(toNativeProposal(next), null);
    assert.throws(() => changeExperienceSource(next, "upbringing", "upbringing.elf.general", catalog), /not selectable/);
    assert.equal(changeExperienceSource(next, "upbringing", "upbringing.street-raised", catalog).draft.backgroundId,
        "background.guild-merchant");
});
test("cultural parent and independent background selections survive a sibling change exactly once", () => {
    const draft = { ...pair, selections: { [cultural]: ["skill.perception"], [baseLanguage]: ["language.common"],
        "upbringing.elf.wood/wood-only": ["choice"] }, startingItemIds: { "background.guild-merchant/item/0": "item.mule" } };
    const change = changeExperienceSource(draft, "upbringing", "upbringing.elf.drow", catalog);
    assert.deepEqual(change.draft.selections, { [cultural]: ["skill.perception"], [baseLanguage]: ["language.common"] });
    assert.deepEqual(change.draft.startingItemIds, draft.startingItemIds);
    assert.deepEqual(change.reconsider, ["upbringing.elf.wood/wood-only"]);
    assert.equal(change.retained.filter(k => k === cultural).length, 1);
    assert.equal(draft.selections["upbringing.elf.wood/wood-only"][0], "choice");
});
test("Guild Merchant recursive branch reveals only selected children and retains baseline language", () => {
    const draft = { ...pair, selections: { [outer]: ["additional-trade-language"], [extra]: ["language.elvish"],
        [baseLanguage]: ["language.common"] } };
    const roots = choiceTree([primary, language(extra), language(baseLanguage)], draft, catalog);
    assert.equal(roots.length, 2);
    assert.equal(roots[0].options[0].children.length, 0);
    assert.equal(roots[0].options[1].children[0].id, extra);
    const changed = changeExperienceChoice(draft, primary, "navigation", true);
    assert.deepEqual(changed.selections, { [outer]: ["navigation"], [baseLanguage]: ["language.common"] });
    assert.throws(() => changeExperienceChoice(draft, language(extra), "language.not-offered", true), /not offered/);
});
test("partial counted choices survive reload without becoming invalid complete submissions", () => {
    const counted = { ...training, count: 2 };
    const draft = changeExperienceChoice(pair, counted, "skill.perception", true);
    assert.deepEqual(draft.selections[cultural], ["skill.perception"]);
    assert.deepEqual(replayExperienceProposal(draft, result({ choices: [counted] })).selections, {});
    assert.deepEqual(reconsiderChoices(draft, result({ choices: [counted] })), []);
    const complete = changeExperienceChoice(draft, counted, "skill.survival", true);
    assert.deepEqual(replayExperienceProposal(complete, result({ choices: [counted] })).selections[cultural],
        ["skill.perception", "skill.survival"]);
});
test("inactive native replacement paths are retained for review rather than silently reassigned", () => {
    const key = cultural + "/duplicate-skill";
    const draft = { ...pair, selections: { [key]: ["skill.survival"], [baseLanguage]: ["language.common"] } };
    const preview = result({ choices: [language(baseLanguage)] });
    assert.deepEqual(reconsiderChoices(draft, preview), [key]);
    assert.deepEqual(replayExperienceProposal(draft, preview).selections, { [baseLanguage]: ["language.common"] });
    assert.deepEqual(removeReviewedChoice(draft, key).selections, { [baseLanguage]: ["language.common"] });
    assert.equal(draft.selections[key][0], "skill.survival");
});
test("native rank/proficiency, contextual effects and equipment replacement provenance remain distinct", () => {
    const provenance = { sourceId: "background.guild-merchant", sourceOccurrenceId: "source", grantId: "grant" };
    const common = { sourceId: provenance.sourceId, occurrenceId: provenance.sourceOccurrenceId,
        benefitId: "grant", kind: "competency-training", targetId: "competency.navigation" };
    const input = result({ candidateGrants: [{ ...common, ranks: 1, proficient: false },
        { ...common, kind: "skill-training", targetId: "skill.survival", ranks: 0, proficient: true,
            originalTargetId: "skill.perception" }],
        candidateEquipment: { ...metadata, status: "resolved", requiredChoices: [], issues: [],
            qualifications: [{ qualificationId: "equipment.weapon.longbow", equipmentCategory: "weapon", provenance: [provenance] }] },
        equipmentReplacements: [{ provenance, originalQualificationId: "equipment.tool.a", replacementQualificationId: "equipment.tool.b" }],
        currency: [{ sourceId: provenance.sourceId, occurrenceId: "source", currency: "gp", quantity: 15 }] });
    assert.deepEqual(decodeExperiencePreview(input, metadata), input);
    assert.throws(() => decodeExperiencePreview({ ...input, earnedProjection: {} }, metadata), /Invalid/);
    assert.throws(() => decodeExperiencePreview({ ...input, candidateEquipment: { ...input.candidateEquipment,
        fingerprint: "b".repeat(64) } }, metadata), /Invalid/);
});
test("preview discovers nested requirements, replays complete choices and never requests earned projection", async () => {
    const draft = { ...pair, selections: { [outer]: ["additional-trade-language"], [extra]: ["language.elvish"],
        [baseLanguage]: ["language.common"], [cultural]: ["skill.perception"] } };
    const requests = [];
    const evaluation = await evaluateExperienceDraft(env, metadata, view(draft), async (url, options) => {
        assert.match(url, /origins\/life-experiences\/preview$/);
        const proposal = JSON.parse(options.body).proposal;
        requests.push(proposal);
        const choices = [primary, language(baseLanguage), { ...training, count: 2 }];
        if (proposal.selections[outer]?.[0] === "additional-trade-language") choices.push(language(extra));
        return reply(envelope(result({ choices })));
    });
    assert.equal(requests.length, 3);
    assert.deepEqual(requests[0].selections, {});
    assert.equal(requests[1].selections[extra], undefined);
    assert.equal(requests[2].selections[extra][0], "language.elvish");
    assert.equal(requests[2].selections[cultural], undefined);
    assert.equal(evaluation.draftRevision, 7);
    assert.equal(evaluation.previewOnly, true);
    assert.deepEqual(draft.selections[cultural], ["skill.perception"]);
});
test("partial source and stale fingerprints do not call Core", async () => {
    const noRequest = () => { throw Error("unexpected request"); };
    assert.equal(await evaluateExperienceDraft(env, metadata, view(emptyExperienceDraft()), noRequest), null);
    await assert.rejects(evaluateExperienceDraft(env, metadata, { ...view(pair), fingerprint: "old" }, noRequest), /rules changed/);
});
test("title-only discovery does not fabricate hierarchy or selection authorization", async () => {
    const observed = [];
    const discovered = await loadExperienceTitleCatalog(env, metadata, async (url, options) => {
        assert.equal(options.credentials, "same-origin");
        const category = new URL(url, "https://example.test").searchParams.get("category");
        observed.push(category);
        return reply({ ...metadata, category, total: 1, offset: 0, options: [
            { id: category + ".example", name: "Example", kind: category, definition: { private: true } }] });
    });
    assert.deepEqual(observed, ["upbringing", "background", "skill", "competency"]);
    assert.equal(discovered.hierarchyComplete, false);
    assert.equal(discovered.nodes.length, 2);
    assert.ok(discovered.nodes.every(node => !node.selectable));
    assert.ok(discovered.nodes.every(node => !("definition" in node)));
});


test("native source discovery keeps Upbringing and Background distinct and requires complete parentage", async () => {
    const records = {
        upbringing: [
            { id: "upbringing.elf.general", name: "General Elf", kind: "upbringing",
                lifeExperience: { parentId: null, selectable: false, description: null } },
            { id: "upbringing.elf.wood", name: "Wood Elf", kind: "upbringing",
                lifeExperience: { parentId: "upbringing.elf.general", selectable: true, description: "Woodland traditions." } }
        ],
        background: [
            { id: "background.entertainer", name: "Entertainer", kind: "background",
                lifeExperience: { parentId: null, selectable: true, description: null } }
        ]
    };
    const discover = (orphan = false) => loadExperienceTitleCatalog(env, metadata, async url => {
        const query = new URL(url, "https://example.test").searchParams;
        const category = query.get("category");
        const options = orphan && category === "upbringing" ? records.upbringing.slice(1) : records[category] ?? [];
        return reply({ ...metadata, category, total: options.length, offset: 0, options,
            includesLifeExperienceHierarchy: category in records });
    });
    const discovered = await discover();
    assert.equal(discovered.hierarchyComplete, true);
    const upbringing = changeExperienceSource(emptyExperienceDraft(), "upbringing", "upbringing.elf.wood", discovered);
    assert.equal(upbringing.draft.upbringingId, "upbringing.elf.wood");
    assert.equal(upbringing.draft.backgroundId, null);
    const background = changeExperienceSource(upbringing.draft, "background", "background.entertainer", discovered);
    assert.equal(background.draft.upbringingId, upbringing.draft.upbringingId);
    assert.equal(background.draft.backgroundId, "background.entertainer");
    const orphan = await discover(true);
    assert.equal(orphan.hierarchyComplete, false);
    assert.ok(orphan.nodes.every(node => !node.selectable));
});
