import {
    formatMechanicalValue,
    type CompetencyPresentationItem,
    type CompetencyView
} from "./character-mechanics.js";
import { createElement, createSectionCard } from "./components.js";
import { renderSourceAttributions } from "./source-attribution.js";

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

    const list = createElement("div", "dd-skill-list");
    items.forEach((item, index) => {
        if (item.kind === "standalone") {
            list.append(renderStandaloneCompetency(item.competency));
            return;
        }
        list.append(renderCompositeCompetency(item, index));
    });
    card.append(list);
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
    row.append(identity, value);
    return row;
}

function renderCompositeDetails(
    item: Extract<CompetencyPresentationItem, { kind: "composite" }>
): HTMLElement {
    const body = createElement("div", "dd-skill-disclosure__body dd-skill-composite-details");

    const resolution = createElement("section", "dd-skill-detail-section dd-skill-detail-section--resolution");
    resolution.append(createElement("h4", "dd-skill-detail-section__title", "Composite calculation"));
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

    const competencies = [item.parent, ...item.components].filter(hasCompetencyDetails);
    if (competencies.length > 0) {
        const grid = createElement("div", "dd-skill-composite-details__competencies");
        for (const competency of competencies) {
            const section = createElement("section", "dd-skill-detail-section");
            section.append(createElement(
                "h4",
                "dd-skill-detail-section__title",
                `${competency.label} ${formatMechanicalValue(competency)}`));
            section.append(renderCompetencyDetailContent(competency));
            grid.append(section);
        }
        body.append(grid);
    }

    return body;
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

    const sources = renderSourceAttributions(competency.sourceAttributions, true);
    if (sources !== null) content.append(sources);
    return content;
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
