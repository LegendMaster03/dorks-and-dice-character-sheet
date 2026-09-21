import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView
} from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";
import {
    findScaffoldValue,
    renderMechanicalScaffold,
    type MechanicalScaffoldSlot
} from "../../core/mechanics/mechanic-value.js";

export const ARMOR_CLASS_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "armor-class", label: "Armor Class", keys: ["defense.ac"], labels: ["Armor Class", "AC"] },
    { id: "touch-armor-class", label: "Touch Armor Class", keys: ["defense.ac.touch"], labels: ["Touch", "Touch AC"] },
    { id: "flat-footed-armor-class", label: "Flat-Footed Armor Class", keys: ["defense.ac.flat-footed"], labels: ["Flat-Footed", "Flat-Footed AC"] }
];

export const SECONDARY_DEFENSE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "damage-reduction", label: "Damage Reduction", keys: ["defense.damage-reduction"] },
    { id: "spell-resistance", label: "Spell Resistance", keys: ["defense.spell-resistance"] }
];

export const DEFENSE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    ...ARMOR_CLASS_SCAFFOLD,
    ...SECONDARY_DEFENSE_SCAFFOLD
];

export function renderArmorClassQuickCard(
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const values = orderedDefenses(mechanics);
    const usedKeys = new Set<string>();
    const primary = findPrimaryArmorClassValue(mechanics, values, usedKeys);
    if (primary !== undefined) usedKeys.add(primary.key);

    const touch = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[1], usedKeys);
    if (touch !== undefined) usedKeys.add(touch.key);
    const flatFooted = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[2], usedKeys);

    const card = createElement("article", "dd-stat dd-stat--armor-class");
    card.setAttribute("data-armor-class-card", "true");

    const primaryRegion = createElement("div", "dd-armor-class__primary");
    primaryRegion.append(
        createElement("h3", "dd-stat__label", "Armor Class"),
        renderArmorClassValue(
            primary,
            ARMOR_CLASS_SCAFFOLD[0],
            "dd-armor-class__primary-value"));

    const variants = createElement("div", "dd-armor-class__variants");
    variants.append(
        renderArmorClassVariant("Touch AC", touch, ARMOR_CLASS_SCAFFOLD[1]),
        renderArmorClassVariant(
            "Flat-Footed AC",
            flatFooted,
            ARMOR_CLASS_SCAFFOLD[2]));

    card.append(primaryRegion, variants);
    return card;
}

export function renderDefenseMechanicsCard(
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const values = orderedDefenses(mechanics);
    const armorClassKeys = new Set(
        resolveArmorClassValues(mechanics, values)
            .filter((value): value is CalculatedMechanicalValueView =>
                value !== undefined)
            .map(value => value.key));
    const secondaryValues = values.filter(value => !armorClassKeys.has(value.key));

    const card = createSectionCard(
        "Defense",
        "dd-mechanic-group-card dd-defense-card");
    const grid = createElement(
        "div",
        "dd-mechanic-group-card__grid dd-defense-card__grid");
    grid.append(...renderMechanicalScaffold(
        secondaryValues,
        SECONDARY_DEFENSE_SCAFFOLD));
    card.append(grid);
    return card;
}

export function renderDefenseScaffold(
    mechanics: CharacterMechanicsView | null
): HTMLElement[] {
    return renderMechanicalScaffold(orderedDefenses(mechanics), DEFENSE_SCAFFOLD);
}

function resolveArmorClassValues(
    mechanics: CharacterMechanicsView | null,
    values: readonly CalculatedMechanicalValueView[]
): readonly (CalculatedMechanicalValueView | undefined)[] {
    const usedKeys = new Set<string>();
    const primary = findPrimaryArmorClassValue(mechanics, values, usedKeys);
    if (primary !== undefined) usedKeys.add(primary.key);
    const touch = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[1], usedKeys);
    if (touch !== undefined) usedKeys.add(touch.key);
    const flatFooted = findScaffoldValue(
        values,
        ARMOR_CLASS_SCAFFOLD[2],
        usedKeys);
    return [primary, touch, flatFooted];
}

function findPrimaryArmorClassValue(
    mechanics: CharacterMechanicsView | null,
    values: readonly CalculatedMechanicalValueView[],
    usedKeys: ReadonlySet<string>
): CalculatedMechanicalValueView | undefined {
    const primaryKey = mechanics?.defenses?.primaryKey;
    if (primaryKey !== undefined) {
        const primary = values.find(value =>
            !usedKeys.has(value.key) && value.key === primaryKey);
        if (primary !== undefined) return primary;
    }
    return findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[0], usedKeys);
}

function renderArmorClassValue(
    value: CalculatedMechanicalValueView | undefined,
    scaffold: MechanicalScaffoldSlot,
    className: string
): HTMLElement {
    const rendered = createElement(
        "strong",
        className,
        value === undefined ? "-" : formatMechanicalValue(value));
    if (value === undefined) {
        rendered.setAttribute("data-sheet-scaffold-key", scaffold.id);
    } else {
        rendered.setAttribute("data-mechanic-key", value.key);
    }
    return rendered;
}

function renderArmorClassVariant(
    label: string,
    value: CalculatedMechanicalValueView | undefined,
    scaffold: MechanicalScaffoldSlot
): HTMLElement {
    const cell = createElement("div", "dd-armor-class__variant");
    if (value === undefined) {
        cell.setAttribute("data-sheet-scaffold-key", scaffold.id);
    } else {
        cell.setAttribute("data-mechanic-key", value.key);
    }
    cell.append(
        createElement("span", "dd-armor-class__variant-label", label),
        createElement(
            "strong",
            "dd-armor-class__variant-value",
            value === undefined ? "-" : formatMechanicalValue(value)));
    return cell;
}

function orderedDefenses(
    mechanics: CharacterMechanicsView | null
): readonly CalculatedMechanicalValueView[] {
    const values = mechanics?.defenses?.values ?? [];
    const key = mechanics?.defenses?.primaryKey;
    const primary = key === undefined
        ? undefined
        : values.find(value => value.key === key);
    return primary === undefined
        ? values
        : [primary, ...values.filter(value => value.key !== key)];
}
