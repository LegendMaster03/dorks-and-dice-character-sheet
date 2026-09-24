import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView
} from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";
import { normalizeMechanicalLabel } from "../../core/mechanics/mechanic-value.js";

const MOVEMENT_PRESENTATION_MODES = [
    { id: "walk", label: "Walk", aliases: ["walk", "walking", "land", "land speed", "speed"] },
    { id: "swim", label: "Swim", aliases: ["swim", "swimming"] },
    { id: "climb", label: "Climb", aliases: ["climb", "climbing"] },
    { id: "fly", label: "Fly", aliases: ["fly", "flying"] }
] as const;

export function renderMovementValues(values: readonly CalculatedMechanicalValueView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-movement-values");
    root.setAttribute("data-movement-state", values === undefined ? "unavailable" : "resolved");

    const supplied = values ?? [];
    const claimed = new Set<CalculatedMechanicalValueView>();
    const grid = createElement("div", "dd-movement-values__grid");
    grid.setAttribute("aria-label", "Movement speeds");

    for (const mode of MOVEMENT_PRESENTATION_MODES) {
        const value = findMovementMode(supplied, mode);
        if (value !== undefined) claimed.add(value);

        // Walking speed is the baseline slot. Alternate modes are shown only
        // when Rules Core actually supplies them, rather than fabricating
        // empty Swim / Climb / Fly entries.
        if (mode.id !== "walk" && value === undefined) continue;
        grid.append(renderMovementMode(mode.id, mode.label, value, mode.id === "walk"));
    }

    for (const value of supplied.filter(value => !claimed.has(value))) {
        grid.append(renderMovementMode(value.key, value.label, value, false));
    }

    root.append(grid);
    return root;
}

function renderMovementMode(
    modeKey: string,
    label: string,
    value: CalculatedMechanicalValueView | undefined,
    primary: boolean
): HTMLElement {
    const mode = createElement(
        "div",
        primary
            ? "dd-movement-values__mode dd-movement-values__mode--primary"
            : "dd-movement-values__mode");
    mode.setAttribute("data-movement-mode", modeKey);
    mode.setAttribute("data-mechanic-key", value?.key ?? `movement.${modeKey}`);
    if (primary) {
        mode.setAttribute("data-movement-primary", value?.key ?? "movement.walk");
    }
    mode.append(
        createElement("span", "dd-movement-values__mode-label", label),
        createElement(
            "strong",
            "dd-movement-values__mode-value",
            value === undefined ? "-" : formatMechanicalValue(value)));
    return mode;
}

function findMovementMode(
    values: readonly CalculatedMechanicalValueView[],
    mode: { readonly id: string; readonly label: string; readonly aliases: readonly string[] }
): CalculatedMechanicalValueView | undefined {
    return values.find(value => {
        const normalizedKey = normalizeMechanicalLabel(value.key);
        const normalizedLabel = normalizeMechanicalLabel(value.label);
        const candidates = [mode.id, mode.label, ...mode.aliases].map(normalizeMechanicalLabel);
        return candidates.some(candidate =>
            normalizedLabel === candidate
            || normalizedKey === candidate
            || normalizedKey.endsWith(candidate));
    });
}
