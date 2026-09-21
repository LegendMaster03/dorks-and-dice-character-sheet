import type {
    CharacterRoutineUiState,
    CharacterSheetAction
} from "../app-state.js";
import type { RuleReferenceState } from "../builder-rules.js";
import type { CharacterStateResponse } from "../character-state-api.js";

export function reduceRoutineState(
    current: CharacterRoutineUiState,
    action: CharacterSheetAction
): CharacterRoutineUiState {
    let routine = current;
    switch (action.type) {
        case "routine-load-started":
            routine = {
                ...createInitialRoutineState(),
                status: "loading"
            };
            break;
        case "routine-loaded":
            routine = routineStateFromResponse(action.state);
            break;
        case "routine-load-failed":
            routine = {
                ...createInitialRoutineState(),
                status: "error",
                message: action.message
            };
            break;
        case "routine-reference-resolved": {
            const occurrence = routine.state?.inventoryItemOccurrences.find(value => value.id === action.occurrenceId);
            const currentReference = routine.references[action.occurrenceId];
            if (occurrence?.ruleConceptKey === action.conceptKey
                && currentReference !== undefined
                && "conceptKey" in currentReference
                && currentReference.conceptKey === action.conceptKey) {
                routine = {
                    ...routine,
                    references: {
                        ...routine.references,
                        [action.occurrenceId]: action.reference
                    }
                };
            }
            break;
        }
        case "inventory-chooser-opened":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        kind: "open",
                        query: "",
                        status: "idle",
                        results: []
                    },
                    mutationError: undefined
                };
            }
            break;
        case "inventory-chooser-query-changed":
            if (routine.inventoryChooser.kind === "open") {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        query: action.query
                    }
                };
            }
            break;
        case "inventory-chooser-load-started":
            if (routine.inventoryChooser.kind === "open") {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "inventory-chooser-loaded":
            if (routine.inventoryChooser.kind === "open"
                && routine.inventoryChooser.query === action.query) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "inventory-chooser-load-failed":
            if (routine.inventoryChooser.kind === "open"
                && routine.inventoryChooser.query === action.query) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "inventory-chooser-closed":
            routine = { ...routine, inventoryChooser: { kind: "closed" } };
            break;
        case "routine-mutation-started":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly && routine.mutation === null) {
                routine = {
                    ...routine,
                    mutation: { kind: action.kind, entryId: action.entryId },
                    mutationError: undefined
                };
            }
            break;
        case "routine-mutation-succeeded":
            routine = routineStateFromResponse(action.state);
            break;
        case "routine-mutation-failed":
            routine = {
                ...routine,
                mutation: null,
                mutationError: action.message
            };
            break;

    }
    return routine;
}

export function createInitialRoutineState(): CharacterRoutineUiState {
    return {
        status: "idle",
        state: null,
        references: {},
        inventoryChooser: { kind: "closed" },
        mutation: null
    };
}

function routineStateFromResponse(state: CharacterStateResponse): CharacterRoutineUiState {
    const references: Record<string, RuleReferenceState> = {};
    for (const occurrence of state.inventoryItemOccurrences) {
        references[occurrence.id] = {
            status: "loading",
            conceptKey: occurrence.ruleConceptKey
        };
    }
    return {
        status: "ready",
        state,
        references,
        inventoryChooser: { kind: "closed" },
        mutation: null
    };
}


