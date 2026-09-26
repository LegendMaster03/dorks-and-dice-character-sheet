import {
    getRulesCoreContext,
    type RulesCoreCharacterAttackResolutionView,
    type RulesCoreCharacterProjectionContextView,
    type RulesCoreContextualHelpView
} from "../rules-core-context.js";
import type {
    CharacterMechanicsView,
    CompetencyView
} from "./character-mechanics.js";
import { createElement } from "./components.js";

interface ContextualHelpIndex {
    helpByTopic: ReadonlyMap<string, RulesCoreContextualHelpView>;
    mechanicNames: ReadonlyMap<string, string>;
    actionResolutionByKey: ReadonlyMap<string, RulesCoreCharacterAttackResolutionView>;
    competencyByKey: ReadonlyMap<string, CompetencyView>;
    mechanics: CharacterMechanicsView;
}

let helpControlSequence = 0;

export function applyContextualRulesHelp(
    mechanics: CharacterMechanicsView | null
): void {
    if (mechanics === null || typeof document === "undefined") return;
    const roots = document.querySelectorAll<HTMLElement>("[data-character-sheet-shell]");
    for (const root of roots) decorateContextualRulesHelp(root, mechanics);
}

export function decorateContextualRulesHelp(
    root: HTMLElement,
    mechanics: CharacterMechanicsView | null
): void {
    if (mechanics === null) return;
    const projection = getRulesCoreContext(mechanics);
    if (projection === undefined) return;

    const index = buildIndex(projection, mechanics);
    for (const node of walk(root)) {
        decorateMechanic(node, index);
        decorateCompetency(node, index);
        decorateActionResolution(node, index);
    }
}

function buildIndex(
    projection: RulesCoreCharacterProjectionContextView,
    mechanics: CharacterMechanicsView
): ContextualHelpIndex {
    const helpByTopic = new Map<string, RulesCoreContextualHelpView>();
    for (const help of projection.helpTopics ?? []) {
        helpByTopic.set(help.topicKey, help);
    }

    const mechanicNames = new Map<string, string>();
    for (const mechanic of projection.mechanics ?? []) {
        mechanicNames.set(mechanic.mechanicKey, mechanic.displayName);
        if (mechanic.help !== null && mechanic.help !== undefined) {
            helpByTopic.set(mechanic.help.topicKey, mechanic.help);
        }
    }

    const actionResolutionByKey = new Map<string, RulesCoreCharacterAttackResolutionView>();
    for (const action of projection.actions ?? []) {
        if (action.attackResolution !== null && action.attackResolution !== undefined) {
            actionResolutionByKey.set(action.actionKey, action.attackResolution);
        }
    }

    const competencyByKey = new Map<string, CompetencyView>();
    for (const competency of mechanics.competencies?.entries ?? []) {
        competencyByKey.set(competency.key, competency);
    }

    return {
        helpByTopic,
        mechanicNames,
        actionResolutionByKey,
        competencyByKey,
        mechanics
    };
}

function decorateMechanic(
    node: HTMLElement,
    index: ContextualHelpIndex
): void {
    if (node.getAttribute("data-contextual-help-decorated") === "true") return;
    const topicKey = node.getAttribute("data-mechanic-key")
        ?? node.getAttribute("data-health-track-key");
    if (topicKey === null) return;

    const help = prominentHelp(index.helpByTopic.get(topicKey));
    if (help === undefined) return;

    const anchor = findLabelAnchor(node) ?? node;
    anchor.append(renderHelpControl(help));
    node.setAttribute("data-contextual-help-decorated", "true");
}

function decorateCompetency(
    node: HTMLElement,
    index: ContextualHelpIndex
): void {
    if (node.getAttribute("data-contextual-competency-help-decorated") === "true") return;
    const key = node.getAttribute("data-skill-id")
        ?? node.getAttribute("data-competency-id");
    if (key === null) return;
    const competency = index.competencyByKey.get(key);
    if (competency === undefined) return;

    const topics: RulesCoreContextualHelpView[] = [];
    addIfProminent(
        topics,
        competency.classSkill !== undefined || competency.supportsClassSkillState === true,
        index.helpByTopic.get("competency.class-skill"));
    addIfProminent(
        topics,
        competency.trainedOnly !== undefined,
        index.helpByTopic.get("competency.trained-only"));
    addIfProminent(
        topics,
        competency.armorCheckPenalty !== undefined,
        index.helpByTopic.get("competency.armor-check-penalty"));
    if (topics.length === 0) return;

    const anchor = findLabelAnchor(node) ?? node;
    const cluster = createElement("span", "dd-contextual-help-cluster");
    for (const help of topics) cluster.append(renderHelpControl(help));
    anchor.append(cluster);
    node.setAttribute("data-contextual-competency-help-decorated", "true");
}

function addIfProminent(
    target: RulesCoreContextualHelpView[],
    applies: boolean,
    help: RulesCoreContextualHelpView | undefined
): void {
    if (!applies) return;
    const visible = prominentHelp(help);
    if (visible !== undefined) target.push(visible);
}

function decorateActionResolution(
    node: HTMLElement,
    index: ContextualHelpIndex
): void {
    if (node.getAttribute("data-attack-resolution-decorated") === "true") return;
    const actionKey = node.getAttribute("data-action-key");
    if (actionKey === null) return;
    const resolution = index.actionResolutionByKey.get(actionKey);
    if (resolution === undefined) return;

    const facts = createElement("dl", "dd-action-resolution__facts");
    if (resolution.targetDefenseKey !== null
        && resolution.targetDefenseKey !== undefined
        && resolution.targetDefenseKey.trim().length > 0) {
        appendFact(
            facts,
            "Target defense",
            displayDefense(resolution.targetDefenseKey, index));
    }
    if (resolution.rollMode.trim().length > 0) {
        appendFact(facts, "Roll mode", humanizeToken(resolution.rollMode));
    }
    if (resolution.targetStateKeys.length > 0) {
        appendFact(
            facts,
            resolution.targetStateKeys.length === 1 ? "Target state" : "Target states",
            resolution.targetStateKeys.map(humanizeToken).join(", "));
    }
    if (facts.children.length === 0) return;

    const section = createElement("section", "dd-action-resolution");
    section.setAttribute("data-attack-resolution", actionKey);
    section.append(
        createElement("h4", "dd-action-resolution__title", "Attack resolution"),
        facts);
    node.append(section);
    node.setAttribute("data-attack-resolution-decorated", "true");
}

function displayDefense(
    key: string,
    index: ContextualHelpIndex
): string {
    const help = index.helpByTopic.get(key);
    if (help !== undefined) return help.displayName;
    const projectedName = index.mechanicNames.get(key);
    if (projectedName !== undefined) return projectedName;
    const defense = index.mechanics.defenses?.values.find(value => value.key === key);
    return defense?.label ?? key;
}

function prominentHelp(
    help: RulesCoreContextualHelpView | undefined
): RulesCoreContextualHelpView | undefined {
    return help?.prominence.toLowerCase() === "prominent" ? help : undefined;
}

function renderHelpControl(help: RulesCoreContextualHelpView): HTMLElement {
    const root = createElement("span", "dd-contextual-help");
    root.setAttribute("data-help-topic", help.topicKey);

    const button = createElement("button", "dd-contextual-help__toggle", "?") as HTMLButtonElement;
    button.type = "button";
    button.title = help.shortText;
    button.setAttribute("aria-label", `Help: ${help.displayName}`);

    const panel = createElement("span", "dd-contextual-help__panel");
    panel.hidden = true;
    panel.setAttribute("role", "tooltip");
    panel.id = `dd-contextual-help-${++helpControlSequence}`;
    button.setAttribute("aria-controls", panel.id);
    button.setAttribute("aria-expanded", "false");
    panel.append(
        createElement("strong", "dd-contextual-help__title", help.displayName),
        createElement("span", "dd-contextual-help__summary", help.shortText));
    if (help.fullText !== null
        && help.fullText !== undefined
        && help.fullText.trim().length > 0
        && help.fullText.trim() !== help.shortText.trim()) {
        panel.append(createElement(
            "span",
            "dd-contextual-help__detail",
            help.fullText));
    }

    let hovered = false;
    let focused = false;
    let pinned = false;
    const sync = (): void => {
        const open = hovered || focused || pinned;
        panel.hidden = !open;
        button.setAttribute("aria-expanded", open ? "true" : "false");
    };
    root.addEventListener("mouseenter", () => {
        hovered = true;
        sync();
    });
    root.addEventListener("mouseleave", () => {
        hovered = false;
        sync();
    });
    button.addEventListener("focus", () => {
        focused = true;
        sync();
    });
    button.addEventListener("blur", () => {
        focused = false;
        sync();
    });
    button.addEventListener("click", event => {
        event.stopPropagation();
        pinned = !pinned;
        sync();
    });

    root.append(button, panel);
    return root;
}

function appendFact(list: HTMLElement, label: string, value: string): void {
    const row = createElement("div", "dd-action-resolution__fact");
    row.append(
        createElement("dt", "dd-action-resolution__term", label),
        createElement("dd", "dd-action-resolution__value", value));
    list.append(row);
}

function findLabelAnchor(root: HTMLElement): HTMLElement | undefined {
    const labelClasses = new Set([
        "dd-mechanic-value__label",
        "dd-split-stat__secondary-label",
        "dd-combat-ac__secondary-label",
        "dd-health-card__label",
        "dd-skill-row__name",
        "dd-competency-row__name"
    ]);
    return walk(root).find(node =>
        node !== root
        && node.className.split(/\s+/).some(value => labelClasses.has(value)));
}

function walk(root: HTMLElement): HTMLElement[] {
    const nodes: HTMLElement[] = [root];
    for (let index = 0; index < nodes.length; index++) {
        const node = nodes[index];
        for (const child of Array.from(node.children)) {
            nodes.push(child as HTMLElement);
        }
    }
    return nodes;
}

function humanizeToken(value: string): string {
    const withoutNamespace = value.includes(".")
        ? value.slice(value.lastIndexOf(".") + 1)
        : value;
    const normalized = withoutNamespace
        .replace(/[_-]+/g, " ")
        .replace(/\s+/g, " ")
        .trim();
    return normalized.length === 0
        ? value
        : normalized[0].toUpperCase() + normalized.slice(1);
}
