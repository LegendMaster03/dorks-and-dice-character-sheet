import type { CharacterSheetRoute } from "./routes.js";

export interface CharacterSheetAppState {
    route: CharacterSheetRoute;
    renderRevision: number;
}

export type CharacterSheetAction =
    | { type: "route-changed"; route: CharacterSheetRoute }
    | { type: "rerender" };

export function createInitialState(route: CharacterSheetRoute): CharacterSheetAppState {
    return { route, renderRevision: 0 };
}

export function reduceAppState(
    state: CharacterSheetAppState,
    action: CharacterSheetAction
): CharacterSheetAppState {
    switch (action.type) {
        case "route-changed":
            return { route: action.route, renderRevision: state.renderRevision + 1 };
        case "rerender":
            return { ...state, renderRevision: state.renderRevision + 1 };
    }
}
