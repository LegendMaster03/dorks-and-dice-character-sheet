import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
    advancementKindLabel,
    buildAdvancementPresentation,
    createCompactAdvancementSummary,
    formatAdvancementOccurrence,
    formatAdvancementProgression
} from "../.test-dist/ui/character-advancement.js";
import { renderAdvancementDetails } from "../.test-dist/ui/advancement.js";

class FakeStyle {
    values = new Map();
    setProperty(name, value) { this.values.set(name, String(value)); }
    getPropertyValue(name) { return this.values.get(name) ?? ""; }
}
class FakeElement {
    constructor(tagName) {
        this.tagName = String(tagName).toUpperCase();
        this.className = "";
        this.textContent = "";
        this.children = [];
        this.attributes = new Map();
        this.style = new FakeStyle();
        this.id = "";
        this.href = "";
        this.target = "";
        this.rel = "";
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
}
globalThis.document = { createElement: tagName => new FakeElement(tagName) };

function walk(root) {
    const nodes = [root];
    for (const child of root.children) nodes.push(...walk(child));
    return nodes;
}
function byAttribute(root, name, value) {
    return walk(root).filter(node => node.getAttribute(name) === value);
}
function visibleText(root) {
    return walk(root).map(node => node.textContent).filter(Boolean).join(" ");
}
const occurrence = (id, kind, displayName, extra = {}) => ({
    occurrenceId: id,
    conceptKey: `${kind}:${id}`,
    kind,
    displayName,
    ...extra
});

test("ordinary Class uses the backend progression label", () => {
    const wizard = occurrence("wizard", "class", "Wizard", { progression: { label: "Level", value: 5 } });
    assert.equal(formatAdvancementOccurrence(wizard), "Wizard · Level 5");
});

test("Class plus Subclass uses only the supplied parent relationship", () => {
    const wizard = occurrence("wizard", "class", "Wizard", { progression: { label: "Level", value: 5 } });
    const evocation = occurrence("evocation", "subclass", "School of Evocation", { parentOccurrenceId: "wizard" });
    const items = buildAdvancementPresentation({ occurrences: [wizard, evocation] });
    assert.equal(items[1].parent?.occurrenceId, "wizard");
    assert.match(createCompactAdvancementSummary({ occurrences: [wizard, evocation] }).value, /School of Evocation/);
});

test("multiclass keeps independent root advancement occurrences", () => {
    const wizard = occurrence("wizard", "class", "Wizard", { progression: { label: "Level", value: 5 } });
    const fighter = occurrence("fighter", "class", "Fighter", { progression: { label: "Level", value: 2 } });
    const summary = createCompactAdvancementSummary({ occurrences: [wizard, fighter] });
    assert.match(summary.value, /Wizard/);
    assert.match(summary.detail, /Fighter/);
});

test("Prestige Class is data rather than a special frontend advancement branch", () => {
    const loremaster = occurrence("loremaster", "prestige-class", "Loremaster", {
        kindLabel: "Prestige Class",
        progression: { label: "Level", value: 3 }
    });
    assert.equal(advancementKindLabel(loremaster), "Prestige Class");
    assert.equal(formatAdvancementProgression(loremaster.progression), "Level 3");
});

test("Position can use Rank without being equated to Character level", () => {
    const cartographer = occurrence("cartographer", "position", "Cartographer", {
        kindLabel: "Position",
        progression: { label: "Rank", value: 2 }
    });
    assert.equal(formatAdvancementOccurrence(cartographer), "Cartographer · Rank 2");
    assert.doesNotMatch(formatAdvancementOccurrence(cartographer), /Level/);
});

test("multiple different advancement kinds coexist on one Character", () => {
    const view = { occurrences: [
        occurrence("wizard", "class", "Wizard", { progression: { label: "Level", value: 5 } }),
        occurrence("loremaster", "prestige-class", "Loremaster", { kindLabel: "Prestige Class", progression: { label: "Level", value: 3 } }),
        occurrence("cartographer", "position", "Cartographer", { kindLabel: "Position", progression: { label: "Rank", value: 2 } }),
        occurrence("boon", "mythic-track", "Star-Bound", { kindLabel: "Mythic Track", progression: { label: "Tier", value: 1 } })
    ] };
    assert.deepEqual(view.occurrences.map(value => value.kind), ["class", "prestige-class", "position", "mythic-track"]);
    assert.equal(createCompactAdvancementSummary(view, 2).overflowCount, 2);
});

test("advancement without a parent remains independent", () => {
    const position = occurrence("cartographer", "position", "Cartographer", { progression: { label: "Rank", value: 2 } });
    const item = buildAdvancementPresentation({ occurrences: [position] })[0];
    assert.equal(item.parent, undefined);
    assert.equal(item.unresolvedParent, false);
});

test("a supplied but unresolved parent does not cause the frontend to invent a relationship", () => {
    const child = occurrence("child", "future-kind", "Future Track", { parentOccurrenceId: "missing" });
    const item = buildAdvancementPresentation({ occurrences: [child] })[0];
    assert.equal(item.parent, undefined);
    assert.equal(item.unresolvedParent, true);
});

test("progression labels are arbitrary backend data rather than a Level-only contract", () => {
    assert.equal(formatAdvancementProgression({ label: "Level", value: 4 }), "Level 4");
    assert.equal(formatAdvancementProgression({ label: "Rank", value: 2 }), "Rank 2");
    assert.equal(formatAdvancementProgression({ label: "Tier", value: "Gold" }), "Tier Gold");
});

test("unknown future advancement kind needs no frontend enum change", () => {
    const future = occurrence("future", "guild-office", "Quartermaster", {
        kindLabel: "Guild Office",
        progression: { label: "Standing", value: 4 }
    });
    assert.equal(advancementKindLabel(future), "Guild Office");
    assert.equal(formatAdvancementOccurrence(future), "Quartermaster · Standing 4");
});

test("full advancement renderer presents parent, kind, progression, grants, and attribution from data", () => {
    const view = { occurrences: [
        occurrence("wizard", "class", "Wizard", {
            kindLabel: "Class",
            progression: { label: "Level", value: 5 },
            progressionDetails: [{ key: "xp", label: "Progress", value: "Milestone" }]
        }),
        occurrence("evocation", "subclass", "School of Evocation", {
            kindLabel: "Subclass",
            parentOccurrenceId: "wizard",
            grantedFeaturesOrMechanics: [{ key: "sculpt", label: "Sculpt Spells" }],
            sourceAttributions: [{ key: "source", label: "Rules source" }]
        })
    ] };
    const rendered = renderAdvancementDetails(view);
    assert.equal(byAttribute(rendered, "data-advancement-kind", "class").length, 1);
    assert.equal(byAttribute(rendered, "data-advancement-kind", "subclass").length, 1);
    assert.match(visibleText(rendered), /Parent: Wizard/);
    assert.match(visibleText(rendered), /Level 5/);
    assert.match(visibleText(rendered), /Sculpt Spells/);
    assert.match(visibleText(rendered), /Rules source/);
});

test("sheet header is generalized around Advancement rather than fixed Class and Subclass columns", async () => {
    const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.match(sheetSource, /headerSummaryItem\("Advancement"/);
    assert.doesNotMatch(sheetSource, /headerSummaryItem\("Class"/);
    assert.doesNotMatch(sheetSource, /headerSummaryItem\("Subclass"/);
    assert.match(sheetSource, /createCompactAdvancementSummary\(advancement\)/);
});

test("production keeps generalized advancement and mechanics projections unavailable until the backend bridge supplies them", async () => {
    const appSource = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
    assert.match(appSource, /state\.guidedBuilder,\s*null,\s*null,\s*\{/s);
});

test("advancement type does not enumerate only current advancement kinds or make Position-specific mechanics", async () => {
    const source = await readFile(new URL("../src/ui/character-advancement.ts", import.meta.url), "utf8");
    assert.match(source, /kind:\s*string/);
    assert.doesNotMatch(source, /kind:\s*"class"\s*\|/);
    assert.doesNotMatch(source, /Acquisitions Incorporated|Cartographer|PositionProgression|positionRank/);
});
