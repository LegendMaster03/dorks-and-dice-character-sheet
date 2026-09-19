import {
    formatMechanicalValue,
    type CompetencyPresentationItem,
    type CompetencyView
} from "./character-mechanics.js";
import { createElement, createInlineState, createSectionCard } from "./components.js";
import { renderSourceAttributions } from "./source-attribution.js";

export function renderSkillsCard(items: readonly CompetencyPresentationItem[] | null): HTMLElement {
    const card = createSectionCard("Skills & Competencies", "dd-support-card dd-skills-card");

    if (items === null) {
        card.setAttribute("data-skills-state", "unavailable");
        card.append(createInlineState("Resolved competencies are not available.", "neutral"));
        return card;
    }

    card.setAttribute("data-skills-state", "resolved");
    if (items.length === 0) {
        card.append(createInlineState("No competencies were supplied for this Character.", "neutral"));
        return card;
    }

    const list = createElement("div", "dd-skill-list");
    items.forEach((item, index) => {
        if (item.kind === "standalone") {
            list.append(renderCompetencyRow(item.competency, "standalone"));
            return;
        }
        list.append(renderCompositeCompetency(item, index));
    });
    card.append(list);
    return card;
}

function renderCompositeCompetency(
    item: Extract<CompetencyPresentationItem, { kind: "composite" }>,
    index: number
): HTMLElement {
    const group = createElement("div", "dd-skill-group dd-skill-group--composite");
    group.setAttribute("role", "group");
    group.setAttribute("data-component-count", String(item.components.length));
    group.style.setProperty("--dd-skill-component-count", String(item.components.length));
    const parentLabelId = `dd-skill-group-${index}-parent-label`;
    group.setAttribute("aria-labelledby", parentLabelId);
    group.append(renderCompetencyRow(item.parent, "parent", parentLabelId));
    for (const component of item.components) {
        group.append(renderCompetencyRow(component, "component"));
    }
    return group;
}

type CompetencyRelationshipRole = "standalone" | "parent" | "component";

function renderCompetencyRow(
    competency: CompetencyView,
    relationship: CompetencyRelationshipRole,
    labelId?: string
): HTMLElement {
    const row = createElement(
        "div",
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

    const name = createElement("span", "dd-skill-row__name", competency.label);
    if (labelId !== undefined) name.id = labelId;
    const value = createElement("span", "dd-skill-row__value", formatMechanicalValue(competency));
    row.append(name, value);

    if (hasCompetencyDetails(competency)) {
        row.append(renderCompetencyDetails(competency));
    }
    return row;
}

function hasCompetencyDetails(competency: CompetencyView): boolean {
    return competency.ranks !== undefined
        || competency.governingAbility !== undefined
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

function renderCompetencyDetails(competency: CompetencyView): HTMLElement {
    const details = createElement("details", "dd-skill-row__details");
    details.append(createElement("summary", "dd-skill-row__details-toggle", "Details"));
    const body = createElement("div", "dd-skill-row__details-body");
    const facts = createElement("dl", "dd-skill-details");
    appendOptionalFact(
        facts,
        "Ranks",
        competency.ranks === undefined
            ? competency.supportsRanks === true ? "Not configured" : undefined
            : String(competency.ranks));
    appendOptionalFact(facts, "Ability", competency.governingAbility);
    appendOptionalFact(
        facts,
        "Training",
        competency.training ?? (competency.supportsTrainingState === true ? "Not configured" : undefined));
    appendOptionalFact(facts, "Family", competency.family);
    appendOptionalFact(facts, "Specialty", competency.specialty);
    if (competency.classSkill !== undefined) {
        appendOptionalFact(facts, "Class skill", competency.classSkill ? "Yes" : "No");
    } else if (competency.supportsClassSkillState === true) {
        appendOptionalFact(facts, "Class skill", "Not configured");
    }
    if (competency.trainedOnly !== undefined) appendOptionalFact(facts, "Trained only", competency.trainedOnly ? "Yes" : "No");
    if (competency.armorCheckPenalty !== undefined) {
        appendOptionalFact(
            facts,
            "Armor Check Penalty",
            competency.armorCheckPenalty.applies
                ? competency.armorCheckPenalty.formattedEffect ?? "Applies"
                : "Does not apply"
        );
    }
    for (const contribution of competency.breakdown ?? []) {
        appendOptionalFact(facts, contribution.label, formatMechanicalValue(contribution));
    }
    for (const related of competency.relatedValues ?? []) {
        appendOptionalFact(facts, related.label, formatMechanicalValue(related));
    }
    if (facts.children.length > 0) body.append(facts);
    const sources = renderSourceAttributions(competency.sourceAttributions, true);
    if (sources !== null) body.append(sources);
    details.append(body);
    return details;
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
