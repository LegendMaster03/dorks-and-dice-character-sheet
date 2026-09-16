import { createInitialState } from "./app-state.js";
import { resolveHostEnvironment } from "./host-environment.js";
import { createApplication } from "./render-lifecycle.js";
import { parseCharacterSheetRoute } from "./routes.js";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) {
    throw new Error("Character Sheet could not find the Dorks & Dice tool root.");
}

const appRoot: HTMLElement = root;
const environment = resolveHostEnvironment(appRoot, window.location.pathname);
const initialState = createInitialState(parseCharacterSheetRoute(environment.toolRoute));

function render(): void {
    const state = application.getState();
    appRoot.replaceChildren(buildPlaceholder(state.route));
}

function buildPlaceholder(route: ReturnType<typeof parseCharacterSheetRoute>): HTMLElement {
    const section = document.createElement("section");
    section.setAttribute("data-character-sheet-state", route.kind);

    const heading = document.createElement("h1");
    heading.textContent = "Character Sheet";
    section.append(heading);

    const detail = document.createElement("p");
    switch (route.kind) {
        case "new":
            detail.textContent = "Development route: new character workflow.";
            break;
        case "character":
            detail.textContent = `Development route: character ${route.characterId}.`;
            section.dataset.characterId = route.characterId;
            break;
        case "invalid":
            detail.textContent = `Unsupported Character Sheet route: ${route.route}`;
            break;
    }

    section.append(detail);
    return section;
}

const application = createApplication(initialState, render);
application.render();
