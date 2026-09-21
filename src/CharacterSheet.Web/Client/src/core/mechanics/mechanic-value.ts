import {
    formatMechanicalValue,
    hasMechanicalDetails,
    type CalculatedMechanicalValueView,
    type DisplayFieldView
} from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";
import { renderSourceAttributions } from "../../ui/source-attribution.js";

export interface MechanicalScaffoldSlot {
    id: string;
    label: string;
    keys: readonly string[];
    labels?: readonly string[];
}

export function renderMechanicalValue(
    value: CalculatedMechanicalValueView,
    compact = false
): HTMLElement {
    const root = createElement(
        "div",
        `dd-mechanic-value${compact ? " dd-mechanic-value--compact" : ""}`);
    root.setAttribute("data-mechanic-key", value.key);

    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", value.label),
        createElement("strong", "dd-mechanic-value__value", formatMechanicalValue(value)));
    root.append(head);

    if (!hasMechanicalDetails(value)) return root;

    const details = createElement("details", "dd-mechanic-value__details");
    details.append(createElement("summary", "dd-mechanic-value__details-toggle", "Details"));
    const body = createElement("div", "dd-mechanic-value__details-body");
    const facts = renderFacts([
        ...(value.breakdown ?? []).map(entry =>
            [entry.label, formatMechanicalValue(entry)] as const),
        ...(value.relatedValues ?? []).map(entry =>
            [entry.label, formatMechanicalValue(entry)] as const)
    ]);
    if (facts !== null) body.append(facts);
    appendSources(body, value.sourceAttributions);
    details.append(body);
    root.append(details);
    return root;
}

export function renderQuickMechanicalValue(
    value: CalculatedMechanicalValueView | undefined
): HTMLElement {
    const root = createElement("div", "dd-quick-mechanic");
    if (value === undefined) {
        root.setAttribute("data-quick-mechanic-state", "unavailable");
        root.append(createElement("p", "dd-stat__value", "-"));
        return root;
    }

    root.setAttribute("data-quick-mechanic-state", "resolved");
    root.setAttribute("data-mechanic-key", value.key);
    root.append(createElement("p", "dd-stat__value", formatMechanicalValue(value)));
    return root;
}

export function findScaffoldValue<T extends CalculatedMechanicalValueView>(
    values: readonly T[],
    slot: MechanicalScaffoldSlot,
    usedKeys: ReadonlySet<string>
): T | undefined {
    const keyMatch = values.find(value =>
        !usedKeys.has(value.key) && slot.keys.includes(value.key));
    if (keyMatch !== undefined) return keyMatch;

    const labels = new Set(
        [slot.label, ...(slot.labels ?? [])].map(normalizeMechanicalLabel));
    return values.find(value =>
        !usedKeys.has(value.key)
        && labels.has(normalizeMechanicalLabel(value.label)));
}

export function renderMechanicalScaffold(
    values: readonly CalculatedMechanicalValueView[],
    slots: readonly MechanicalScaffoldSlot[]
): HTMLElement[] {
    const usedKeys = new Set<string>();
    const cells = slots.map(slot => {
        const value = findScaffoldValue(values, slot, usedKeys);
        if (value !== undefined) {
            usedKeys.add(value.key);
            return renderMechanicalValue(value, true);
        }
        return renderScaffoldMechanicalValue(slot);
    });

    for (const value of values) {
        if (!usedKeys.has(value.key)) cells.push(renderMechanicalValue(value, true));
    }
    return cells;
}

export function renderScaffoldMechanicalValue(slot: MechanicalScaffoldSlot): HTMLElement {
    const root = createElement(
        "div",
        "dd-mechanic-value dd-mechanic-value--compact dd-mechanic-value--scaffold");
    root.setAttribute("data-sheet-scaffold-key", slot.id);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", slot.label),
        createElement("strong", "dd-mechanic-value__value", "-"));
    root.append(head);
    return root;
}

export function renderDisplayFields(
    fields: readonly DisplayFieldView[] | undefined,
    title?: string
): HTMLElement | null {
    if (!fields?.length) return null;
    const root = createElement("div", "dd-display-fields");
    if (title) root.append(createElement("h5", "dd-display-fields__title", title));
    const list = createElement("dl", "dd-display-fields__list");
    for (const field of fields) appendDefinitionRow(list, field.label, field.value);
    root.append(list);
    return root;
}

export function renderFacts(
    entries: readonly (readonly [string, string | undefined] | undefined)[]
): HTMLElement | null {
    const list = createElement("dl", "dd-display-fields__list");
    for (const entry of entries) {
        if (entry?.[1]?.trim()) appendDefinitionRow(list, entry[0], entry[1]);
    }
    return list.children.length === 0 ? null : list;
}

export function appendSources(
    target: HTMLElement,
    sources: Parameters<typeof renderSourceAttributions>[0]
): void {
    const rendered = renderSourceAttributions(sources, true);
    if (rendered !== null) target.append(rendered);
}

export function normalizeMechanicalLabel(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "");
}

export function joinOptional(
    left: string | undefined,
    right: string | undefined,
    separator: string
): string | undefined {
    if (left === undefined) return right;
    if (right === undefined) return left;
    return `${left}${separator}${right}`;
}

function appendDefinitionRow(list: HTMLElement, label: string, value: string): void {
    const row = createElement("div", "dd-definition-row");
    row.append(
        createElement("dt", "dd-definition-row__term", label),
        createElement("dd", "dd-definition-row__value", value));
    list.append(row);
}
