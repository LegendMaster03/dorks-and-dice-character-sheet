import type { CharacterRoutineUiState } from "../../app-state.js";
import type {
    CharacterInventoryItemOccurrenceResponse,
    CharacterInventoryItemOccurrenceStateInput
} from "../../character-state-api.js";
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

    content.append(renderCurrencyBalances(routine, editable, pending, handlers));

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
        if (occurrence.ruleConceptKey === null) {
            item.append(
                createElement(
                    "h3",
                    "dd-inventory-item__name",
                    occurrence.customName ?? "Unnamed custom item"),
                createElement("p", "dd-routine-meta", "Manual inventory item"));
        } else {
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
        }
        item.append(renderOccurrenceStateSummary(occurrence, routine));
        const occurrenceMechanics = renderItemOccurrenceMechanics(
            findItemOccurrenceMechanics(mechanics, occurrence.id));
        if (occurrenceMechanics !== null) item.append(occurrenceMechanics);
        if (editable) {
            item.append(renderOccurrenceStateEditor(
                occurrence,
                routine,
                pending,
                handlers));
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

function renderCurrencyBalances(
    routine: CharacterRoutineUiState,
    editable: boolean,
    pending: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const section = createElement("section", "dd-inventory-currency");
    section.append(createElement("h3", "dd-inventory-currency__title", "Currency"));
    const balances = routine.state?.currencyBalances ?? [];

    if (editable) {
        const form = createElement("form", "dd-inventory-currency__form");
        const keyLabel = createElement("label", "dd-sheet-screen__label", "Currency key");
        const key = createElement("input", "dd-sheet-screen__input");
        key.type = "text";
        key.maxLength = 160;
        key.placeholder = "gp or campaign-scrip";
        keyLabel.append(key);

        const amountLabel = createElement("label", "dd-sheet-screen__label", "Amount");
        const amount = createElement("input", "dd-sheet-screen__input");
        amount.type = "number";
        amount.step = "1";
        amount.min = String(Number.MIN_SAFE_INTEGER);
        amount.max = String(Number.MAX_SAFE_INTEGER);
        amount.inputMode = "numeric";
        amount.value = "0";
        amountLabel.append(amount);

        const save = createElement("button", "dd-button dd-button--secondary", "Set Currency");
        save.type = "submit";
        save.disabled = pending;
        form.append(keyLabel, amountLabel, save);
        form.addEventListener("submit", event => {
            event.preventDefault();
            const normalizedKey = key.value.trim();
            const parsed = parseCurrencyAmount(amount.value);
            if (normalizedKey.length === 0) {
                key.setCustomValidity("Currency key is required.");
                key.reportValidity();
                return;
            }
            key.setCustomValidity("");
            if (parsed === null) {
                amount.setCustomValidity("Amount must be a safe whole number.");
                amount.reportValidity();
                return;
            }
            amount.setCustomValidity("");
            handlers.setCurrencyBalance(normalizedKey, parsed);
        });
        section.append(form);
    }

    if (balances.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No currency balances recorded."));
        return section;
    }

    const list = createElement("div", "dd-inventory-currency__list");
    for (const balance of balances) {
        const row = createElement("div", "dd-inventory-currency__row");
        row.setAttribute("data-currency-key", balance.currencyKey);
        row.append(
            createElement("strong", "dd-inventory-currency__key", balance.currencyKey),
            createElement("span", "dd-inventory-currency__amount", String(balance.amount)));

        if (editable) {
            const input = createElement("input", "dd-sheet-screen__input");
            input.type = "number";
            input.step = "1";
            input.min = String(Number.MIN_SAFE_INTEGER);
            input.max = String(Number.MAX_SAFE_INTEGER);
            input.value = String(balance.amount);
            input.disabled = pending;
            input.setAttribute("aria-label", `${balance.currencyKey} amount`);
            const save = createButton(
                routine.mutation?.kind === "currency-update"
                    && routine.mutation.entryId === balance.currencyKey
                    ? "Saving…"
                    : "Save",
                "dd-button dd-button--secondary",
                () => {
                    const parsed = parseCurrencyAmount(input.value);
                    if (parsed === null) {
                        input.setCustomValidity("Amount must be a safe whole number.");
                        input.reportValidity();
                        return;
                    }
                    input.setCustomValidity("");
                    handlers.setCurrencyBalance(balance.currencyKey, parsed);
                },
                pending);
            const remove = createButton(
                routine.mutation?.kind === "currency-delete"
                    && routine.mutation.entryId === balance.currencyKey
                    ? "Removing…"
                    : "Remove",
                "dd-button dd-button--ghost",
                () => handlers.removeCurrencyBalance(balance.currencyKey),
                pending);
            row.append(input, save, remove);
        }
        list.append(row);
    }
    section.append(list);
    return section;
}

function parseCurrencyAmount(value: string): number | null {
    const normalized = value.trim();
    if (!/^-?\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed) ? parsed : null;
}

function renderOccurrenceStateSummary(
    occurrence: CharacterInventoryItemOccurrenceResponse,
    routine: CharacterRoutineUiState
): HTMLElement {
    const summary = createElement("p", "dd-routine-meta");
    summary.setAttribute("data-inventory-state-summary", occurrence.id);
    const parts = [
        `Qty ${occurrence.quantity}`,
        occurrence.isCarried ? "Carried" : "Not carried",
        occurrence.isEquipped ? "Equipped" : null,
        occurrence.isAttuned ? "Attuned" : null
    ].filter((value): value is string => value !== null);
    if (occurrence.containerOccurrenceId !== null) {
        parts.push(`In ${inventoryOccurrenceLabel(routine, occurrence.containerOccurrenceId)}`);
    }
    summary.textContent = parts.join(" • ");
    return summary;
}

function renderOccurrenceStateEditor(
    occurrence: CharacterInventoryItemOccurrenceResponse,
    routine: CharacterRoutineUiState,
    pending: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const details = createElement("details", "dd-inventory-item__state-details");
    details.setAttribute("data-inventory-state-editor", occurrence.id);
    details.append(createElement("summary", "dd-inventory-item__state-toggle", "Item state"));

    const editor = createElement("div", "dd-inventory-item__state-editor");
    const quantityLabel = createElement("label", "dd-sheet-screen__label", "Quantity");
    const quantity = createElement("input", "dd-sheet-screen__input");
    quantity.type = "number";
    quantity.step = "1";
    quantity.min = "1";
    quantity.max = "2147483647";
    quantity.inputMode = "numeric";
    quantity.value = String(occurrence.quantity);
    quantityLabel.append(quantity);
    editor.append(quantityLabel);

    const carried = stateCheckbox("Carried", occurrence.isCarried);
    const equipped = stateCheckbox("Equipped", occurrence.isEquipped);
    const attuned = stateCheckbox("Attuned", occurrence.isAttuned);
    editor.append(carried.label, equipped.label, attuned.label);

    const containerLabel = createElement("label", "dd-sheet-screen__label", "Container");
    const container = createElement("select", "dd-sheet-screen__input");
    const none = createElement("option");
    none.value = "";
    none.textContent = "None";
    container.append(none);
    for (const candidate of routine.state?.inventoryItemOccurrences ?? []) {
        if (candidate.id === occurrence.id) continue;
        const option = createElement("option");
        option.value = candidate.id;
        option.textContent = inventoryOccurrenceLabel(routine, candidate.id);
        if (candidate.id === occurrence.containerOccurrenceId) {
            option.selected = true;
        }
        container.append(option);
    }
    container.value = occurrence.containerOccurrenceId ?? "";
    containerLabel.append(container);
    editor.append(containerLabel);

    const save = createButton(
        routine.mutation?.kind === "inventory-update"
            && routine.mutation.entryId === occurrence.id
            ? "Saving…"
            : "Save item state",
        "dd-button dd-button--secondary",
        () => {
            const parsed = parseInventoryQuantity(quantity.value);
            if (parsed === null) {
                quantity.setCustomValidity("Quantity must be a whole number from 1 through 2147483647.");
                quantity.reportValidity();
                return;
            }
            quantity.setCustomValidity("");
            const input: CharacterInventoryItemOccurrenceStateInput = {
                quantity: parsed,
                isCarried: carried.input.checked,
                isEquipped: equipped.input.checked,
                isAttuned: attuned.input.checked,
                containerOccurrenceId: container.value.length === 0 ? null : container.value
            };
            handlers.updateInventoryItem(occurrence.id, input);
        },
        pending);
    editor.append(save);
    details.append(editor);
    return details;
}

function stateCheckbox(
    labelText: string,
    checked: boolean
): { label: HTMLLabelElement; input: HTMLInputElement } {
    const label = createElement("label", "dd-inventory-item__state-check");
    const input = createElement("input");
    input.type = "checkbox";
    input.checked = checked;
    label.append(input, createElement("span", undefined, labelText));
    return { label, input };
}

function inventoryOccurrenceLabel(
    routine: CharacterRoutineUiState,
    occurrenceId: string
): string {
    const occurrences = routine.state?.inventoryItemOccurrences ?? [];
    const occurrence = occurrences.find(value => value.id === occurrenceId);
    if (occurrence === undefined) return "Unavailable container";
    const reference = routine.references[occurrenceId];
    const base = occurrence.ruleConceptKey === null
        ? (occurrence.customName ?? "Unnamed custom item")
        : reference === undefined
            ? occurrence.ruleConceptKey
            : toRuleReferenceDisplay(reference).value;
    const matching = occurrences.filter(value =>
        occurrence.ruleConceptKey === null
            ? value.ruleConceptKey === null && value.customName === occurrence.customName
            : value.ruleConceptKey === occurrence.ruleConceptKey);
    if (matching.length <= 1) return base;
    const index = matching.findIndex(value => value.id === occurrenceId);
    return index < 0 ? base : `${base} #${index + 1}`;
}

function parseInventoryQuantity(value: string): number | null {
    const normalized = value.trim();
    if (!/^\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed) && parsed >= 1 && parsed <= 2147483647
        ? parsed
        : null;
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

