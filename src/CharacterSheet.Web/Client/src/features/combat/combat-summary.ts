import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView
} from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";
import { renderArmorClassCombatCard } from "../defense/defense.js";
import { findInitiativeValue } from "../initiative/initiative.js";
import { renderQuickMechanicalValue } from "../../core/mechanics/mechanic-value.js";

export function renderCombatSummaryBand(
    mechanics: CharacterMechanicsView | null,
    conditions: HTMLElement
): HTMLElement {
    const band = createElement("section", "dd-combat-band");
    band.setAttribute("aria-label", "Combat summary");

    const initiative = createElement("article", "dd-combat-band__initiative");
    initiative.append(
        createElement("h3", "dd-combat-band__heading", "Initiative"),
        renderQuickMechanicalValue(findInitiativeValue(mechanics?.combatFundamentals)));

    band.append(
        initiative,
        renderArmorClassCombatCard(mechanics),
        renderDefensesCard(mechanics),
        conditions
    );
    return band;
}

function renderDefensesCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const card = createElement("article", "dd-combat-band__defenses");
    card.append(createElement("h3", "dd-combat-band__heading", "Defenses"));

    const groups = createElement("div", "dd-combat-defenses");
    groups.append(
        renderDefenseGroup("Resistances", mechanics?.resistances),
        renderDefenseGroup("Immunities", mechanics?.immunities),
        renderDefenseGroup("Vulnerabilities", mechanics?.vulnerabilities)
    );
    card.append(groups);
    return card;
}

function renderDefenseGroup(
    label: string,
    values: readonly CalculatedMechanicalValueView[] | undefined
): HTMLElement {
    const group = createElement("div", "dd-combat-defenses__group");
    group.append(createElement("span", "dd-combat-defenses__label", label));
    const content = createElement("div", "dd-combat-defenses__values");
    if ((values?.length ?? 0) === 0) {
        content.append(createElement("span", "dd-combat-defenses__empty", "-"));
    } else {
        for (const value of values ?? []) {
            const item = createElement("span", "dd-combat-defenses__item");
            item.setAttribute("data-mechanic-key", value.key);
            item.append(
                createElement("span", "dd-combat-defenses__item-label", value.label),
                createElement("strong", "dd-combat-defenses__item-value", formatMechanicalValue(value)));
            content.append(item);
        }
    }
    group.append(content);
    return group;
}
