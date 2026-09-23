import { createElement } from "../../ui/components.js";

export const CHARACTER_INSPIRATION_STATE_KEY = "inspiration";

export interface InspirationControlOptions {
    current: boolean | undefined;
    readOnly: boolean;
    saving: boolean;
    onSet(value: boolean): Promise<boolean>;
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

    let current = options.current;
    const toggle = createElement(
        "button",
        "dd-inspiration__indicator");
    toggle.type = "button";
    toggle.setAttribute("data-inspiration-toggle", "true");

    const syncToggle = (): void => {
        card.setAttribute("data-inspiration-state", current ? "on" : "off");
        toggle.textContent = current ? "✓" : "○";
        toggle.setAttribute("aria-pressed", current ? "true" : "false");
        toggle.setAttribute(
            "aria-label",
            options.readOnly
                ? `Inspiration ${current ? "on" : "off"}.`
                : `Inspiration ${current ? "on" : "off"}. Toggle ${current ? "off" : "on"}.`);
    };

    syncToggle();
    toggle.disabled = options.readOnly || options.saving;
    toggle.addEventListener("click", async () => {
        if (toggle.disabled) return;
        const next = !current;
        toggle.disabled = true;
        const saved = await options.onSet(next);
        if (saved) {
            current = next;
            toggle.removeAttribute?.("data-inspiration-save-error");
            toggle.title = "";
            syncToggle();
        } else {
            toggle.setAttribute("data-inspiration-save-error", "true");
            toggle.title = "Inspiration could not be saved.";
        }
        toggle.disabled = options.readOnly;
    });
    card.append(toggle);
    return card;
}
