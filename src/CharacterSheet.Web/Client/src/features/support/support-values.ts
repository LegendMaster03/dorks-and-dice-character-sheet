import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView,
    type CompetencyView
} from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";

export function renderPassiveValuesCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    return renderSupportValuesCard("Passive Values", "passive", mechanics?.passiveValues);
}

export function renderSensesCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    return renderSupportValuesCard("Senses", "senses", mechanics?.senses);
}

export function renderSensesSummaryCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const card = createSectionCard("Senses", "dd-support-card dd-support-values dd-senses-summary-card");
    card.setAttribute("data-support-values-kind", "senses-summary");

    const passiveValues = mechanics?.passiveValues;
    const senses = mechanics?.senses;
    card.setAttribute(
        "data-support-scaffold-state",
        passiveValues === undefined && senses === undefined ? "unavailable" : "resolved");

    card.append(
        renderSupportGroup("Passive Values", passiveValues, "passive"),
        renderSupportGroup("Additional Senses", senses, "senses")
    );
    return card;
}

export function renderTrainingCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const competencyRows = (mechanics?.competencies?.entries ?? [])
        .filter(isNonSkillTraining)
        .map(value => ({ key: value.key, label: value.label, value: value.training! }));
    return renderSupportValuesCard(
        "Proficiencies & Training",
        "training",
        mechanics?.training,
        competencyRows);
}

function renderSupportGroup(
    label: string,
    values: readonly CalculatedMechanicalValueView[] | undefined,
    kind: string
): HTMLElement {
    const group = createElement("section", "dd-support-values__group");
    group.setAttribute("data-support-group", kind);
    group.append(createElement("h3", "dd-support-values__group-title", label));

    const list = createElement("div", "dd-support-scaffold__list");
    const supplied = values ?? [];
    if (supplied.length === 0) {
        const row = createElement("div", "dd-support-scaffold__row dd-support-scaffold__row--empty");
        row.append(createElement("strong", "dd-support-scaffold__value", "-"));
        list.append(row);
    } else {
        for (const value of supplied) {
            const row = createElement("div", "dd-support-scaffold__row");
            row.setAttribute("data-mechanic-key", value.key);
            row.append(
                createElement("span", "dd-support-scaffold__label", value.label),
                createElement("strong", "dd-support-scaffold__value", formatMechanicalValue(value)));
            list.append(row);
        }
    }
    group.append(list);
    return group;
}

function renderSupportValuesCard(
    title: string,
    kind: string,
    values: readonly CalculatedMechanicalValueView[] | undefined,
    additionalRows: readonly { key: string; label: string; value: string }[] = []
): HTMLElement {
    const card = createSectionCard(title, "dd-support-card dd-support-values");
    card.setAttribute("data-support-values-kind", kind);
    const list = createElement("div", "dd-support-scaffold__list");
    const supplied = values ?? [];

    if (supplied.length === 0 && additionalRows.length === 0) {
        card.setAttribute("data-support-scaffold-state", "unavailable");
        const row = createElement("div", "dd-support-scaffold__row dd-support-scaffold__row--empty");
        row.append(createElement("strong", "dd-support-scaffold__value", "-"));
        list.append(row);
    } else {
        card.setAttribute("data-support-scaffold-state", "resolved");
        for (const value of supplied) {
            const row = createElement("div", "dd-support-scaffold__row");
            row.setAttribute("data-mechanic-key", value.key);
            row.append(
                createElement("span", "dd-support-scaffold__label", value.label),
                createElement("strong", "dd-support-scaffold__value", formatMechanicalValue(value)));
            list.append(row);
        }
        for (const value of additionalRows) {
            const row = createElement("div", "dd-support-scaffold__row");
            row.setAttribute("data-competency-key", value.key);
            row.append(
                createElement("span", "dd-support-scaffold__label", value.label),
                createElement("strong", "dd-support-scaffold__value", value.value));
            list.append(row);
        }
    }

    card.append(list);
    return card;
}

function isNonSkillTraining(value: CompetencyView): boolean {
    const training = value.training?.trim();
    return training !== undefined
        && training.length > 0
        && value.kind?.trim().toLowerCase() !== "skill";
}
