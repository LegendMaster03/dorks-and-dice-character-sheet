import type { CharacterSheetAction, CharacterSheetAppState } from "./app-state.js";
import { reduceAppState } from "./app-state.js";

export type RenderFunction = (state: Readonly<CharacterSheetAppState>) => void;

export interface CharacterSheetApplication {
    getState(): Readonly<CharacterSheetAppState>;
    dispatch(action: CharacterSheetAction): void;
    render(): void;
}

export function createApplication(
    initialState: CharacterSheetAppState,
    renderFunction: RenderFunction
): CharacterSheetApplication {
    let state = initialState;
    let rendering = false;

    const render = (): void => {
        if (rendering) return;
        rendering = true;
        try {
            renderFunction(state);
        } finally {
            rendering = false;
        }
    };

    return {
        getState: () => state,
        dispatch(action: CharacterSheetAction): void {
            state = reduceAppState(state, action);
            render();
        },
        render
    };
}
