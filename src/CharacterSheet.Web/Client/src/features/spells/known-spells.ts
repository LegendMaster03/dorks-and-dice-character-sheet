import type { CharacterRoutineUiState } from "../../app-state.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import { toRuleReferenceDisplay } from "../../ui/sheet-model.js";
import type { KnownSpellCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderKnownSpellsSection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: KnownSpellCharacterHandlers
): HTMLElement {
    const section = createElement("section", "dd-known-spells");
    section.append(createElement("h3", "dd-known-spells__title", "Known Spells"));

    if (routine.status === "idle" || routine.status === "loading") {
        section.append(createInlineState("Loading known spells…", "loading"));
        return section;
    }
    if (routine.status === "error" || routine.state === null) {
        section.append(createInlineState(
            routine.message ?? "Known spells are unavailable.",
            "error"));
        return section;
    }

    const editable = !readOnly && !routine.state.readOnly;
    const pending = routine.mutation !== null;
    const known = (routine.state.rulesInputs ?? [])
        .filter(input => input.kind === "knownSpell");

    if (routine.mutationError !== undefined) {
        section.append(createInlineState(routine.mutationError, "error"));
    }

    if (editable) {
        section.append(createButton(
            "Add Known Spell",
            "dd-button dd-button--primary",
            handlers.openChooser,
            pending));
    }

    if (routine.spellChooser.kind === "open" && editable) {
        section.append(renderSpellChooser(routine, handlers));
    }

    if (known.length === 0) {
        section.append(createElement(
            "p",
            "dd-routine-empty",
            editable
                ? "No known spells yet. Add one from the rule catalog."
                : "No known spells have been added."));
        return section;
    }

    const list = createElement("div", "dd-known-spells__list");
    for (const input of known) {
        const item = createElement("article", "dd-known-spell");
        item.setAttribute("data-known-spell-key", input.key);
        const reference = routine.references[input.id] ?? {
            status: "loading" as const,
            conceptKey: input.key
        };
        const display = toRuleReferenceDisplay(reference);
        item.append(
            createElement(
                "h4",
                "dd-known-spell__name",
                display.tone === "unavailable"
                    ? "Unavailable spell reference"
                    : display.value),
            createElement(
                "p",
                "dd-routine-meta",
                display.detail ?? input.key));

        if (editable) {
            const removing = routine.mutation?.kind === "rules-input-delete"
                && routine.mutation.entryId === `knownSpell:${input.key}`;
            item.append(createButton(
                removing ? "Removing…" : "Remove",
                "dd-button dd-button--ghost",
                () => handlers.remove(input.key),
                pending));
        }
        list.append(item);
    }
    section.append(list);
    return section;
}

function renderSpellChooser(
    routine: CharacterRoutineUiState,
    handlers: KnownSpellCharacterHandlers
): HTMLElement {
    const chooser = routine.spellChooser;
    const section = createElement("section", "dd-rule-chooser dd-spell-chooser");
    if (chooser.kind !== "open") return section;

    const heading = createElement("h4", "dd-rule-chooser__title", "Add Known Spell");
    const search = createElement("form", "dd-rule-chooser__search");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search available spells";
    input.setAttribute("aria-label", "Search available spells");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search");
    submit.type = "submit";
    const close = createButton(
        "Close",
        "dd-button dd-button--ghost",
        handlers.closeChooser);
    search.append(input, submit, close);
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.search(input.value);
    });
    section.append(heading, search);

    if (chooser.status === "idle" || chooser.status === "loading") {
        section.append(createInlineState("Loading available spells…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(
            chooser.message ?? "Spell search failed.",
            "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching spells."));
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
                () => handlers.add(rule.conceptKey),
                routine.mutation !== null));
        results.append(result);
    }
    section.append(results);
    return section;
}
