import type { CharacterRoutineUiState } from "../../app-state.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import type { RoutineCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderNotesSection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const content = createElement("div", "dd-routine-section dd-notes");
    if (routine.status === "idle" || routine.status === "loading") {
        content.append(createInlineState("Loading Character notes…", "loading"));
        return content;
    }
    if (routine.status === "error" || routine.state === null) {
        content.append(createInlineState(
            routine.message ?? "Character notes are unavailable.",
            "error"));
        return content;
    }

    const pending = routine.mutation !== null;
    const editable = !readOnly && !routine.state.readOnly;
    if (routine.mutationError !== undefined) {
        content.append(createInlineState(routine.mutationError, "error"));
    }

    if (editable) {
        const form = createElement("form", "dd-note-form");
        const label = createElement("label", "dd-routine-label", "Add note");
        const input = createElement("textarea", "dd-routine-textarea");
        input.rows = 4;
        input.maxLength = 10000;
        input.setAttribute("aria-label", "New Character note");
        const submit = createElement("button", "dd-button dd-button--primary", "Add Note");
        submit.type = "submit";
        submit.disabled = pending;
        form.append(label, input, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            if (pending || input.value.trim().length === 0) return;
            handlers.addNote(input.value);
        });
        content.append(form);
    }

    if (routine.state.notes.length === 0) {
        content.append(createElement(
            "p",
            "dd-routine-empty",
            editable
                ? "No notes yet. Add a note above."
                : "No notes have been added."));
        return content;
    }

    const list = createElement("div", "dd-note-list");
    for (const note of routine.state.notes) {
        const item = createElement("article", "dd-note");
        item.setAttribute("data-note-id", note.id);
        const meta = createElement(
            "p",
            "dd-routine-meta",
            `Updated ${new Date(note.updatedAt).toLocaleString()}`);

        if (editable) {
            const editor = createElement("textarea", "dd-routine-textarea dd-note__editor");
            editor.rows = Math.max(3, Math.min(12, note.content.split(/\r?\n/).length + 1));
            editor.maxLength = 10000;
            editor.value = note.content;
            editor.setAttribute("aria-label", "Character note");
            editor.disabled = pending;

            const actions = createElement("div", "dd-routine-actions");
            const save = createButton(
                routine.mutation?.kind === "note-update" && routine.mutation.entryId === note.id
                    ? "Saving…"
                    : "Save",
                "dd-button dd-button--secondary",
                () => handlers.updateNote(note.id, editor.value),
                pending);
            const remove = createButton(
                routine.mutation?.kind === "note-delete" && routine.mutation.entryId === note.id
                    ? "Deleting…"
                    : "Delete",
                "dd-button dd-button--ghost",
                () => {
                    if (window.confirm("Delete this Character note?")) {
                        handlers.deleteNote(note.id);
                    }
                },
                pending);
            actions.append(save, remove);
            item.append(editor, meta, actions);
        } else {
            const body = createElement("p", "dd-note__body", note.content);
            item.append(body, meta);
        }
        list.append(item);
    }
    content.append(list);
    return content;
}

