import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView
} from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";
import { renderSplitStat } from "../../ui/split-stat.js";
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

    card.append(renderSplitStat({
        label: "Armor Class",
        primaryValue: primary === undefined ? "-" : formatMechanicalValue(primary),
        primaryAttributes: metricAttributes(primary, ARMOR_CLASS_SCAFFOLD[0]),
        className: "dd-armor-class__presentation",
        secondary: [
            {
                label: "Touch AC",
                value: touch === undefined ? "-" : formatMechanicalValue(touch),
                attributes: metricAttributes(touch, ARMOR_CLASS_SCAFFOLD[1])
            },
            {
                label: "Flat-Footed AC",
                value: flatFooted === undefined ? "-" : formatMechanicalValue(flatFooted),
                attributes: metricAttributes(flatFooted, ARMOR_CLASS_SCAFFOLD[2])
            }
        ]
    }));
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

function metricAttributes(
    value: CalculatedMechanicalValueView | undefined,
    scaffold: MechanicalScaffoldSlot
): Record<string, string> {
    return value === undefined
        ? { "data-sheet-scaffold-key": scaffold.id }
        : { "data-mechanic-key": value.key };
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
