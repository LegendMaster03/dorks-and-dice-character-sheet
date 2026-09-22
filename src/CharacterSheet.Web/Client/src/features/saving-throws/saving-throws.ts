import type { SavingThrowView } from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";
import {
    findScaffoldValue,
    renderFacts,
    renderMechanicalValue,
    renderScaffoldMechanicalValue,
    type MechanicalScaffoldSlot
} from "../../core/mechanics/mechanic-value.js";

export const SAVE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    {
        id: "fortitude",
        label: "Fortitude Save",
        keys: ["save.fortitude", "saving-throw.fortitude"],
        labels: ["Fortitude", "Fortitude Save"]
    },
    {
        id: "reflex",
        label: "Reflex Save",
        keys: ["save.reflex", "saving-throw.reflex"],
        labels: ["Reflex", "Reflex Save"]
    },
    {
        id: "will",
        label: "Will Save",
        keys: ["save.will", "saving-throw.will"],
        labels: ["Will", "Will Save"]
    }
];

export function renderSavingThrowsCard(
    saves: readonly SavingThrowView[] | undefined
): HTMLElement {
    const card = createSectionCard("Saving Throws", "dd-support-card dd-saving-throws-card");
    card.setAttribute(
        "data-saving-throws-state",
        saves === undefined ? "unavailable" : "resolved");
    const values = saves ?? [];
    const grid = createElement("div", "dd-saving-throws-card__grid");
    grid.append(...renderSavingThrowScaffold(values));
    card.append(grid);
    return card;
}

export function renderSavingThrowScaffold(
    saves: readonly SavingThrowView[]
): HTMLElement[] {
    const usedKeys = new Set<string>();
    const cells = SAVE_SCAFFOLD.map(slot => {
        const save = findScaffoldValue(
            saves,
            slot,
            usedKeys) as SavingThrowView | undefined;
        if (save !== undefined) {
            usedKeys.add(save.key);
            return renderSavingThrowCell(save);
        }

        const item = createElement("div", "dd-saving-throw");
        item.append(renderScaffoldMechanicalValue(slot));
        return item;
    });

    for (const save of saves) {
        if (!usedKeys.has(save.key)) cells.push(renderSavingThrowCell(save));
    }
    return cells;
}

function renderSavingThrowCell(save: SavingThrowView): HTMLElement {
    const item = createElement("div", "dd-saving-throw");
    item.append(renderMechanicalValue(save, true));
    const meta = renderFacts([
        ["Ability", save.governingAbility],
        ["Training", save.training]
    ]);
    if (meta !== null) item.append(meta);
    return item;
}
