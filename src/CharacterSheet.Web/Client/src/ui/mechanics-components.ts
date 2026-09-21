import {
    formatHealthTrack,
    formatMechanicalValue,
    hasMechanicalDetails,
    type ActionAttackView,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView,
    type DisplayFieldView,
    type SavingThrowView
} from "./character-mechanics.js";
import { createElement, createInlineState, createSectionCard } from "./components.js";
import { renderSourceAttributions } from "./source-attribution.js";

export function renderMechanicalValue(value: CalculatedMechanicalValueView, compact = false): HTMLElement {
    const root = createElement("div", `dd-mechanic-value${compact ? " dd-mechanic-value--compact" : ""}`);
    root.setAttribute("data-mechanic-key", value.key);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(createElement("span", "dd-mechanic-value__label", value.label), createElement("strong", "dd-mechanic-value__value", formatMechanicalValue(value)));
    root.append(head);
    if (!hasMechanicalDetails(value)) return root;
    const details = createElement("details", "dd-mechanic-value__details");
    details.append(createElement("summary", "dd-mechanic-value__details-toggle", "Details"));
    const body = createElement("div", "dd-mechanic-value__details-body");
    const facts = renderFacts([
        ...(value.breakdown ?? []).map(entry => [entry.label, formatMechanicalValue(entry)] as const),
        ...(value.relatedValues ?? []).map(entry => [entry.label, formatMechanicalValue(entry)] as const)
    ]);
    if (facts !== null) body.append(facts);
    appendSources(body, value.sourceAttributions);
    details.append(body);
    root.append(details);
    return root;
}

export function renderSavingThrowsCard(saves: readonly SavingThrowView[] | undefined): HTMLElement {
    const card = createSectionCard("Saving Throws", "dd-support-card dd-saving-throws-card");
    card.setAttribute("data-saving-throws-state", saves === undefined ? "unavailable" : "resolved");
    if (saves === undefined || saves.length === 0) {
        card.append(renderUnavailableValue());
        return card;
    }
    const list = createElement("div", "dd-mechanic-list");
    for (const save of saves) {
        const item = createElement("div", "dd-saving-throw");
        item.append(renderMechanicalValue(save, true));
        const meta = renderFacts([["Ability", save.governingAbility], ["Training", save.training]]);
        if (meta !== null) item.append(meta);
        list.append(item);
    }
    card.append(list);
    return card;
}

export function findInitiativeValue(
    values: readonly CalculatedMechanicalValueView[] | undefined
): CalculatedMechanicalValueView | undefined {
    return values?.find(isInitiativeValue);
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

export function renderCombatMechanicsSummary(mechanics: CharacterMechanicsView | null): HTMLElement {
    const section = createElement("section", "dd-combat-summary dd-combat-summary--mechanics");
    section.setAttribute("aria-labelledby", "dd-combat-heading");
    const heading = createElement("h2", "dd-visually-hidden", "Combat summary");
    heading.id = "dd-combat-heading";
    section.append(heading);
    if (mechanics === null) {
        section.setAttribute("data-combat-mechanics-state", "unavailable");
        section.append(renderUnavailableValue());
        return section;
    }

    section.setAttribute("data-combat-mechanics-state", "resolved");
    let groupCount = 0;

    const defenses = orderedDefenses(mechanics);
    if (defenses.length > 0) {
        appendCombatGroup(
            section,
            "Defense",
            "defense",
            defenses.map(value => renderMechanicalValue(value, true)));
        groupCount += 1;
    }

    if ((mechanics.savingThrows?.length ?? 0) > 0) {
        appendCombatGroup(
            section,
            "Saving Throws",
            "saves",
            mechanics.savingThrows!.map(renderSavingThrowCell));
        groupCount += 1;
    }

    if ((mechanics.healthTracks?.length ?? 0) > 0) {
        appendCombatGroup(
            section,
            "Health",
            "health",
            mechanics.healthTracks!.map(renderHealthTrack));
        groupCount += 1;
    }

    const initiative = findInitiativeValue(mechanics.combatFundamentals);
    const combatValues = (mechanics.combatFundamentals ?? [])
        .filter(value => value !== initiative);
    if (combatValues.length > 0) {
        appendCombatGroup(
            section,
            "Combat",
            "combat",
            combatValues.map(value => renderMechanicalValue(value, true)));
        groupCount += 1;
    }

    if (groupCount === 0) section.append(renderUnavailableValue());
    return section;
}

function appendCombatGroup(
    target: HTMLElement,
    label: string,
    role: "defense" | "saves" | "health" | "combat",
    cells: readonly HTMLElement[]
): void {
    const group = createElement("section", `dd-combat-summary__group dd-combat-summary__group--${role}`);
    group.setAttribute("data-combat-group", role);
    group.append(createElement("h3", "dd-combat-summary__group-title", label));
    const grid = createElement("div", `dd-combat-summary__grid dd-combat-summary__grid--${role}`);
    grid.append(...cells);
    group.append(grid);
    target.append(group);
}

function renderSavingThrowCell(save: SavingThrowView): HTMLElement {
    const item = createElement("div", "dd-saving-throw");
    item.append(renderMechanicalValue(save, true));
    const meta = renderFacts([["Ability", save.governingAbility], ["Training", save.training]]);
    if (meta !== null) item.append(meta);
    return item;
}

function renderHealthTrack(track: NonNullable<CharacterMechanicsView["healthTracks"]>[number]): HTMLElement {
    const cell = createElement("div", "dd-mechanic-value dd-mechanic-value--compact dd-health-track");
    cell.setAttribute("data-health-track-key", track.key);
    cell.setAttribute("data-health-track-role", track.role);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", track.label),
        createElement("strong", "dd-mechanic-value__value", formatHealthTrack(track)));
    cell.append(head);
    if (track.detail) cell.append(createElement("span", "dd-mechanic-value__meta", track.detail));
    appendSources(cell, track.sourceAttributions);
    return cell;
}

function isInitiativeValue(value: CalculatedMechanicalValueView): boolean {
    if (value.label.trim().toLowerCase() === "initiative") return true;
    return value.key
        .toLowerCase()
        .split(/[^a-z0-9]+/)
        .includes("initiative");
}

export function renderMovementValues(values: readonly CalculatedMechanicalValueView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-movement-values");
    root.setAttribute("data-movement-state", values === undefined ? "unavailable" : "resolved");
    if (values === undefined || values.length === 0) {
        root.append(renderUnavailableValue());
    } else {
        root.append(...values.map(value => renderMechanicalValue(value, true)));
    }
    return root;
}

export function renderActionsPresentation(actions: readonly ActionAttackView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-action-list");
    if (actions === undefined || actions.length === 0) {
        root.append(createInlineState(actions === undefined ? "Resolved actions and attacks are not available." : "No actions or attacks were supplied for this Character.", "neutral"));
        return root;
    }
    for (const action of actions) {
        const item = createElement("article", "dd-action-card");
        item.setAttribute("data-action-key", action.key);
        item.append(createElement("h3", "dd-action-card__name", action.name));
        if (action.actionType) item.append(createElement("span", "dd-action-card__type", action.actionType));
        if (action.attackOrCheck) item.append(renderMechanicalValue(action.attackOrCheck, true));
        const facts = renderFacts([
            ["Damage", action.damage === undefined ? undefined : action.damageType === undefined ? action.damage : `${action.damage} ${action.damageType}`],
            ["Critical", joinOptional(action.criticalRange, action.criticalMultiplier, " / ")],
            ["Range", action.range], ["Reach", action.reach], ["Ammunition", action.ammunition], ["Target", action.target]
        ]);
        if (facts !== null) item.append(facts);
        if (action.notes?.length) {
            const notes = createElement("ul", "dd-action-card__notes");
            for (const note of action.notes) notes.append(createElement("li", undefined, note));
            item.append(notes);
        }
        appendSources(item, action.sourceAttributions);
        root.append(item);
    }
    return root;
}

export function renderDisplayFields(fields: readonly DisplayFieldView[] | undefined, title?: string): HTMLElement | null {
    if (!fields?.length) return null;
    const root = createElement("div", "dd-display-fields");
    if (title) root.append(createElement("h5", "dd-display-fields__title", title));
    const list = createElement("dl", "dd-display-fields__list");
    for (const field of fields) appendDefinitionRow(list, field.label, field.value);
    root.append(list);
    return root;
}

export function renderFacts(entries: readonly (readonly [string, string | undefined] | undefined)[]): HTMLElement | null {
    const list = createElement("dl", "dd-display-fields__list");
    for (const entry of entries) if (entry?.[1]?.trim()) appendDefinitionRow(list, entry[0], entry[1]);
    return list.children.length === 0 ? null : list;
}

function renderUnavailableValue(): HTMLElement {
    const root = createElement("div", "dd-mechanic-value dd-mechanic-value--compact dd-mechanic-value--placeholder");
    root.append(createElement("strong", "dd-mechanic-value__value", "-"));
    return root;
}

function orderedDefenses(mechanics: CharacterMechanicsView): readonly CalculatedMechanicalValueView[] {
    const values = mechanics.defenses?.values ?? [];
    const key = mechanics.defenses?.primaryKey;
    const primary = key === undefined ? undefined : values.find(value => value.key === key);
    return primary === undefined ? values : [primary, ...values.filter(value => value.key !== key)];
}

function appendDefinitionRow(list: HTMLElement, label: string, value: string): void {
    const row = createElement("div", "dd-definition-row");
    row.append(createElement("dt", "dd-definition-row__term", label), createElement("dd", "dd-definition-row__value", value));
    list.append(row);
}

function appendSources(target: HTMLElement, sources: Parameters<typeof renderSourceAttributions>[0]): void {
    const rendered = renderSourceAttributions(sources, true);
    if (rendered !== null) target.append(rendered);
}

function joinOptional(left: string | undefined, right: string | undefined, separator: string): string | undefined {
    if (left === undefined) return right;
    if (right === undefined) return left;
    return `${left}${separator}${right}`;
}
