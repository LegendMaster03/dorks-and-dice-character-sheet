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
            const knownSpell = routine.state?.rulesInputs?.find(value =>
                value.id === action.occurrenceId && value.kind === "knownSpell");
            const condition = routine.state?.conditions?.find(value => value.id === action.occurrenceId);
            const conceptKey = occurrence?.ruleConceptKey
                ?? knownSpell?.key
                ?? condition?.ruleConceptKey;
            const currentReference = routine.references[action.occurrenceId];
            if (conceptKey === action.conceptKey
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
        case "spell-chooser-opened":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly) {
                routine = {
                    ...routine,
                    spellChooser: {
                        kind: "open",
                        query: "",
                        status: "idle",
                        results: []
                    },
                    mutationError: undefined
                };
            }
            break;
        case "spell-chooser-query-changed":
            if (routine.spellChooser.kind === "open") {
                routine = {
                    ...routine,
                    spellChooser: {
                        ...routine.spellChooser,
                        query: action.query
                    }
                };
            }
            break;
        case "spell-chooser-load-started":
            if (routine.spellChooser.kind === "open") {
                routine = {
                    ...routine,
                    spellChooser: {
                        ...routine.spellChooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "spell-chooser-loaded":
            if (routine.spellChooser.kind === "open"
                && routine.spellChooser.query === action.query) {
                routine = {
                    ...routine,
                    spellChooser: {
                        ...routine.spellChooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "spell-chooser-load-failed":
            if (routine.spellChooser.kind === "open"
                && routine.spellChooser.query === action.query) {
                routine = {
                    ...routine,
                    spellChooser: {
                        ...routine.spellChooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "spell-chooser-closed":
            routine = { ...routine, spellChooser: { kind: "closed" } };
            break;
        case "condition-chooser-opened":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly) {
                routine = {
                    ...routine,
                    conditionChooser: {
                        kind: "open",
                        query: "",
                        status: "idle",
                        results: []
                    },
                    mutationError: undefined
                };
            }
            break;
        case "condition-chooser-query-changed":
            if (routine.conditionChooser.kind === "open") {
                routine = {
                    ...routine,
                    conditionChooser: {
                        ...routine.conditionChooser,
                        query: action.query
                    }
                };
            }
            break;
        case "condition-chooser-load-started":
            if (routine.conditionChooser.kind === "open") {
                routine = {
                    ...routine,
                    conditionChooser: {
                        ...routine.conditionChooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "condition-chooser-loaded":
            if (routine.conditionChooser.kind === "open"
                && routine.conditionChooser.query === action.query) {
                routine = {
                    ...routine,
                    conditionChooser: {
                        ...routine.conditionChooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "condition-chooser-load-failed":
            if (routine.conditionChooser.kind === "open"
                && routine.conditionChooser.query === action.query) {
                routine = {
                    ...routine,
                    conditionChooser: {
                        ...routine.conditionChooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "condition-chooser-closed":
            routine = { ...routine, conditionChooser: { kind: "closed" } };
            break;
        case "recovery-started":
            if (routine.status === "ready"
                && routine.state !== null
                && !routine.state.readOnly
                && routine.recovery.kind !== "resolving") {
                routine = {
                    ...routine,
                    recovery: {
                        kind: "resolving",
                        procedureKey: action.procedureKey,
                        request: action.request
                    }
                };
            }
            break;
        case "recovery-continuation":
            routine = {
                ...routine,
                recovery: {
                    kind: "continuation",
                    procedureKey: action.procedureKey,
                    request: action.request,
                    resolution: action.resolution
                }
            };
            break;
        case "recovery-succeeded":
            routine = routineStateFromResponse(action.state);
            break;
        case "recovery-failed":
            routine = {
                ...routine,
                recovery: {
                    kind: "error",
                    procedureKey: action.procedureKey,
                    request: action.request,
                    message: action.message
                }
            };
            break;
        case "recovery-cancelled":
            routine = { ...routine, recovery: { kind: "closed" } };
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
        spellChooser: { kind: "closed" },
        conditionChooser: { kind: "closed" },
        recovery: { kind: "closed" },
        mutation: null
    };
}

function routineStateFromResponse(state: CharacterStateResponse): CharacterRoutineUiState {
    const normalizedState: CharacterStateResponse = {
        ...state,
        conditions: state.conditions ?? [],
        rulesInputs: state.rulesInputs ?? [],
        hitPointGains: state.hitPointGains ?? []
    };
    const references: Record<string, RuleReferenceState> = {};
    for (const occurrence of normalizedState.inventoryItemOccurrences) {
        references[occurrence.id] = {
            status: "loading",
            conceptKey: occurrence.ruleConceptKey
        };
    }
    for (const input of normalizedState.rulesInputs ?? []) {
        if (input.kind !== "knownSpell") continue;
        references[input.id] = {
            status: "loading",
            conceptKey: input.key
        };
    }
    for (const condition of normalizedState.conditions) {
        if (condition.ruleConceptKey === null) continue;
        references[condition.id] = {
            status: "loading",
            conceptKey: condition.ruleConceptKey
        };
    }
    return {
        status: "ready",
        state: normalizedState,
        references,
        inventoryChooser: { kind: "closed" },
        spellChooser: { kind: "closed" },
        conditionChooser: { kind: "closed" },
        mutation: null
    };
}


