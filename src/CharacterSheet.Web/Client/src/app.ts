import "./styles.css";
import { createInitialState } from "./app-state.js";
import {
    clearCharacterBuildChoice,
    loadCharacterBuild,
    setCharacterBuildChoice,
    type CharacterBuildResponse,
    type CharacterBuilderChoice
} from "./builder-api.js";
import {
    filterSubclassesForClass,
    getStartingClassEntry,
    getStoredChoiceConceptKey,
    resolveStoredChoice
} from "./builder-rules.js";
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
import { searchResolvedRules } from "./rules-core-api.js";
import { parseCharacterSheetRoute } from "./routes.js";
import { createButton, createElement, createInlineState, createStateCard } from "./ui/components.js";
import { renderCharacterHeader, renderCharacterWorkspace } from "./ui/sheet.js";
import type { SheetSection } from "./ui/sheet-model.js";

const root = document.getElementById("tool-root");
if (!(root instanceof HTMLElement)) {
    throw new Error("Character Sheet could not find the Dorks & Dice tool root.");
}

const appRoot: HTMLElement = root;
const environment = resolveHostEnvironment(appRoot, window.location.pathname);
const route = parseCharacterSheetRoute(environment.toolRoute);
const initialState = createInitialState(route);
ensureCharacterSheetStylesheet();

function ensureCharacterSheetStylesheet(): void {
    const href = new URL("./app.css", import.meta.url).href;
    const existing = document.querySelector<HTMLLinkElement>("link[data-character-sheet-stylesheet]");
    if (existing?.href === href) return;

    existing?.remove();
    const stylesheet = document.createElement("link");
    stylesheet.rel = "stylesheet";
    stylesheet.href = href;
    stylesheet.dataset.characterSheetStylesheet = "true";
    document.head.append(stylesheet);
}

function render(): void {
    const state = application.getState();
    let content: HTMLElement;

    switch (state.screen.kind) {
        case "new-character":
            content = renderNewCharacter(
                state.screen.status === "error" ? state.screen.message : undefined,
                state.screen.recoveryCharacterId);
            break;
        case "loading":
            content = renderStateScreen("Loading Character Sheet", "Loading Character and sheet state…", "loading");
            break;
        case "submitting":
            content = renderStateScreen(
                state.screen.operation === "new-character" ? "Creating Character" : "Starting digital Character Sheet",
                state.screen.operation === "new-character"
                    ? "Creating the canonical Site Character and initializing its digital sheet…"
                    : "Initializing the digital Character Sheet…",
                "loading");
            break;
        case "basic-character":
            content = renderBasicCharacter(state.screen.character);
            break;
        case "rich-character":
            content = renderWorkspace(state.screen.character, false, state.activeSheetSection);
            break;
        case "archived":
            content = renderWorkspace(state.screen.character, true, state.activeSheetSection);
            break;
        case "not-found":
            content = renderStateScreen("Character unavailable", "This Character could not be found or is not available to this account.", "warning");
            break;
        case "error":
            content = renderStateScreen("Character Sheet unavailable", state.screen.message, "error");
            break;
        case "invalid":
            content = renderStateScreen("Unsupported Character Sheet route", state.screen.route, "warning");
            break;
    }

    content.setAttribute("data-character-sheet-state", state.screen.kind);
    appRoot.replaceChildren(content);
}

function renderNewCharacter(message?: string, recoveryCharacterId?: string): HTMLElement {
    const screen = createElement("section", "dd-sheet-screen");
    const panel = createElement("section", "dd-sheet-screen__panel");
    panel.append(
        createElement("h1", "dd-sheet-screen__title", "Build a Character"),
        createElement(
            "p",
            "dd-sheet-screen__copy",
            "Create the Site-owned Character first. Character Sheet will then attach its digital build state to that canonical Character identity."));

    if (!environment.embedded) {
        panel.append(createInlineState(
            "Standalone development does not invent Site Character ownership. Open this route through Dorks & Dice to create a canonical Character.",
            "warning"));
        screen.append(panel);
        return screen;
    }

    if (message !== undefined) {
        panel.append(createInlineState(message, "error"));
    }

    if (recoveryCharacterId !== undefined) {
        panel.append(createButton("Open Character", "dd-button dd-button--primary", () => {
            window.location.assign(buildCharacterRouteUrl(environment, recoveryCharacterId));
        }));
        screen.append(panel);
        return screen;
    }

    const form = createElement("form", "dd-sheet-screen__form");
    const label = createElement("label", "dd-sheet-screen__label", "Character name");
    const input = createElement("input", "dd-sheet-screen__input");
    input.name = "characterName";
    input.required = true;
    input.autocomplete = "off";
    label.append(input);
    const submit = createElement("button", "dd-button dd-button--primary", "Build Character");
    submit.type = "submit";
    form.append(label, submit);
    form.addEventListener("submit", event => {
        event.preventDefault();
        if (application.getState().screen.kind === "submitting") return;
        void submitNewCharacter(input.value);
    });
    panel.append(form);
    screen.append(panel);
    return screen;
}

function renderBasicCharacter(character: CharacterSheetBootstrapResponse): HTMLElement {
    const shell = createElement("article", "dd-sheet");
    shell.append(renderCharacterHeader(character, application.getState().builder, false));
    const body = createElement("div", "dd-sheet-screen");
    const panel = createElement("section", "dd-character-init");
    panel.append(
        createElement("h2", "dd-character-init__name", "Digital sheet not initialized"),
        createElement(
            "p",
            "dd-character-init__copy",
            "This Site Character exists, but Character Sheet does not yet have rich Character-owned build state for it."),
        createButton("Build Digital Sheet", "dd-button dd-button--primary", () => void initializeExistingCharacter(character)));
    body.append(panel);
    shell.append(body);
    return shell;
}

function renderWorkspace(
    character: CharacterSheetBootstrapResponse,
    forceReadOnly: boolean,
    activeSection: SheetSection
): HTMLElement {
    const workspace = renderCharacterWorkspace(
        character,
        application.getState().builder,
        activeSection,
        forceReadOnly,
        {
            openChooser,
            clearChoice: target => void clearChoice(character.characterId, target),
            submitChooserSearch: (target, query) => void loadChooser(target, query),
            closeChooser: () => application.dispatch({ type: "chooser-closed" }),
            saveChoice: (target, conceptKey) => void saveChoice(character.characterId, target, conceptKey),
            selectSection: section => application.dispatch({ type: "sheet-section-selected", section })
        });
    workspace.append(renderDevelopmentDetails(character));
    return workspace;
}

function renderStateScreen(
    title: string,
    message: string,
    tone: "neutral" | "loading" | "warning" | "error" | "readonly"
): HTMLElement {
    const screen = createElement("section", "dd-sheet-screen");
    screen.append(createStateCard(title, message, tone));
    return screen;
}

function renderDevelopmentDetails(character: CharacterSheetBootstrapResponse): HTMLElement {
    const details = createElement("details", "dd-sheet-details");
    const summary = createElement("summary", undefined, "Technical details");
    const characterId = createElement("p", undefined, `Character ID: ${character.characterId}`);
    details.append(summary, characterId);
    return details;
}

async function bootstrapCharacter(): Promise<void> {
    if (route.kind !== "character") return;
    try {
        const character = await loadCharacterSheet(environment, route.characterId);
        application.dispatch({ type: "character-loaded", character });
        if (character?.hasRichSheet) {
            await bootstrapBuild(character.characterId);
        }
    } catch (error) {
        application.dispatch({ type: "load-failed", message: errorMessage(error) });
    }
}

async function bootstrapBuild(characterId: string): Promise<void> {
    application.dispatch({ type: "builder-load-started" });
    try {
        const build = await loadCharacterBuild(environment, characterId);
        application.dispatch({ type: "builder-loaded", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "builder-load-failed", message: errorMessage(error) });
    }
}

async function resolveBuildReferences(build: CharacterBuildResponse): Promise<void> {
    await Promise.all((["raceSpecies", "startingClass", "subclass"] as const).map(async target => {
        const conceptKey = getStoredChoiceConceptKey(build, target);
        if (conceptKey === null) return;
        const reference = await resolveStoredChoice(environment, build, target);
        application.dispatch({ type: "rule-reference-resolved", target, conceptKey, reference });
    }));
}

function openChooser(target: CharacterBuilderChoice): void {
    application.dispatch({ type: "chooser-opened", target });
    void loadChooser(target, "");
}

async function loadChooser(target: CharacterBuilderChoice, query: string): Promise<void> {
    const normalizedQuery = query.trim();
    application.dispatch({ type: "chooser-load-started", target, query: normalizedQuery });
    try {
        const entityType = target === "raceSpecies"
            ? "race"
            : target === "startingClass" ? "class" : "subclass";
        const catalog = await searchResolvedRules(environment, entityType, normalizedQuery);
        let results = catalog.rules.filter(rule => rule.entityType === entityType);
        if (target === "subclass") {
            const build = application.getState().builder.build;
            const startingClass = build === null ? null : getStartingClassEntry(build);
            if (startingClass === null) {
                throw new Error("Choose a Class before selecting a Subclass.");
            }
            results = filterSubclassesForClass(results, startingClass.ruleConceptKey);
        }
        application.dispatch({ type: "chooser-loaded", target, query: normalizedQuery, results });
    } catch (error) {
        application.dispatch({
            type: "chooser-load-failed",
            target,
            query: normalizedQuery,
            message: errorMessage(error)
        });
    }
}

async function saveChoice(
    characterId: string,
    target: CharacterBuilderChoice,
    conceptKey: string
): Promise<void> {
    application.dispatch({ type: "selection-save-started", target });
    try {
        const classAdvancementEntryId = target === "subclass" ? currentStartingClassId() : undefined;
        const build = await setCharacterBuildChoice(
            environment,
            characterId,
            target,
            conceptKey,
            classAdvancementEntryId);
        application.dispatch({ type: "selection-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "selection-save-failed", message: errorMessage(error) });
    }
}

async function clearChoice(characterId: string, target: CharacterBuilderChoice): Promise<void> {
    application.dispatch({ type: "selection-save-started", target });
    try {
        const classAdvancementEntryId = target === "subclass" ? currentStartingClassId() : undefined;
        const build = await clearCharacterBuildChoice(
            environment,
            characterId,
            target,
            classAdvancementEntryId);
        application.dispatch({ type: "selection-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "selection-save-failed", message: errorMessage(error) });
    }
}

function currentStartingClassId(): string {
    const build = application.getState().builder.build;
    const startingClass = build === null ? null : getStartingClassEntry(build);
    if (startingClass === null) {
        throw new Error("Choose a Class before selecting a Subclass.");
    }
    return startingClass.id;
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
        const initialized = await initializeCharacterSheet(environment, character.characterId);
        application.dispatch({ type: "character-loaded", character: initialized });
        await bootstrapBuild(initialized.characterId);
    } catch (error) {
        application.dispatch({ type: "load-failed", message: errorMessage(error) });
    }
}

function errorMessage(error: unknown): string {
    return error instanceof Error ? error.message : "Character Sheet request failed.";
}

const application = createApplication(initialState, render);
application.render();
void bootstrapCharacter();
