import { createElement } from "../../ui/components.js";

export const CHARACTER_INSPIRATION_STATE_KEY = "inspiration";

export interface InspirationControlOptions {
    current: boolean | undefined;
    readOnly: boolean;
    saving: boolean;
    onSet(value: boolean): void;
}

export function renderInspirationQuickCard(
    options: InspirationControlOptions
): HTMLElement {
    const card = createElement("article", "dd-stat dd-stat--inspiration");
    const heading = createElement("h3", "dd-stat__label", "Inspiration");
    card.append(heading);

    if (options.current === undefined) {
        card.setAttribute("data-inspiration-state", "unavailable");
        card.append(createElement("strong", "dd-inspiration__indicator", "-"));
        return card;
    }

    card.setAttribute("data-inspiration-state", options.current ? "on" : "off");
    const toggle = createElement(
        "button",
        "dd-inspiration__indicator",
        options.current ? "✓" : "○");
    toggle.type = "button";
    toggle.disabled = options.readOnly || options.saving;
    toggle.setAttribute("data-inspiration-toggle", "true");
    toggle.setAttribute("aria-pressed", options.current ? "true" : "false");
    toggle.setAttribute(
        "aria-label",
        options.readOnly
            ? `Inspiration ${options.current ? "on" : "off"}.`
            : `Inspiration ${options.current ? "on" : "off"}. Toggle ${options.current ? "off" : "on"}.`);
    toggle.addEventListener("click", () => options.onSet(!options.current));
    card.append(toggle);
    return card;
}
