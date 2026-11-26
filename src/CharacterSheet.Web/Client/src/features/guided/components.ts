import { createButton, createElement, createSectionCard } from "../../ui/components.js";
import { matchingRuleNodes, type AllocationView, type ChoiceRequirement, type DecisionLink,
    type DependencyChange, type DisplayFact, type DraftSaveStatus, type RuleDetail, type RuleNode,
    type ValidationIssue } from "./contracts.js";

const buttonClass = "dd-button dd-button--secondary";

/** The caller owns search/selection state so a render never creates a second draft. */
export function renderRuleBrowser(nodes: readonly RuleNode[], query: string, selectedId: string | null,
    readOnly: boolean, onSearch: (query: string) => void, onInspect: (id: string) => void): HTMLElement {
    const card = createSectionCard("Browse rules", "dd-guided-browser");
    const label = createElement("label", undefined, "Search rules");
    const input = createElement("input");
    input.type = "search";
    input.value = query;
    input.addEventListener("input", () => onSearch(input.value));
    label.append(input);
    card.append(label);
    const visible = matchingRuleNodes(nodes, query);
    const byId = new Map(visible.map(node => [node.id, node]));
    const visited = new Set<string>();
    function appendNode(parent: HTMLElement, node: RuleNode): void {
        if (visited.has(node.id)) return;
        visited.add(node.id);
        const item = createElement("li");
        const button = createButton(node.label, buttonClass, () => onInspect(node.id));
        if (node.id === selectedId) button.setAttribute("aria-current", "true");
        item.append(button);
        if (!node.selectable || readOnly || node.reason) item.append(createElement("p", undefined,
            node.reason ?? (!node.selectable ? "Browse only" : "Read only")));
        const children = visible.filter(child => child.parentId === node.id);
        if (children.length) {
            const list = createElement("ul");
            children.forEach(child => appendNode(list, child));
            item.append(list);
        }
        parent.append(item);
    }
    const list = createElement("ul", "dd-guided-tree");
    visible.filter(node => !node.parentId || !byId.has(node.parentId)).forEach(node => appendNode(list, node));
    // Malformed/orphaned hierarchy remains inspectable without infinite recursion.
    visible.forEach(node => appendNode(list, node));
    card.append(list);
    if (!visible.length) card.append(createElement("p", undefined, "No matching rules."));
    return card;
}
export function renderFacts(facts: readonly DisplayFact[]): HTMLElement {
    const list = createElement("dl", "dd-guided-facts");
    for (const fact of facts) {
        const value = createElement("dd", undefined, fact.value);
        if (fact.source) value.append(createElement("small", undefined, " — " + fact.source.label));
        list.append(createElement("dt", undefined, fact.label), value);
    }
    return list;
}
export function renderRuleDetail(detail: RuleDetail): HTMLElement {
    const card = createElement("article", "dd-guided-detail");
    card.append(createElement("h3", undefined, detail.title));
    if (detail.description) card.append(createElement("p", undefined, detail.description));
    if (detail.source) card.append(createElement("p", undefined, "Source: " + detail.source.label));
    if (detail.facts) card.append(renderFacts(detail.facts));
    for (const feature of detail.features ?? []) {
        const group = createElement("details");
        group.append(createElement("summary", undefined, feature.title), renderRuleDetail(feature));
        card.append(group);
    }
    return card;
}
export function renderDecisionNavigation(decisions: readonly DecisionLink[], current: string,
    navigate: (id: string) => void): HTMLElement {
    const nav = createElement("nav", "dd-guided-secondary");
    nav.setAttribute("aria-label", "Configuration decisions");
    for (const decision of decisions) {
        const button = createButton(decision.label, buttonClass, () => navigate(decision.id));
        if (decision.id === current) button.setAttribute("aria-current", "step");
        nav.append(button);
    }
    return nav;
}
export function renderValidation(issues: readonly ValidationIssue[], navigate: (id: string) => void): HTMLElement {
    const section = createSectionCard("Validation");
    const list = createElement("ul");
    for (const issue of issues) {
        const item = createElement("li", undefined, issue.severity.replaceAll("-", " ") + ": " + issue.message);
        if (issue.decision) {
            const decision = issue.decision;
            item.append(createButton(decision.label, buttonClass, () => navigate(decision.id)));
        }
        list.append(item);
    }
    section.append(list);
    if (!issues.length) section.append(createElement("p", undefined, "No issues reported."));
    return section;
}
export function renderAllocation(view: AllocationView): HTMLElement {
    const section = createSectionCard(view.label);
    section.append(renderFacts([
        { label: "Used", value: view.used ?? "Unresolved" },
        { label: "Allowance", value: view.allowance ?? "Unresolved" },
        { label: "Remaining", value: view.remaining ?? "Unresolved" }
    ]), createElement("p", undefined, view.status));
    if (view.detail) section.append(createElement("p", undefined, view.detail));
    return section;
}
export interface ChoiceActions {
    select(requirementId: string, optionId: string, selected: boolean): void;
    browseCatalog(requirementId: string, catalogId: string): void;
    navigate(decisionId: string): void;
}
/** Render exactly the active native branch. Never create hidden selections or
 * enforce a game-specific count; the adapter and native evaluation own validity. */
export function renderChoice(requirement: ChoiceRequirement, readOnly: boolean, actions: ChoiceActions): HTMLElement {
    const group = createElement("fieldset", "dd-guided-choice");
    group.append(createElement("legend", undefined, requirement.label));
    if (requirement.source) group.append(createElement("p", undefined, "Source: " + requirement.source.label));
    if (requirement.minimum !== undefined || requirement.maximum !== undefined)
        group.append(createElement("p", undefined, "Selected: " + requirement.selectedIds.length +
            (requirement.minimum === undefined ? "" : "; minimum: " + requirement.minimum) +
            (requirement.maximum === undefined ? "" : "; maximum: " + requirement.maximum)));
    for (const option of requirement.options) {
        const label = createElement("label");
        const input = createElement("input");
        input.type = "checkbox";
        input.checked = requirement.selectedIds.includes(option.id);
        input.disabled = readOnly || (!input.checked && !!option.unavailableReason);
        input.addEventListener("change", () => actions.select(requirement.id, option.id, input.checked));
        label.append(input, document.createTextNode(option.label));
        group.append(label);
        if (option.unavailableReason) group.append(createElement("p", undefined, option.unavailableReason));
        if (option.source) group.append(createElement("small", undefined, option.source.label));
        if (input.checked) for (const child of option.children ?? []) group.append(renderChoice(child, readOnly, actions));
    }
    if (requirement.catalogId) {
        const catalogId = requirement.catalogId;
        group.append(createButton("Browse options", buttonClass,
            () => actions.browseCatalog(requirement.id, catalogId), readOnly));
    }
    if (requirement.issues?.length) group.append(renderValidation(requirement.issues, actions.navigate));
    return group;
}
export function renderDependencyReview(change: DependencyChange, confirm: () => void, cancel: () => void,
    readOnly: boolean): HTMLElement {
    const section = createSectionCard("Review changes");
    for (const [label, entries] of [["Retained", change.retained], ["Reconsider", change.reconsider],
        ["Newly available", change.available]] as const) {
        section.append(createElement("h3", undefined, label));
        const list = createElement("ul");
        entries.forEach(entry => list.append(createElement("li", undefined, entry)));
        section.append(list);
    }
    section.append(createButton("Apply changes", buttonClass, confirm, readOnly),
        createButton("Cancel", buttonClass, cancel));
    return section;
}
export function renderDraftStatus(status: DraftSaveStatus): HTMLElement {
    const labels: Record<DraftSaveStatus, string> = {
        unavailable: "Draft status unavailable", dirty: "Unsaved changes", saving: "Saving…",
        saved: "Draft saved", failed: "Save failed — retry", conflict: "Changed elsewhere — review before saving",
        stale: "Rules changed — review required", "read-only": "Read only"
    };
    const node = createElement("p", "dd-guided-draft-status", labels[status]);
    node.setAttribute("role", "status");
    return node;
}
/** Browser/preview are optional; smaller screens use accessible native disclosures. */
export function renderWorkspace(work: HTMLElement, browser?: HTMLElement, preview?: HTMLElement): HTMLElement {
    const workspace = createElement("div", "dd-guided-workspace");
    workspace.dataset.guidedLayout = browser && preview ? "three" : browser ? "browser" : preview ? "preview" : "single";
    if (browser) {
        const section = createElement("details", "dd-guided-workspace__side");
        section.open = true;
        section.append(createElement("summary", undefined, "Rule browser"), browser);
        workspace.append(section);
    }
    workspace.append(work);
    if (preview) {
        const section = createElement("details", "dd-guided-workspace__side");
        section.open = true;
        section.append(createElement("summary", undefined, "Preview"), preview);
        workspace.append(section);
    }
    return workspace;
}
