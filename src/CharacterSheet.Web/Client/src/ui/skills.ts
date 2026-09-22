import {
    formatMechanicalValue,
    type CompetencyPresentationItem,
    type CompetencyView
} from "./character-mechanics.js";
import { createElement, createSectionCard } from "./components.js";
import { renderSourceAttributionDisclosure } from "./source-attribution.js";

export function renderSkillsCard(items: readonly CompetencyPresentationItem[] | null): HTMLElement {
    const card = createSectionCard("Skills & Competencies", "dd-support-card dd-skills-card");

    if (items === null) {
        card.setAttribute("data-skills-state", "unavailable");
        card.append(renderUnavailableSkillValue());
        return card;
    }

    card.setAttribute("data-skills-state", "resolved");
    if (items.length === 0) {
        card.append(renderUnavailableSkillValue());
        return card;
    }

    const controls = createElement("div", "dd-skills-controls");
    const search = createElement("input", "dd-skills-search");
    search.type = "search";
    search.placeholder = "Search skills";
    search.setAttribute("aria-label", "Search skills and competencies");
    controls.append(search);

    const list = createElement("div", "dd-skill-list");
    const renderedItems = items.map((item, index) => {
        const element = item.kind === "standalone"
            ? renderStandaloneCompetency(item.competency)
            : renderCompositeCompetency(item, index);
        list.append(element);
        return {
            element,
            searchText: competencySearchText(item)
        };
    });

    const noMatches = createElement(
        "p",
        "dd-skills-no-matches",
        "No skills match this search.");
    noMatches.hidden = true;

    search.addEventListener("input", () => {
        const query = search.value.trim().toLowerCase();
        let visible = 0;
        for (const item of renderedItems) {
            const matches = query.length === 0 || item.searchText.includes(query);
            item.element.hidden = !matches;
            if (matches) visible++;
        }
        noMatches.hidden = visible !== 0;
    });

    card.append(controls, list, noMatches);
    return card;
}

function renderUnavailableSkillValue(): HTMLElement {
    const list = createElement("div", "dd-skill-list");
    const row = createElement("div", "dd-skill-row dd-skill-row--placeholder");
    row.append(createElement("span", "dd-skill-row__value", "-"));
    list.append(row);
    return list;
}

function renderStandaloneCompetency(competency: CompetencyView): HTMLElement {
    if (!hasCompetencyDetails(competency)) {
        return renderCompetencyRow(competency, "standalone");
    }

    const disclosure = createElement("details", "dd-skill-disclosure dd-skill-disclosure--standalone");
    disclosure.setAttribute("data-skill-disclosure", competency.key);
    const summary = createElement("summary", "dd-skill-disclosure__summary");
    summary.append(renderCompetencyRow(competency, "standalone", undefined, "span"));
    disclosure.append(summary, renderCompetencyDetailsBody(competency));
    return disclosure;
}

function renderCompositeCompetency(
    item: Extract<CompetencyPresentationItem, { kind: "composite" }>,
    index: number
): HTMLElement {
    const disclosure = createElement("details", "dd-skill-disclosure dd-skill-disclosure--composite");
    disclosure.setAttribute("data-composite-skill", item.parent.key);

    const summary = createElement("summary", "dd-skill-disclosure__summary");
    const group = createElement("span", "dd-skill-group dd-skill-group--composite");
    group.setAttribute("role", "group");
    group.setAttribute("data-component-count", String(item.components.length));
    group.style.setProperty("--dd-skill-component-count", String(item.components.length));
    const parentLabelId = `dd-skill-group-${index}-parent-label`;
    group.setAttribute("aria-labelledby", parentLabelId);
    group.append(renderCompetencyRow(item.parent, "parent", parentLabelId, "span"));
    for (const component of item.components) {
        group.append(renderCompetencyRow(component, "component", undefined, "span"));
    }
    summary.append(group);

    disclosure.append(summary, renderCompositeDetails(item));
    return disclosure;
}

function competencySearchText(item: CompetencyPresentationItem): string {
    const competencies = item.kind === "standalone"
        ? [item.competency]
        : [item.parent, ...item.components];
    return competencies
        .flatMap(value => [
            value.label,
            value.governingAbility,
            value.training,
            value.family,
            value.specialty
        ])
        .filter((value): value is string => value !== undefined && value.trim().length > 0)
        .join(" ")
        .toLowerCase();
}

type CompetencyRelationshipRole = "standalone" | "parent" | "component";
type CompetencyRowTag = "div" | "span";

function renderCompetencyRow(
    competency: CompetencyView,
    relationship: CompetencyRelationshipRole,
    labelId?: string,
    tag: CompetencyRowTag = "div"
): HTMLElement {
    const row = createElement(
        tag,
        "dd-skill-row dd-skill-row--" + relationship + (
            relationship === "parent"
                ? " dd-skill-group__parent"
                : relationship === "component"
                    ? " dd-skill-group__component"
                    : ""
        )
    );
    row.setAttribute("data-skill-row", "true");
    row.setAttribute("data-skill-id", competency.key);
    row.setAttribute("data-skill-role", relationship);
    if (competency.kind !== undefined) row.setAttribute("data-competency-kind", competency.kind);

    const training = renderTrainingMarker(competency);
    const identity = createElement("span", "dd-skill-row__identity");
    const name = createElement("span", "dd-skill-row__name", competency.label);
    if (labelId !== undefined) name.id = labelId;
    identity.append(name);
    if (competency.governingAbility !== undefined && competency.governingAbility.trim().length > 0) {
        identity.append(createElement(
            "span",
            "dd-skill-row__ability",
            abbreviateAbility(competency.governingAbility)));
    }

    const value = createElement("span", "dd-skill-row__value", formatMechanicalValue(competency));
    row.append(training, identity, value);
    return row;
}

type TrainingMarkerState = "unresolved" | "none" | "proficient" | "expertise" | "other";

function renderTrainingMarker(competency: CompetencyView): HTMLElement {
    const training = competency.training?.trim();
    if (training === undefined || training.length === 0) {
        const marker = createElement(
            "span",
            "dd-skill-row__training dd-skill-row__training--unresolved",
            "-");
        marker.setAttribute("aria-label", `${competency.label} training unresolved`);
        marker.title = "Training state is not resolved.";
        marker.setAttribute("data-skill-training", "unresolved");
        marker.setAttribute("data-skill-training-state", "unresolved");
        return marker;
    }

    const state = classifyTrainingMarker(training);
    const marker = createElement(
        "span",
        `dd-skill-row__training dd-skill-row__training--resolved dd-skill-row__training--${state}`,
        state === "other" ? abbreviateTrainingState(training) : "");
    marker.setAttribute("aria-label", `${competency.label} training: ${training}`);
    marker.title = training;
    marker.setAttribute("data-skill-training", training);
    marker.setAttribute("data-skill-training-state", state);
    return marker;
}

function classifyTrainingMarker(training: string): TrainingMarkerState {
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

function abbreviateTrainingState(training: string): string {
    const words = training.trim().split(/\s+/).filter(Boolean);
    if (words.length > 1) {
        return words.map(word => word[0] ?? "").join("").slice(0, 4).toUpperCase();
    }
    return training.slice(0, 4).toUpperCase();
}

function renderCompositeDetails(
    item: Extract<CompetencyPresentationItem, { kind: "composite" }>
): HTMLElement {
    const body = createElement("div", "dd-skill-disclosure__body dd-skill-composite-details");

    const resolution = createElement("section", "dd-skill-detail-section dd-skill-detail-section--resolution");
    resolution.append(createElement("h4", "dd-skill-detail-section__title", "Calculation"));
    const resolutionFacts = createElement("dl", "dd-skill-details");
    for (const component of item.components) {
        appendOptionalFact(resolutionFacts, component.label, formatMechanicalValue(component));
    }
    appendOptionalFact(
        resolutionFacts,
        "Method",
        item.relationship.composition === undefined
            ? undefined
            : formatRelationshipToken(item.relationship.composition));
    appendOptionalFact(
        resolutionFacts,
        "Resolution",
        item.relationship.resolutionKind === undefined
            ? undefined
            : formatRelationshipToken(item.relationship.resolutionKind));
    appendOptionalFact(resolutionFacts, item.parent.label, formatMechanicalValue(item.parent));
    resolution.append(resolutionFacts);
    body.append(resolution);

    const competencies = [item.parent, ...item.components];
    const mechanical = competencies.filter(hasCompetencyMechanicalDetails);
    if (mechanical.length > 0) {
        const section = createElement("section", "dd-skill-detail-section");
        section.append(createElement("h4", "dd-skill-detail-section__title", "Skill mechanics"));
        const list = createElement("div", "dd-skill-mechanics-list");
        for (const competency of mechanical) {
            list.append(renderCompactCompetencyMechanics(competency));
        }
        section.append(list);
        body.append(section);
    }

    const sources = renderSourceAttributionDisclosure(collectCompetencySources(competencies));
    if (sources !== null) body.append(sources);
    return body;
}

function renderCompactCompetencyMechanics(competency: CompetencyView): HTMLElement {
    const item = createElement("article", "dd-skill-mechanics-item");
    const heading = createElement("div", "dd-skill-mechanics-item__heading");
    heading.append(
        createElement("strong", "dd-skill-mechanics-item__name", competency.label),
        createElement("span", "dd-skill-mechanics-item__value", formatMechanicalValue(competency))
    );
    item.append(heading);

    const facts = createElement("dl", "dd-skill-details dd-skill-details--compact");
    appendOptionalFact(
        facts,
        "Ability",
        competency.governingAbility === undefined
            ? undefined
            : abbreviateAbility(competency.governingAbility));
    appendOptionalFact(
        facts,
        "Ranks",
        competency.ranks === undefined
            ? competency.supportsRanks === true ? "-" : undefined
            : String(competency.ranks));
    appendOptionalFact(
        facts,
        "Training",
        competency.training ?? (competency.supportsTrainingState === true ? "-" : undefined));
    if (competency.classSkill !== undefined) {
        appendOptionalFact(facts, "Class skill", competency.classSkill ? "Yes" : "No");
    } else if (competency.supportsClassSkillState === true) {
        appendOptionalFact(facts, "Class skill", "-");
    }
    if (competency.trainedOnly !== undefined) {
        appendOptionalFact(facts, "Trained only", competency.trainedOnly ? "Yes" : "No");
    }
    if (competency.armorCheckPenalty !== undefined) {
        appendOptionalFact(
            facts,
            "Armor Check Penalty",
            competency.armorCheckPenalty.applies
                ? competency.armorCheckPenalty.formattedEffect ?? "Applies"
                : "Does not apply");
    }
    appendOptionalFact(facts, "Family", competency.family);
    appendOptionalFact(facts, "Specialty", competency.specialty);
    for (const contribution of competency.breakdown ?? []) {
        appendOptionalFact(facts, contribution.label, formatMechanicalValue(contribution));
    }
    for (const related of competency.relatedValues ?? []) {
        appendOptionalFact(facts, related.label, formatMechanicalValue(related));
    }
    if (facts.children.length > 0) item.append(facts);
    return item;
}

function collectCompetencySources(
    competencies: readonly CompetencyView[]
): NonNullable<CompetencyView["sourceAttributions"]> {
    const byKey = new Map<string, NonNullable<CompetencyView["sourceAttributions"]>[number]>();
    for (const competency of competencies) {
        for (const source of competency.sourceAttributions ?? []) {
            if (!byKey.has(source.key)) byKey.set(source.key, source);
        }
    }
    return [...byKey.values()];
}

function renderCompetencyDetailsBody(competency: CompetencyView): HTMLElement {
    const body = createElement("div", "dd-skill-disclosure__body");
    body.append(renderCompetencyDetailContent(competency));
    return body;
}

function renderCompetencyDetailContent(competency: CompetencyView): HTMLElement {
    const content = createElement("div", "dd-skill-detail-content");

    const calculation = createElement("section", "dd-skill-detail-section");
    calculation.append(createElement("h4", "dd-skill-detail-section__title", "Calculation"));
    const calculationFacts = createElement("dl", "dd-skill-details");
    appendOptionalFact(
        calculationFacts,
        "Ranks",
        competency.ranks === undefined
            ? competency.supportsRanks === true ? "-" : undefined
            : String(competency.ranks));
    for (const contribution of competency.breakdown ?? []) {
        appendOptionalFact(calculationFacts, contribution.label, formatMechanicalValue(contribution));
    }
    for (const related of competency.relatedValues ?? []) {
        appendOptionalFact(calculationFacts, related.label, formatMechanicalValue(related));
    }
    if (calculationFacts.children.length > 0) {
        calculation.append(calculationFacts);
        content.append(calculation);
    }

    const rules = createElement("section", "dd-skill-detail-section");
    rules.append(createElement("h4", "dd-skill-detail-section__title", "Skill rules"));
    const facts = createElement("dl", "dd-skill-details");
    appendOptionalFact(
        facts,
        "Training",
        competency.training ?? (competency.supportsTrainingState === true ? "-" : undefined));
    appendOptionalFact(facts, "Family", competency.family);
    appendOptionalFact(facts, "Specialty", competency.specialty);
    if (competency.classSkill !== undefined) {
        appendOptionalFact(facts, "Class skill", competency.classSkill ? "Yes" : "No");
    } else if (competency.supportsClassSkillState === true) {
        appendOptionalFact(facts, "Class skill", "-");
    }
    if (competency.trainedOnly !== undefined) {
        appendOptionalFact(facts, "Trained only", competency.trainedOnly ? "Yes" : "No");
    }
    if (competency.armorCheckPenalty !== undefined) {
        appendOptionalFact(
            facts,
            "Armor Check Penalty",
            competency.armorCheckPenalty.applies
                ? competency.armorCheckPenalty.formattedEffect ?? "Applies"
                : "Does not apply");
    }
    if (facts.children.length > 0) {
        rules.append(facts);
        content.append(rules);
    }

    const sources = renderSourceAttributionDisclosure(competency.sourceAttributions);
    if (sources !== null) content.append(sources);
    return content;
}

function hasCompetencyMechanicalDetails(competency: CompetencyView): boolean {
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
        || (competency.breakdown?.length ?? 0) > 0
        || (competency.relatedValues?.length ?? 0) > 0;
}

function hasCompetencyDetails(competency: CompetencyView): boolean {
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
        || (competency.breakdown?.length ?? 0) > 0
        || (competency.relatedValues?.length ?? 0) > 0
        || (competency.sourceAttributions?.length ?? 0) > 0;
}

function abbreviateAbility(ability: string): string {
    const normalized = ability.trim();
    return normalized.slice(0, 3).toUpperCase();
}

function formatRelationshipToken(value: string): string {
    const words = value
        .trim()
        .replace(/[_-]+/g, " ")
        .replace(/\s+/g, " ");
    return words.length === 0
        ? value
        : words[0].toUpperCase() + words.slice(1);
}

function appendOptionalFact(list: HTMLElement, label: string, value: string | undefined): void {
    if (value === undefined || value.trim().length === 0) return;
    const row = createElement("div", "dd-definition-row");
    row.append(
        createElement("dt", "dd-definition-row__term", label),
        createElement("dd", "dd-definition-row__value", value)
    );
    list.append(row);
}
