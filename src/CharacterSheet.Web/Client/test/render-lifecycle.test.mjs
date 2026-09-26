import assert from "node:assert/strict";
import test from "node:test";
import { createInitialState } from "../.test-dist/app-state.js";
import { createApplication } from "../.test-dist/render-lifecycle.js";
import { attachRulesCoreContext } from "../.test-dist/rules-core-context.js";
import { decorateContextualRulesHelp } from "../.test-dist/ui/contextual-help.js";

test("render lifecycle runs only through explicit render or state dispatch", () => {
    const revisions = [];
    const application = createApplication(
        createInitialState({ kind: "new" }),
        state => revisions.push(state.renderRevision)
    );

    assert.deepEqual(revisions, []);
    application.render();
    application.dispatch({ type: "rerender" });
    application.dispatch({ type: "rerender" }, { render: false });

    assert.deepEqual(revisions, [0, 1]);
    assert.equal(application.getState().renderRevision, 2);

    application.render();
    assert.deepEqual(revisions, [0, 1, 2]);
});

class FakeElement {
    constructor(tagName, className = "", textContent = "") {
        this.tagName = String(tagName).toUpperCase();
        this.className = className;
        this.textContent = textContent;
        this.children = [];
        this.attributes = new Map();
        this.listeners = new Map();
        this.hidden = false;
        this.id = "";
        this.title = "";
        this.type = "";
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
    addEventListener(name, handler) {
        const handlers = this.listeners.get(name) ?? [];
        handlers.push(handler);
        this.listeners.set(name, handlers);
    }
}

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

test("contextual help exposes unfamiliar mechanics without rewriting attack resolution semantics", () => {
    const mechanics = {
        defenses: {
            primaryKey: "defense.ac.total",
            values: [
                { key: "defense.ac.total", label: "Armor Class", effectiveValue: 18 },
                { key: "defense.ac.touch", label: "Touch Armor Class", effectiveValue: 13 }
            ]
        },
        competencies: {
            entries: [{
                key: "skill.tumble",
                label: "Tumble",
                effectiveValue: "+7",
                classSkill: true,
                trainedOnly: true,
                armorCheckPenalty: { applies: true }
            }]
        }
    };
    const projection = {
        mechanics: [
            {
                mechanicKey: "defense.ac.total",
                displayName: "Armor Class",
                help: {
                    topicKey: "defense.ac.total",
                    displayName: "Armor Class",
                    shortText: "The defense normally targeted by attacks.",
                    prominence: "standard"
                }
            },
            {
                mechanicKey: "defense.ac.touch",
                displayName: "Touch Armor Class",
                help: {
                    topicKey: "defense.ac.touch",
                    displayName: "Touch Armor Class",
                    shortText: "Use this defense only when the rule explicitly targets touch AC.",
                    fullText: "A spell attack does not target Touch Armor Class merely because it is magical.",
                    prominence: "prominent"
                }
            }
        ],
        helpTopics: [
            {
                topicKey: "defense.ac.flat-footed",
                displayName: "Flat-Footed Armor Class",
                shortText: "Use this defense only when the rule explicitly calls for it.",
                prominence: "prominent"
            },
            {
                topicKey: "competency.class-skill",
                displayName: "Class Skill",
                shortText: "Class Skill records whether the effective rules treat this competency as a class skill.",
                prominence: "prominent"
            },
            {
                topicKey: "competency.trained-only",
                displayName: "Trained Only",
                shortText: "A Trained Only competency requires the training specified by the effective rules.",
                prominence: "prominent"
            },
            {
                topicKey: "competency.armor-check-penalty",
                displayName: "Armor Check Penalty",
                shortText: "Armor Check Penalty applies only when the competency rules say it does.",
                prominence: "prominent"
            }
        ],
        actions: [{
            actionKey: "action.unseen-strike",
            attackResolution: {
                targetDefenseKey: "defense.ac.total",
                rollMode: "advantage",
                targetStateKeys: ["state.unseen-attacker"]
            }
        }]
    };
    attachRulesCoreContext(mechanics, projection);

    const root = new FakeElement("article");
    const normalAc = new FakeElement("div");
    normalAc.setAttribute("data-mechanic-key", "defense.ac.total");
    normalAc.append(new FakeElement("span", "dd-mechanic-value__label", "Armor Class"));
    const touchAc = new FakeElement("div");
    touchAc.setAttribute("data-mechanic-key", "defense.ac.touch");
    touchAc.append(new FakeElement("span", "dd-mechanic-value__label", "Touch AC"));
    const skill = new FakeElement("span");
    skill.setAttribute("data-skill-id", "skill.tumble");
    skill.append(new FakeElement("span", "dd-skill-row__name", "Tumble"));
    const action = new FakeElement("article");
    action.setAttribute("data-action-key", "action.unseen-strike");
    root.append(normalAc, touchAc, skill, action);

    const previousDocument = globalThis.document;
    globalThis.document = { createElement: tagName => new FakeElement(tagName) };
    try {
        decorateContextualRulesHelp(root, mechanics);
    } finally {
        if (previousDocument === undefined) {
            delete globalThis.document;
        } else {
            globalThis.document = previousDocument;
        }
    }

    assert.equal(byAttribute(root, "data-help-topic", "defense.ac.total").length, 0);
    assert.equal(byAttribute(root, "data-help-topic", "defense.ac.touch").length, 1);
    assert.equal(byAttribute(root, "data-help-topic", "competency.class-skill").length, 1);
    assert.equal(byAttribute(root, "data-help-topic", "competency.trained-only").length, 1);
    assert.equal(byAttribute(root, "data-help-topic", "competency.armor-check-penalty").length, 1);

    const resolution = byAttribute(root, "data-attack-resolution", "action.unseen-strike");
    assert.equal(resolution.length, 1);
    const resolutionText = visibleText(resolution[0]);
    assert.match(resolutionText, /Target defense Armor Class/);
    assert.match(resolutionText, /Roll mode Advantage/);
    assert.match(resolutionText, /Target state Unseen attacker/);
    assert.doesNotMatch(resolutionText, /Touch Armor Class|Flat-Footed Armor Class/);
});
