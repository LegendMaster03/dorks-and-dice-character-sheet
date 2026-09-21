import type {
    CharacterPresentationUiState,
    CharacterSheetAction
} from "../app-state.js";

export function reducePresentationState(
    current: CharacterPresentationUiState,
    action: CharacterSheetAction
): CharacterPresentationUiState {
    let presentation = current;
    switch (action.type) {
        case "presentation-load-started":
            presentation = {
                ...presentation,
                status: "loading",
                requestId: action.requestId,
                message: undefined
            };
            break;
        case "presentation-loaded":
            if (presentation.requestId === action.requestId) {
                presentation = {
                    status: "ready",
                    requestId: action.requestId,
                    advancement: action.presentation.advancement,
                    mechanics: action.presentation.mechanics
                };
            }
            break;
        case "presentation-load-failed":
            if (presentation.requestId === action.requestId) {
                presentation = {
                    status: "error",
                    requestId: action.requestId,
                    advancement: null,
                    mechanics: null,
                    message: action.message
                };
            }
            break;

    }
    return presentation;
}

export function createInitialPresentationState(): CharacterPresentationUiState {
    return {
        status: "idle",
        requestId: 0,
        advancement: null,
        mechanics: null
    };
}


