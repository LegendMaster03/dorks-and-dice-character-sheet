import { createInitialState } from "./app-state.js";
import {
    buildCharacterRouteUrl,
    createNewCharacterAndSheet,
    initializeCharacterSheet,
    loadCharacterSheet,
    RichSheetInitializationError,
    type CharacterSheetBootstrapResponse
} from "./character-api.js";
import { resolveHostEnvironment } from "./host-environment.js";
import { createApplication } from "./render-lifecycle.js";
import { parseCharacterSheetRoute } from "./routes.js";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) {
    throw new Error("Character Sheet could not find the Dorks & Dice tool root.");
}

const appRoot: HTMLElement = root;
const environment = resolveHostEnvironment(appRoot, window.location.pathname);
const route = parseCharacterSheetRoute(environment.toolRoute);
const initialState = createInitialState(route);

function render(): void {
    const state = application.getState();
    const section = document.createElement("section");
    section.setAttribute("data-character-sheet-state", state.screen.kind);

    const heading = document.createElement("h1");
    heading.textContent = "Character Sheet";
    section.append(heading);

    switch (state.screen.kind) {
        case "new-character":
            renderNewCharacter(section, state.screen.status === "error" ? state.screen.message : undefined, state.screen.recoveryCharacterId);
            break;
        case "loading":
            appendParagraph(section, "Loading Character Sheet…");
            break;
        case "submitting":
            if (state.screen.operation === "new-character") {
                appendParagraph(section, "Creating Character…");
            } else {
                appendParagraph(section, "Starting digital Character Sheet…");
            }
            break;
        case "basic-character":
            renderBasicCharacter(section, state.screen.character);
            break;
        case "rich-character":
            renderRichCharacter(section, state.screen.character);
            break;
        case "archived":
            renderArchivedCharacter(section, state.screen.character);
            break;
        case "not-found":
            appendParagraph(section, "This Character is unavailable.");
            break;
        case "error":
            appendParagraph(section, state.screen.message);
            break;
        case "invalid":
            appendParagraph(section, `Unsupported Character Sheet route: ${state.screen.route}`);
            break;
    }

    appRoot.replaceChildren(section);
}

function renderNewCharacter(section: HTMLElement, message?: string, recoveryCharacterId?: string): void {
    const title = document.createElement("h2");
    title.textContent = "Build a Character";
    section.append(title);

    if (!environment.embedded) {
        appendParagraph(section, "Standalone development does not invent Site Character ownership. Open this route through Dorks & Dice to create a canonical Character.");
        return;
    }

    if (message !== undefined) {
        appendParagraph(section, message);
    }

    if (recoveryCharacterId !== undefined) {
        const recoveryButton = document.createElement("button");
        recoveryButton.type = "button";
        recoveryButton.textContent = "Open Character";
        recoveryButton.addEventListener("click", () => {
            window.location.assign(buildCharacterRouteUrl(environment, recoveryCharacterId));
        });
        section.append(recoveryButton);
        return;
    }

    const form = document.createElement("form");
    const label = document.createElement("label");
    label.textContent = "Character name";
    const input = document.createElement("input");
    input.name = "characterName";
    input.required = true;
    input.autocomplete = "off";
    label.append(input);
    form.append(label);

    const submit = document.createElement("button");
    submit.type = "submit";
    submit.textContent = "Build Character";
    form.append(submit);

    form.addEventListener("submit", event => {
        event.preventDefault();
        if (application.getState().screen.kind === "submitting") return;
        void submitNewCharacter(input.value);
    });

    section.append(form);
}

function renderBasicCharacter(section: HTMLElement, character: CharacterSheetBootstrapResponse): void {
    appendCharacterHeading(section, character.name);
    appendParagraph(section, "This Site Character does not have a digital Character Sheet yet.");
    const button = document.createElement("button");
    button.type = "button";
    button.textContent = "Build Digital Sheet";
    button.addEventListener("click", () => void initializeExistingCharacter(character));
    section.append(button);
}

function renderRichCharacter(section: HTMLElement, character: CharacterSheetBootstrapResponse): void {
    appendCharacterHeading(section, character.name);
    appendParagraph(section, "Digital character build in progress.");
    appendParagraph(section, `Character ID: ${character.characterId}`);
}

function renderArchivedCharacter(section: HTMLElement, character: CharacterSheetBootstrapResponse): void {
    appendCharacterHeading(section, character.name);
    appendParagraph(section, "This Character is archived. Its digital Character Sheet is preserved but can not be edited or initialized while archived.");
    appendParagraph(section, "Restore the Character through the Dorks & Dice Site to continue editing.");
}

async function bootstrapCharacter(): Promise<void> {
    if (route.kind !== "character") return;
    try {
        application.dispatch({
            type: "character-loaded",
            character: await loadCharacterSheet(environment, route.characterId)
        });
    } catch (error) {
        application.dispatch({ type: "load-failed", message: errorMessage(error) });
    }
}

async function submitNewCharacter(name: string): Promise<void> {
    application.dispatch({ type: "new-submit-started" });
    try {
        const characterId = await createNewCharacterAndSheet(environment, name);
        window.location.assign(buildCharacterRouteUrl(environment, characterId));
    } catch (error) {
        if (error instanceof RichSheetInitializationError) {
            application.dispatch({
                type: "new-submit-failed",
                message: error.message,
                recoveryCharacterId: error.characterId
            });
            return;
        }

        application.dispatch({ type: "new-submit-failed", message: errorMessage(error) });
    }
}

async function initializeExistingCharacter(character: CharacterSheetBootstrapResponse): Promise<void> {
    application.dispatch({ type: "sheet-submit-started", character });
    try {
        application.dispatch({
            type: "character-loaded",
            character: await initializeCharacterSheet(environment, character.characterId)
        });
    } catch (error) {
        application.dispatch({ type: "load-failed", message: errorMessage(error) });
    }
}

function appendCharacterHeading(section: HTMLElement, name: string): void {
    const heading = document.createElement("h2");
    heading.textContent = name;
    section.append(heading);
}

function appendParagraph(section: HTMLElement, text: string): void {
    const paragraph = document.createElement("p");
    paragraph.textContent = text;
    section.append(paragraph);
}

function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : "Character Sheet request failed.";
}

const application = createApplication(initialState, render);
application.render();
void bootstrapCharacter();
