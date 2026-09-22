import type { CalculatedMechanicalValueView } from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";
import { renderQuickMechanicalValue } from "../../core/mechanics/mechanic-value.js";
import { normalizeMechanicalLabel } from "../../core/mechanics/mechanic-value.js";

export function findProficiencyBonusValue(
    values: readonly CalculatedMechanicalValueView[] | undefined
): CalculatedMechanicalValueView | undefined {
    return values?.find(value => {
        const key = normalizeMechanicalLabel(value.key);
        const label = normalizeMechanicalLabel(value.label);
        return key === "proficiencybonus"
            || key.endsWith("proficiencybonus")
            || label === "proficiencybonus";
    });
}

export function renderProficiencyQuickCard(
    values: readonly CalculatedMechanicalValueView[] | undefined
): HTMLElement {
    const value = findProficiencyBonusValue(values);
    const card = createElement("article", "dd-stat dd-stat--proficiency");
    card.setAttribute(
        "data-proficiency-bonus-state",
        value === undefined ? "unavailable" : "resolved");
    card.append(
        createElement("h3", "dd-stat__label", "Proficiency Bonus"),
        renderQuickMechanicalValue(value));
    return card;
}
