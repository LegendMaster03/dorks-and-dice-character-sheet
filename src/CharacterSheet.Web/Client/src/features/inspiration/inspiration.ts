import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView
} from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";

export function renderInspirationQuickCard(
    value: CalculatedMechanicalValueView | undefined
): HTMLElement {
    const card = createElement("article", "dd-stat dd-stat--inspiration");
    card.setAttribute("data-inspiration-state", value === undefined ? "unavailable" : "resolved");

    const indicator = createElement(
        "strong",
        "dd-inspiration__indicator",
        value === undefined ? "-" : formatMechanicalValue(value));
    if (value !== undefined) indicator.setAttribute("data-mechanic-key", value.key);

    card.append(
        createElement("h3", "dd-stat__label", "Inspiration"),
        indicator);
    return card;
}
