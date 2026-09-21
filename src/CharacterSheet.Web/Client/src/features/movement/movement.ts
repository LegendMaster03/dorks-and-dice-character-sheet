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
    const walk = findMovementMode(supplied, MOVEMENT_PRESENTATION_MODES[0]);
    if (walk !== undefined) claimed.add(walk);

    const primary = createElement("div", "dd-movement-values__primary");
    primary.setAttribute("data-movement-primary", walk?.key ?? "movement.walk");
    primary.setAttribute("data-mechanic-key", walk?.key ?? "movement.walk");
    primary.setAttribute("data-movement-mode", "walk");
    primary.append(
        createElement("span", "dd-movement-values__primary-label", "Walk"),
        createElement(
            "strong",
            "dd-movement-values__primary-value",
            walk === undefined ? "-" : formatMechanicalValue(walk)));
    root.append(primary);

    const variants = createElement("div", "dd-movement-values__variants");
    variants.setAttribute("aria-label", "Additional movement speeds");

    for (const mode of MOVEMENT_PRESENTATION_MODES.slice(1)) {
        const value = findMovementMode(supplied, mode);
        if (value !== undefined) claimed.add(value);
        variants.append(renderMovementVariant(mode.id, mode.label, value));
    }

    for (const value of supplied.filter(value => !claimed.has(value))) {
        variants.append(renderMovementVariant(value.key, value.label, value));
    }

    root.append(variants);
    return root;
}

function renderMovementVariant(
    modeKey: string,
    label: string,
    value: CalculatedMechanicalValueView | undefined
): HTMLElement {
    const variant = createElement("div", "dd-movement-values__variant");
    variant.setAttribute("data-movement-mode", modeKey);
    variant.setAttribute("data-mechanic-key", value?.key ?? `movement.${modeKey}`);
    variant.append(
        createElement("span", "dd-movement-values__variant-label", label),
        createElement(
            "strong",
            "dd-movement-values__variant-value",
            value === undefined ? "-" : formatMechanicalValue(value)));
    return variant;
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
