import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { renderSkillsCard } from "../.test-dist/ui/skills.js";

class FakeStyle {
    values = new Map();

    setProperty(name, value) {
        this.values.set(name, String(value));
    }

    getPropertyValue(name) {
        return this.values.get(name) ?? "";
    }
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
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
    }

    getAttribute(name) {
        return this.attributes.get(name) ?? null;
    }

    append(...children) {
        this.children.push(...children);
    }
}

globalThis.document = {
    createElement(tagName) {
        return new FakeElement(tagName);
    }
};

function walk(root) {
    const nodes = [root];
    for (const child of root.children) {
        nodes.push(...walk(child));
    }
    return nodes;
}

function byClass(root, className) {
    return walk(root).filter(node => node.className.split(/\s+/).includes(className));
}

function byAttribute(root, name, value) {
    return walk(root).filter(node => node.getAttribute(name) === value);
}

function visibleText(root) {
    return walk(root).map(node => node.textContent).filter(Boolean).join(" ");
}

const standalone = skill => ({ kind: "standalone", skill });
const composite = (parent, components) => ({ kind: "composite", parent, components });
const skill = (id, label, displayValue) => ({ id, label, displayValue });

test("standalone skill renders as one ordinary row without a composite container", () => {
    const card = renderSkillsCard([
        standalone(skill("navigation", "Navigation", "+7"))
    ]);

    assert.equal(byClass(card, "dd-skill-row--standalone").length, 1);
    assert.equal(byClass(card, "dd-skill-group--composite").length, 0);
    assert.equal(byAttribute(card, "data-skill-id", "navigation").length, 1);
    assert.match(visibleText(card), /Navigation/);
    assert.match(visibleText(card), /\+7/);
});

test("two-component composite renders one parent cell spanning two component rows", () => {
    const card = renderSkillsCard([
        composite(
            skill("fieldcraft", "Fieldcraft", "+5"),
            [
                skill("tracking", "Tracking", "+6"),
                skill("foraging", "Foraging", "+4")
            ]
        )
    ]);

    const groups = byClass(card, "dd-skill-group--composite");
    assert.equal(groups.length, 1);
    assert.equal(groups[0].style.getPropertyValue("--dd-skill-component-count"), "2");
    assert.equal(byAttribute(groups[0], "data-skill-role", "parent").length, 1);
    assert.equal(byAttribute(groups[0], "data-skill-role", "component").length, 2);
});

test("three-component composite renders one parent cell spanning three component rows", () => {
    const card = renderSkillsCard([
        composite(
            skill("mobility", "Mobility", "+4"),
            [
                skill("vaulting", "Vaulting", "+5"),
                skill("sprinting", "Sprinting", "+4"),
                skill("wading", "Wading", "+3")
            ]
        )
    ]);

    const group = byClass(card, "dd-skill-group--composite")[0];
    assert.equal(group.style.getPropertyValue("--dd-skill-component-count"), "3");
    assert.equal(byAttribute(group, "data-skill-role", "component").length, 3);
});

test("renderer is generic and does not special-case known Rules Core skill names", async () => {
    const card = renderSkillsCard([
        composite(
            skill("signal-analysis", "Signal Analysis", "A"),
            [
                skill("spectral", "Spectral Reading", "B"),
                skill("temporal", "Temporal Reading", "C")
            ]
        )
    ]);
    assert.match(visibleText(card), /Signal Analysis/);
    assert.match(visibleText(card), /Spectral Reading/);
    assert.match(visibleText(card), /Temporal Reading/);

    const source = await readFile(new URL("../src/ui/skills.ts", import.meta.url), "utf8");
    assert.doesNotMatch(source, /\b(?:Stealth|Hide|Move Silently|Perception|Listen|Spot|Athletics|Climb|Jump|Swim|Acrobatics|Balance|Tumble)\b/);
});

test("parent and components remain actual skill rows", () => {
    const card = renderSkillsCard([
        composite(
            skill("craft", "Craft", "+2"),
            [
                skill("joinery", "Joinery", "+3"),
                skill("masonry", "Masonry", "+1")
            ]
        )
    ]);

    assert.equal(byAttribute(card, "data-skill-row", "true").length, 3);
    assert.equal(byAttribute(card, "data-skill-id", "craft").length, 1);
    assert.equal(byAttribute(card, "data-skill-id", "joinery").length, 1);
    assert.equal(byAttribute(card, "data-skill-id", "masonry").length, 1);
});

test("presentation adds no permanent Derived, Independent, Composite, or Parent labels", () => {
    const card = renderSkillsCard([
        standalone(skill("singing", "Singing", "+1")),
        composite(
            skill("research", "Research", "+4"),
            [
                skill("archives", "Archives", "+5"),
                skill("interviews", "Interviews", "+3")
            ]
        )
    ]);

    assert.doesNotMatch(visibleText(card), /\b(?:Derived|Independent|Composite|Parent)\b/);
});

test("mobile CSS stacks the parent and component relationship instead of squeezing two columns", async () => {
    const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
    assert.match(css, /\.dd-skill-group__parent\s*\{[^}]*grid-row:\s*1\s*\/\s*span\s*var\(--dd-skill-component-count\)/s);
    assert.match(
        css,
        /@media \(max-width: 720px\)[\s\S]*\.dd-skill-group--composite\s*\{[^}]*grid-template-columns:\s*1fr;[\s\S]*\.dd-skill-group__parent\s*\{[^}]*grid-row:\s*auto;[\s\S]*\.dd-skill-group__component\s*\{[^}]*grid-column:\s*1;/s
    );
});

test("composite accessibility groups components under the real parent skill label", () => {
    const card = renderSkillsCard([
        composite(
            skill("analysis", "Analysis", "+4"),
            [
                skill("evidence", "Evidence", "+5"),
                skill("inference", "Inference", "+3")
            ]
        )
    ]);

    const group = byClass(card, "dd-skill-group--composite")[0];
    assert.equal(group.getAttribute("role"), "group");
    const labelledBy = group.getAttribute("aria-labelledby");
    assert.ok(labelledBy);
    const labels = walk(group).filter(node => node.id === labelledBy);
    assert.equal(labels.length, 1);
    assert.equal(labels[0].textContent, "Analysis");
});

test("production Character Sheet keeps Skills unavailable until real resolved skill state exists", async () => {
    const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.match(sheetSource, /skills\.append\(renderSkillsCard\(null\)\)/);
    assert.doesNotMatch(sheetSource, /kind:\s*"composite"|displayValue|\b(?:Stealth|Hide|Move Silently|Perception|Listen|Spot|Athletics|Climb|Jump|Swim|Acrobatics|Balance|Tumble)\b/);
});
