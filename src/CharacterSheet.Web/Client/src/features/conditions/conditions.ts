import type { CharacterRoutineUiState } from "../../app-state.js";
import type {
    CharacterConditionOccurrenceResponse,
    CharacterConditionStateInput
} from "../../character-state-api.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import type { RoutineCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderConditionsCard(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const card = createElement("article", "dd-combat-band__conditions");
    card.setAttribute("data-conditions-card", "true");

    const header = createElement("div", "dd-combat-conditions__header");
    header.append(createElement("h3", "dd-combat-band__heading", "Conditions"));
    card.append(header);

    if (routine.status === "idle" || routine.status === "loading") {
        card.append(createElement("span", "dd-combat-conditions__empty", "-"));
        return card;
    }
    if (routine.status === "error" || routine.state === null) {
        card.append(createInlineState(
            routine.message ?? "Conditions are unavailable.",
            "error"));
        return card;
    }

    const editable = !readOnly && !routine.state.readOnly;
    const pending = routine.mutation !== null;
    if (editable) {
        header.append(createButton(
            "Add Condition",
            "dd-button dd-button--secondary dd-condition-add",
            handlers.openConditionChooser,
            pending));
    }

    if (routine.mutationError !== undefined) {
        card.append(createInlineState(routine.mutationError, "error"));
    }

    const list = createElement("div", "dd-combat-conditions__list");
    for (const condition of routine.state.conditions) {
        list.append(renderCondition(condition, routine, editable, handlers));
    }
    if (list.children.length === 0) {
        list.append(createElement("span", "dd-combat-conditions__empty", "-"));
    }
    card.append(list);

    if (editable && routine.conditionChooser.kind === "open") {
        card.append(renderConditionChooser(routine, handlers));
    }
    return card;
}

function renderCondition(
    condition: CharacterConditionOccurrenceResponse,
    routine: CharacterRoutineUiState,
    editable: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const item = createElement("div", "dd-condition");
    item.setAttribute("data-condition-id", condition.id);
    item.setAttribute(
        "data-condition-kind",
        condition.ruleConceptKey === null ? "custom" : "rules-defined");

    const summary = createElement("div", "dd-condition__summary");
    const identity = createElement("span", "dd-condition__identity");
    identity.append(createElement(
        "strong",
        "dd-condition__name",
        conditionDisplayName(condition, routine)));

    if (condition.duration !== null) {
        identity.append(createElement(
            "span",
            "dd-condition__duration",
            condition.duration));
    }
    summary.append(identity);

    if (condition.level !== null) {
        summary.append(renderLevelControl(condition, routine, editable, handlers));
    } else if (condition.counterCurrent !== null || condition.counterMaximum !== null) {
        summary.append(createElement(
            "strong",
            "dd-condition__counter",
            formatCounter(condition.counterCurrent, condition.counterMaximum)));
    }

    if (editable) {
        const actions = createElement("span", "dd-condition__actions");
        const edit = createButton(
            "Edit",
            "dd-button dd-button--ghost dd-condition__edit",
            () => toggleConditionEditor(item),
            routine.mutation !== null);
        const remove = createButton(
            "×",
            "dd-button dd-button--ghost dd-condition__remove",
            () => handlers.removeCondition(condition.id),
            routine.mutation !== null);
        remove.setAttribute("aria-label", "Remove " + conditionDisplayName(condition, routine));
        actions.append(edit, remove);
        summary.append(actions);
    }
    item.append(summary);

    if (editable) {
        const editor = renderConditionEditor(condition, handlers, routine.mutation !== null);
        editor.hidden = true;
        item.append(editor);
    }
    return item;
}

function renderLevelControl(
    condition: CharacterConditionOccurrenceResponse,
    routine: CharacterRoutineUiState,
    editable: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const control = createElement("span", "dd-condition__level");
    const level = condition.level ?? 0;
    if (editable) {
        control.append(createButton(
            "−",
            "dd-condition__level-button",
            () => handlers.updateCondition(condition.id, {
                ...stateInput(condition),
                level: Math.max(0, level - 1)
            }),
            routine.mutation !== null || level <= 0));
    }
    const value = createElement("strong", "dd-condition__level-value", String(level));
    value.setAttribute("aria-label", "Level " + String(level));
    control.append(value);
    if (editable) {
        control.append(createButton(
            "+",
            "dd-condition__level-button",
            () => handlers.updateCondition(condition.id, {
                ...stateInput(condition),
                level: level + 1
            }),
            routine.mutation !== null));
    }
    return control;
}

function renderConditionEditor(
    condition: CharacterConditionOccurrenceResponse,
    handlers: RoutineCharacterHandlers,
    pending: boolean
): HTMLElement {
    const form = createElement("form", "dd-condition__editor");
    const customName = condition.ruleConceptKey === null
        ? renderInput("Name", "text", condition.customName ?? "")
        : null;
    const level = renderInput("Level / Stage", "number", optionalNumber(condition.level));
    const current = renderInput("Counter", "number", optionalNumber(condition.counterCurrent));
    const maximum = renderInput("Maximum", "number", optionalNumber(condition.counterMaximum));
    const duration = renderInput("Duration", "text", condition.duration ?? "");
    const notes = renderInput("Notes", "text", condition.notes ?? "");

    for (const field of [customName, level, current, maximum, duration, notes]) {
        if (field !== null) form.append(field.label);
    }
    const save = createElement("button", "dd-button dd-button--secondary", "Save") as HTMLButtonElement;
    save.type = "submit";
    save.disabled = pending;
    form.append(save);
    form.addEventListener("submit", event => {
        event.preventDefault();
        handlers.updateCondition(condition.id, {
            customName: customName === null ? null : customName.input.value,
            level: optionalInteger(level.input.value),
            counterCurrent: optionalInteger(current.input.value),
            counterMaximum: optionalInteger(maximum.input.value),
            duration: optionalText(duration.input.value),
            notes: optionalText(notes.input.value)
        });
    });
    return form;
}

function renderConditionChooser(
    routine: CharacterRoutineUiState,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const chooser = routine.conditionChooser;
    const panel = createElement("section", "dd-condition-chooser");
    panel.setAttribute("data-condition-chooser", "true");
    if (chooser.kind !== "open") return panel;

    panel.append(createElement("h4", "dd-condition-chooser__title", "Add Condition"));

    const search = createElement("form", "dd-condition-chooser__search");
    const input = createElement("input", "dd-condition-chooser__input") as HTMLInputElement;
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search conditions or type a custom condition";
    input.setAttribute("aria-label", "Condition name");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search") as HTMLButtonElement;
    submit.type = "submit";
    const custom = createElement("button", "dd-button dd-button--primary", "Create Custom") as HTMLButtonElement;
    custom.type = "button";
    const close = createButton("Close", "dd-button dd-button--ghost", handlers.closeConditionChooser);
    search.append(input, submit, custom, close);

    const options = renderConditionOptions();
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.searchConditions(input.value);
    });
    custom.onclick = () => {
        const name = input.value.trim();
        if (name.length === 0) return;
        handlers.addCustomCondition(name, readConditionOptions(options));
    };
    panel.append(search, options.container);

    if (chooser.status === "loading" || chooser.status === "idle") {
        panel.append(createInlineState("Loading available conditions…", "loading"));
        return panel;
    }
    if (chooser.status === "error") {
        panel.append(createInlineState(chooser.message ?? "Condition search failed.", "error"));
        return panel;
    }

    if (chooser.results.length > 0) {
        const results = createElement("div", "dd-condition-chooser__results");
        for (const rule of chooser.results) {
            const result = createElement("div", "dd-condition-chooser__result");
            result.append(
                createElement("strong", "dd-condition-chooser__result-name", rule.displayName),
                createButton(
                    "Add",
                    "dd-button dd-button--secondary",
                    () => handlers.addRuleCondition(
                        rule.conceptKey,
                        readConditionOptions(options)),
                    routine.mutation !== null));
            results.append(result);
        }
        panel.append(results);
    } else {
        panel.append(createElement(
            "p",
            "dd-condition-chooser__empty",
            chooser.query.length === 0
                ? "No rules-defined conditions are available."
                : "No matching rules-defined condition. Create it as a custom condition if needed."));
    }
    return panel;
}

interface ConditionOptions {
    container: HTMLElement;
    level: HTMLInputElement;
    current: HTMLInputElement;
    maximum: HTMLInputElement;
    duration: HTMLInputElement;
    notes: HTMLInputElement;
}

function renderConditionOptions(): ConditionOptions {
    const container = createElement("details", "dd-condition-chooser__options");
    container.append(createElement("summary", undefined, "More options"));
    const body = createElement("div", "dd-condition-chooser__options-grid");
    const level = renderInput("Level / Stage", "number", "");
    const current = renderInput("Counter", "number", "");
    const maximum = renderInput("Maximum", "number", "");
    const duration = renderInput("Duration", "text", "");
    const notes = renderInput("Notes", "text", "");
    body.append(level.label, current.label, maximum.label, duration.label, notes.label);
    container.append(body);
    return {
        container,
        level: level.input,
        current: current.input,
        maximum: maximum.input,
        duration: duration.input,
        notes: notes.input
    };
}

function readConditionOptions(options: ConditionOptions): CharacterConditionStateInput {
    return {
        level: optionalInteger(options.level.value),
        counterCurrent: optionalInteger(options.current.value),
        counterMaximum: optionalInteger(options.maximum.value),
        duration: optionalText(options.duration.value),
        notes: optionalText(options.notes.value)
    };
}

function renderInput(
    labelText: string,
    type: "text" | "number",
    value: string
): { label: HTMLElement; input: HTMLInputElement } {
    const label = createElement("label", "dd-condition-field");
    label.append(createElement("span", "dd-condition-field__label", labelText));
    const input = createElement("input", "dd-condition-field__input") as HTMLInputElement;
    input.type = type;
    input.value = value;
    if (type === "number") {
        input.min = "0";
        input.step = "1";
        input.inputMode = "numeric";
    }
    label.append(input);
    return { label, input };
}

function conditionDisplayName(
    condition: CharacterConditionOccurrenceResponse,
    routine: CharacterRoutineUiState
): string {
    if (condition.customName !== null) return condition.customName;
    if (condition.ruleConceptKey === null) return "Condition";
    const reference = routine.references[condition.id];
    if (reference?.status === "resolved" && reference.rule.entityType === "condition") {
        return reference.rule.displayName;
    }
    return condition.ruleConceptKey;
}

function stateInput(condition: CharacterConditionOccurrenceResponse): CharacterConditionStateInput {
    return {
        customName: condition.customName,
        level: condition.level,
        counterCurrent: condition.counterCurrent,
        counterMaximum: condition.counterMaximum,
        duration: condition.duration,
        notes: condition.notes
    };
}

function toggleConditionEditor(item: HTMLElement): void {
    const editor = Array.from(item.children).find(child =>
        (child as HTMLElement).className.split(/\s+/).includes("dd-condition__editor")) as HTMLElement | undefined;
    if (editor !== undefined) editor.hidden = !editor.hidden;
}

function optionalInteger(value: string): number | null {
    const normalized = value.trim();
    if (normalized.length === 0) return null;
    const parsed = Number(normalized);
    return Number.isInteger(parsed) && parsed >= 0 ? parsed : null;
}

function optionalText(value: string): string | null {
    const normalized = value.trim();
    return normalized.length === 0 ? null : normalized;
}

function optionalNumber(value: number | null): string {
    return value === null ? "" : String(value);
}

function formatCounter(current: number | null, maximum: number | null): string {
    if (current !== null && maximum !== null) return String(current) + " / " + String(maximum);
    if (current !== null) return String(current);
    if (maximum !== null) return "/ " + String(maximum);
    return "-";
}
