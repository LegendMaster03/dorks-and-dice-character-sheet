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
    resolveStoredChoice,
    type RuleReferenceState
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
import {
    searchResolvedRules,
    type ResolvedRuleCatalogItem
} from "./rules-core-api.js";
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
            renderNewCharacter(
                section,
                state.screen.status === "error" ? state.screen.message : undefined,
                state.screen.recoveryCharacterId);
            break;
        case "loading":
            appendParagraph(section, "Loading Character Sheet…");
            break;
        case "submitting":
            appendParagraph(
                section,
                state.screen.operation === "new-character"
                    ? "Creating Character…"
                    : "Starting digital Character Sheet…");
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
        appendParagraph(
            section,
            "Standalone development does not invent Site Character ownership. Open this route through Dorks & Dice to create a canonical Character.");
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
    renderCharacterBuilder(section, character, false);
    appendDevelopmentDetails(section, character);
}

function renderArchivedCharacter(section: HTMLElement, character: CharacterSheetBootstrapResponse): void {
    appendCharacterHeading(section, character.name);
    appendParagraph(
        section,
        "This Character is archived. Its digital Character Sheet is preserved and read-only until the Character is restored through the Dorks & Dice Site.");
    if (character.hasRichSheet) {
        renderCharacterBuilder(section, character, true);
    }
    appendDevelopmentDetails(section, character);
}

function renderCharacterBuilder(
    section: HTMLElement,
    character: CharacterSheetBootstrapResponse,
    forceReadOnly: boolean
): void {
    const title = document.createElement("h2");
    title.textContent = "Character Builder";
    section.append(title);

    const builder = application.getState().builder;
    if (builder.status === "idle" || builder.status === "loading") {
        appendParagraph(section, "Loading character build…");
        return;
    }
    if (builder.status === "error") {
        appendParagraph(section, builder.message ?? "Unable to load character build state.");
        return;
    }
    if (builder.build === null) {
        appendParagraph(section, "Character build state is unavailable.");
        return;
    }

    const readOnly = forceReadOnly || builder.build.readOnly;
    renderChoice(
        section,
        character.characterId,
        "raceSpecies",
        "Race / Species",
        builder.references.raceSpecies,
        readOnly);
    renderChoice(
        section,
        character.characterId,
        "startingClass",
        "Starting Class",
        builder.references.startingClass,
        readOnly);

    const startingClass = getStartingClassEntry(builder.build);
    renderChoice(
        section,
        character.characterId,
        "subclass",
        "Subclass",
        builder.references.subclass,
        readOnly,
        startingClass !== null,
        "Choose a Class before selecting a Subclass.");

    appendParagraph(section, "Build status: In progress");
    if (builder.saveError !== undefined) {
        appendParagraph(section, builder.saveError);
    }

    if (builder.chooser.kind === "open") {
        renderRuleChooser(section, character.characterId, builder.chooser.target);
    }
}

function renderChoice(
    section: HTMLElement,
    characterId: string,
    target: CharacterBuilderChoice,
    labelText: string,
    reference: RuleReferenceState,
    readOnly: boolean,
    available = true,
    unavailableMessage?: string
): void {
    const container = document.createElement("div");
    container.setAttribute("data-builder-choice", target);

    const label = document.createElement("h3");
    label.textContent = labelText;
    container.append(label);
    renderRuleReference(container, reference);

    if (!available) {
        if (unavailableMessage !== undefined) {
            appendParagraph(container, unavailableMessage);
        }
        section.append(container);
        return;
    }

    if (!readOnly) {
        const choose = document.createElement("button");
        choose.type = "button";
        choose.disabled = application.getState().builder.saving !== null;
        choose.textContent = reference.status === "none" ? "Choose" : "Replace";
        choose.addEventListener("click", () => openChooser(target));
        container.append(choose);

        if (reference.status !== "none") {
            const clear = document.createElement("button");
            clear.type = "button";
            clear.disabled = application.getState().builder.saving !== null;
            clear.textContent = "Clear";
            clear.addEventListener("click", () => void clearChoice(characterId, target));
            container.append(clear);
        }
    }

    section.append(container);
}

function renderRuleReference(container: HTMLElement, reference: RuleReferenceState): void {
    switch (reference.status) {
        case "none":
            appendParagraph(container, "Not selected.");
            break;
        case "loading":
            appendParagraph(container, "Resolving saved choice through Rules Core…");
            break;
        case "resolved":
            appendParagraph(container, reference.rule.displayName);
            appendRuleMetadata(container, reference.rule.editionDisplayName, reference.rule.sourceCode, reference.rule.packageDisplayName);
            break;
        case "unavailable":
            appendParagraph(container, `Unavailable saved rule (${reference.conceptKey}). The selection has been retained.`);
            break;
        case "error":
            appendParagraph(
                container,
                `Rules Core could not resolve saved rule ${reference.conceptKey}: ${reference.message}`);
            break;
    }
}

function renderRuleChooser(
    section: HTMLElement,
    characterId: string,
    target: CharacterBuilderChoice
): void {
    const chooser = application.getState().builder.chooser;
    if (chooser.kind !== "open" || chooser.target !== target) return;

    const container = document.createElement("div");
    container.setAttribute("data-rule-chooser", target);
    const heading = document.createElement("h3");
    heading.textContent = target === "raceSpecies"
        ? "Choose Race / Species"
        : target === "startingClass" ? "Choose Starting Class" : "Choose Subclass";
    container.append(heading);

    const form = document.createElement("form");
    const input = document.createElement("input");
    input.type = "search";
    input.name = "ruleSearch";
    input.value = chooser.query;
    input.placeholder = "Search available rules";
    input.autocomplete = "off";
    form.append(input);
    const search = document.createElement("button");
    search.type = "submit";
    search.textContent = "Search";
    form.append(search);
    form.addEventListener("submit", event => {
        event.preventDefault();
        void loadChooser(target, input.value);
    });
    container.append(form);

    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = "Cancel";
    cancel.addEventListener("click", () => application.dispatch({ type: "chooser-closed" }));
    container.append(cancel);

    if (chooser.status === "idle" || chooser.status === "loading") {
        appendParagraph(container, "Loading Rules Core catalog…");
    } else if (chooser.status === "error") {
        appendParagraph(container, chooser.message ?? "Rules Core catalog is unavailable.");
    } else if (chooser.results.length === 0) {
        appendParagraph(container, "No matching rules are available.");
    } else {
        const list = document.createElement("ul");
        for (const rule of chooser.results) {
            list.append(renderRuleChooserResult(characterId, target, rule));
        }
        container.append(list);
    }

    section.append(container);
}

function renderRuleChooserResult(
    characterId: string,
    target: CharacterBuilderChoice,
    rule: ResolvedRuleCatalogItem
): HTMLLIElement {
    const item = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.disabled = application.getState().builder.saving !== null;
    button.textContent = rule.displayName;
    button.addEventListener("click", () => void saveChoice(characterId, target, rule.conceptKey));
    item.append(button);
    appendRuleMetadata(item, rule.editionDisplayName, rule.sourceCode, rule.packageDisplayName);
    return item;
}

function appendRuleMetadata(
    container: HTMLElement,
    editionDisplayName: string,
    sourceCode: string,
    packageDisplayName: string
): void {
    const parts = [editionDisplayName, sourceCode, packageDisplayName]
        .map(value => value.trim())
        .filter(value => value.length > 0);
    if (parts.length > 0) {
        const metadata = document.createElement("small");
        metadata.textContent = parts.join(" • ");
        container.append(metadata);
    }
}

function appendDevelopmentDetails(section: HTMLElement, character: CharacterSheetBootstrapResponse): void {
    const details = document.createElement("details");
    const summary = document.createElement("summary");
    summary.textContent = "Details";
    details.append(summary);
    appendParagraph(details, `Character ID: ${character.characterId}`);
    section.append(details);
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
    await Promise.all(((["raceSpecies", "startingClass", "subclass"] as const)).map(async target => {
        const conceptKey = getStoredChoiceConceptKey(build, target);
        if (conceptKey === null) return;
        const reference = await resolveStoredChoice(environment, build, target);
        application.dispatch({
            type: "rule-reference-resolved",
            target,
            conceptKey,
            reference
        });
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

        application.dispatch({
            type: "chooser-loaded",
            target,
            query: normalizedQuery,
            results
        });
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
