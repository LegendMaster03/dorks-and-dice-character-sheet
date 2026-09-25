import {
    formatMechanicalValue,
    type CompetencyPresentationItem,
    type CompetencyView
} from "./character-mechanics.js";
import { createButton, createElement, createSectionCard } from "./components.js";
import { renderSourceAttributionDisclosure } from "./source-attribution.js";
import type { CompetencyRankControlOptions } from "./skills.js";

export function renderCompetenciesCard(
    items: readonly CompetencyPresentationItem[] | null,
    control: CompetencyRankControlOptions = {}
): HTMLElement {
    const card = createSectionCard(
        "Competencies",
        "dd-support-card dd-competencies-card");
    card.setAttribute("data-competency-card", "competencies");

    if (items === null) {
        card.setAttribute("data-competencies-state", "unavailable");
        card.append(renderPlaceholder());
        return card;
    }

    card.setAttribute("data-competencies-state", "resolved");
    if (items.length === 0) {
        card.append(renderPlaceholder());
        return card;
    }

    const controls = createElement("div", "dd-competency-controls");
    const search = createElement("input", "dd-competency-search");
    search.type = "search";
    search.placeholder = "Search competencies";
    search.setAttribute("aria-label", "Search competencies");
    controls.append(search);

    const list = createElement("div", "dd-competency-list");
    const rendered = items.map((item, index) => {
        const element = renderPresentationItem(item, index, control);
        list.append(element);
        return {
            element,
            searchText: competencySearchText(item)
        };
    });

    const noMatches = createElement(
        "p",
        "dd-competency-no-matches",
        "No competencies match this search.");
    noMatches.hidden = true;

    search.addEventListener("input", () => {
        const query = search.value.trim().toLowerCase();
        let visible = 0;
        for (const item of rendered) {
            const matches = query.length === 0 || item.searchText.includes(query);
            item.element.hidden = !matches;
            if (matches) visible++;
        }
        noMatches.hidden = visible !== 0;
    });

    card.append(controls, list, noMatches);
    return card;
}

function renderPresentationItem(
    item: CompetencyPresentationItem,
    index: number,
    control: CompetencyRankControlOptions
): HTMLElement {
    if (item.kind === "standalone") {
        return renderCompetencyDisclosure(item.competency, control);
    }

    if (item.kind === "family") {
        const disclosure = createElement(
            "details",
            "dd-competency-disclosure dd-competency-disclosure--family");
        disclosure.setAttribute("data-competency-family", item.parent.key);

        const summary = createElement("summary", "dd-competency-disclosure__summary");
        summary.append(renderSummaryRow(
            item.parent,
            `${item.members.length} ${item.members.length === 1 ? "member" : "members"}`));
        disclosure.append(summary);

        const body = createElement("div", "dd-competency-disclosure__body");
        body.append(createElement(
            "h4",
            "dd-competency-detail-section__title",
            "Competency members"));
        const members = createElement("div", "dd-competency-members");
        for (const member of item.members) {
            members.append(renderCompetencyDisclosure(member, control));
        }
        if (item.members.length === 0) {
            members.append(createElement(
                "p",
                "dd-competency-empty",
                "No members are available from the current rules projection."));
        }
        body.append(members);
        disclosure.append(body);
        return disclosure;
    }

    const disclosure = createElement(
        "details",
        "dd-competency-disclosure dd-competency-disclosure--composite");
    disclosure.setAttribute("data-composite-competency", item.parent.key);
    disclosure.setAttribute("data-composite-index", String(index));

    const summary = createElement("summary", "dd-competency-disclosure__summary");
    summary.append(renderSummaryRow(
        item.parent,
        `${item.components.length} ${item.components.length === 1 ? "component" : "components"}`));
    disclosure.append(summary);

    const body = createElement("div", "dd-competency-disclosure__body");
    const components = createElement("section", "dd-competency-detail-section");
    components.append(createElement(
        "h4",
        "dd-competency-detail-section__title",
        "Components"));
    const componentList = createElement("div", "dd-competency-components");
    for (const component of item.components) {
        componentList.append(renderSummaryRow(component));
    }
    components.append(componentList);
    body.append(components);

    appendRelationshipFacts(body, item.relationship);
    appendCompetencyDetails(body, item.parent, control);

    disclosure.append(body);
    return disclosure;
}

function renderCompetencyDisclosure(
    competency: CompetencyView,
    control: CompetencyRankControlOptions
): HTMLElement {
    if (!hasCompetencyBreakout(competency) && !canEditRank(competency, control)) {
        const row = renderSummaryRow(competency);
        row.className += " dd-competency-row--standalone";
        return row;
    }

    const disclosure = createElement(
        "details",
        "dd-competency-disclosure dd-competency-disclosure--standalone");
    disclosure.setAttribute("data-competency-disclosure", competency.key);

    const summary = createElement("summary", "dd-competency-disclosure__summary");
    summary.append(renderSummaryRow(competency));
    disclosure.append(summary);

    const body = createElement("div", "dd-competency-disclosure__body");
    body.append(createElement(
        "h4",
        "dd-competency-breakout-title",
        "Competency details"));
    appendCompetencyDetails(body, competency, control);
    disclosure.append(body);
    return disclosure;
}

function renderSummaryRow(
    competency: CompetencyView,
    trailingText?: string
): HTMLElement {
    const row = createElement("span", "dd-competency-row");
    row.setAttribute("data-competency-row", "true");
    row.setAttribute("data-competency-id", competency.key);
    if (competency.kind !== undefined) {
        row.setAttribute("data-competency-kind", competency.kind);
    }

    row.append(
        renderTrainingMarker(competency),
        createElement("span", "dd-competency-row__name", competency.label));

    const state = trailingText ?? summaryState(competency);
    row.append(createElement("span", "dd-competency-row__state", state));
    return row;
}

function summaryState(competency: CompetencyView): string {
    const training = competency.training?.trim();
    if (training !== undefined && training.length > 0) {
        return training;
    }
    if (competency.supportsTrainingState === true) {
        return "Training -";
    }

    const value = formatMechanicalValue(competency);
    return value === "-" ? "" : value;
}

function renderTrainingMarker(competency: CompetencyView): HTMLElement {
    const training = competency.training?.trim();
    if (training === undefined || training.length === 0) {
        const marker = createElement(
            "span",
            "dd-competency-row__training dd-competency-row__training--unresolved",
            "");
        marker.setAttribute("aria-label", `${competency.label} training unresolved`);
        marker.title = "Training state is not resolved.";
        return marker;
    }

    const state = classifyTraining(training);
    const marker = createElement(
        "span",
        `dd-competency-row__training dd-competency-row__training--${state}`,
        state === "other" ? abbreviateTraining(training) : "");
    marker.setAttribute("aria-label", `${competency.label} training: ${training}`);
    marker.title = training;
    return marker;
}

type TrainingState = "none" | "proficient" | "expertise" | "other";

function classifyTraining(training: string): TrainingState {
    const normalized = training
        .trim()
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, " ")
        .replace(/\s+/g, " ")
        .trim();

    if (/\b(?:expertise|expert|mastery|master)\b/.test(normalized)
        || normalized.includes("double proficiency")
        || normalized.includes("enhanced proficiency")) {
        return "expertise";
    }
    if (normalized === "none"
        || normalized.includes("not proficient")
        || normalized.includes("no proficiency")
        || /\buntrained\b/.test(normalized)) {
        return "none";
    }
    if (/\bproficient\b/.test(normalized)
        || /\btrained\b/.test(normalized)) {
        return "proficient";
    }
    return "other";
}

function abbreviateTraining(training: string): string {
    const words = training.trim().split(/\s+/).filter(Boolean);
    return words.length > 1
        ? words.map(word => word[0] ?? "").join("").slice(0, 2).toUpperCase()
        : training.slice(0, 2).toUpperCase();
}

function appendCompetencyDetails(
    body: HTMLElement,
    competency: CompetencyView,
    control: CompetencyRankControlOptions
): void {
    const stateFacts = createElement("dl", "dd-competency-facts");
    appendFact(
        stateFacts,
        "Training",
        competency.training ?? (competency.supportsTrainingState === true ? "-" : undefined));
    appendFact(
        stateFacts,
        "Ranks",
        competency.ranks === undefined
            ? competency.supportsRanks === true ? "-" : undefined
            : String(competency.ranks));
    appendFact(stateFacts, "Ability", competency.governingAbility);
    if (competency.classSkill !== undefined) {
        appendFact(stateFacts, "Class skill", competency.classSkill ? "Yes" : "No");
    } else if (competency.supportsClassSkillState === true) {
        appendFact(stateFacts, "Class skill", "-");
    }
    if (competency.trainedOnly !== undefined) {
        appendFact(stateFacts, "Trained only", competency.trainedOnly ? "Yes" : "No");
    }
    if (competency.armorCheckPenalty !== undefined) {
        appendFact(
            stateFacts,
            "Armor Check Penalty",
            competency.armorCheckPenalty.applies
                ? competency.armorCheckPenalty.formattedEffect ?? "Applies"
                : "Does not apply");
    }
    appendFact(stateFacts, "Family", competency.family);
    appendFact(stateFacts, "Specialty", competency.specialty);
    for (const contribution of competency.breakdown ?? []) {
        appendFact(stateFacts, contribution.label, formatMechanicalValue(contribution));
    }
    for (const related of competency.relatedValues ?? []) {
        appendFact(stateFacts, related.label, formatMechanicalValue(related));
    }
    appendDetailSection(body, "State", stateFacts);

    if ((competency.facets?.length ?? 0) > 0) {
        const facets = createElement("div", "dd-competency-facets");
        for (const facet of competency.facets ?? []) {
            const facetRow = createElement("article", "dd-competency-facet");
            facetRow.append(createElement(
                "strong",
                "dd-competency-facet__name",
                humanizeToken(facet.facetType)));

            const capabilities = [
                facet.supportsRanks ? "ranks" : null,
                facet.supportsClassSkillState ? "class-skill state" : null,
                facet.supportsTrainingState ? "training" : null
            ].filter((value): value is string => value !== null);
            if (capabilities.length > 0) {
                facetRow.append(createElement(
                    "span",
                    "dd-competency-facet__capabilities",
                    capabilities.join(" · ")));
            }
            facets.append(facetRow);
        }
        appendSection(body, "Implementations", facets);
    }

    if ((competency.relatedCompetencies?.length ?? 0) > 0) {
        const relations = createElement("div", "dd-competency-relations");
        for (const relation of competency.relatedCompetencies ?? []) {
            const row = createElement("div", "dd-competency-relation");
            row.append(
                createElement("strong", "dd-competency-relation__name", relation.targetName),
                createElement(
                    "span",
                    "dd-competency-relation__kind",
                    relation.scope === undefined
                        ? humanizeToken(relation.kind)
                        : `${humanizeToken(relation.kind)} · ${humanizeToken(relation.scope)}`));
            if (relation.sharesTrainingState === true) {
                row.append(createElement(
                    "span",
                    "dd-competency-relation__training",
                    "Shares training state"));
            }
            relations.append(row);
        }
        appendSection(body, "Related competencies", relations);
    }

    const rankEditor = renderRankEditor(competency, control);
    if (rankEditor !== null) {
        appendSection(body, "Ranks", rankEditor);
    }

    const sources = renderSourceAttributionDisclosure(competency.sourceAttributions);
    if (sources !== null) body.append(sources);
}

function appendRelationshipFacts(
    body: HTMLElement,
    relationship: Extract<CompetencyPresentationItem, { kind: "composite" }>["relationship"]
): void {
    const facts = createElement("dl", "dd-competency-facts");
    appendFact(
        facts,
        "Method",
        relationship.composition === undefined
            ? undefined
            : humanizeToken(relationship.composition));
    appendFact(
        facts,
        "Resolution",
        relationship.resolutionKind === undefined
            ? undefined
            : humanizeToken(relationship.resolutionKind));
    appendDetailSection(body, "Relationship", facts);
}

function appendDetailSection(
    body: HTMLElement,
    title: string,
    facts: HTMLElement
): void {
    if (facts.children.length === 0) return;
    appendSection(body, title, facts);
}

function appendSection(body: HTMLElement, title: string, content: HTMLElement): void {
    const section = createElement("section", "dd-competency-detail-section");
    section.append(
        createElement("h4", "dd-competency-detail-section__title", title),
        content);
    body.append(section);
}

function appendFact(list: HTMLElement, label: string, value: string | undefined): void {
    if (value === undefined || value.trim().length === 0) return;
    const row = createElement("div", "dd-competency-fact");
    row.append(
        createElement("dt", "dd-competency-fact__term", label),
        createElement("dd", "dd-competency-fact__value", value));
    list.append(row);
}

function renderRankEditor(
    competency: CompetencyView,
    control: CompetencyRankControlOptions
): HTMLElement | null {
    if (!canEditRank(competency, control)) return null;
    const mutationKey = competency.rankInputKey?.trim();
    if (mutationKey === undefined || mutationKey.length === 0) return null;

    const editor = createElement("div", "dd-competency-rank-editor");
    editor.setAttribute("data-competency-rank-editor", competency.key);
    editor.setAttribute("data-competency-rank-input", mutationKey);

    const input = createElement("input", "dd-sheet-screen__input");
    input.type = "number";
    input.step = "1";
    input.min = "0";
    input.max = "2147483647";
    input.inputMode = "numeric";
    input.value = typeof competency.ranks === "number"
        && Number.isSafeInteger(competency.ranks)
        ? String(competency.ranks)
        : "";
    input.setAttribute("aria-label", `${competency.label} ranks`);
    input.addEventListener("input", () => input.setCustomValidity(""));

    const pending = control.savingKey !== null && control.savingKey !== undefined;
    const saving = control.savingKey === mutationKey;
    const actions = createElement("div", "dd-competency-rank-editor__actions");
    actions.append(createButton(
        saving ? "Saving…" : "Save",
        "dd-button dd-button--secondary",
        () => {
            const parsed = parseRank(input.value);
            if (parsed === null) {
                input.setCustomValidity(
                    "Ranks must be a whole number from 0 through 2147483647.");
                input.reportValidity();
                return;
            }
            input.setCustomValidity("");
            control.onSetRank!(mutationKey, parsed);
        },
        pending));

    if (typeof competency.ranks === "number"
        && Number.isSafeInteger(competency.ranks)
        && control.onClearRank !== undefined) {
        actions.append(createButton(
            "Clear",
            "dd-button dd-button--ghost",
            () => control.onClearRank!(mutationKey),
            pending));
    }

    editor.append(input, actions);
    return editor;
}

function canEditRank(
    competency: CompetencyView,
    control: CompetencyRankControlOptions
): boolean {
    return competency.supportsRanks === true
        && competency.rankInputKey !== undefined
        && competency.rankInputKey.trim().length > 0
        && control.readOnly !== true
        && control.onSetRank !== undefined;
}

function parseRank(value: string): number | null {
    const normalized = value.trim();
    if (!/^\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed)
        && parsed >= 0
        && parsed <= 2147483647
        ? parsed
        : null;
}

function hasCompetencyBreakout(competency: CompetencyView): boolean {
    return competency.ranks !== undefined
        || competency.training !== undefined
        || competency.classSkill !== undefined
        || competency.trainedOnly !== undefined
        || competency.armorCheckPenalty !== undefined
        || competency.family !== undefined
        || competency.specialty !== undefined
        || competency.supportsRanks === true
        || competency.supportsClassSkillState === true
        || competency.supportsTrainingState === true
        || (competency.facets?.length ?? 0) > 0
        || (competency.relatedCompetencies?.length ?? 0) > 0
        || (competency.breakdown?.length ?? 0) > 0
        || (competency.relatedValues?.length ?? 0) > 0
        || (competency.sourceAttributions?.length ?? 0) > 0;
}

function competencySearchText(item: CompetencyPresentationItem): string {
    const values = item.kind === "standalone"
        ? [item.competency]
        : item.kind === "family"
            ? [item.parent, ...item.members]
            : [item.parent, ...item.components];

    return values
        .flatMap(value => [
            value.label,
            value.training,
            value.governingAbility,
            value.family,
            value.specialty,
            ...(value.facets ?? []).map(facet => facet.facetType),
            ...(value.relatedCompetencies ?? []).map(related => related.targetName)
        ])
        .filter((value): value is string => value !== undefined && value.trim().length > 0)
        .join(" ")
        .toLowerCase();
}

function humanizeToken(value: string): string {
    const normalized = value
        .trim()
        .replace(/[_-]+/g, " ")
        .replace(/\s+/g, " ");
    return normalized.length === 0
        ? value
        : normalized[0].toUpperCase() + normalized.slice(1);
}

function renderPlaceholder(): HTMLElement {
    const list = createElement("div", "dd-competency-list");
    const row = createElement("div", "dd-competency-row dd-competency-row--placeholder");
    row.append(createElement("span", "dd-competency-row__state", "-"));
    list.append(row);
    return list;
}
