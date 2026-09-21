import type { CalculatedMechanicalValueView } from "../../ui/character-mechanics.js";

export function findInitiativeValue(
    values: readonly CalculatedMechanicalValueView[] | undefined
): CalculatedMechanicalValueView | undefined {
    return values?.find(isInitiativeValue);
}

function isInitiativeValue(value: CalculatedMechanicalValueView): boolean {
    if (value.label.trim().toLowerCase() === "initiative") return true;
    return value.key
        .toLowerCase()
        .split(/[^a-z0-9]+/)
        .includes("initiative");
}
