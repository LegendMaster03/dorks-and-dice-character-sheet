import type {
    CharacterRoutineUiState,
    RoutineMutationKind
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";
import {
    loadCharacterState,
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
        // Native item and spell summaries are not currently exposed. Keep
        // their Character-owned identities without calling the deleted API.
        const withoutCatalog = [
            ...state.inventoryItemOccurrences
                .filter(occurrence => occurrence.ruleConceptKey !== null)
                .map(occurrence => ({ id: occurrence.id, key: occurrence.ruleConceptKey! })),
            ...(state.rulesInputs ?? [])
                .filter(input => input.kind === "knownSpell")
                .map(input => ({ id: input.id, key: input.key }))
        ];
        for (const item of withoutCatalog) {
            application.dispatch({
                type: "routine-reference-resolved",
                occurrenceId: item.id,
                conceptKey: item.key,
                reference: { status: "unavailable", conceptKey: item.key }
            });
        }

        const conditions = (state.conditions ?? [])
            .filter(condition => condition.ruleConceptKey !== null);
        if (conditions.length === 0) return;
        let metadata: Awaited<ReturnType<typeof loadNativeRuleset>>;
        try {
            metadata = await loadNativeRuleset(environment);
        } catch (error) {
            for (const condition of conditions) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: condition.id,
                    conceptKey: condition.ruleConceptKey!,
                    reference: {
                        status: "error", conceptKey: condition.ruleConceptKey!,
                        message: requestErrorMessage(error)
                    }
                });
            }
            return;
        }
        await Promise.all(conditions.map(async condition => {
            const conceptKey = condition.ruleConceptKey!;
            try {
                const [regular, leveled] = await Promise.all([
                    searchNativeOptions(environment, metadata, "condition", conceptKey, 100),
                    searchNativeOptions(environment, metadata, "leveled-condition", conceptKey, 100)
                ]);
                const rule = [...regular.options, ...leveled.options]
                    .find(option => option.id === conceptKey);
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: condition.id,
                    conceptKey,
                    reference: rule === undefined
                        ? { status: "unavailable", conceptKey }
                        : { status: "resolved", conceptKey, rule }
                });
            } catch (error) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: condition.id,
                    conceptKey,
                    reference: {
                        status: "error", conceptKey,
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
