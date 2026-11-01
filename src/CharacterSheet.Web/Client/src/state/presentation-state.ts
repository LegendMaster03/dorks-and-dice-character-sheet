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
                message: undefined,
                equipmentPreviewStatus: "loading",
                equipmentPreview: null,
                equipmentPreviewMessage: undefined
            };
            break;
        case "presentation-loaded":
            if (presentation.requestId === action.requestId) {
                presentation = {
                    status: "ready",
                    requestId: action.requestId,
                    advancement: action.presentation.advancement,
                    mechanics: action.presentation.mechanics,
                    mechanicsStatus: action.presentation.mechanicsStatus,
                    mechanicsMessage: action.presentation.mechanicsMessage,
                    equipmentPreviewStatus: presentation.equipmentPreviewStatus,
                    equipmentPreview: presentation.equipmentPreview,
                    equipmentPreviewMessage: presentation.equipmentPreviewMessage
                };
            }
            break;
        case "starting-equipment-preview-loaded":
            if (presentation.requestId === action.requestId)
                presentation = { ...presentation,
                    equipmentPreviewStatus: "ready",
                    equipmentPreview: action.preview,
                    equipmentPreviewMessage: undefined };
            break;
        case "starting-equipment-preview-failed":
            if (presentation.requestId === action.requestId)
                presentation = { ...presentation,
                    equipmentPreviewStatus: "error",
                    equipmentPreview: null,
                    equipmentPreviewMessage: action.message };
            break;
        case "presentation-load-failed":
            if (presentation.requestId === action.requestId) {
                presentation = {
                    status: "error",
                    requestId: action.requestId,
                    advancement: null,
                    mechanics: null,
                    mechanicsStatus: "unavailable",
                    mechanicsMessage: action.message,
                    message: action.message,
                    equipmentPreviewStatus: presentation.equipmentPreviewStatus,
                    equipmentPreview: presentation.equipmentPreview,
                    equipmentPreviewMessage: presentation.equipmentPreviewMessage
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


