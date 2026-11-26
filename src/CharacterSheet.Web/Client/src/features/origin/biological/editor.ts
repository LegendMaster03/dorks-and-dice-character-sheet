/** Method-specific workspace. The shared controller owns navigation, canonical
 * draft writes, revision conflicts, and authorization; this editor owns no grants. */
import { createButton, createElement, createSectionCard } from "../../../ui/components.js";
import { renderChoice, renderDecisionNavigation, renderDependencyReview,
    renderDraftStatus, renderFacts, renderValidation, renderWorkspace } from "../../guided/components.js";
import { isCurrentEvaluation, type EditorContext, type MethodEditor, type NativePreset } from "../../guided/contracts.js";
import type { NativeOriginDraftProposal } from "../origin-api.js";
import { addNativeContributor, biologicalReference, evaluateBiological, nativeContributorAllowed,
    newBiologicalProposal, record, removeContributor, setExpression, setFiniteChoice, setInherited,
    composableOffers, setComposition, clearComposition,
    supportsBiologicalDraft, type BiologicalEvaluation, type BiologicalProposal, type Offer, type CharacteristicGroup } from "./model.js";

export interface BiologicalEditorContext extends EditorContext<NativeOriginDraftProposal, unknown> {
    /** The preview result must contain the complete native preview envelope,
     * including top-level allocation, rather than only its construction result. */
    readonly presets: readonly NativePreset[];
    readonly sourceLabel?: (id: string) => string | undefined;
    readonly characteristicLabel?: (id: string) => string | undefined;
    readonly characteristicDescription?: (id: string) => string | undefined;
    readonly characteristicCategory?: (id: string) => string | undefined;
    /** View state only: retain disclosure/search across canonical draft writes. */
    readonly characteristicBrowser?: { query: string; expanded: Map<string, boolean> };
}
const decisions = [
    { id: "contributors", label: "Contributors" },
    { id: "inheritance", label: "Inheritance & Expression" },
    { id: "configuration", label: "Configuration" },
    { id: "review", label: "Review" }
] as const;
const buttonClass = "dd-button dd-button--secondary";
const human = (id: string) => id.replace(/^(species|ancestry|trait|creature-type)\./, "")
    .replace(/([a-z])([A-Z])/g, "$1 $2").replace(/[.-]/g, " ")
    .replace(/\b\w/g, c => c.toUpperCase());
const name = (context: BiologicalEditorContext, id: string) =>
    context.characteristicLabel?.(id) ?? human(id);
const sourceName = (context: BiologicalEditorContext, id: string) =>
    context.sourceLabel?.(id) ?? context.presets.find(p => p.id === id)?.name ?? human(id);
function valueText(value: unknown, depth = 0): string {
    if (depth > 3 || value == null) return "";
    if (typeof value === "string") return human(value);
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    if (Array.isArray(value)) return value.map(v => valueText(v, depth + 1)).filter(Boolean).join(", ");
    if (!record(value)) return "";
    return Object.entries(value).filter(([k]) => !["id", "source", "occurrence", "authorization"].includes(k))
        .map(([k,v]) => { const label = valueText(v, depth + 1); return label ? human(k) + ": " + label : ""; })
        .filter(Boolean).join("; ");
}
function contributorLabel(context: BiologicalEditorContext, proposal: BiologicalProposal, offer: Offer): string {
    const contributor = proposal.contributors.find(c => record(c) && c.id === offer.contributorId);
    const index = proposal.contributors.indexOf(contributor);
    const id = record(contributor) && record(contributor.source) && typeof contributor.source.id === "string" ?
        contributor.source.id : offer.sourceId;
    return "Contributor " + (index + 1) + " — " + sourceName(context, id);
}
function renderContributors(context: BiologicalEditorContext, proposal: BiologicalProposal,
    evaluation: BiologicalEvaluation, locked: boolean, save: (p: BiologicalProposal) => void): HTMLElement {
    const section = createSectionCard("Biological contributors");
    section.append(createElement("p", undefined, "Select native Species or Ancestry exemplars. Each contributor is a distinct source occurrence, even if two contributors use the same source."));
    const review = createElement("div");
    const picker = createElement("select");
    picker.setAttribute("aria-label", "Native biological contributor source");
    const query = createElement("input");
    query.type = "search";
    query.setAttribute("aria-label", "Search Species and Ancestries");
    function pathLabel(preset: NativePreset): string {
        const result = [preset.name];
        const seen = new Set([preset.id]);
        let parent = preset.parentId;
        while (parent && !seen.has(parent)) {
            seen.add(parent);
            const found = context.presets.find(p => p.id === parent);
            if (!found) break;
            result.unshift(found.name);
            parent = found.parentId;
        }
        return result.join(" / ");
    }
    function populate(): void {
        const previous = picker.value;
        picker.replaceChildren();
        const placeholder = createElement("option", undefined, "Choose a source");
        placeholder.value = "";
        picker.append(placeholder);
        const term = query.value.trim().toLowerCase();
        for (const preset of context.presets) {
            const label = pathLabel(preset);
            if (term && !label.toLowerCase().includes(term)) continue;
            if (!["species", "ancestry"].includes(preset.kind.toLowerCase())) continue;
            const option = createElement("option", undefined, label);
            option.value = preset.id;
            option.disabled = !nativeContributorAllowed(preset);
            picker.append(option);
        }
        picker.value = [...picker.options].some(o => o.value === previous) ? previous : "";
    }
    populate();
    query.addEventListener("input", populate);
    query.disabled = locked;
    picker.disabled = locked;
    function selectedPreset(): NativePreset | undefined {
        return context.presets.find(p => p.id === picker.value && nativeContributorAllowed(p));
    }
    for (const [index, contributor] of proposal.contributors.entries()) {
        if (!record(contributor) || !record(contributor.source) || typeof contributor.id !== "string" ||
            typeof contributor.source.id !== "string") continue;
        const id = contributor.id;
        const native = contributor.source.kind === 0 || contributor.source.kind === "Native";
        const card = createElement("article", "dd-build-choice");
        card.append(createElement("h3", undefined, "Contributor " + (index + 1)),
            createElement("p", undefined, native ? sourceName(context, contributor.source.id) : "Existing character reference"),
            createElement("p", undefined, native ? "Native catalog exemplar" : "Host verification required"));
        const offers = evaluation.groups.flatMap(g => g.offers).filter(o => o.contributorId === id);
        if (evaluation.current) card.append(createElement("p", undefined, offers.length + " eligible hereditary offers"));
        function change(replacement: NativePreset | undefined): void {
            const result = removeContributor(proposal, evaluation, id);
            const next = replacement ? addNativeContributor(result.proposal, "source-" + crypto.randomUUID(), replacement) : result.proposal;
            review.replaceChildren(renderDependencyReview(result.change,
                () => save(next), () => review.replaceChildren(), locked));
        }
        card.append(createButton("Remove contributor", buttonClass, () => change(undefined), locked || (!evaluation.current && (proposal.biologicalTransmission.candidateIds.length > 0 || proposal.manifestations.length > 0)) || !native),
            createButton("Replace with selected source below", buttonClass, () => {
                const replacement = selectedPreset();
                if (replacement) change(replacement);
            }, locked || (!evaluation.current && (proposal.biologicalTransmission.candidateIds.length > 0 || proposal.manifestations.length > 0)) || !native));
        section.append(card);
    }
    section.append(query, picker, createButton("Add contributor", buttonClass, () => {
        const selected = selectedPreset();
        if (selected) save(addNativeContributor(proposal, "source-" + crypto.randomUUID(), selected));
    }, locked), review);
    section.append(createElement("p", undefined, "Verified existing-character contributors require the host relationship and profile verification provider. Ownership inspection alone does not authorize inheritance."));
    return section;
}
function renderOffer(context: BiologicalEditorContext, proposal: BiologicalProposal,
    evaluation: BiologicalEvaluation, offer: Offer, locked: boolean, save: (p: BiologicalProposal) => void,
    configure: boolean): HTMLElement {
    const card = createElement("article", "dd-build-choice");
    card.append(createElement("h4", undefined, contributorLabel(context, proposal, offer)),
        createElement("p", undefined, "Source: " + sourceName(context, offer.sourceId)),
        createElement("p", undefined, "State: " + human(offer.state)));
    if (offer.requestedExpression && offer.state !== "expressed")
        card.append(createElement("p", undefined, "Expression requested; not granted by current evaluation."));
    const authored = record(offer.value) ? offer.value : null;
    // Show the distinct source-bound parameters before the generic description.
    const summary = valueText(offer.parameters) || (authored ?
        [authored.spell, authored.level == null ? null : "Level " + authored.level]
            .filter(v => typeof v === "string").join(" · ") : valueText(offer.value));
    if (summary) card.append(createElement("p", undefined, summary));
    const evidence = createElement("details");
    evidence.append(createElement("summary", undefined, "Source configuration and requirements"),
        renderFacts([
            { label: "Offered configuration", value: valueText(offer.value) || "Not specified" },
            { label: "Parameters", value: valueText(offer.parameters) || "Not specified" }
        ]));
    if (offer.explanation) evidence.append(createElement("p", undefined, offer.explanation));
    card.append(evidence);
    const charges = evaluation.charges.filter(charge => Array.isArray(charge.grantIds) &&
        charge.grantIds.some(id => typeof id === "string" && offer.expressionIds.includes(id)));
    for (const charge of charges) {
        if (typeof charge.load === "number")
            card.append(createElement("p", undefined, "Evaluated charge: " + charge.load + (Array.isArray(charge.grantIds) && charge.grantIds.length > 1 ? " (shared charge)" : "") + " points"));
    }
    if (!configure) {
        const composition = proposal.relationships?.some(r => r.candidateIds.includes(offer.id));
        card.append(createButton(offer.inherited ? "Remove inherited instance" : "Inherit instance", buttonClass,
            () => save(setInherited(proposal, evaluation, offer.id, !offer.inherited)), locked),
            createButton(offer.requestedExpression ? "Withdraw expression request" : "Request expression", buttonClass,
                () => save(setExpression(proposal, evaluation, offer.id, !offer.requestedExpression)), locked || !offer.inherited || !!composition));
    }
    if (offer.inherited) {
        const details = createElement("details");
        details.open = configure;
        details.append(createElement("summary", undefined, "Configure characteristic"));
        const choices = offer.requestedExpression ?
            proposal.manifestations.find(m => m.candidateId === offer.id)?.choices :
            proposal.biologicalTransmission.latentConfigurations?.find(c => c.candidateId === offer.id)?.choices;
        for (const requirement of offer.requirements) {
            if (typeof requirement.id !== "string" || (requirement.timing !== 0 && requirement.timing !== "Creation")) continue;
            const requirementId = requirement.id;
            const selected = choices?.find(c => c.requirementId === requirementId)?.selectedOptionIds ?? [];
            const options = Array.isArray(requirement.finiteOptions) ? requirement.finiteOptions.filter((v): v is string => typeof v === "string") : [];
            if (requirement.optionCatalogId != null || !options.length) {
                details.append(createElement("p", undefined, "This choice requires native catalog eligibility or an authorized configuration provider."));
                continue;
            }
            const maximum = typeof requirement.maximum === "number" ? requirement.maximum : undefined;
            const minimum = typeof requirement.minimum === "number" ? requirement.minimum : undefined;
            details.append(renderChoice({ id: requirementId, label: human(typeof requirement.parameterKey === "string" ? requirement.parameterKey : "Required choice"),
                kind: maximum === 1 ? "finite" : "counted", selectedIds: selected, minimum, maximum,
                options: options.map(id => ({ id, label: human(id), unavailableReason: maximum !== undefined &&
                    maximum !== 1 && selected.length >= maximum && !selected.includes(id) ? "Selection limit reached" : undefined })) },
                locked, { select: (_, optionId, active) => {
                    const next = active ? maximum === 1 ? [optionId] : [...selected, optionId] : selected.filter(id => id !== optionId);
                    save(setFiniteChoice(proposal, evaluation, offer.id, requirementId, next));
                }, browseCatalog: () => {}, navigate: context.navigate }));
        }
        if (!offer.requirements.length) details.append(createElement("p", undefined, "No creation choices returned for this occurrence."));
        card.append(details);
    }
    return card;
}
function renderComposition(context: BiologicalEditorContext, proposal: BiologicalProposal,
    evaluation: BiologicalEvaluation, group: CharacteristicGroup, locked: boolean,
    save: (p: BiologicalProposal) => void): HTMLElement | null {
    const prior = proposal.resolutions?.find(r => r.relationshipId === "guided-compose:" + group.id);
    const offers = composableOffers(group);
    if (offers.length < 2 && !prior) return null;
    const panel = createElement("details", "dd-guided-detail");
    panel.open = !!prior;
    panel.append(createElement("summary", undefined, "Combine inherited configurations"),
        createElement("p", undefined, "Choose a base expression and the source of each native recombinable parameter. The underlying genes remain separate; Rules Core validates the single proposed phenotype."));
    if (prior) panel.append(createButton("Clear combined expression", buttonClass,
        () => save(clearComposition(proposal, group.id)), locked));
    if (offers.length < 2) {
        panel.append(createElement("p", undefined, "Current native composition options are unavailable. Review or clear the previous request."));
        return panel;
    }
    const base = createElement("select");
    base.setAttribute("aria-label", "Base expression source");
    const addSources = (select: HTMLSelectElement, parameter?: string) => {
        for (const offer of offers) {
            const option = createElement("option", undefined, contributorLabel(context, proposal, offer) +
                (parameter && record(offer.parameters) ? " — " + valueText(offer.parameters[parameter]) : ""));
            option.value = offer.id; select.append(option);
        }
        select.disabled = locked;
    };
    addSources(base);
    base.value = prior?.replacementCandidateId ?? offers.find(o => o.requestedExpression)?.id ?? offers[0]!.id;
    if (!base.value) base.value = offers[0]!.id;
    const baseLabel = createElement("label", undefined, "Base expression source "); baseLabel.append(base);
    panel.append(baseLabel);
    const selections = new Map<string, HTMLSelectElement>();
    for (const key of group.presentation?.recombinableParameters ?? []) {
        const label = createElement("label", undefined, human(key) + " source ");
        const select = createElement("select"); select.setAttribute("aria-label", human(key) + " source");
        addSources(select, key);
        select.value = prior?.replacementParameterSources[key] ?? base.value;
        if (!select.value) select.value = base.value;
        selections.set(key, select); label.append(select); panel.append(label);
    }
    const error = createElement("p"); error.setAttribute("role", "alert");
    panel.append(createButton("Apply combined expression", buttonClass, () => {
        try {
            error.textContent = "";
            save(setComposition(proposal, evaluation, group.id, base.value,
                Object.fromEntries([...selections].map(([key, select]) => [key, select.value]))));
        } catch (e) { error.textContent = e instanceof Error ? e.message : "Native composition unavailable."; }
    }, locked), error);
    return panel;
}

function renderCharacteristics(context: BiologicalEditorContext, proposal: BiologicalProposal,
    evaluation: BiologicalEvaluation, locked: boolean, save: (p: BiologicalProposal) => void,
    configure: boolean): HTMLElement {
    const section = createSectionCard(configure ? "Configuration" : "Inheritance & Expression");
    if (!evaluation.current) {
        section.append(createElement("p", undefined, "A current native evaluation is required to edit hereditary offers."),
            createButton("Refresh evaluation", buttonClass, () => void context.retry()));
        return section;
    }
    const query = createElement("input");
    query.type = "search";
    query.setAttribute("aria-label", "Search characteristics");
    query.placeholder = "Search characteristics";
    const browser = context.characteristicBrowser;
    query.value = browser?.query ?? "";
    const groups = createElement("div", "dd-biological-groups");
    function populate(): void {
        groups.replaceChildren();
        const term = query.value.trim().toLowerCase();
        const categories = new Map<string, HTMLElement>();
        for (const group of evaluation.groups) {
            const authored = group.offers.map(o => o.value).find(v => record(v) && typeof v.name === "string");
            const title = group.presentation?.name ?? context.characteristicLabel?.(group.id) ??
                (record(authored) && typeof authored.name === "string" ? authored.name : name(context, group.id));
            const offers = group.offers.filter(o => !configure || o.inherited);
            if (!offers.length || (term && ![title, ...offers.map(o => contributorLabel(context, proposal, o))].some(s => s.toLowerCase().includes(term)))) continue;
            const category = group.presentation?.category ?? context.characteristicCategory?.(group.id) ??
                (group.kind === "Intrinsic" || group.kind === "0" ? "Physical characteristics" : "Other characteristics");
            let area = categories.get(category);
            if (!area) {
                area = createElement("section");
                area.append(createElement("h3", undefined, category));
                categories.set(category, area);
                groups.append(area);
            }
            const detail = createElement("details", "dd-biological-gene");
            const key = (configure ? "configure:" : "inherit:") + group.id;
            const inheritedCount = offers.filter(o => o.inherited).length;
            const expressedCount = offers.filter(o => o.state === "expressed").length;
            detail.open = !!term || (browser?.expanded.get(key) ?? (configure || inheritedCount > 0));
            detail.append(createElement("summary", undefined, title + " — " + inheritedCount +
                (evaluation.maxInherited == null ? "" : "/" + evaluation.maxInherited) + " inherited" +
                (expressedCount ? " · " + group.expressions.length +
                    (evaluation.maxExpressed == null ? "" : "/" + evaluation.maxExpressed) + " effective expression" +
                    (group.expressions.length === 1 ? "" : "s") : "")));
            detail.addEventListener("toggle", () => {
                if (!term && detail.isConnected) browser?.expanded.set(key, detail.open);
            });
            const description = group.presentation?.description ?? context.characteristicDescription?.(group.id) ??
                (record(authored) && typeof authored.description === "string" ? authored.description : undefined);
            if (description) detail.append(createElement("p", undefined, description));
            for (const offer of offers) detail.append(renderOffer(context, proposal, evaluation, offer, locked, save, configure));
            const composition = renderComposition(context, proposal, evaluation, group, locked, save);
            if (composition) detail.append(composition);
            if (group.expressions.length) {
                const effects = createElement("details");
                effects.append(createElement("summary", undefined, "Evaluated consequences — preview only"));
                for (const grant of group.expressions) {
                    effects.append(createElement("p", undefined, valueText(grant.parameters) || valueText(grant.effectiveValue) ||
                        "Native effective characteristic"));
                    if (record(grant.parameterSources)) effects.append(renderFacts(Object.entries(grant.parameterSources).map(([key, id]) => {
                        const source = group.offers.find(o => o.id === id);
                        return { label: human(key), value: source ? contributorLabel(context, proposal, source) : "Native source evidence" };
                    })));
                }
                detail.append(effects);
            }
            area.append(detail);
        }
        if (!categories.size) groups.append(createElement("p", undefined, "No matching hereditary offers."));
    }
    query.addEventListener("input", () => { if (browser) browser.query = query.value; populate(); });
    populate();
    section.append(query, groups);
    return section;
}
export function renderBiologicalEditor(context: BiologicalEditorContext): HTMLElement {
    const section = createSectionCard("Biological Origination", "dd-custom-origin");
    const error = createElement("p");
    error.setAttribute("role", "alert");
    const draft = context.draft;
    const proposal = draft?.proposal;
    const locked = context.readOnly || !!draft?.readOnly || draft?.saveStatus === "saving" ||
        draft?.saveStatus === "conflict" || draft?.saveStatus === "stale" || draft?.saveStatus === "failed";
    let inFlight = false;
    const save = (next: BiologicalProposal) => {
        if (locked || inFlight) return;
        inFlight = true;
        error.textContent = "";
        void context.save(next).catch(() => {
            error.textContent = "Draft save failed. Review the shared draft status before retrying.";
        }).finally(() => { inFlight = false; });
    };
    section.append(renderDecisionNavigation(decisions, context.decisionId, context.navigate),
        renderDraftStatus(draft?.saveStatus ?? "unavailable"), error);
    if (!proposal) {
        section.append(createButton("Start biological draft", buttonClass,
            () => save(newBiologicalProposal("initial-" + context.characterId)), locked));
        return section;
    }
    if (!supportsBiologicalDraft(proposal)) {
        section.append(createElement("p", undefined, "This saved Origin uses another or older construction contract. Existing evidence is preserved. Use an authorized migration or method switch."));
        return section;
    }
    const current = !!draft && !!context.preview && isCurrentEvaluation(draft, context.preview);
    const evaluation = evaluateBiological(proposal, context.preview?.result, current);
    const allocation = createElement("div", "dd-origin-allocation");
    allocation.append(renderFacts([
        { label: "Used", value: evaluation.allocation.used ?? "Unresolved" },
        { label: "Allowance", value: evaluation.allocation.allowance ?? "Unresolved" },
        { label: "Remaining", value: evaluation.allocation.remaining ?? "Unresolved" }
    ]));
    allocation.append(createElement("p", undefined, evaluation.allocation.detail ??
        (evaluation.allocation.status === "within" ? "Within allocation" : "Native allocation unavailable")));
    if (evaluation.maxInherited != null && evaluation.maxExpressed != null)
        allocation.append(createElement("p", undefined, "Per characteristic: " + evaluation.maxInherited +
            " inherited instances; " + evaluation.maxExpressed + " effective expressions."));
    section.append(allocation);
    let work: HTMLElement;
    switch (context.decisionId) {
        case "inheritance": work = renderCharacteristics(context, proposal, evaluation, locked, save, false); break;
        case "configuration": work = renderCharacteristics(context, proposal, evaluation, locked, save, true); break;
        case "review":
            work = createSectionCard("Biological review");
            work.append(createElement("p", undefined, "This is a proposed configuration, not an established character."),
                renderFacts([{label: "Contributors", value: String(proposal.contributors.length)},
                    {label: "Requested inherited instances", value: String(proposal.biologicalTransmission.candidateIds.length)},
                    {label: "Requested expressions", value: String(proposal.manifestations.length)}]),
                createButton("Review inheritance", buttonClass, () => context.navigate("inheritance")),
                createButton("Review configuration", buttonClass, () => context.navigate("configuration")),
                createElement("p", undefined, "Production confirmation and finalization require the shared server lifecycle and native authorization integration."));
            break;
        default: work = renderContributors(context, proposal, evaluation, locked, save);
    }
    const preview = createSectionCard("Character preview");
    const inherited = evaluation.groups.flatMap(g => g.offers).filter(o => o.inherited);
    preview.append(renderFacts([
        { label: "Inherited", value: String(inherited.length) },
        { label: "Expressed", value: String(inherited.filter(o => o.state === "expressed").length) },
        { label: "Latent", value: String(inherited.filter(o => o.state === "latent").length) },
        { label: "Unresolved", value: String(inherited.filter(o => o.state === "unresolved").length) }
    ]));
    for (const group of evaluation.groups.filter(g => g.expressions.length)) {
        const effects = createElement("details");
        effects.append(createElement("summary", undefined, name(context, group.id)));
        for (const grant of group.expressions) effects.append(createElement("p", undefined, valueText(grant.effectiveValue)));
        preview.append(effects);
    }
    const validation = createElement("details");
    validation.open = context.decisionId === "review";
    validation.append(createElement("summary", undefined, evaluation.issues.length + " validation issues"),
        renderValidation(evaluation.issues, context.navigate));
    preview.append(validation);
    if (!evaluation.current) preview.append(createElement("p", undefined, "Current native results are unavailable."));
    section.append(renderWorkspace(work, undefined, preview));
    if (locked && draft?.saveStatus === "failed")
        section.append(createButton("Retry through shared draft controller", buttonClass, () => void context.retry()));
    return section;
}
export const biologicalEditor: MethodEditor<BiologicalEditorContext> = {
    reference: biologicalReference, decisions, render: renderBiologicalEditor
};
