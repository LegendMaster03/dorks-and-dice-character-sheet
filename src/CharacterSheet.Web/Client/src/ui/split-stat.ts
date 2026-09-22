import { createElement } from "./components.js";

export interface SplitStatCell {
    label: string;
    value: string;
    className?: string;
    attributes?: Readonly<Record<string, string>>;
}

export interface SplitStatOptions {
    label: string;
    primaryValue: string;
    primaryDetail?: string;
    primaryContext?: string;
    primaryAttributes?: Readonly<Record<string, string>>;
    className?: string;
    secondary: readonly SplitStatCell[];
}

export function renderSplitStat(options: SplitStatOptions): HTMLElement {
    const presentation = createElement(
        "div",
        `dd-split-stat ${options.className ?? ""}`.trim());

    const primary = createElement("div", "dd-split-stat__primary");
    const value = createElement(
        "strong",
        "dd-split-stat__primary-value",
        options.primaryValue);
    applyAttributes(value, options.primaryAttributes);
    primary.append(
        createElement("h3", "dd-stat__label", options.label),
        value);

    if (options.primaryDetail !== undefined) {
        primary.append(createElement(
            "p",
            "dd-split-stat__detail",
            options.primaryDetail));
    }
    if (options.primaryContext !== undefined) {
        primary.append(createElement(
            "p",
            "dd-split-stat__context",
            options.primaryContext));
    }

    const secondary = createElement("div", "dd-split-stat__secondary");
    secondary.style.setProperty(
        "--dd-split-stat-secondary-count",
        String(Math.max(1, options.secondary.length)));

    for (const item of options.secondary) {
        const cell = createElement(
            "div",
            `dd-split-stat__secondary-cell ${item.className ?? ""}`.trim());
        applyAttributes(cell, item.attributes);
        cell.append(
            createElement("span", "dd-split-stat__secondary-label", item.label),
            createElement("strong", "dd-split-stat__secondary-value", item.value));
        secondary.append(cell);
    }

    presentation.append(primary, secondary);
    return presentation;
}

function applyAttributes(
    element: HTMLElement,
    attributes: Readonly<Record<string, string>> | undefined
): void {
    for (const [name, value] of Object.entries(attributes ?? {})) {
        element.setAttribute(name, value);
    }
}
