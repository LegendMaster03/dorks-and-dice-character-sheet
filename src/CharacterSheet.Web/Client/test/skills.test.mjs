import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { renderSkillsCard } from "../.test-dist/ui/skills.js";
import { buildCompetencyPresentation } from "../.test-dist/ui/character-mechanics.js";

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
    addEventListener(name, handler) {
        this.listeners ??= new Map();
        const handlers = this.listeners.get(name) ?? [];
        handlers.push(handler);
        this.listeners.set(name, handlers);
    }
    dispatch(name) {
        for (const handler of this.listeners?.get(name) ?? []) handler({ target: this });
    }
    setCustomValidity(message) { this.validationMessage = String(message); }
    reportValidity() { return (this.validationMessage ?? "").length === 0; }
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
const composite = (parent, components, relationship = {}) => ({
    kind: "composite",
    parent,
    components,
    relationship: {
        parentKey: parent.key,
        componentKeys: components.map(value => value.key),
        ...relationship
    }
});

test("missing competency data keeps the Skills surface and shows a neutral dash", () => {
    for (const items of [null, []]) {
        const card = renderSkillsCard(items);
        assert.equal(byClass(card, "dd-skill-row--placeholder").length, 1);
        assert.match(visibleText(card), /-/);
        assert.doesNotMatch(
            visibleText(card),
            /Resolved competencies are not available|No competencies were supplied for this Character/);
    }
});

test("skill card includes working search across parent and component names", () => {
    const card = renderSkillsCard([
        standalone(competency("arcana", "Arcana", "+4", { governingAbility: "intelligence" })),
        composite(
            competency("acrobatics", "Acrobatics", "+3"),
            [
                competency("balance", "Balance", "+2", { governingAbility: "dexterity" }),
                competency("tumble", "Tumble", "+4", { governingAbility: "dexterity" })
            ]
        )
    ]);
    const search = byClass(card, "dd-skills-search")[0];
    assert.ok(search);
    search.value = "balance";
    search.dispatch("input");

    const arcana = byAttribute(card, "data-skill-id", "arcana")[0];
    const compositeDisclosure = byAttribute(card, "data-composite-skill", "acrobatics")[0];
    assert.equal(arcana.hidden, true);
    assert.equal(compositeDisclosure.hidden, false);
});

test("skill rows use one aligned line: proficiency, governing stat, name, then modifier", () => {
    const card = renderSkillsCard([
        standalone(competency("arcana", "Arcana", "+7", {
            governingAbility: "Intelligence",
            training: "Proficient"
        }))
    ]);
    const row = byAttribute(card, "data-skill-id", "arcana")[0];
    assert.ok(row);
    const rowChildren = row.children;
    assert.equal(rowChildren.length, 4);
    assert.match(rowChildren[0].className, /dd-skill-row__training/);
    assert.equal(rowChildren[1].className, "dd-skill-row__ability");
    assert.equal(rowChildren[1].textContent, "INT");
    assert.equal(rowChildren[2].className, "dd-skill-row__name");
    assert.equal(rowChildren[2].textContent, "Arcana");
    assert.equal(rowChildren[3].className, "dd-skill-row__value");
    assert.equal(rowChildren[3].textContent, "+7");
});

test("standalone competency renders as one ordinary row", () => {
    const card = renderSkillsCard([standalone(competency("navigation", "Navigation", "+7"))]);
    assert.equal(byClass(card, "dd-skill-row--standalone").length, 1);
    assert.equal(byClass(card, "dd-skill-group--composite").length, 0);
    assert.equal(byAttribute(card, "data-skill-id", "navigation").length, 1);
    assert.match(visibleText(card), /Navigation/);
    assert.match(visibleText(card), /\+7/);
});

test("Rules Core family metadata adds a nested specialty disclosure without changing ordinary rows", () => {
    const presentation = buildCompetencyPresentation({
        entries: [
            competency("family.artisan", "Artisan Work", "-", {
                family: "artisan",
                isFamily: true
            }),
            competency("specialty.glass", "Glasswork", "+6", {
                kind: "specialized-skill",
                family: "artisan",
                specialty: "glass",
                supportsRanks: true,
                ranks: 4
            }),
            competency("navigation", "Navigation", "+3")
        ],
        relationships: []
    });

    assert.equal(presentation.length, 2);
    const family = presentation.find(item => item.kind === "family");
    assert.ok(family);
    assert.equal(family.parent.key, "family.artisan");
    assert.deepEqual(family.members.map(member => member.key), ["specialty.glass"]);

    const card = renderSkillsCard(presentation);
    const familyDisclosure = byAttribute(card, "data-skill-family", "family.artisan")[0];
    assert.ok(familyDisclosure);
    assert.equal(byAttribute(familyDisclosure, "data-skill-family-summary", "family.artisan").length, 1);
    assert.equal(byAttribute(familyDisclosure, "data-skill-id", "family.artisan").length, 0);
    assert.equal(byClass(familyDisclosure, "dd-skill-family-members").length, 1);
    assert.equal(byAttribute(familyDisclosure, "data-skill-id", "specialty.glass").length, 1);
    assert.equal(byClass(card, "dd-skill-disclosure--composite").length, 0);
    assert.equal(byAttribute(card, "data-skill-id", "navigation").length, 1);
});

test("family category without projected specialties does not render as an ordinary skill row", () => {
    const presentation = buildCompetencyPresentation({
        entries: [
            competency("skill.craft", "Craft", "-", {
                kind: "skill",
                family: "Craft",
                isFamily: true
            })
        ]
    });

    const card = renderSkillsCard(presentation);
    const familyDisclosure = byAttribute(card, "data-skill-family", "skill.craft")[0];
    assert.ok(familyDisclosure);
    assert.equal(byAttribute(card, "data-skill-id", "skill.craft").length, 0);
    assert.equal(byAttribute(card, "data-skill-family-summary", "skill.craft").length, 1);
    assert.match(visibleText(familyDisclosure), /Craft/);
    assert.match(visibleText(familyDisclosure), /No specialties/);
});


test("composite competency supports arbitrary component counts and preserves hierarchy", () => {
    const card = renderSkillsCard([
        composite(
            competency("fieldcraft", "Fieldcraft", "+5", { governingAbility: "wisdom" }),
            [
                competency("tracking", "Tracking", "+6", { governingAbility: "wisdom" }),
                competency("foraging", "Foraging", "+4", { governingAbility: "wisdom" }),
                competency("weather", "Weather Sense", "+3", { governingAbility: "wisdom" })
            ],
            { composition: "average-floor", resolutionKind: "derive-parent" }
        )
    ]);
    const group = byClass(card, "dd-skill-group--composite")[0];
    assert.equal(group.style.getPropertyValue("--dd-skill-component-count"), "3");
    assert.equal(byAttribute(group, "data-skill-role", "parent").length, 1);
    assert.equal(byAttribute(group, "data-skill-role", "component").length, 3);
    assert.equal(byClass(card, "dd-skill-disclosure--composite").length, 1);
    assert.match(visibleText(card), /Calculation/);
    assert.match(visibleText(card), /Average floor/);
    const parent = byAttribute(card, "data-skill-role", "parent")[0];
    assert.match(visibleText(parent), /WIS/);
    assert.doesNotMatch(visibleText(card), /\bDetails\b/);
});

test("composite skill sources are collapsed into one secondary disclosure", () => {
    const sourceA = { key: "srd3", label: "SRD3", detail: "3e" };
    const sourceB = { key: "srd35", label: "SRD35", detail: "3.5e" };
    const card = renderSkillsCard([
        composite(
            competency("parent", "Parent", "-", { sourceAttributions: [sourceA] }),
            [
                competency("a", "A", "-", { supportsRanks: true, sourceAttributions: [sourceA] }),
                competency("b", "B", "-", { supportsRanks: true, sourceAttributions: [sourceB] })
            ]
        )
    ]);
    assert.equal(byClass(card, "dd-source-attribution-disclosure").length, 1);
    assert.equal(byClass(card, "dd-source-attribution").length, 2);
    assert.equal(byClass(card, "dd-skill-mechanics-item").length, 2);
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
    assert.equal(byClass(card, "dd-skill-disclosure--standalone").length, 1);
    assert.equal(byClass(card, "dd-skill-row__details").length, 0);
    assert.doesNotMatch(visibleText(card), /\bDetails\b/);
    assert.match(visibleText(card), /Ranks/);
    assert.match(visibleText(card), /Class skill/);
    assert.match(visibleText(card), /Armor Check Penalty/);
});

test("rank-capable competencies expose sparse Character-owned rank editing", () => {
    let saved = null;
    let cleared = null;
    const card = renderSkillsCard([
        standalone(competency("skill.arcana", "Arcana", "+9", {
            ranks: 5,
            supportsRanks: true
        }))
    ], {
        readOnly: false,
        onSetRank(key, ranks) { saved = [key, ranks]; },
        onClearRank(key) { cleared = key; }
    });

    const editor = byAttribute(card, "data-competency-rank-editor", "skill.arcana")[0];
    assert.ok(editor);
    const input = walk(editor).find(node => node.tagName === "INPUT");
    assert.ok(input);
    assert.equal(input.value, "5");
    input.value = "7";
    const buttons = walk(editor).filter(node => node.tagName === "BUTTON");
    const save = buttons.find(button => button.textContent === "Save");
    const clear = buttons.find(button => button.textContent === "Clear");
    assert.ok(save);
    assert.ok(clear);
    save.dispatch("click");
    clear.dispatch("click");
    assert.deepEqual(saved, ["skill.arcana", 7]);
    assert.equal(cleared, "skill.arcana");
});

test("rank editing is not exposed for competencies that do not support ranks", () => {
    const card = renderSkillsCard([
        standalone(competency("tool.thieves-tools", "Thieves' Tools", "+5", {
            supportsRanks: false,
            supportsTrainingState: true
        }))
    ], {
        readOnly: false,
        onSetRank() {
            throw new Error("rank callback should not be reachable");
        }
    });
    assert.equal(byClass(card, "dd-skill-rank-editor").length, 0);
});

test("skill rows use reference-style proficiency markers without guessing unresolved training", () => {
    const card = renderSkillsCard([
        standalone(competency("arcana", "Arcana", "+7", {
            governingAbility: "Intelligence",
            training: "Proficient"
        })),
        standalone(competency("history", "History", "+5", {
            governingAbility: "Intelligence"
        }))
    ]);

    const arcana = byAttribute(card, "data-skill-id", "arcana")[0];
    const history = byAttribute(card, "data-skill-id", "history")[0];
    const arcanaTraining = byAttribute(arcana, "data-skill-training", "Proficient")[0];
    const historyTraining = byAttribute(history, "data-skill-training", "unresolved")[0];

    assert.ok(arcanaTraining);
    assert.ok(historyTraining);
    assert.equal(arcanaTraining.textContent, "");
    assert.equal(historyTraining.textContent, "-");
    assert.equal(arcanaTraining.getAttribute("data-skill-training-state"), "proficient");
    assert.equal(historyTraining.getAttribute("data-skill-training-state"), "unresolved");
    assert.match(arcanaTraining.getAttribute("aria-label"), /Arcana training: Proficient/);
    assert.match(historyTraining.getAttribute("aria-label"), /History training unresolved/);
});


test("composite skill rows keep each backend-supplied proficiency marker independent", () => {
    const card = renderSkillsCard([
        composite(
            competency("fieldcraft", "Fieldcraft", "+5", { training: "Proficient" }),
            [
                competency("tracking", "Tracking", "+7", { training: "Expertise" }),
                competency("foraging", "Foraging", "+3", { training: "Untrained" })
            ]
        )
    ]);

    const parent = byAttribute(card, "data-skill-id", "fieldcraft")[0];
    const tracking = byAttribute(card, "data-skill-id", "tracking")[0];
    const foraging = byAttribute(card, "data-skill-id", "foraging")[0];

    assert.equal(byAttribute(parent, "data-skill-training-state", "proficient").length, 1);
    assert.equal(byAttribute(tracking, "data-skill-training-state", "expertise").length, 1);
    assert.equal(byAttribute(foraging, "data-skill-training-state", "none").length, 1);
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
    assert.doesNotMatch(source, /\b(?:Stealth|Hide|Move Silently|Perception|Listen|Spot|Athletics|Climb|Jump|Swim|Acrobatics|Balance|Tumble|Craft|Perform|Profession)\b/);
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

test("production Character Sheet drives competencies from the nullable mechanics projection", async () => {
    const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.doesNotMatch(sheetSource, /renderSkillsCard\(null\)/);
    assert.match(sheetSource, /mechanics\?\.competencies === undefined/);
    assert.match(sheetSource, /buildCompetencyPresentation\(mechanics\.competencies\)/);
    assert.match(sheetSource, /renderSkillsCard\(\s*competencyPresentation,\s*\{/);
    assert.match(sheetSource, /onSetRank:\s*handlers\.rules\.setCompetencyRank/);
    assert.match(sheetSource, /onClearRank:\s*handlers\.rules\.clearCompetencyRank/);
});
