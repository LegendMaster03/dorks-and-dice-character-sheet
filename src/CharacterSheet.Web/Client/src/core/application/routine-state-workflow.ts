import type {
    CharacterRoutineUiState,
    RoutineMutationKind
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";
import {
    loadCharacterState,
    CharacterSheetApiError,
    type CharacterStateResponse
} from "../../character-state-api.js";
import { requestErrorMessage } from "./request-error.js";

export interface RoutineMutationOptions {
    render?: boolean;
    resolveReferences?: boolean;
}

export interface RoutineStateWorkflow {
    load(characterId: string): Promise<void>;
    mutate(
        kind: RoutineMutationKind,
        operation: () => Promise<CharacterStateResponse>,
        entryId?: string,
        options?: RoutineMutationOptions
    ): Promise<boolean>;
    current(): Readonly<CharacterRoutineUiState>;
}

export function createRoutineStateWorkflow(
    application: CharacterSheetApplication,
    environment: HostEnvironment
): RoutineStateWorkflow {
    async function resolveReferences(state: CharacterStateResponse): Promise<void> {
        const entries = [
            ...state.inventoryItemOccurrences
                .filter(entry => entry.ruleConceptKey !== null)
                .map(entry => ({ id: entry.id, key: entry.ruleConceptKey!, category: "item" as const })),
            ...(state.rulesInputs ?? [])
                .filter(entry => entry.kind === "knownSpell")
                .map(entry => ({ id: entry.id, key: entry.key, category: "spell" as const })),
            ...(state.conditions ?? [])
                .filter(entry => entry.ruleConceptKey !== null)
                .map(entry => ({ id: entry.id, key: entry.ruleConceptKey!, category: "condition" as const }))
        ];
        if (entries.length === 0) return;

        // Old saved source-specific IDs are never silently rewritten into native
        // IDs. Unsupported identities remain visible with unavailable status.
        const nativeEntries = entries.filter(entry =>
            entry.key.startsWith(entry.category + "."));
        for (const entry of entries.filter(entry =>
            !entry.key.startsWith(entry.category + "."))) {
            application.dispatch({
                type: "routine-reference-resolved",
                occurrenceId: entry.id,
                conceptKey: entry.key,
                reference: { status: "unavailable", conceptKey: entry.key }
            });
        }
        if (nativeEntries.length === 0) return;

        let metadata: Awaited<ReturnType<typeof loadNativeRuleset>>;
        try {
            metadata = await loadNativeRuleset(environment);
        } catch (error) {
            for (const entry of nativeEntries) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: entry.id,
                    conceptKey: entry.key,
                    reference: {
                        status: "error", conceptKey: entry.key,
                        message: requestErrorMessage(error)
                    }
                });
            }
            return;
        }
        await Promise.all(nativeEntries.map(async entry => {
            try {
                const regular = await searchNativeOptions(
                    environment, metadata, entry.category, entry.key, 100);
                const leveled = entry.category === "condition"
                    ? await searchNativeOptions(
                        environment, metadata, "leveled-condition", entry.key, 100)
                    : null;
                const rule = [...regular.options, ...(leveled?.options ?? [])]
                    .find(option => option.id === entry.key);
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: entry.id,
                    conceptKey: entry.key,
                    reference: rule === undefined
                        ? { status: "unavailable", conceptKey: entry.key }
                        : { status: "resolved", conceptKey: entry.key, rule }
                });
            } catch (error) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: entry.id,
                    conceptKey: entry.key,
                    reference: {
                        status: "error", conceptKey: entry.key,
                        message: requestErrorMessage(error)
                    }
                });
            }
        }));
    }

    return {
        async load(characterId: string): Promise<void> {
            application.dispatch({ type: "routine-load-started" });
            try {
                const state = await loadCharacterState(environment, characterId);
                application.dispatch({ type: "routine-loaded", state });
                await resolveReferences(state);
            } catch (error) {
                application.dispatch({
                    type: "routine-load-failed",
                    message: requestErrorMessage(error)
                });
            }
        },

        async mutate(
            kind: RoutineMutationKind,
            operation: () => Promise<CharacterStateResponse>,
            entryId?: string,
            options: RoutineMutationOptions = {}
        ): Promise<boolean> {
            const routine = application.getState().routine;
            if (routine.status !== "ready"
                || routine.state === null
                || routine.state.readOnly
                || routine.mutation !== null) {
                return false;
            }

            application.dispatch({
                type: "routine-mutation-started",
                kind,
                entryId
            }, { render: options.render });
            try {
                const next = await operation();
                application.dispatch({
                    type: "routine-mutation-succeeded",
                    state: next
                }, { render: options.render });
                if (options.resolveReferences !== false) {
                    await resolveReferences(next);
                }
                return true;
            } catch (error) {
                if (kind === "native-resource-action" &&
                    error instanceof CharacterSheetApiError &&
                    error.status === 409) {
                    // The conflict indicates that a previous resource value is
                    // stale. Reload without automatically replaying expenditure.
                    try {
                        const fresh = await loadCharacterState(
                            environment, routine.state.characterId);
                        application.dispatch({
                            type: "routine-loaded", state: fresh
                        }, { render: options.render });
                        await resolveReferences(fresh);
                    } catch {
                        // Keep the original conflict visible if reloading fails.
                    }
                }
                application.dispatch({
                    type: "routine-mutation-failed",
                    message: requestErrorMessage(error)
                }, { render: options.render });
                return false;
            }
        },

        current(): Readonly<CharacterRoutineUiState> {
            return application.getState().routine;
        }
    };
}
