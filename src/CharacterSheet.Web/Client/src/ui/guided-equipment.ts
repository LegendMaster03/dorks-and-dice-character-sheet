import type { CharacterRulesInputStateResponse } from "../character-state-api.js";
import { getRulesCoreContext } from "../rules-core-context.js";
import {
    RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX,
    toRulesCoreRuntimeRollStateKey
} from "../rules-core-runtime-input.js";
import {
    createButton,
    createElement,
    createInlineState,
    createSectionCard
} from "./components.js";
import type {
    CharacterMechanicsView,
    DisplayFieldView,
    SourceAttributionView
} from "./character-mechanics.js";
import { renderSourceAttributions } from "./source-attribution.js";

const STARTING_EQUIPMENT_GRANT_KINDS = new Set([
    "starting-equipment-item",
    "starting-equipment-custom",
    "starting-equipment-currency"
]);

export interface GuidedEquipmentItemView {
    conceptKey?: string;
    displayName: string;
    quantity: number;
    detail?: string;
}

export interface GuidedEquipmentOptionView {
    value: string;
    displayName: string;
    items: readonly GuidedEquipmentItemView[];
    fields?: readonly DisplayFieldView[];
}

export interface GuidedEquipmentChoiceView {
    choiceKey: string;
    displayName: string;
    state: string;
    required: boolean;
    options: readonly GuidedEquipmentOptionView[];
    selectedValue?: string;
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface GuidedEquipmentRollView {
    rollKey: string;
    displayName: string;
    state: string;
    required: boolean;
    selectedValue?: number;
    detail?: string;
}

export interface GuidedEquipmentConflictView {
    conflictKey: string;
    message: string;
}

export interface GuidedStartingEquipmentView {
    grants: readonly GuidedEquipmentItemView[];
    choices: readonly GuidedEquipmentChoiceView[];
    rolls: readonly GuidedEquipmentRollView[];
    conflicts: readonly GuidedEquipmentConflictView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface GuidedEquipmentHandlers {
    selectChoice(choiceKey: string, value: string): void;
    materializeItems(): void;
}

export function projectGuidedStartingEquipment(
    mechanics: CharacterMechanicsView | null,
    rulesInputs: readonly CharacterRulesInputStateResponse[] = []
): GuidedStartingEquipmentView | null {
    if (mechanics === null) return null;

    const projection = getRulesCoreContext(mechanics);
    if (projection === undefined) return null;

    const rawGrants = (projection.grants ?? []).filter(grant =>
        STARTING_EQUIPMENT_GRANT_KINDS.has(grant.kind));
    const ruleChoices = (mechanics.ruleChoices ?? []).filter(choice =>
        choice.kind === "starting-equipment");
    const rawMechanics = (projection.mechanics ?? []).filter(value =>
        value.kind === "starting-equipment");
    const startingMechanicKeys = new Set(rawMechanics.map(value => value.mechanicKey));
    const rawConflicts = (mechanics.projectionConflicts ?? []).filter(conflict =>
        conflict.conflictKey.includes("starting-equipment")
        || conflict.relatedMechanicKeys.some(key => startingMechanicKeys.has(key)));
    const conflicts = rawConflicts.map(conflict => ({
        conflictKey: conflict.conflictKey,
        message: conflict.message
    }));

    const storedRolls = new Map<string, number>();
    for (const input of rulesInputs) {
        if (input.kind !== "integerFact"
            || input.integerValue === null
            || !input.key.startsWith(RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX)) {
            continue;
        }
        const rollKey = input.key.slice(RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX.length);
        if (rollKey.length > 0) storedRolls.set(rollKey, input.integerValue);
    }

    const rollKeys = new Set<string>();
    const rollMechanics = new Map<string, (typeof rawMechanics)[number]>();
    for (const mechanic of rawMechanics) {
        for (const rollKey of mechanic.requiredRolls ?? []) {
            rollKeys.add(rollKey);
            rollMechanics.set(rollKey, mechanic);
        }
        for (const contribution of mechanic.contributions ?? []) {
            if (storedRolls.has(contribution.contributionKey)) {
                rollKeys.add(contribution.contributionKey);
                rollMechanics.set(contribution.contributionKey, mechanic);
            }
        }
    }
    for (const conflict of rawConflicts) {
        if (!conflict.conflictKey.startsWith("conflict.")) continue;
        const candidate = conflict.conflictKey.slice("conflict.".length);
        if (!storedRolls.has(candidate)) continue;
        const relatedMechanic = rawMechanics.find(value =>
            conflict.relatedMechanicKeys.includes(value.mechanicKey));
        if (relatedMechanic !== undefined) {
            rollKeys.add(candidate);
            rollMechanics.set(candidate, relatedMechanic);
        }
    }

    const rolls: GuidedEquipmentRollView[] = [...rollKeys]
        .map(rollKey => {
            const mechanic = rollMechanics.get(rollKey);
            const required = mechanic?.requiredRolls?.includes(rollKey) === true;
            return {
                rollKey,
                displayName: mechanic === undefined
                    ? "Starting Equipment Roll"
                    : `${mechanic.displayName} Roll`,
                state: mechanic?.state ?? (required ? "roll-required" : "resolved"),
                required,
                selectedValue: storedRolls.get(rollKey),
                detail: formatMechanicDetail(mechanic)
            };
        })
        .sort((left, right) => left.displayName.localeCompare(right.displayName));

    if (rawGrants.length === 0
        && ruleChoices.length === 0
        && rolls.length === 0
        && conflicts.length === 0) {
        return null;
    }

    const grouped = new Map<string, GuidedEquipmentItemView>();
    for (const grant of rawGrants) {
        const conceptKey = grant.kind === "starting-equipment-item"
            ? grant.targetKey
            : undefined;
        const detail = grant.kind === "starting-equipment-currency"
            ? "Starting currency"
            : grant.kind === "starting-equipment-custom"
                ? "Special starting equipment"
                : undefined;
        const groupingKey = [
            grant.kind,
            grant.targetKey,
            grant.displayName,
            grant.sourceConceptKey ?? ""
        ].join("|");
        const existing = grouped.get(groupingKey);
        if (existing === undefined) {
            grouped.set(groupingKey, {
                conceptKey,
                displayName: grant.displayName,
                quantity: 1,
                detail
            });
        } else {
            grouped.set(groupingKey, {
                ...existing,
                quantity: existing.quantity + 1
            });
        }
    }

    const choices: GuidedEquipmentChoiceView[] = ruleChoices.map(choice => ({
        choiceKey: choice.choiceKey,
        displayName: choice.displayName,
        state: choice.state,
        required: true,
        options: choice.options.map(option => ({
            value: option.value,
            displayName: option.displayName,
            items: option.conceptKey === undefined
                ? []
                : [{
                    conceptKey: option.conceptKey,
                    displayName: option.displayName,
                    quantity: 1
                }]
        })),
        selectedValue: choice.selectedValue,
        sourceAttributions: choice.sourceAttributions
    }));

    const sourceAttributions = uniqueSourceAttributions(
        ruleChoices.flatMap(choice => choice.sourceAttributions ?? []));

    return {
        grants: [...grouped.values()].sort((left, right) =>
            left.displayName.localeCompare(right.displayName)),
        choices,
        rolls,
        conflicts,
        sourceAttributions: sourceAttributions.length === 0 ? undefined : sourceAttributions
    };
}

export function renderGuidedStartingEquipment(
    equipment: GuidedStartingEquipmentView,
    handlers: GuidedEquipmentHandlers,
    pending = false
): HTMLElement {
    const section = createSectionCard("Starting Equipment", "dd-guided-builder__equipment");
    section.setAttribute("data-guided-starting-equipment", "true");
    section.append(createElement(
        "p",
        "dd-guided-builder__section-copy",
        "Review equipment granted automatically and make any required starting-equipment choices or rolls."));

    if (equipment.grants.length > 0) {
        section.append(renderEquipmentItems("Granted Equipment", equipment.grants));
    }

    if (equipment.choices.length > 0) {
        const choices = createElement("div", "dd-guided-equipment__choices");
        for (const choice of equipment.choices) {
            choices.append(renderEquipmentChoice(choice, handlers, pending));
        }
        section.append(choices);
    }

    if (equipment.rolls.length > 0) {
        const rolls = createElement("div", "dd-guided-equipment__choices");
        for (const roll of equipment.rolls) {
            rolls.append(renderEquipmentRoll(roll, handlers, pending));
        }
        section.append(rolls);
    }

    if (equipment.grants.length === 0
        && equipment.choices.length === 0
        && equipment.rolls.length === 0
        && equipment.conflicts.length === 0) {
        section.append(createInlineState(
            "No starting equipment grants, choices, or rolls are required.",
            "neutral"));
    }

    if (equipment.conflicts.length > 0) {
        const conflicts = createElement("section", "dd-guided-builder__conflicts");
        conflicts.append(createElement(
            "h3",
            "dd-guided-builder__subheading",
            "Starting Equipment Conflicts"));
        for (const conflict of equipment.conflicts) {
            const item = createInlineState(conflict.message, "warning");
            item.setAttribute("data-rule-conflict-key", conflict.conflictKey);
            conflicts.append(item);
        }
        section.append(conflicts);
    }

    const hasUnresolvedChoice = equipment.choices.some(choice =>
        choice.required && choice.state !== "resolved");
    const hasUnresolvedRoll = equipment.rolls.some(roll =>
        roll.required && roll.state !== "resolved");
    const canMaterialize = !pending
        && !hasUnresolvedChoice
        && !hasUnresolvedRoll
        && equipment.conflicts.length === 0;
    const materialization = createElement("section", "dd-guided-equipment__materialize");
    materialization.append(createElement(
        "p",
        "dd-guided-builder__section-copy",
        "Apply the resolved starting equipment to this Character. Item and special-equipment grants become normal Inventory entries, and starting currency is added to the Character's Currency balances. Reapplying adjusts only state previously acquired from starting equipment; later manual changes are preserved."));
    if (equipment.grants.some(grant => grant.detail === "Starting currency")) {
        materialization.append(createInlineState(
            "Starting currency is applied to the Character's Currency balances rather than represented as an Inventory item.",
            "neutral"));
    }
    const actions = createElement("div", "dd-build-choice__actions");
    const apply = createButton(
        "Apply Starting Equipment",
        "dd-button dd-button--primary",
        handlers.materializeItems,
        !canMaterialize);
    apply.setAttribute("data-guided-equipment-materialize", "true");
    actions.append(apply);
    materialization.append(actions);
    section.append(materialization);

    const sources = renderSourceAttributions(equipment.sourceAttributions, true);
    if (sources !== null) section.append(sources);
    return section;
}

function renderEquipmentChoice(
    choice: GuidedEquipmentChoiceView,
    handlers: GuidedEquipmentHandlers,
    pending: boolean
): HTMLElement {
    const fieldset = createElement("fieldset", "dd-build-choice dd-guided-equipment__choice");
    fieldset.setAttribute("data-guided-equipment-choice", choice.choiceKey);
    fieldset.setAttribute("data-guided-equipment-choice-state", choice.state);
    fieldset.disabled = pending;

    const legend = createElement("legend", "dd-build-choice__label", choice.displayName);
    fieldset.append(legend);

    if (choice.required && choice.selectedValue === undefined) {
        fieldset.append(createInlineState("Choose one option.", "warning"));
    }

    if (choice.options.length === 0) {
        fieldset.append(createInlineState(
            "This starting-equipment choice has no selectable options.",
            "warning"));
    } else {
        const options = createElement("div", "dd-guided-equipment__options");
        for (const option of choice.options) {
            const label = createElement("label", "dd-guided-equipment__option");
            label.setAttribute("data-guided-equipment-option", option.value);

            const input = createElement("input");
            input.type = "radio";
            input.name = `guided-equipment:${choice.choiceKey}`;
            input.value = option.value;
            input.checked = option.value === choice.selectedValue;
            input.addEventListener("change", () => {
                if (input.checked) handlers.selectChoice(choice.choiceKey, option.value);
            });

            const content = createElement("span", "dd-guided-equipment__option-content");
            content.append(createElement(
                "strong",
                "dd-guided-equipment__option-title",
                option.displayName));

            const summary = renderEquipmentOptionContents(option);
            if (summary !== null) content.append(summary);
            label.append(input, content);
            options.append(label);
        }
        fieldset.append(options);
    }

    const sources = renderSourceAttributions(choice.sourceAttributions, true);
    if (sources !== null) fieldset.append(sources);
    return fieldset;
}

function renderEquipmentRoll(
    roll: GuidedEquipmentRollView,
    handlers: GuidedEquipmentHandlers,
    pending: boolean
): HTMLElement {
    const fieldset = createElement("fieldset", "dd-build-choice dd-guided-equipment__choice");
    fieldset.setAttribute("data-guided-equipment-roll", roll.rollKey);
    fieldset.setAttribute("data-guided-equipment-roll-state", roll.state);
    fieldset.disabled = pending;
    fieldset.append(createElement("legend", "dd-build-choice__label", roll.displayName));

    if (roll.detail !== undefined) {
        fieldset.append(createElement("p", "dd-guided-builder__section-copy", roll.detail));
    }
    if (roll.required && roll.selectedValue === undefined) {
        fieldset.append(createInlineState("Enter the roll result required by Rules Core.", "warning"));
    }

    const actions = createElement("div", "dd-build-choice__actions");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "number";
    input.step = "1";
    input.required = true;
    input.setAttribute("aria-label", roll.displayName);
    if (roll.selectedValue !== undefined) input.value = String(roll.selectedValue);
    const save = createButton(
        roll.selectedValue === undefined ? "Set Roll" : "Replace Roll",
        "dd-button dd-button--secondary",
        () => {
            if (!input.checkValidity()) {
                input.reportValidity();
                return;
            }
            const value = Number(input.value);
            if (Number.isInteger(value)) {
                handlers.selectChoice(
                    toRulesCoreRuntimeRollStateKey(roll.rollKey),
                    String(value));
            }
        },
        pending);
    actions.append(input, save);
    fieldset.append(actions);
    return fieldset;
}

function renderEquipmentItems(
    heading: string,
    items: readonly GuidedEquipmentItemView[]
): HTMLElement {
    const group = createElement("section", "dd-guided-equipment__grants");
    group.append(createElement("h3", "dd-guided-builder__subheading", heading));
    const list = createElement("ul", "dd-guided-equipment__item-list");
    for (const item of items) {
        const row = createElement("li", "dd-guided-equipment__item");
        const quantity = item.quantity === 1 ? "" : `${item.quantity} × `;
        row.append(createElement(
            "span",
            "dd-guided-equipment__item-name",
            `${quantity}${item.displayName}`));
        if (item.detail !== undefined && item.detail.trim().length > 0) {
            row.append(createElement(
                "span",
                "dd-guided-equipment__item-detail",
                item.detail));
        }
        list.append(row);
    }
    group.append(list);
    return group;
}

function renderEquipmentOptionContents(option: GuidedEquipmentOptionView): HTMLElement | null {
    if (option.items.length === 0 && (option.fields?.length ?? 0) === 0) {
        return null;
    }

    const content = createElement("div", "dd-guided-equipment__option-details");
    if (option.items.length > 0) {
        const list = createElement("ul", "dd-guided-equipment__item-list");
        for (const item of option.items) {
            const row = createElement("li", "dd-guided-equipment__item");
            const quantity = item.quantity === 1 ? "" : `${item.quantity} × `;
            row.append(createElement(
                "span",
                "dd-guided-equipment__item-name",
                `${quantity}${item.displayName}`));
            if (item.detail !== undefined && item.detail.trim().length > 0) {
                row.append(createElement(
                    "span",
                    "dd-guided-equipment__item-detail",
                    item.detail));
            }
            list.append(row);
        }
        content.append(list);
    }

    if ((option.fields?.length ?? 0) > 0) {
        const fields = createElement("dl", "dd-guided-equipment__fields");
        for (const field of option.fields ?? []) {
            fields.append(
                createElement("dt", "dd-guided-equipment__field-label", field.label),
                createElement("dd", "dd-guided-equipment__field-value", field.value));
        }
        content.append(fields);
    }
    return content;
}

function formatMechanicDetail(
    mechanic: ReturnType<typeof getRulesCoreContext> extends infer _ ? {
        textValue?: string | null;
        unit?: string | null;
    } | undefined : never
): string | undefined {
    if (mechanic === undefined) return undefined;
    const text = mechanic.textValue?.trim();
    const unit = mechanic.unit?.trim();
    if (text === undefined || text.length === 0) return unit || undefined;
    if (unit === undefined || unit.length === 0) return text;
    return `${text} ${unit}`;
}

function uniqueSourceAttributions(
    values: readonly SourceAttributionView[]
): readonly SourceAttributionView[] {
    const unique = new Map<string, SourceAttributionView>();
    for (const value of values) {
        if (!unique.has(value.key)) unique.set(value.key, value);
    }
    return [...unique.values()];
}

export { toRulesCoreRuntimeRollStateKey };
