import type {
    CharacterRoutineUiState,
    RoutineMutationKind
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import {
    loadCharacterState,
    type CharacterStateResponse
} from "../../character-state-api.js";
import { resolveRuleConcept } from "../../rules-core-api.js";
import { requestErrorMessage } from "./request-error.js";

export interface RoutineStateWorkflow {
    load(characterId: string): Promise<void>;
    mutate(
        kind: RoutineMutationKind,
        operation: () => Promise<CharacterStateResponse>,
        entryId?: string
    ): Promise<boolean>;
    current(): Readonly<CharacterRoutineUiState>;
}

export function createRoutineStateWorkflow(
    application: CharacterSheetApplication,
    environment: HostEnvironment
): RoutineStateWorkflow {
    async function resolveReferences(state: CharacterStateResponse): Promise<void> {
        await Promise.all(state.inventoryItemOccurrences.map(async occurrence => {
            try {
                const rule = await resolveRuleConcept(
                    environment,
                    occurrence.ruleConceptKey);
                const reference = rule !== null
                    && rule.entityType === "item"
                    && rule.conceptKey === occurrence.ruleConceptKey
                    ? {
                        status: "resolved" as const,
                        conceptKey: occurrence.ruleConceptKey,
                        rule
                    }
                    : {
                        status: "unavailable" as const,
                        conceptKey: occurrence.ruleConceptKey
                    };
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: occurrence.id,
                    conceptKey: occurrence.ruleConceptKey,
                    reference
                });
            } catch (error) {
                application.dispatch({
                    type: "routine-reference-resolved",
                    occurrenceId: occurrence.id,
                    conceptKey: occurrence.ruleConceptKey,
                    reference: {
                        status: "error",
                        conceptKey: occurrence.ruleConceptKey,
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
            entryId?: string
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
            });
            try {
                const next = await operation();
                application.dispatch({
                    type: "routine-mutation-succeeded",
                    state: next
                });
                await resolveReferences(next);
                return true;
            } catch (error) {
                application.dispatch({
                    type: "routine-mutation-failed",
                    message: requestErrorMessage(error)
                });
                return false;
            }
        },

        current(): Readonly<CharacterRoutineUiState> {
            return application.getState().routine;
        }
    };
}
