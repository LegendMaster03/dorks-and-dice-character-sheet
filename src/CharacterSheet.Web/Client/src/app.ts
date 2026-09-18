import "./styles.css";
import { createInitialState } from "./app-state.js";
import {
    addCharacterFeatOccurrence,
    clearCharacterBaseAbilityScore,
    clearCharacterBuildChoice,
    loadCharacterBuild,
    removeCharacterFeatOccurrence,
    setCharacterBaseAbilityScore,
    setCharacterBuildChoice,
    type CharacterAbilityKey,
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
import {
    addCharacterNote,
    addInventoryItemOccurrence,
    loadCharacterState,
    removeCharacterNote,
    removeInventoryItemOccurrence,
    updateCharacterNote
} from "./character-state-api.js";
import { resolveHostEnvironment } from "./host-environment.js";
import { createApplication } from "./render-lifecycle.js";
import { resolveRuleConcept, searchResolvedRules } from "./rules-core-api.js";
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
    const state = application.getState();
    const workspace = renderCharacterWorkspace(
        character,
        state.builder,
        state.routine,
        activeSection,
        forceReadOnly,
        state.sheetMode,
        state.guidedBuilder,
        null,
        {
            structural: {
                openChooser,
                clearChoice: target => void clearChoice(character.characterId, target),
                submitChooserSearch: (target, query) => void loadChooser(target, query),
                closeChooser: () => application.dispatch({ type: "chooser-closed" }),
                saveChoice: (target, conceptKey) => void saveChoice(character.characterId, target, conceptKey),
                setBaseAbilityScore: (abilityKey, score) =>
                    void saveBaseAbilityScore(character.characterId, abilityKey, score),
                clearBaseAbilityScore: abilityKey =>
                    void clearBaseAbilityScore(character.characterId, abilityKey)
            },
            feats: {
                openChooser: () => openFeatChooser(),
                closeChooser: () => application.dispatch({ type: "feat-chooser-closed" }),
                search: query => void loadFeatChooser(query),
                add: conceptKey => void addFeat(character.characterId, conceptKey),
                remove: occurrenceId => void removeFeat(character.characterId, occurrenceId)
            },
            routine: {
                addNote: content => void addNote(character.characterId, content),
                updateNote: (noteId, content) => void updateNote(character.characterId, noteId, content),
                deleteNote: noteId => void deleteNote(character.characterId, noteId),
                openInventoryChooser: () => openInventoryChooser(),
                closeInventoryChooser: () => application.dispatch({ type: "inventory-chooser-closed" }),
                searchInventory: query => void loadInventoryChooser(query),
                addInventoryItem: conceptKey => void addInventoryItem(character.characterId, conceptKey),
                removeInventoryItem: occurrenceId => void removeInventoryItem(character.characterId, occurrenceId)
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
                bootstrapBuild(character.characterId),
                bootstrapRoutine(character.characterId)
            ]);
        }
    } catch (error) {
        application.dispatch({ type: "load-failed", message: errorMessage(error) });
    }
}

async function bootstrapRoutine(characterId: string): Promise<void> {
    application.dispatch({ type: "routine-load-started" });
    try {
        const routine = await loadCharacterState(environment, characterId);
        application.dispatch({ type: "routine-loaded", state: routine });
        await resolveRoutineReferences(routine);
    } catch (error) {
        application.dispatch({ type: "routine-load-failed", message: errorMessage(error) });
    }
}

async function resolveRoutineReferences(routine: Awaited<ReturnType<typeof loadCharacterState>>): Promise<void> {
    await Promise.all(routine.inventoryItemOccurrences.map(async occurrence => {
        try {
            const rule = await resolveRuleConcept(environment, occurrence.ruleConceptKey);
            const reference = rule !== null
                && rule.entityType === "item"
                && rule.conceptKey === occurrence.ruleConceptKey
                ? { status: "resolved" as const, conceptKey: occurrence.ruleConceptKey, rule }
                : { status: "unavailable" as const, conceptKey: occurrence.ruleConceptKey };
            application.dispatch({
                type: "routine-reference-resolved",
                occurrenceId: occurrence.id,
                conceptKey: occurrence.ruleConceptKey,
                reference
            });
        } catch (error) {
            application.dispatch({
                type: "routine-reference-resolved",
                occurrenceId: occurrence.id,
                conceptKey: occurrence.ruleConceptKey,
                reference: {
                    status: "error",
                    conceptKey: occurrence.ruleConceptKey,
                    message: errorMessage(error)
                }
            });
        }
    }));
}

async function applyRoutineMutation(
    kind: "note-add" | "note-update" | "note-delete" | "inventory-add" | "inventory-delete",
    operation: () => ReturnType<typeof addCharacterNote>,
    entryId?: string
): Promise<void> {
    const routine = application.getState().routine;
    if (routine.status !== "ready"
        || routine.state === null
        || routine.state.readOnly
        || routine.mutation !== null) {
        return;
    }

    application.dispatch({ type: "routine-mutation-started", kind, entryId });
    try {
        const next = await operation();
        application.dispatch({ type: "routine-mutation-succeeded", state: next });
        await resolveRoutineReferences(next);
    } catch (error) {
        application.dispatch({ type: "routine-mutation-failed", message: errorMessage(error) });
    }
}

async function addNote(characterId: string, content: string): Promise<void> {
    if (content.trim().length === 0) return;
    await applyRoutineMutation(
        "note-add",
        () => addCharacterNote(environment, characterId, content));
}

async function updateNote(characterId: string, noteId: string, content: string): Promise<void> {
    if (content.trim().length === 0) return;
    await applyRoutineMutation(
        "note-update",
        () => updateCharacterNote(environment, characterId, noteId, content),
        noteId);
}

async function deleteNote(characterId: string, noteId: string): Promise<void> {
    await applyRoutineMutation(
        "note-delete",
        () => removeCharacterNote(environment, characterId, noteId),
        noteId);
}

function openInventoryChooser(): void {
    application.dispatch({ type: "inventory-chooser-opened" });
    void loadInventoryChooser("");
}

async function loadInventoryChooser(query: string): Promise<void> {
    const normalizedQuery = query.trim();
    application.dispatch({ type: "inventory-chooser-load-started", query: normalizedQuery });
    try {
        const catalog = await searchResolvedRules(environment, "item", normalizedQuery);
        application.dispatch({
            type: "inventory-chooser-loaded",
            query: normalizedQuery,
            results: catalog.rules.filter(rule => rule.entityType === "item")
        });
    } catch (error) {
        application.dispatch({
            type: "inventory-chooser-load-failed",
            query: normalizedQuery,
            message: errorMessage(error)
        });
    }
}

async function addInventoryItem(characterId: string, conceptKey: string): Promise<void> {
    await applyRoutineMutation(
        "inventory-add",
        () => addInventoryItemOccurrence(environment, characterId, conceptKey));
}

async function removeInventoryItem(characterId: string, occurrenceId: string): Promise<void> {
    await applyRoutineMutation(
        "inventory-delete",
        () => removeInventoryItemOccurrence(environment, characterId, occurrenceId),
        occurrenceId);
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
    const structural = (["raceSpecies", "startingClass", "subclass"] as const).map(async target => {
        const conceptKey = getStoredChoiceConceptKey(build, target);
        if (conceptKey === null) return;
        const reference = await resolveStoredChoice(environment, build, target);
        application.dispatch({ type: "rule-reference-resolved", target, conceptKey, reference });
    });
    const feats = build.progressionEntries
        .filter(value => value.kind === "feat")
        .map(async occurrence => {
            try {
                const rule = await resolveRuleConcept(environment, occurrence.ruleConceptKey);
                const reference = rule !== null
                    && rule.entityType === "feat"
                    && rule.conceptKey === occurrence.ruleConceptKey
                    ? { status: "resolved" as const, conceptKey: occurrence.ruleConceptKey, rule }
                    : { status: "unavailable" as const, conceptKey: occurrence.ruleConceptKey };
                application.dispatch({
                    type: "feat-reference-resolved",
                    occurrenceId: occurrence.id,
                    conceptKey: occurrence.ruleConceptKey,
                    reference
                });
            } catch (error) {
                application.dispatch({
                    type: "feat-reference-resolved",
                    occurrenceId: occurrence.id,
                    conceptKey: occurrence.ruleConceptKey,
                    reference: {
                        status: "error",
                        conceptKey: occurrence.ruleConceptKey,
                        message: errorMessage(error)
                    }
                });
            }
        });
    await Promise.all([...structural, ...feats]);
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

function openFeatChooser(): void {
    application.dispatch({ type: "feat-chooser-opened" });
    void loadFeatChooser("");
}

async function loadFeatChooser(query: string): Promise<void> {
    const normalizedQuery = query.trim();
    application.dispatch({ type: "feat-chooser-load-started", query: normalizedQuery });
    try {
        const catalog = await searchResolvedRules(environment, "feat", normalizedQuery);
        application.dispatch({
            type: "feat-chooser-loaded",
            query: normalizedQuery,
            results: catalog.rules.filter(rule => rule.entityType === "feat")
        });
    } catch (error) {
        application.dispatch({
            type: "feat-chooser-load-failed",
            query: normalizedQuery,
            message: errorMessage(error)
        });
    }
}

async function addFeat(characterId: string, conceptKey: string): Promise<void> {
    const builder = application.getState().builder;
    if (builder.status !== "ready"
        || builder.build === null
        || builder.build.readOnly
        || builder.saving !== null
        || builder.savingAbility !== null
        || builder.savingFeat !== null) {
        return;
    }

    application.dispatch({ type: "feat-save-started" });
    try {
        const build = await addCharacterFeatOccurrence(environment, characterId, conceptKey);
        application.dispatch({ type: "feat-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "feat-save-failed", message: errorMessage(error) });
    }
}

async function removeFeat(characterId: string, occurrenceId: string): Promise<void> {
    const builder = application.getState().builder;
    if (builder.status !== "ready"
        || builder.build === null
        || builder.build.readOnly
        || builder.saving !== null
        || builder.savingAbility !== null
        || builder.savingFeat !== null) {
        return;
    }

    application.dispatch({ type: "feat-save-started", occurrenceId });
    try {
        const build = await removeCharacterFeatOccurrence(environment, characterId, occurrenceId);
        application.dispatch({ type: "feat-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "feat-save-failed", message: errorMessage(error) });
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

async function saveBaseAbilityScore(
    characterId: string,
    abilityKey: CharacterAbilityKey,
    score: number
): Promise<void> {
    application.dispatch({ type: "ability-save-started", abilityKey });
    try {
        const build = await setCharacterBaseAbilityScore(environment, characterId, abilityKey, score);
        application.dispatch({ type: "ability-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "ability-save-failed", abilityKey, message: errorMessage(error) });
    }
}

async function clearBaseAbilityScore(
    characterId: string,
    abilityKey: CharacterAbilityKey
): Promise<void> {
    application.dispatch({ type: "ability-save-started", abilityKey });
    try {
        const build = await clearCharacterBaseAbilityScore(environment, characterId, abilityKey);
        application.dispatch({ type: "ability-saved", build });
        await resolveBuildReferences(build);
    } catch (error) {
        application.dispatch({ type: "ability-save-failed", abilityKey, message: errorMessage(error) });
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
        await Promise.all([
            bootstrapBuild(initialized.characterId),
            bootstrapRoutine(initialized.characterId)
        ]);
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
