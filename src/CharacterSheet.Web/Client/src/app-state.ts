import type { CharacterSheetBootstrapResponse } from "./character-api.js";
import type { CharacterSheetRoute } from "./routes.js";

export type CharacterSheetScreen =
    | { kind: "new-character"; status: "ready" | "error"; message?: string; recoveryCharacterId?: string }
    | { kind: "loading" }
    | { kind: "submitting"; operation: "new-character" | "initialize-sheet"; character?: CharacterSheetBootstrapResponse }
    | { kind: "basic-character"; character: CharacterSheetBootstrapResponse }
    | { kind: "rich-character"; character: CharacterSheetBootstrapResponse }
    | { kind: "archived"; character: CharacterSheetBootstrapResponse }
    | { kind: "not-found" }
    | { kind: "error"; message: string }
    | { kind: "invalid"; route: string };

export interface CharacterSheetAppState {
    route: CharacterSheetRoute;
    screen: CharacterSheetScreen;
    renderRevision: number;
}

export type CharacterSheetAction =
    | { type: "character-loaded"; character: CharacterSheetBootstrapResponse | null }
    | { type: "load-failed"; message: string }
    | { type: "new-submit-started" }
    | { type: "new-submit-failed"; message: string; recoveryCharacterId?: string }
    | { type: "sheet-submit-started"; character: CharacterSheetBootstrapResponse }
    | { type: "rerender" };

export function createInitialState(route: CharacterSheetRoute): CharacterSheetAppState {
    let screen: CharacterSheetScreen;
    switch (route.kind) {
        case "new":
            screen = { kind: "new-character", status: "ready" };
            break;
        case "character":
            screen = { kind: "loading" };
            break;
        case "invalid":
            screen = { kind: "invalid", route: route.route };
            break;
    }

    return { route, screen, renderRevision: 0 };
}

export function reduceAppState(
    state: CharacterSheetAppState,
    action: CharacterSheetAction
): CharacterSheetAppState {
    let screen = state.screen;
    switch (action.type) {
        case "character-loaded":
            if (action.character === null) {
                screen = { kind: "not-found" };
            } else if (action.character.lifecycle === "Archived") {
                screen = { kind: "archived", character: action.character };
            } else if (action.character.hasRichSheet) {
                screen = { kind: "rich-character", character: action.character };
            } else {
                screen = { kind: "basic-character", character: action.character };
            }
            break;
        case "load-failed":
            screen = { kind: "error", message: action.message };
            break;
        case "new-submit-started":
            screen = { kind: "submitting", operation: "new-character" };
            break;
        case "new-submit-failed":
            screen = {
                kind: "new-character",
                status: "error",
                message: action.message,
                recoveryCharacterId: action.recoveryCharacterId
            };
            break;
        case "sheet-submit-started":
            screen = { kind: "submitting", operation: "initialize-sheet", character: action.character };
            break;
        case "rerender":
            break;
    }

    return { ...state, screen, renderRevision: state.renderRevision + 1 };
}
