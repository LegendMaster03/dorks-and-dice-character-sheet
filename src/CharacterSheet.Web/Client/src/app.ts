import "./styles.css";
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
import { createButton, createElement, createInlineState, createStateCard } from "./ui/components.js";
import { renderCharacterHeader, renderCharacterWorkspace } from "./ui/sheet.js";
import type { SheetSection } from "./ui/sheet-model.js";
import { createPresentationWorkflow } from "./core/application/presentation-workflow.js";
import { createRoutineStateWorkflow } from "./core/application/routine-state-workflow.js";
import { createBuildStateWorkflow } from "./core/application/build-state-workflow.js";
import { requestErrorMessage } from "./core/application/request-error.js";
import { createAdvancementWorkflow } from "./features/advancement/advancement-workflow.js";
import { createAbilityWorkflow } from "./features/abilities/ability-workflow.js";
import { createFeatWorkflow } from "./features/features/feat-workflow.js";
import { createHealthWorkflow } from "./features/health/health-workflow.js";
import { createInventoryWorkflow } from "./features/inventory/inventory-workflow.js";
import { createNotesWorkflow } from "./features/notes/notes-workflow.js";

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
    const state = application.getState();
    const workspace = renderCharacterWorkspace(
        character,
        state.builder,
        state.routine,
        activeSection,
        forceReadOnly,
        state.sheetMode,
        state.guidedBuilder,
        state.presentation.status === "ready" ? state.presentation.advancement : null,
        state.presentation.status === "ready" ? state.presentation.mechanics : null,
        {
            structural: {
                openChooser: target => advancementWorkflow.openChooser(target),
                clearChoice: target => void advancementWorkflow.clear(character.characterId, target),
                submitChooserSearch: (target, query) => void advancementWorkflow.search(target, query),
                closeChooser: () => advancementWorkflow.closeChooser(),
                saveChoice: (target, conceptKey) => void advancementWorkflow.save(character.characterId, target, conceptKey),
                setBaseAbilityScore: (abilityKey, score) =>
                    void abilityWorkflow.save(character.characterId, abilityKey, score),
                clearBaseAbilityScore: abilityKey =>
                    void abilityWorkflow.clear(character.characterId, abilityKey)
            },
            feats: {
                openChooser: () => featWorkflow.openChooser(),
                closeChooser: () => featWorkflow.closeChooser(),
                search: query => void featWorkflow.search(query),
                add: conceptKey => void featWorkflow.add(character.characterId, conceptKey),
                remove: occurrenceId => void featWorkflow.remove(character.characterId, occurrenceId)
            },
            routine: {
                setCurrentHitPoints: currentHitPoints =>
                    void healthWorkflow.setCurrentHitPoints(character.characterId, currentHitPoints),
                addNote: content => void notesWorkflow.add(character.characterId, content),
                updateNote: (noteId, content) => void notesWorkflow.update(character.characterId, noteId, content),
                deleteNote: noteId => void notesWorkflow.remove(character.characterId, noteId),
                openInventoryChooser: () => inventoryWorkflow.openChooser(),
                closeInventoryChooser: () => inventoryWorkflow.closeChooser(),
                searchInventory: query => void inventoryWorkflow.search(query),
                addInventoryItem: conceptKey => void inventoryWorkflow.add(character.characterId, conceptKey),
                removeInventoryItem: occurrenceId => void inventoryWorkflow.remove(character.characterId, occurrenceId)
            },
            selectSection: section => application.dispatch({ type: "sheet-section-selected", section }),
            enterEditMode: () => dispatchAndFocus(
                { type: "sheet-edit-entered" },
                '[data-sheet-mode-control="view"]'),
            leaveEditMode: () => dispatchAndFocus(
                { type: "sheet-edit-exited" },
                '[data-sheet-mode-control="edit"]'),
            openGuidedBuilder: () => dispatchAndFocus(
                { type: "guided-builder-opened" },
                '[data-sheet-mode-control="guided-close"]'),
            closeGuidedBuilder: () => dispatchAndFocus(
                { type: "guided-builder-closed" },
                "[data-sheet-mode-control]"),
            selectGuidedBuilderSection: section => dispatchAndFocus(
                { type: "guided-builder-section-selected", section },
                `[data-guided-builder-section="${section}"]`)
        });
    workspace.append(renderDevelopmentDetails(character));
    return workspace;
}

function dispatchAndFocus(
    action: Parameters<typeof application.dispatch>[0],
    selector: string
): void {
    application.dispatch(action);
    queueMicrotask(() => {
        const target = appRoot.querySelector<HTMLElement>(selector);
        target?.focus();
    });
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
            await Promise.all([
                buildStateWorkflow.load(character.characterId),
                routineStateWorkflow.load(character.characterId),
                presentationWorkflow.load(character.characterId)
            ]);
        }
    } catch (error) {
        application.dispatch({ type: "load-failed", message: requestErrorMessage(error) });
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
        application.dispatch({ type: "new-submit-failed", message: requestErrorMessage(error) });
    }
}

async function initializeExistingCharacter(character: CharacterSheetBootstrapResponse): Promise<void> {
    application.dispatch({ type: "sheet-submit-started", character });
    try {
        const initialized = await initializeCharacterSheet(environment, character.characterId);
        application.dispatch({ type: "character-loaded", character: initialized });
        await Promise.all([
            buildStateWorkflow.load(initialized.characterId),
            routineStateWorkflow.load(initialized.characterId),
            presentationWorkflow.load(initialized.characterId)
        ]);
    } catch (error) {
        application.dispatch({ type: "load-failed", message: requestErrorMessage(error) });
    }
}

const application = createApplication(initialState, render);
const presentationWorkflow = createPresentationWorkflow(application, environment);
const routineStateWorkflow = createRoutineStateWorkflow(application, environment);
const buildStateWorkflow = createBuildStateWorkflow(application, environment);
const advancementWorkflow = createAdvancementWorkflow(
    application,
    buildStateWorkflow,
    presentationWorkflow,
    environment);
const abilityWorkflow = createAbilityWorkflow(
    application,
    buildStateWorkflow,
    presentationWorkflow,
    environment);
const featWorkflow = createFeatWorkflow(
    application,
    buildStateWorkflow,
    presentationWorkflow,
    environment);
const healthWorkflow = createHealthWorkflow(routineStateWorkflow, environment);
const inventoryWorkflow = createInventoryWorkflow(
    application,
    routineStateWorkflow,
    presentationWorkflow,
    environment);
const notesWorkflow = createNotesWorkflow(routineStateWorkflow, environment);

application.render();
void bootstrapCharacter();
