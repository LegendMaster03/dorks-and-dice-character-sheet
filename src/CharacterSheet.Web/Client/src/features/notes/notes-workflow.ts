import {
    addCharacterNote,
    removeCharacterNote,
    updateCharacterNote
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";

export interface NotesWorkflow {
    add(characterId: string, content: string): Promise<void>;
    update(characterId: string, noteId: string, content: string): Promise<void>;
    remove(characterId: string, noteId: string): Promise<void>;
}

export function createNotesWorkflow(
    routine: RoutineStateWorkflow,
    environment: HostEnvironment
): NotesWorkflow {
    return {
        async add(characterId: string, content: string): Promise<void> {
            if (content.trim().length === 0) return;
            await routine.mutate(
                "note-add",
                () => addCharacterNote(environment, characterId, content));
        },

        async update(
            characterId: string,
            noteId: string,
            content: string
        ): Promise<void> {
            if (content.trim().length === 0) return;
            await routine.mutate(
                "note-update",
                () => updateCharacterNote(
                    environment,
                    characterId,
                    noteId,
                    content),
                noteId);
        },

        async remove(characterId: string, noteId: string): Promise<void> {
            await routine.mutate(
                "note-delete",
                () => removeCharacterNote(environment, characterId, noteId),
                noteId);
        }
    };
}
