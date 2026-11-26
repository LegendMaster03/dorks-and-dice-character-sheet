import { createButton, createElement, createSectionCard } from "../../ui/components.js";
import { renderChoice, renderDecisionNavigation, renderDependencyReview, renderDraftStatus, renderFacts,
    renderRuleBrowser, renderRuleDetail, renderValidation, renderWorkspace, type ChoiceActions } from "../guided/components.js";
import { isCurrentEvaluation, type ChoiceRequirement, type DraftView, type EvaluationView, type EditorContext, type RuleNode } from "../guided/contracts.js";
import { changeExperienceChoice, changeExperienceSource, choiceTree, labelFor, reconsiderChoices, removeReviewedChoice,
    type ExperienceCatalog, type ExperienceDraft, type ExperienceKind, type ExperiencePreview, type NativeGrant, type SourceChange } from "./model.js";

export const experienceDecisions = [
    { id: "upbringing", label: "Upbringing" }, { id: "background", label: "Background" },
    { id: "benefits", label: "Benefits" }, { id: "review", label: "Review" }
] as const;
export interface ExperienceWorkspaceContext extends EditorContext<ExperienceDraft, ExperiencePreview> {
    readonly catalog: ExperienceCatalog;
    readonly suggestedUpbringingId?: string | null;
    readonly query: string;
    readonly inspectedId: string | null;
    readonly onSearch: (query: string) => void;
    readonly inspect: (id: string) => void;
    readonly pendingSourceChange: (SourceChange & { draftRevision: number }) | null;
    readonly reviewSourceChange: (change: (SourceChange & { draftRevision: number }) | null) => void;
    readonly openEquipment: () => void;
    /** The application supplies the real Skill/Competency hierarchy, including
     * nonselectable families and independently trainable composite nodes. */
    readonly trainingNodes?: readonly RuleNode[];
}
const secondary = "dd-button dd-button--secondary";
function description(grant: NativeGrant): string | undefined {
    if (!grant.authoredEffect || typeof grant.authoredEffect !== "object") return undefined;
    const effect = grant.authoredEffect as Record<string, unknown>;
    if (typeof effect.description === "string") return effect.description;
    const benefit = effect.authoredBenefit;
    if (benefit && typeof benefit === "object" && "description" in benefit && typeof benefit.description === "string")
        return benefit.description;
    return undefined;
}
function grantGroup(kind: string): string {
    switch (kind) {
        case "skill-training": return "Skill training";
        case "competency-training": return "Competency training";
        case "language": return "Languages";
        case "scoped-skill-effect": return "Contextual check effects";
        case "learned-cantrip": case "learned-spell-reference": return "Learned spells";
        case "manual-feature": return "Learned and conditional features";
        case "vehicle-qualification": return "Vehicle qualifications (execution unavailable)";
        default: return "Other native benefits";
    }
}
function grantLabel(grant: NativeGrant, catalog: ExperienceCatalog): string {
    const title = labelFor(catalog, grant.targetId);
    if (grant.kind === "skill-training") return title + " — proficiency";
    if (grant.kind === "competency-training") return title + " — " +
        [grant.ranks > 0 ? grant.ranks + (grant.ranks === 1 ? " rank" : " ranks") : "",
            grant.proficient ? "proficiency" : ""].filter(Boolean).join(", ");
    if (grant.kind === "scoped-skill-effect") return title + " — qualifying checks only";
    return title;
}
/** Every candidate is shown as its own occurrence. Do not aggregate ranks or
 * duplicate proficiency in the UI; that belongs to verified Core composition. */
export function renderExperienceBenefits(preview: ExperiencePreview, catalog: ExperienceCatalog): HTMLElement {
    const card = createSectionCard("Combined candidate benefits");
    card.append(createElement("p", undefined, "Proposed learned benefits, not earned or applied to the character."));
    if (preview.sources.length) card.append(renderFacts(preview.sources.map(source => ({
        label: source.inherited ? "Inherited cultural source" : "Selected source", value: labelFor(catalog, source.sourceId)
    }))));
    const groups = new Map<string, NativeGrant[]>();
    for (const grant of preview.candidateGrants) {
        const key = grantGroup(grant.kind);
        const group = groups.get(key) ?? [];
        group.push(grant);
        groups.set(key, group);
    }
    for (const [group, grants] of groups) {
        const section = createElement("section");
        section.append(createElement("h3", undefined, group));
        for (const grant of grants) {
            const chosen = !!grant.optionId || preview.choices.some(c => c.key === grant.grantPath);
            const detail = createElement("details");
            const replacement = grant.originalTargetId ? " — replaces " + labelFor(catalog, grant.originalTargetId) : "";
            detail.append(createElement("summary", undefined, grantLabel(grant, catalog) + replacement),
                renderFacts([
                    { label: "Source", value: labelFor(catalog, grant.sourceId) },
                    { label: "Benefit", value: labelFor(catalog, grant.benefitId) },
                    { label: "Basis", value: chosen || grant.originalTargetId ? "Selected" : "Fixed" },
                    { label: "Applicability", value: grant.kind === "scoped-skill-effect" || grant.kind === "manual-feature" ?
                        "Contextual or manual; not an unconditional modifier" : "Candidate only" }
                ]));
            const text = description(grant);
            if (text) detail.append(createElement("p", undefined, text));
            section.append(detail);
        }
        card.append(section);
    }
    const qualifications = createElement("section");
    qualifications.append(createElement("h3", undefined, "Equipment qualifications"));
    if (preview.candidateEquipment?.status === "resolved") {
        for (const qualification of preview.candidateEquipment.qualifications) {
            const group = createElement("details");
            group.append(createElement("summary", undefined, labelFor(catalog, qualification.qualificationId) +
                " — " + qualification.equipmentCategory + " proficiency"));
            group.append(renderFacts(qualification.provenance.map(p => ({
                label: labelFor(catalog, p.grantId), value: labelFor(catalog, p.sourceId)
            }))));
            qualifications.append(group);
        }
        if (!preview.candidateEquipment.qualifications.length)
            qualifications.append(createElement("p", undefined, "No candidate qualifications."));
    } else qualifications.append(createElement("p", undefined, "Qualification projection is incomplete or unavailable."));
    for (const replacement of preview.equipmentReplacements) qualifications.append(createElement("p", undefined,
        labelFor(catalog, replacement.originalQualificationId) + " replaced with " +
        labelFor(catalog, replacement.replacementQualificationId) + " — " + labelFor(catalog, replacement.provenance.sourceId)));
    card.append(qualifications);
    if (!preview.candidateGrants.length) card.append(createElement("p", undefined, "No candidate benefits reported."));
    return card;
}

export interface EquipmentOption { id: string; label: string }
export interface ExperienceEquipmentContext {
    evaluation: EvaluationView<ExperiencePreview>;
    catalog: ExperienceCatalog;
    draft: DraftView<ExperienceDraft>;
    readOnly: boolean;
    /** Options must be returned by the existing ordinary-item authority for
     * this exact intent; ordinary display names are never treated as IDs. */
    itemOptions?: Readonly<Record<string, readonly EquipmentOption[]>>;
    onSelect?: (draft: ExperienceDraft) => void;
}
/** Mount this in the existing Equipment step. No inventory APIs are called.
 * A starting-item intention remains separate from an owned inventory item. */
export function renderExperienceEquipment(context: ExperienceEquipmentContext): HTMLElement {
    const { evaluation, catalog } = context;
    const card = createSectionCard("Life Experiences equipment intentions");
    if (!isCurrentEvaluation(context.draft, evaluation) || context.draft.rulesetId !== catalog.rulesetId ||
        context.draft.fingerprint !== catalog.fingerprint || !["saved", "read-only"].includes(context.draft.saveStatus)) {
        card.append(createElement("p", undefined, "Current equipment intentions are unavailable. Reload or complete the draft save."));
        return card;
    }
    const draft = context.draft.proposal, preview = evaluation.result;
    const readOnly = context.readOnly || context.draft.readOnly || context.draft.saveStatus === "read-only";
    card.append(createElement("p", undefined, "Unclaimed starting entitlements. No items or currency have been awarded."));
    for (const item of preview.startingItems) {
        const group = createElement("section");
        group.append(createElement("h3", undefined, item.name + " × " + item.quantity),
            renderFacts([
                { label: "Source", value: labelFor(catalog, item.key.split("/")[0]!) },
                ...(item.expectedEquipmentTypeId ? [{ label: "Required type", value: labelFor(catalog, item.expectedEquipmentTypeId) }] : []),
                { label: "Restrictions", value: [item.requiresMundane ? "Mundane" : "", item.requiresInexpensive ? "Inexpensive" : ""].filter(Boolean).join(", ") || "None reported" },
                { label: "Selection", value: item.selectedItemId ? labelFor(catalog, item.selectedItemId) : "Not yet resolved" }
            ]));
        if (item.ordinaryOptions.length) group.append(createElement("p", undefined, "Authored options: " + item.ordinaryOptions.join(", ")));
        const options = context.itemOptions?.[item.key];
        if (options && context.onSelect) {
            const onSelect = context.onSelect;
            for (const option of options) group.append(createButton(
                (draft.startingItemIds[item.key] === option.id ? "Selected: " : "Select: ") + option.label, secondary,
                () => onSelect({ ...draft, startingItemIds: { ...draft.startingItemIds, [item.key]: option.id } }), readOnly));
        } else group.append(createElement("p", undefined, "Ordinary inventory identity verification is required."));
        card.append(group);
    }
    for (const money of preview.currency) card.append(createElement("p", undefined,
        money.quantity + " " + money.currency + " — " + labelFor(catalog, money.sourceId)));
    if (!preview.startingItems.length && !preview.currency.length)
        card.append(createElement("p", undefined, "No equipment or currency intentions reported."));
    return card;
}

/** Preserve hierarchical training presentation and exact independent options.
 * Noneligible ancestors are only labels, never synthetic selectable grants. */
function renderTrainingChoice(requirement: ChoiceRequirement, nodes: readonly RuleNode[], readOnly: boolean,
    actions: ChoiceActions): HTMLElement {
    const options = new Map(requirement.options.map(o => [o.id, o]));
    const nodeById = new Map(nodes.map(n => [n.id, n]));
    const included = new Set<string>();
    for (const option of requirement.options) {
        let id: string | null | undefined = option.id;
        while (id && !included.has(id)) {
            included.add(id);
            id = nodeById.get(id)?.parentId;
        }
    }
    const group = createElement("fieldset", "dd-guided-choice");
    group.append(createElement("legend", undefined, requirement.label),
        createElement("p", undefined, "Selected: " + requirement.selectedIds.length + " / " + requirement.maximum));
    if (requirement.source) group.append(createElement("p", undefined, "Source: " + requirement.source.label));
    const visited = new Set<string>();
    const visible = nodes.filter(n => included.has(n.id));
    const append = (container: HTMLElement, node: RuleNode) => {
        if (visited.has(node.id)) return;
        visited.add(node.id);
        const li = createElement("li");
        const option = options.get(node.id);
        if (option && node.selectable) {
            const label = createElement("label");
            const input = createElement("input");
            input.type = "checkbox";
            input.checked = requirement.selectedIds.includes(option.id);
            input.disabled = readOnly || (!input.checked && (!!option.unavailableReason ||
                (requirement.maximum !== 1 && requirement.selectedIds.length >= (requirement.maximum ?? Infinity))));
            input.addEventListener("change", () => actions.select(requirement.id, option.id, input.checked));
            label.append(input, document.createTextNode(option.label));
            li.append(label);
        } else li.append(createElement("span", undefined, node.label));
        const children = visible.filter(n => n.parentId === node.id);
        if (children.length) {
            const list = createElement("ul");
            for (const child of children) append(list, child);
            li.append(list);
        }
        container.append(li);
    };
    const list = createElement("ul", "dd-guided-tree");
    visible.filter(n => !n.parentId || !included.has(n.parentId)).forEach(n => append(list, n));
    visible.forEach(n => append(list, n));
    group.append(list);
    if (requirement.options.some(o => !nodeById.has(o.id) || !nodeById.get(o.id)?.selectable))
        group.append(createElement("p", undefined, "Some native options lack verified training hierarchy metadata; selection is unavailable for those entries."));
    if (requirement.issues?.length) group.append(renderValidation(requirement.issues, actions.navigate));
    return group;
}

export function renderExperienceWorkspace(context: ExperienceWorkspaceContext): HTMLElement {
    const work = createSectionCard("Life Experiences");
    work.append(renderDecisionNavigation(experienceDecisions, context.decisionId, context.navigate));
    if (!context.draft) {
        work.append(createElement("p", undefined, "Life Experiences draft storage is unavailable."));
        return renderWorkspace(work);
    }
    const draft = context.draft.proposal;
    const catalogCurrent = context.catalog.rulesetId === context.draft.rulesetId &&
        context.catalog.fingerprint === context.draft.fingerprint;
    const disabled = context.readOnly || context.draft.readOnly || !catalogCurrent ||
        ["saving", "conflict", "stale", "failed", "unavailable", "read-only"].includes(context.draft.saveStatus);
    const preview = context.preview && isCurrentEvaluation(context.draft, context.preview) && catalogCurrent &&
        ["saved", "read-only"].includes(context.draft.saveStatus) ?
        context.preview.result : null;
    work.append(renderDraftStatus(context.draft.saveStatus));
    if (context.draft.saveStatus === "failed") work.append(createButton("Retry draft save", secondary, () => { void context.retry(); }, context.readOnly));
    if (!catalogCurrent) work.append(createElement("p", undefined, "The draft and catalog do not match. Reload and review before editing."));
    work.append(renderFacts([
        { label: "Upbringing", value: draft.upbringingId ? labelFor(context.catalog, draft.upbringingId) : "Not selected" },
        { label: "Background", value: draft.backgroundId ? labelFor(context.catalog, draft.backgroundId) : "Not selected" }
    ]));
    if (context.suggestedUpbringingId) work.append(createElement("p", undefined,
        "Suggested Upbringing: " + labelFor(context.catalog, context.suggestedUpbringingId) + " (not automatically selected)."));
    const save = (proposal: ExperienceDraft) => { if (!disabled) void context.save(proposal); };
    let browser: HTMLElement | undefined;
    if (context.decisionId === "upbringing" || context.decisionId === "background") {
        const kind: ExperienceKind = context.decisionId;
        const nodes = context.catalog.nodes.filter(n => n.kind === kind);
        browser = renderRuleBrowser(nodes, context.query, context.inspectedId, disabled, context.onSearch, context.inspect);
        const inspected = nodes.find(n => n.id === context.inspectedId);
        if (!context.catalog.hierarchyComplete) work.append(createElement("p", undefined,
            "Full native hierarchy and selection metadata are unavailable. Title results are browse only."));
        if (inspected) {
            work.append(renderRuleDetail(inspected.detail), createButton("Select " + inspected.label, secondary, () => {
                const change = changeExperienceSource(draft, kind, inspected.id, context.catalog);
                if (change.reconsider.length) context.reviewSourceChange({ ...change, draftRevision: context.draft!.revision });
                else save(change.draft);
            }, disabled || !inspected.selectable || !context.catalog.hierarchyComplete));
        }

    }
    if (context.pendingSourceChange && context.pendingSourceChange.draftRevision === context.draft.revision) {
        const change = context.pendingSourceChange;
        const summarize = (key: string) => key.split("/").map(id => labelFor(context.catalog, id)).join(" — ");
        work.append(renderDependencyReview({ retained: change.retained.map(summarize),
            reconsider: change.reconsider.map(summarize), available: [] }, () => {
            save(change.draft);
            context.reviewSourceChange(null);
        }, () => context.reviewSourceChange(null), disabled));
    }
    if (context.decisionId === "benefits" || context.decisionId === "review") {
        if (!preview) work.append(createElement("p", undefined,
            !draft.upbringingId || !draft.backgroundId ? "Select both sources to evaluate benefits." : "Current native preview unavailable."));
        else {
            const native = new Map(preview.choices.map(c => [c.key, c]));
            const actions: ChoiceActions = {
                select: (key, id, selected) => {
                    const choice = native.get(key);
                    if (!choice) return;
                    const next = changeExperienceChoice(draft, choice, id, selected);
                    const reconsider = Object.keys(draft.selections).filter(k => k !== key && !(k in next.selections));
                    if (reconsider.length) context.reviewSourceChange({ draft: next, reconsider,
                        retained: Object.keys(next.selections).filter(k => k !== key), draftRevision: context.draft!.revision });
                    else save(next);
                },
                browseCatalog: () => {}, navigate: context.navigate
            };
            const render = (requirement: ChoiceRequirement): HTMLElement => {
                const choice = native.get(requirement.id);
                const training = choice && ["skill", "competency", "duplicate-skill"].includes(choice.kind);
                if (training && !context.trainingNodes) {
                    const group = createSectionCard(requirement.label);
                    group.append(createElement("p", undefined, "Verified hierarchical Skill and Competency discovery is unavailable."));
                    return group;
                }
                const plain: ChoiceRequirement = { ...requirement, options: requirement.options.map(o => ({ ...o, children: undefined,
                    unavailableReason: o.unavailableReason ?? (requirement.maximum !== 1 &&
                        requirement.selectedIds.length >= (requirement.maximum ?? Infinity) &&
                        !requirement.selectedIds.includes(o.id) ? "Required count reached" : undefined) })) };
                const section = training ? renderTrainingChoice(plain, context.trainingNodes!, disabled, actions) :
                    renderChoice(plain, disabled, actions);
                for (const option of requirement.options) if (requirement.selectedIds.includes(option.id))
                    for (const child of option.children ?? []) section.append(render(child));
                return section;
            };
            if (context.decisionId === "benefits") for (const requirement of choiceTree(preview.choices, draft, context.catalog))
                work.append(render(requirement));
            for (const key of reconsiderChoices(draft, preview)) work.append(createButton(
                "Remove reviewed choice: " + key.split("/").map(id => labelFor(context.catalog, id)).join(" — "),
                secondary, () => save(removeReviewedChoice(draft, key)), disabled));
            work.append(renderValidation(context.preview!.issues.map(issue => issue.decision?.id === "equipment" ?
                { ...issue, decision: undefined } : issue), context.navigate));
            if (context.decisionId === "review") {
                work.append(createElement("p", undefined, "Review is nonauthoritative. Whole-character training and earned benefit verification remain required."),
                    renderFacts(preview.choices.map(choice => ({
                        label: labelFor(context.catalog, choice.key.split("/").at(-1)!),
                        value: (draft.selections[choice.key] ?? []).map(id => labelFor(context.catalog, id)).join(", ") ||
                            "Required — not selected",
                        source: { label: labelFor(context.catalog, choice.key.split("/")[0]!) }
                    }))),
                    renderExperienceEquipment({ evaluation: context.preview!, catalog: context.catalog, draft: context.draft!, readOnly: true }));
            }
        }
    }
    work.append(createButton("Continue to Equipment intentions", secondary, context.openEquipment));
    const summary = preview ? renderExperienceBenefits(preview, context.catalog) : createSectionCard("Candidate benefits");
    if (!preview) summary.append(createElement("p", undefined, "Awaiting current native evaluation."));
    return renderWorkspace(work, browser, summary);
}
