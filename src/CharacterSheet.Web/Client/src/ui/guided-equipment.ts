import {
    createElement,
    createInlineState,
    createSectionCard
} from "./components.js";
import type { DisplayFieldView, SourceAttributionView } from "./character-mechanics.js";
import { renderSourceAttributions } from "./source-attribution.js";

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

export interface GuidedStartingEquipmentView {
    grants: readonly GuidedEquipmentItemView[];
    choices: readonly GuidedEquipmentChoiceView[];
    sourceAttributions?: readonly SourceAttributionView[];
}

export interface GuidedEquipmentHandlers {
    selectChoice(choiceKey: string, value: string): void;
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
        "Review equipment granted automatically and make any required starting-equipment choices."));

    if (equipment.grants.length > 0) {
        section.append(renderEquipmentItems("Granted Equipment", equipment.grants));
    }

    if (equipment.choices.length === 0) {
        if (equipment.grants.length === 0) {
            section.append(createInlineState(
                "No starting equipment grants or choices are required.",
                "neutral"));
        }
    } else {
        const choices = createElement("div", "dd-guided-equipment__choices");
        for (const choice of equipment.choices) {
            choices.append(renderEquipmentChoice(choice, handlers, pending));
        }
        section.append(choices);
    }

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
