import type { CharacterRoutineUiState } from "../../app-state.js";
import { getRulesCoreContext } from "../../rules-core-context.js";
import type { CharacterMechanicsView } from "../../ui/character-mechanics.js";
import { createButton, createElement, createInlineState } from "../../ui/components.js";
import type { RoutineCharacterHandlers } from "../../ui/sheet-contracts.js";

const STARTING_EQUIPMENT_APPLIED_KEY = "starting-equipment.applied";

export function renderStartingEquipment(
    mechanics: CharacterMechanicsView | null,
    routine: CharacterRoutineUiState,
    handlers: RoutineCharacterHandlers
): HTMLElement | null {
    if (mechanics === null) return null;

    const grants = (getRulesCoreContext(mechanics)?.grants ?? [])
        .filter(grant => grant.kind.startsWith("starting-equipment-"));
    const choices = (mechanics.ruleChoices ?? [])
        .filter(choice => choice.kind === "starting-equipment");
    if (grants.length === 0 && choices.length === 0) return null;

    const section = createElement("section", "dd-guided-builder__rules");
    section.setAttribute("data-starting-equipment", "true");
    section.append(createElement(
        "h3",
        "dd-guided-builder__subheading",
        "Starting Equipment"));

    const pendingChoices = choices.some(choice => choice.state !== "resolved");
    const applied = (routine.state?.rulesInputs ?? []).some(input =>
        input.kind === "stringFact"
        && input.key === STARTING_EQUIPMENT_APPLIED_KEY);

    const grouped = new Map<string, { label: string; quantity: number }>();
    for (const grant of grants) {
        const key = [grant.kind, grant.targetKey, grant.displayName].join("|");
        const existing = grouped.get(key);
        if (existing === undefined) {
            grouped.set(key, {
                label: grant.kind === "starting-equipment-currency"
                    ? grant.targetKey.toUpperCase()
                    : grant.displayName,
                quantity: 1
            });
        } else {
            existing.quantity += 1;
        }
    }

    if (grouped.size > 0) {
        const list = createElement("ul", "dd-guided-builder__review-list");
        for (const grant of grouped.values()) {
            const item = createElement("li", "dd-guided-builder__review-item");
            item.append(createElement(
                "span",
                "dd-guided-builder__review-detail",
                grant.quantity > 1
                    ? `${grant.label} ×${grant.quantity}`
                    : grant.label));
            list.append(item);
        }
        section.append(list);
    }

    if (pendingChoices) {
        section.append(createInlineState(
            "Complete the starting-equipment choices above before adding the resolved equipment.",
            "warning"));
        return section;
    }

    if (applied) {
        section.append(createInlineState(
            "Starting equipment has been added to this Character's inventory and currency.",
            "neutral"));
        return section;
    }

    if (grants.length > 0) {
        const applying = routine.mutation?.kind === "inventory-add"
            && routine.mutation.entryId === "starting-equipment";
        const button = createButton(
            applying ? "Adding Starting Equipment…" : "Add Starting Equipment",
            "dd-button dd-button--primary",
            handlers.applyStartingEquipment);
        button.disabled = applying || routine.status !== "ready" || routine.state === null;
        section.append(button);
    }

    return section;
}
