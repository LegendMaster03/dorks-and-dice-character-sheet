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
import { setCharacterAdvancementProgress } from "./character-state-api.js";
import { resolveHostEnvironment } from "./host-environment.js";
import { createApplication } from "./render-lifecycle.js";
import { parseCharacterSheetRoute } from "./routes.js";
import { createButton, createElement, createInlineState, createStateCard } from "./ui/components.js";
import { renderCharacterHeader, renderCharacterWorkspace } from "./ui/sheet.js";
import type { SheetSection } from "./ui/sheet-model.js";
import { createPresentationWorkflow } from "./core/application/presentation-workflow.js";
import { createRoutineStateWorkflow } from "./core/application/routine-state-workflow.js";
import { createRulesInputWorkflow } from "./core/application/rules-input-workflow.js";
import { createBuildStateWorkflow } from "./core/application/build-state-workflow.js";
import { requestErrorMessage } from "./core/application/request-error.js";
import { createAdvancementWorkflow } from "./features/advancement/advancement-workflow.js";
import { createAbilityWorkflow } from "./features/abilities/ability-workflow.js";
import { createFeatWorkflow } from "./features/features/feat-workflow.js";
import { createHealthWorkflow } from "./features/health/health-workflow.js";
import { createProfileWorkflow } from "./features/profile/profile-workflow.js";
import { createCharacterArtWorkflow } from "./features/profile/character-art-workflow.js";
import { buildCharacterArtContentUrl } from "./features/profile/character-art-api.js";
import { createRecoveryWorkflow } from "./features/health/recovery-workflow.js";
import { createInventoryWorkflow } from "./features/inventory/inventory-workflow.js";
import { createNotesWorkflow } from "./features/notes/notes-workflow.js";
import { createConditionsWorkflow } from "./features/conditions/conditions-workflow.js";
import { createKnownSpellWorkflow } from "./features/spells/known-spell-workflow.js";

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
                state.screen.operation === "new-character" ? "Creating Character" : "Setting up Character Sheet",
                state.screen.operation === "new-character"
                    ? "Creating your Character and setting up its Character Sheet…"
                    : "Setting up the Character Sheet…",
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
            "Create a Character in Dorks & Dice, then set up the Character Sheet that is saved with it."));

    if (!environment.embedded) {
        panel.append(createInlineState(
            "Character creation is available through the Dorks & Dice Site. Open this page there to create and save a Character.",
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
        createElement("h2", "dd-character-init__name", "Character Sheet not set up"),
        createElement(
            "p",
            "dd-character-init__copy",
            "This Character is saved, but its Character Sheet has not been set up yet."),
        createButton("Set Up Character Sheet", "dd-button dd-button--primary", () => void initializeExistingCharacter(character)));
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
                setAdvancementLevel: (occurrenceId, level) =>
                    void advancementWorkflow.setLevel(character.characterId, occurrenceId, level),
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
            spells: {
                openChooser: () => knownSpellWorkflow.openChooser(),
                closeChooser: () => knownSpellWorkflow.closeChooser(),
                search: query => void knownSpellWorkflow.search(query),
                add: conceptKey => void knownSpellWorkflow.add(character.characterId, conceptKey),
                remove: conceptKey => void knownSpellWorkflow.remove(character.characterId, conceptKey)
            },
            rules: {
                setChoice: (choiceKey, value) =>
                    void rulesInputWorkflow.setChoice(character.characterId, choiceKey, value),
                clearChoice: choiceKey =>
                    void rulesInputWorkflow.clearChoice(character.characterId, choiceKey),
                setResource: (resourceKey, currentValue) =>
                    void rulesInputWorkflow.setResource(character.characterId, resourceKey, currentValue),
                setCompetencyRank: (competencyKey, ranks) =>
                    void rulesInputWorkflow.set(character.characterId, {
                        kind: "competencyRank",
                        key: competencyKey,
                        integerValue: ranks
                    }),
                clearCompetencyRank: competencyKey =>
                    void rulesInputWorkflow.remove(
                        character.characterId,
                        "competencyRank",
                        competencyKey),
                setHitPointGain: (advancementOccurrenceId, classLevel, hitDieValue) =>
                    void rulesInputWorkflow.setHitPointGain(
                        character.characterId,
                        advancementOccurrenceId,
                        classLevel,
                        hitDieValue),
                clearHitPointGain: (advancementOccurrenceId, classLevel) =>
                    void rulesInputWorkflow.clearHitPointGain(
                        character.characterId,
                        advancementOccurrenceId,
                        classLevel)
            },
            routine: {
                setInspiration: inspired =>
                    rulesInputWorkflow.setBooleanFact(
                        character.characterId,
                        "inspiration",
                        inspired,
                        {
                            refreshPresentation: false,
                            render: false,
                            resolveReferences: false
                        }),
                setCurrencyBalance: (currencyKey, amount) =>
                    void inventoryWorkflow.setCurrency(character.characterId, currencyKey, amount),
                removeCurrencyBalance: currencyKey =>
                    void inventoryWorkflow.removeCurrency(character.characterId, currencyKey),
                setProfile: input =>
                    void profileWorkflow.save(character.characterId, input),
                setAdvancementProgress: value =>
                    void routineStateWorkflow.mutate(
                        "progression-update",
                        () => setCharacterAdvancementProgress(
                            environment,
                            character.characterId,
                            value),
                        undefined,
                        { resolveReferences: false }),
                uploadArt: file =>
                    void characterArtWorkflow.upload(character.characterId, file),
                setPortrait: assetId =>
                    void characterArtWorkflow.setPortrait(character.characterId, assetId),
                clearPortrait: () =>
                    void characterArtWorkflow.clearPortrait(character.characterId),
                deleteArt: assetId =>
                    void characterArtWorkflow.remove(character.characterId, assetId),
                artContentUrl: assetId =>
                    buildCharacterArtContentUrl(environment, character.characterId, assetId),
                setCurrentHitPoints: currentHitPoints =>
                    void healthWorkflow.setCurrentHitPoints(character.characterId, currentHitPoints),
                setDeathSaves: (successes, failures) =>
                    void healthWorkflow.setDeathSaves(character.characterId, successes, failures),
                recover: procedureKey =>
                    void recoveryWorkflow.begin(character.characterId, procedureKey),
                continueRecovery: input =>
                    void recoveryWorkflow.continue(character.characterId, input),
                cancelRecovery: () => recoveryWorkflow.cancel(),
                addNote: content => void notesWorkflow.add(character.characterId, content),
                updateNote: (noteId, content) => void notesWorkflow.update(character.characterId, noteId, content),
                deleteNote: noteId => void notesWorkflow.remove(character.characterId, noteId),
                openInventoryChooser: () => inventoryWorkflow.openChooser(),
                closeInventoryChooser: () => inventoryWorkflow.closeChooser(),
                searchInventory: query => void inventoryWorkflow.search(query),
                addInventoryItem: conceptKey => void inventoryWorkflow.add(character.characterId, conceptKey),
                updateInventoryItem: (occurrenceId, input) =>
                    void inventoryWorkflow.update(character.characterId, occurrenceId, input),
                removeInventoryItem: occurrenceId => void inventoryWorkflow.remove(character.characterId, occurrenceId),
                openConditionChooser: () => conditionsWorkflow.openChooser(),
                closeConditionChooser: () => conditionsWorkflow.closeChooser(),
                searchConditions: query => void conditionsWorkflow.search(query),
                addRuleCondition: (conceptKey, input) =>
                    void conditionsWorkflow.addRule(character.characterId, conceptKey, input),
                addCustomCondition: (customName, input) =>
                    void conditionsWorkflow.addCustom(character.characterId, customName, input),
                updateCondition: (conditionId, input) =>
                    void conditionsWorkflow.update(character.characterId, conditionId, input),
                removeCondition: conditionId =>
                    void conditionsWorkflow.remove(character.characterId, conditionId)
            },
            selectSection: section => dispatchAndFocus(
                { type: "sheet-section-selected", section },
                `[data-sheet-section-tab="${section}"]`),
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
const profileWorkflow = createProfileWorkflow(routineStateWorkflow, environment);
const characterArtWorkflow = createCharacterArtWorkflow(routineStateWorkflow, environment);
const recoveryWorkflow = createRecoveryWorkflow(
    application,
    presentationWorkflow,
    environment);
const rulesInputWorkflow = createRulesInputWorkflow(
    routineStateWorkflow,
    presentationWorkflow,
    environment);
const knownSpellWorkflow = createKnownSpellWorkflow(
    application,
    rulesInputWorkflow,
    environment);
const inventoryWorkflow = createInventoryWorkflow(
    application,
    routineStateWorkflow,
    presentationWorkflow,
    environment);
const notesWorkflow = createNotesWorkflow(routineStateWorkflow, environment);
const conditionsWorkflow = createConditionsWorkflow(
    application,
    routineStateWorkflow,
    environment);

application.render();
void bootstrapCharacter();
