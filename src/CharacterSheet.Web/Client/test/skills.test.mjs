import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { renderSkillsCard } from "../.test-dist/ui/skills.js";

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
function byClass(root, className) { return walk(root).filter(node => node.className.split(/\s+/).includes(className)); }
function byAttribute(root, name, value) { return walk(root).filter(node => node.getAttribute(name) === value); }
function visibleText(root) { return walk(root).map(node => node.textContent).filter(Boolean).join(" "); }

const competency = (key, label, formattedValue, extra = {}) => ({
    key, label, effectiveValue: formattedValue, formattedValue, ...extra
});
const standalone = value => ({ kind: "standalone", competency: value });
const composite = (parent, components) => ({ kind: "composite", parent, components });

test("standalone competency renders as one ordinary row", () => {
    const card = renderSkillsCard([standalone(competency("navigation", "Navigation", "+7"))]);
    assert.equal(byClass(card, "dd-skill-row--standalone").length, 1);
    assert.equal(byClass(card, "dd-skill-group--composite").length, 0);
    assert.equal(byAttribute(card, "data-skill-id", "navigation").length, 1);
    assert.match(visibleText(card), /Navigation/);
    assert.match(visibleText(card), /\+7/);
});

test("composite competency supports arbitrary component counts and preserves hierarchy", () => {
    const card = renderSkillsCard([
        composite(
            competency("fieldcraft", "Fieldcraft", "+5"),
            [competency("tracking", "Tracking", "+6"), competency("foraging", "Foraging", "+4"), competency("weather", "Weather Sense", "+3")]
        )
    ]);
    const group = byClass(card, "dd-skill-group--composite")[0];
    assert.equal(group.style.getPropertyValue("--dd-skill-component-count"), "3");
    assert.equal(byAttribute(group, "data-skill-role", "parent").length, 1);
    assert.equal(byAttribute(group, "data-skill-role", "component").length, 3);
});

test("ranked specialty competency progressively discloses metadata", () => {
    const card = renderSkillsCard([standalone(competency("specialty", "Specialty Work", "+11", {
        kind: "skill",
        ranks: 8,
        governingAbility: "Intelligence",
        classSkill: true,
        trainedOnly: true,
        armorCheckPenalty: { applies: true, formattedEffect: "-2 applied" },
        specialty: "Fine work"
    }))]);
    assert.equal(byClass(card, "dd-skill-row__details").length, 1);
    assert.match(visibleText(card), /Ranks/);
    assert.match(visibleText(card), /Class skill/);
    assert.match(visibleText(card), /Armor Check Penalty/);
});

test("specialty Skill and tool proficiency remain separate rows", () => {
    const card = renderSkillsCard([
        standalone(competency("specialty", "Specialty Work", "+11", { kind: "skill", specialty: "Fine work" })),
        standalone(competency("tool", "Artisan Tools", "Proficient", { kind: "tool", training: "Proficient" }))
    ]);
    assert.equal(byAttribute(card, "data-competency-kind", "skill").length, 1);
    assert.equal(byAttribute(card, "data-competency-kind", "tool").length, 1);
    assert.equal(byClass(card, "dd-skill-row--standalone").length, 2);
});

test("renderer remains generic and does not special-case known Rules Core skill names", async () => {
    const source = await readFile(new URL("../src/ui/skills.ts", import.meta.url), "utf8");
    assert.doesNotMatch(source, /\b(?:Stealth|Hide|Move Silently|Perception|Listen|Spot|Athletics|Climb|Jump|Swim|Acrobatics|Balance|Tumble)\b/);
});

test("presentation adds no permanent Derived, Independent, Composite, or Parent labels", () => {
    const card = renderSkillsCard([
        standalone(competency("solo", "Solo", "+1")),
        composite(competency("group", "Group", "+4"), [competency("part-a", "Part A", "+5"), competency("part-b", "Part B", "+3")])
    ]);
    assert.doesNotMatch(visibleText(card), /\b(?:Derived|Independent|Composite|Parent)\b/);
});

test("composite accessibility groups components under the real parent competency label", () => {
    const card = renderSkillsCard([
        composite(competency("analysis", "Analysis", "+4"), [competency("evidence", "Evidence", "+5"), competency("inference", "Inference", "+3")])
    ]);
    const group = byClass(card, "dd-skill-group--composite")[0];
    assert.equal(group.getAttribute("role"), "group");
    const labelledBy = group.getAttribute("aria-labelledby");
    assert.ok(labelledBy);
    const labels = walk(group).filter(node => node.id === labelledBy);
    assert.equal(labels.length, 1);
    assert.equal(labels[0].textContent, "Analysis");
});

test("production Character Sheet keeps competencies unavailable until real mechanics projection exists", async () => {
    const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.match(sheetSource, /renderSkillsCard\(null\)/);
});
