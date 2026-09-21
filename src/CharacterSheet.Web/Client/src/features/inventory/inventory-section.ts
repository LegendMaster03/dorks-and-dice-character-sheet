import type { CharacterRoutineUiState } from "../../app-state.js";
import {
    findItemOccurrenceMechanics,
    type InventoryMechanicsView
} from "../../ui/character-mechanics.js";
import {
    renderInventoryMechanics,
    renderItemOccurrenceMechanics
} from "../../ui/procedure-components.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import { toRuleReferenceDisplay } from "../../ui/sheet-model.js";
import type { RoutineCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderInventorySection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers,
    mechanics: InventoryMechanicsView | undefined
): HTMLElement {
    const content = createElement("div", "dd-routine-section dd-inventory");
    if (routine.status === "idle" || routine.status === "loading") {
        content.append(createInlineState("Loading Character inventory…", "loading"));
        appendInventoryMechanicsPresentation(content, mechanics);
        return content;
    }
    if (routine.status === "error" || routine.state === null) {
        content.append(createInlineState(
            routine.message ?? "Character inventory is unavailable.",
            "error"));
        appendInventoryMechanicsPresentation(content, mechanics);
        return content;
    }

    const pending = routine.mutation !== null;
    const editable = !readOnly && !routine.state.readOnly;
    if (routine.mutationError !== undefined) {
        content.append(createInlineState(routine.mutationError, "error"));
    }

    if (editable) {
        const actions = createElement("div", "dd-routine-actions");
        actions.append(createButton(
            "Add Item",
            "dd-button dd-button--primary",
            handlers.openInventoryChooser,
            pending));
        content.append(actions);
    }

    if (routine.inventoryChooser.kind === "open" && editable) {
        content.append(renderInventoryChooser(routine, handlers));
    }

    if (routine.state.inventoryItemOccurrences.length === 0) {
        content.append(createElement(
            "p",
            "dd-routine-empty",
            editable
                ? "No items yet. Add one from the rule catalog."
                : "No items have been added."));
    }

    const list = createElement("div", "dd-inventory-list");
    for (const occurrence of routine.state.inventoryItemOccurrences) {
        const item = createElement("article", "dd-inventory-item");
        item.setAttribute("data-inventory-occurrence-id", occurrence.id);
        const reference = routine.references[occurrence.id] ?? {
            status: "loading" as const,
            conceptKey: occurrence.ruleConceptKey
        };
        const display = toRuleReferenceDisplay(reference);
        const title = display.tone === "unavailable"
            ? "Unavailable item reference"
            : display.value;
        item.append(
            createElement("h3", "dd-inventory-item__name", title),
            createElement(
                "p",
                "dd-routine-meta",
                display.detail ?? occurrence.ruleConceptKey)
        );
        const occurrenceMechanics = renderItemOccurrenceMechanics(
            findItemOccurrenceMechanics(mechanics, occurrence.id));
        if (occurrenceMechanics !== null) item.append(occurrenceMechanics);
        if (editable) {
            item.append(createButton(
                routine.mutation?.kind === "inventory-delete"
                    && routine.mutation.entryId === occurrence.id
                    ? "Removing…"
                    : "Remove",
                "dd-button dd-button--ghost",
                () => {
                    if (window.confirm("Remove this item from the Character?")) {
                        handlers.removeInventoryItem(occurrence.id);
                    }
                },
                pending));
        }
        list.append(item);
    }
    if (list.children.length > 0) content.append(list);
    appendInventoryMechanicsPresentation(content, mechanics);
    return content;
}

function appendInventoryMechanicsPresentation(
    target: HTMLElement,
    mechanics: InventoryMechanicsView | undefined
): void {
    const rendered = renderInventoryMechanics(mechanics);
    if (rendered !== null) target.append(rendered);
}

function renderInventoryChooser(
    routine: CharacterRoutineUiState,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const chooser = routine.inventoryChooser;
    const section = createElement("section", "dd-rule-chooser dd-inventory-chooser");
    if (chooser.kind !== "open") return section;

    const heading = createElement("h3", "dd-rule-chooser__title", "Add Inventory Item");
    const search = createElement("form", "dd-rule-chooser__search");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search available items";
    input.setAttribute("aria-label", "Search available items");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search");
    submit.type = "submit";
    const close = createButton("Close", "dd-button dd-button--ghost", handlers.closeInventoryChooser);
    search.append(input, submit, close);
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.searchInventory(input.value);
    });
    section.append(heading, search);

    if (chooser.status === "idle" || chooser.status === "loading") {
        section.append(createInlineState("Loading available items…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(chooser.message ?? "Item search failed.", "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching items."));
        return section;
    }

    const results = createElement("div", "dd-rule-chooser__results");
    for (const rule of chooser.results) {
        const result = createElement("article", "dd-rule-chooser__result");
        result.append(
            createElement("strong", "dd-rule-chooser__result-name", rule.displayName),
            createElement(
                "span",
                "dd-rule-chooser__result-meta",
                [rule.editionDisplayName, rule.sourceCode].filter(Boolean).join(" • ")),
            createElement("span", "dd-rule-chooser__result-key", rule.conceptKey),
            createButton(
                "Add",
                "dd-button dd-button--primary",
                () => handlers.addInventoryItem(rule.conceptKey),
                routine.mutation !== null)
        );
        results.append(result);
    }
    section.append(results);
    return section;
}

