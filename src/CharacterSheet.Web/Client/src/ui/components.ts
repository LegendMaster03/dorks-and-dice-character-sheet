export type StateTone = "neutral" | "loading" | "warning" | "error" | "readonly";

export function createElement<K extends keyof HTMLElementTagNameMap>(
    tag: K,
    className?: string,
    text?: string
): HTMLElementTagNameMap[K] {
    const element = document.createElement(tag);
    if (className !== undefined) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
}

export function createButton(
    label: string,
    className: string,
    onClick: () => void,
    disabled = false
): HTMLButtonElement {
    const button = createElement("button", className, label);
    button.type = "button";
    button.disabled = disabled;
    button.addEventListener("click", onClick);
    return button;
}

export function createStateCard(
    title: string,
    message: string,
    tone: StateTone = "neutral"
): HTMLElement {
    const card = createElement("section", `dd-sheet-state dd-sheet-state--${tone}`);
    card.setAttribute("role", tone === "error" ? "alert" : "status");
    const heading = createElement("h1", "dd-sheet-state__title", title);
    const copy = createElement("p", "dd-sheet-state__message", message);
    card.append(heading, copy);
    return card;
}

export function createInlineState(message: string, tone: StateTone = "neutral"): HTMLElement {
    const state = createElement("p", `dd-inline-state dd-inline-state--${tone}`, message);
    state.setAttribute("role", tone === "error" ? "alert" : "status");
    return state;
}

export function createSectionCard(title: string, className = ""): HTMLElement {
    const card = createElement("section", `dd-sheet-card ${className}`.trim());
    const heading = createElement("h2", "dd-sheet-card__title", title);
    card.append(heading);
    return card;
}

export function createPlaceholder(label: string, message: string, compact = false): HTMLElement {
    const placeholder = createElement("div", compact ? "dd-placeholder dd-placeholder--compact" : "dd-placeholder");
    placeholder.setAttribute("data-unimplemented-mechanic", label);
    const name = createElement("span", "dd-placeholder__label", label);
    const detail = createElement("span", "dd-placeholder__message", message);
    placeholder.append(name, detail);
    return placeholder;
}
