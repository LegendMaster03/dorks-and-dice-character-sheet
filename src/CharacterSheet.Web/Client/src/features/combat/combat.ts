import type { CharacterMechanicsView } from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";
import {
    renderMechanicalScaffold,
    type MechanicalScaffoldSlot
} from "../../core/mechanics/mechanic-value.js";
import { findInitiativeValue } from "../initiative/initiative.js";
import { renderDefenseScaffold } from "../defense/defense.js";
import { renderHealthScaffold } from "../health/health.js";
import { renderSavingThrowScaffold } from "../saving-throws/saving-throws.js";

const COMBAT_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    {
        id: "base-attack-bonus",
        label: "Base Attack Bonus",
        keys: ["combat.base-attack-bonus"]
    },
    {
        id: "grapple-modifier",
        label: "Grapple Modifier",
        keys: ["combat.grapple"],
        labels: ["Grapple"]
    }
];

export function renderCombatFundamentalsCard(
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const card = createSectionCard(
        "Combat",
        "dd-mechanic-group-card dd-combat-fundamentals-card");
    const initiative = findInitiativeValue(mechanics?.combatFundamentals);
    const values = (mechanics?.combatFundamentals ?? [])
        .filter(value => value !== initiative);
    const grid = createElement(
        "div",
        "dd-mechanic-group-card__grid dd-combat-fundamentals-card__grid");
    grid.append(...renderMechanicalScaffold(values, COMBAT_SCAFFOLD));
    card.append(grid);
    return card;
}

export function renderCombatMechanicsSummary(
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const section = createElement(
        "section",
        "dd-combat-summary dd-combat-summary--mechanics");
    section.setAttribute("aria-labelledby", "dd-combat-heading");
    section.setAttribute(
        "data-combat-mechanics-state",
        mechanics === null ? "unavailable" : "resolved");
    const heading = createElement(
        "h2",
        "dd-visually-hidden",
        "Combat summary");
    heading.id = "dd-combat-heading";
    section.append(heading);

    appendCombatGroup(
        section,
        "Defense",
        "defense",
        renderDefenseScaffold(mechanics));
    appendCombatGroup(
        section,
        "Saving Throws",
        "saves",
        renderSavingThrowScaffold(mechanics?.savingThrows ?? []));
    appendCombatGroup(
        section,
        "Health",
        "health",
        renderHealthScaffold(mechanics));

    const initiative = findInitiativeValue(mechanics?.combatFundamentals);
    const combatValues = (mechanics?.combatFundamentals ?? [])
        .filter(value => value !== initiative);
    appendCombatGroup(
        section,
        "Combat",
        "combat",
        renderMechanicalScaffold(combatValues, COMBAT_SCAFFOLD));

    return section;
}

function appendCombatGroup(
    target: HTMLElement,
    label: string,
    role: "defense" | "saves" | "health" | "combat",
    cells: readonly HTMLElement[]
): void {
    const group = createElement(
        "section",
        `dd-combat-summary__group dd-combat-summary__group--${role}`);
    group.setAttribute("data-combat-group", role);
    group.append(
        createElement("h3", "dd-combat-summary__group-title", label));
    const grid = createElement(
        "div",
        `dd-combat-summary__grid dd-combat-summary__grid--${role}`);
    grid.append(...cells);
    group.append(grid);
    target.append(group);
}
