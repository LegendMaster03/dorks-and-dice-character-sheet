import test from "node:test";
import assert from "node:assert/strict";
import { presetEntries, presetRuleNodes, suggestedUpbringing, selectPreset, initialMethod,
    matchingPreview, reconcilePreset } from "../.test-dist/features/origin/conventional/model.js";
import { matchingRuleNodes } from "../.test-dist/features/guided/contracts.js";
import { createInitialOriginProposal } from "../.test-dist/features/origin/origin-builder-model.js";

// These are bounded projections of actual native definitions at Core
// 0baa466b7fc8f55e5d675b886f76e95f9a4b551f; same fields and defaults as NativeOriginDiscoveryEndpoints.
// Source paths: base/orc.json, base/elf.json, base/dragonborn.json, ancestry/elf/wood.json, ancestry/elf/high.json, ancestry/dragonborn/chromatic.json, ancestry/dragonborn/chromatic/red.json, ancestry/dragonborn/chromatic/blue.json, base/human.json.
const presets = [
  {
    "id": "species.orc",
    "name": "Orc",
    "kind": "species",
    "parentId": null,
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "species.elf",
    "name": "Elf",
    "kind": "species",
    "parentId": null,
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "species.dragonborn",
    "name": "Dragonborn",
    "kind": "species",
    "parentId": null,
    "selectable": false,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.elf.wood",
    "name": "Wood Elf",
    "kind": "ancestry",
    "parentId": "species.elf",
    "selectable": true,
    "defaultUpbringingId": "upbringing.elf.wood",
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.elf.high",
    "name": "High Elf",
    "kind": "ancestry",
    "parentId": "species.elf",
    "selectable": true,
    "defaultUpbringingId": "upbringing.elf.high",
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.dragonborn.chromatic",
    "name": "Chromatic Dragonborn",
    "kind": "ancestry",
    "parentId": "species.dragonborn",
    "selectable": false,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.dragonborn.red",
    "name": "Red Dragonborn",
    "kind": "ancestry",
    "parentId": "ancestry.dragonborn.chromatic",
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.dragonborn.blue",
    "name": "Blue Dragonborn",
    "kind": "ancestry",
    "parentId": "ancestry.dragonborn.chromatic",
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "species.human",
    "name": "Human",
    "kind": "species",
    "parentId": null,
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  }
];
// Additional pinned definitions: base/tiefling.json, ancestry/tiefling/{abyssal,infernal}.json.
presets.push(...[
  {
    "id": "species.tiefling",
    "name": "Tiefling",
    "kind": "species",
    "parentId": null,
    "selectable": false,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": true
  },
  {
    "id": "ancestry.tiefling.abyssal",
    "name": "Abyssal Tiefling",
    "kind": "ancestry",
    "parentId": "species.tiefling",
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  },
  {
    "id": "ancestry.tiefling.infernal",
    "name": "Infernal Tiefling",
    "kind": "ancestry",
    "parentId": "species.tiefling",
    "selectable": true,
    "defaultUpbringingId": null,
    "acquisitionMethodIds": [],
    "ancestrySelectionRequired": false
  }
]);
const entries = presetEntries(presets, "biological");
const entry = id => entries.find(e => e.preset.id === id);
test("Orc is selectable directly without ancestry", () => {
    assert.deepEqual(entry("species.orc").selection, { speciesId: "species.orc", ancestryId: null });
});
test("Tiefling requires an Ancestry; its native leaves remain selectable", () => {
    assert.equal(entry("species.tiefling").preset.ancestrySelectionRequired, true);
    assert.equal(entry("species.tiefling").selection, null);
    assert.deepEqual(entry("ancestry.tiefling.abyssal").selection,
        { speciesId: "species.tiefling", ancestryId: "ancestry.tiefling.abyssal" });
});
test("Elf ancestry resolves the native parent Species", () => {
    assert.deepEqual(entry("ancestry.elf.wood").selection,
        { speciesId: "species.elf", ancestryId: "ancestry.elf.wood" });
});
test("Dragonborn hierarchy keeps the nonselectable root and family", () => {
    const red = entry("ancestry.dragonborn.red");
    assert.deepEqual(red.path.map(p => p.id), ["species.dragonborn", "ancestry.dragonborn.chromatic", "ancestry.dragonborn.red"]);
    assert.equal(entry("species.dragonborn").selection, null);
    assert.equal(entry("ancestry.dragonborn.chromatic").selection, null);
    assert.deepEqual(red.selection, { speciesId: "species.dragonborn", ancestryId: "ancestry.dragonborn.red" });
    const nodes = matchingRuleNodes(presetRuleNodes(entries), red.preset.name);
    for (const p of red.path) assert.ok(nodes.some(n => n.id === p.id));
});
test("native suggested Upbringing is not placed in the canonical proposal", () => {
    const wood = entry("ancestry.elf.wood");
    assert.ok(suggestedUpbringing(wood));
    const proposal = selectPreset(createInitialOriginProposal("test"), wood.selection);
    assert.equal(proposal.selectedAncestryId, wood.preset.id);
    assert.equal(Object.hasOwn(proposal, "upbringingId"), false);
    assert.deepEqual(proposal.manifestations, []);
    assert.strictEqual(selectPreset(proposal, wood.selection), proposal);
});
test("missing parents and duplicate identities fail closed", () => {
    const leaf = entry("ancestry.dragonborn.red").preset;
    assert.equal(presetEntries([leaf], "biological")[0].selection, null);
    assert.throws(() => presetEntries([leaf, leaf], "biological"), /Duplicate/);
});
test("acquisition metadata excludes a gated path (contract boundary, not a published playable ancestry)", () => {
    const restricted = presets.map(p => p.id === "ancestry.dragonborn.chromatic"
        ? { ...p, acquisitionMethodIds: ["rite-of-rebirth"] } : p);
    assert.equal(presetEntries(restricted, "biological").find(e => e.preset.id === "ancestry.dragonborn.red").selection, null);
});
test("unregistered/reference and post-origination methods are not initial editor choices", () => {
    assert.equal(initialMethod({ stage: "post-origination", support: "initial-preview" }), false);
    assert.equal(initialMethod({ stage: null, support: "reference-only" }), false);
    assert.equal(initialMethod({ stage: null, support: "assembly-preview" }), true);
});
test("wrong proposal identity and stale native snapshot fail preview matching", () => {
    const proposal = selectPreset(createInitialOriginProposal("test"), entry("ancestry.elf.wood").selection);
    // Transport boundary check only; not an evaluated mechanical fixture.
    const preview = { previewOnly: true, rulesetId: "first-party-001", fingerprint: "test",
        result: { evaluatedProposal: { ...proposal, candidates: [] } } };
    assert.equal(matchingPreview(preview, proposal, "first-party-001", "test"), true);
    assert.equal(matchingPreview(preview, proposal, "first-party-001", "stale"), false);
    assert.equal(matchingPreview(preview, { ...proposal, selectedAncestryId: "ancestry.elf.high" }, "first-party-001", "test"), false);
    assert.throws(() => reconcilePreset(proposal, proposal, preview, preview, "first-party-001", "stale", new Map()));
});
