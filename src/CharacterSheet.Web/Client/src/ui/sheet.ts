import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState,
    GuidedBuilderUiState,
    SheetMode
} from "../app-state.js";
import type { CharacterAbilityKey } from "../builder-api.js";
import type { CharacterSheetBootstrapResponse } from "../character-api.js";
import type { CharacterBuilderHandlers } from "./builder.js";
import { renderCharacterBuilder } from "./builder.js";
import {
    createCompactAdvancementSummary,
    type CharacterAdvancementView
} from "./character-advancement.js";
import { renderAdvancementDetails } from "./advancement.js";
import {
    buildCompetencyPresentation,
    findAbilityValue,
    findItemOccurrenceMechanics,
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView,
    type InventoryMechanicsView
} from "./character-mechanics.js";
import {
    findInitiativeValue,
    renderActionsPresentation,
    renderCombatMechanicsSummary,
    renderFacts,
    renderMechanicalValue,
    renderMovementValues,
    renderQuickMechanicalValue
} from "./mechanics-components.js";
import {
    renderChecksAndProceduresPresentation,
    renderInventoryMechanics,
    renderItemOccurrenceMechanics,
    renderSpellcastingPresentation
} from "./procedure-components.js";
import {
    createButton,
    createElement,
    createInlineState,
    createSectionCard
} from "./components.js";
import { renderSkillsCard } from "./skills.js";
import { renderSourceAttributions } from "./source-attribution.js";
import {
    ABILITY_SCORE_DEFINITIONS,
    createCharacterHeaderModel,
    getAbilityScoreActionPolicy,
    getBaseAbilityScoreDisplay,
    getGuidedBuilderSectionStates,
    hasPendingBuildMutation,
    humanizeBuilderStatus,
    parseBaseAbilityScoreInput,
    SHEET_SECTIONS,
    toRuleReferenceDisplay,
    type AbilityScoreDefinition,
    type GuidedBuilderSection,
    type SheetSection
} from "./sheet-model.js";

export interface StructuralCharacterHandlers extends CharacterBuilderHandlers {
    setBaseAbilityScore(abilityKey: CharacterAbilityKey, score: number): void;
    clearBaseAbilityScore(abilityKey: CharacterAbilityKey): void;
}

export interface RoutineCharacterHandlers {
    addNote(content: string): void;
    updateNote(noteId: string, content: string): void;
    deleteNote(noteId: string): void;
    openInventoryChooser(): void;
    closeInventoryChooser(): void;
    searchInventory(query: string): void;
    addInventoryItem(conceptKey: string): void;
    removeInventoryItem(occurrenceId: string): void;
}

export interface FeatCharacterHandlers {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): void;
    add(conceptKey: string): void;
    remove(occurrenceId: string): void;
}

export interface CharacterSheetHandlers {
    structural: StructuralCharacterHandlers;
    feats: FeatCharacterHandlers;
    routine: RoutineCharacterHandlers;
    selectSection(section: SheetSection): void;
    enterEditMode(): void;
    leaveEditMode(): void;
    openGuidedBuilder(): void;
    closeGuidedBuilder(): void;
    selectGuidedBuilderSection(section: GuidedBuilderSection): void;
}

export function renderCharacterWorkspace(
    character: CharacterSheetBootstrapResponse,
    builder: CharacterBuilderUiState,
    routine: CharacterRoutineUiState,
    activeSection: SheetSection,
    forceReadOnly: boolean,
    sheetMode: SheetMode,
    guidedBuilder: GuidedBuilderUiState,
    advancement: CharacterAdvancementView | null,
    mechanics: CharacterMechanicsView | null,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const shell = createElement("article", "dd-sheet");
    shell.setAttribute("data-character-sheet-shell", "true");
    const readOnly = forceReadOnly
        || builder.build?.readOnly === true
        || routine.state?.readOnly === true
        || character.lifecycle === "Archived";
    const editable = !readOnly && builder.status === "ready" && builder.build !== null;
    const structuralEditing = editable && sheetMode === "edit";
    shell.setAttribute("data-read-only", readOnly ? "true" : "false");
    shell.setAttribute("data-sheet-mode", sheetMode);
    shell.setAttribute("data-guided-builder-open", guidedBuilder.open ? "true" : "false");

    shell.append(renderCharacterHeader(character, builder, forceReadOnly, advancement));
    if (advancement !== null && advancement.occurrences.length > 0) {
        shell.append(renderAdvancementDetails(advancement));
    }
    if (editable) {
        shell.append(renderModeControls(sheetMode, guidedBuilder, handlers));
    }
    if (readOnly) {
        shell.append(renderReadOnlyBanner(character.lifecycle === "Archived"));
    }

    if (guidedBuilder.open && editable) {
        shell.append(renderGuidedBuilder(character.characterId, builder, guidedBuilder, handlers));
        return shell;
    }

    const mechanicsSources = renderCharacterMechanicsSources(mechanics);
    if (mechanicsSources !== null) shell.append(mechanicsSources);

    shell.append(renderCoreStats(builder, structuralEditing, readOnly, mechanics, handlers.structural));

    const workspace = createElement("div", "dd-sheet__workspace");
    const support = createElement("aside", "dd-sheet__support dd-sheet__support--left");
    support.setAttribute("aria-label", "Character supporting statistics");
    support.append(
        renderSupportScaffoldCard("Passive Values", [
            ["passive-perception", "Perception"],
            ["passive-investigation", "Investigation"],
            ["passive-insight", "Insight"]
        ]),
        renderSupportScaffoldCard("Proficiencies & Training", [
            ["armor-training", "Armor"],
            ["weapon-training", "Weapons"],
            ["tool-training", "Tools"],
            ["languages", "Languages"]
        ])
    );

    const skills = createElement("section", "dd-sheet__skills");
    skills.setAttribute("aria-label", "Character skills");
    const competencyPresentation = mechanics?.competencies === undefined
        ? null
        : buildCompetencyPresentation(mechanics.competencies);
    skills.append(renderSkillsCard(competencyPresentation));

    const primary = createElement("section", "dd-sheet__main");
    primary.setAttribute("aria-label", "Character details and controls");
    primary.append(renderCombatMechanicsSummary(mechanics));
    if (structuralEditing) {
        primary.append(renderCharacterBuilder(
            character.characterId,
            builder,
            forceReadOnly,
            handlers.structural));
    }
    primary.append(renderPrimaryContent(
        activeSection,
        builder,
        routine,
        structuralEditing,
        readOnly,
        mechanics,
        handlers));

    workspace.append(support, skills, primary);
    shell.append(workspace);
    return shell;
}

function renderModeControls(
    sheetMode: SheetMode,
    guidedBuilder: GuidedBuilderUiState,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const controls = createElement("div", "dd-sheet-mode-bar");
    controls.setAttribute("role", "group");
    controls.setAttribute("aria-label", "Character configuration mode");

    if (guidedBuilder.open) {
        const closeBuilder = createButton(
            "Back to Character Sheet",
            "dd-button dd-button--ghost",
            handlers.closeGuidedBuilder);
        closeBuilder.setAttribute("data-sheet-mode-control", "guided-close");
        controls.append(closeBuilder);
        return controls;
    }

    const editing = sheetMode === "edit";
    const editToggle = createButton(
        editing ? "Done Editing" : "Edit Character",
        editing ? "dd-button dd-button--primary" : "dd-button dd-button--secondary",
        editing ? handlers.leaveEditMode : handlers.enterEditMode);
    editToggle.setAttribute("aria-pressed", editing ? "true" : "false");
    editToggle.setAttribute("data-sheet-mode-control", editing ? "view" : "edit");

    const guided = createButton(
        "Guided Setup",
        "dd-button dd-button--ghost",
        handlers.openGuidedBuilder);
    guided.setAttribute("data-sheet-mode-control", "guided");
    controls.append(editToggle, guided);
    return controls;
}

function renderGuidedBuilder(
    characterId: string,
    builder: CharacterBuilderUiState,
    guidedBuilder: GuidedBuilderUiState,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const container = createElement("section", "dd-guided-builder");
    container.setAttribute("aria-labelledby", "dd-guided-builder-heading");
    container.setAttribute("data-guided-builder", "true");

    const heading = createElement("h2", "dd-guided-builder__title", "Guided Character Setup");
    heading.id = "dd-guided-builder-heading";
    container.append(
        heading,
        createElement(
            "p",
            "dd-guided-builder__intro",
            "Use any section that is useful. The Character Sheet remains available even when setup is incomplete."));

    const sectionStates = getGuidedBuilderSectionStates(builder);
    const nav = createElement("nav", "dd-guided-builder__nav");
    nav.setAttribute("aria-label", "Guided builder sections");
    for (const section of sectionStates) {
        const active = section.id === guidedBuilder.activeSection;
        const button = createButton(
            `${section.label} · ${guidedStatusLabel(section.status)}`,
            active
                ? "dd-guided-builder__nav-button dd-guided-builder__nav-button--active"
                : "dd-guided-builder__nav-button",
            () => handlers.selectGuidedBuilderSection(section.id));
        button.setAttribute("data-guided-builder-section", section.id);
        if (active) button.setAttribute("aria-current", "page");
        nav.append(button);
    }
    container.append(nav);

    const panel = createElement("div", "dd-guided-builder__panel");
    panel.setAttribute("data-guided-builder-active-section", guidedBuilder.activeSection);

    switch (guidedBuilder.activeSection) {
        case "species":
            panel.append(renderCharacterBuilder(
                characterId,
                builder,
                false,
                handlers.structural,
                { title: "Species", choices: ["raceSpecies"] }));
            break;
        case "advancement":
            panel.append(renderCharacterBuilder(
                characterId,
                builder,
                false,
                handlers.structural,
                { title: "Advancement", choices: ["startingClass", "subclass"] }));
            break;
        case "abilities": {
            const abilities = createSectionCard("Base Ability Scores", "dd-guided-builder__abilities");
            abilities.append(createElement(
                "p",
                "dd-guided-builder__section-copy",
                "These are the persisted base-score inputs currently supported by the Character backend."));
            const grid = createElement("div", "dd-core-stats__abilities dd-guided-builder__ability-grid");
            for (const definition of ABILITY_SCORE_DEFINITIONS) {
                grid.append(renderAbilityScoreCard(
                    definition,
                    builder,
                    true,
                    false,
                    handlers.structural));
            }
            abilities.append(grid);
            panel.append(abilities);
            break;
        }
        case "review": {
            const review = createSectionCard("Character Setup", "dd-guided-builder__review");
            review.append(createElement(
                "p",
                "dd-guided-builder__section-copy",
                "This list reports only configuration the current backend can determine. It does not invent edition-specific completion requirements."));
            const list = createElement("ul", "dd-guided-builder__review-list");
            for (const section of sectionStates.filter(value => value.id !== "review")) {
                const item = createElement("li", "dd-guided-builder__review-item");
                item.setAttribute("data-guided-builder-status", section.status);
                item.append(
                    createElement("strong", "dd-guided-builder__review-label", section.label),
                    createElement("span", "dd-guided-builder__review-status", guidedStatusLabel(section.status)),
                    createElement("span", "dd-guided-builder__review-detail", section.detail));
                list.append(item);
            }
            review.append(list);
            panel.append(review);
            break;
        }
    }

    container.append(panel);
    return container;
}

function guidedStatusLabel(status: ReturnType<typeof getGuidedBuilderSectionStates>[number]["status"]): string {
    switch (status) {
        case "resolved":
            return "Resolved";
        case "incomplete":
            return "Incomplete";
        case "available":
            return "Available";
        case "unavailable":
            return "Unavailable";
    }
}

export function renderCharacterHeader(
    character: CharacterSheetBootstrapResponse,
    builder: CharacterBuilderUiState,
    forceReadOnly: boolean,
    advancement: CharacterAdvancementView | null = null
): HTMLElement {
    const model = createCharacterHeaderModel(character, builder, forceReadOnly);
    const header = createElement("header", "dd-sheet-header");

    const identity = createElement("div", "dd-sheet-header__identity");
    const monogram = createElement("div", "dd-sheet-header__portrait", characterInitials(character.name));
    monogram.setAttribute("aria-hidden", "true");
    const text = createElement("div", "dd-sheet-header__text");
    const name = createElement("h1", "dd-sheet-header__name", model.name);
    const statusLine = createElement("div", "dd-sheet-header__status");
    statusLine.append(createElement("span", "dd-sheet-header__lifecycle", model.lifecycleLabel));
    const builderStatus = humanizeBuilderStatus(model.builderStatus);
    if (builderStatus !== null) {
        statusLine.append(createElement("span", "dd-sheet-header__separator", "•"));
        statusLine.append(createElement("span", "dd-sheet-header__builder-status", builderStatus));
    }
    if (model.campaignContext !== null) {
        statusLine.append(createElement("span", "dd-sheet-header__separator", "•"));
        statusLine.append(createElement("span", "dd-sheet-header__campaign", model.campaignContext));
    }
    text.append(name, statusLine);
    identity.append(monogram, text);

    const advancementSummary = advancement === null
        ? legacyAdvancementHeaderSummary(model.startingClass.value, model.subclass.value)
        : createCompactAdvancementSummary(advancement);
    const summary = createElement("dl", "dd-sheet-header__summary");
    summary.append(
        headerSummaryItem("Race / Species", model.raceSpecies.value, model.raceSpecies.detail),
        headerSummaryItem("Advancement", advancementSummary.value, advancementSummary.detail)
    );
    if (model.readOnly) {
        summary.append(headerSummaryItem("Sheet state", "Read-only", "Restore the Character through the Site to edit."));
    }

    header.append(identity, summary);
    return header;
}

function renderReadOnlyBanner(archived: boolean): HTMLElement {
    const banner = createElement("section", "dd-readonly-banner");
    banner.setAttribute("role", "status");
    const title = createElement(
        "strong",
        "dd-readonly-banner__title",
        archived ? "Archived Character — read-only" : "Character Sheet — read-only");
    const message = createElement(
        "span",
        "dd-readonly-banner__message",
        archived
            ? "The digital Character Sheet is preserved. Restore the Character through the Dorks & Dice Site before making changes."
            : "Editing is unavailable for this Character Sheet state.");
    banner.append(title, message);
    return banner;
}

function renderCoreStats(
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null,
    handlers: StructuralCharacterHandlers
): HTMLElement {
    const section = createElement("section", "dd-core-stats");
    section.setAttribute("aria-labelledby", "dd-core-stats-heading");
    const heading = createElement("h2", "dd-visually-hidden", "Core statistics");
    heading.id = "dd-core-stats-heading";
    section.append(heading);

    const abilityGrid = createElement("div", "dd-core-stats__abilities");
    for (const definition of ABILITY_SCORE_DEFINITIONS) {
        abilityGrid.append(renderAbilityScoreCard(
            definition,
            builder,
            structuralEditing,
            readOnly,
            handlers,
            findAbilityValue(mechanics?.abilityValues, definition.key)));
    }

    const quickGrid = createElement("div", "dd-core-stats__quick");
    const movement = createElement("article", "dd-stat dd-stat--movement");
    movement.append(
        createElement("h3", "dd-stat__label", "Movement"),
        renderMovementValues(mechanics?.movement));

    const initiative = createElement("article", "dd-stat dd-stat--initiative");
    initiative.append(
        createElement("h3", "dd-stat__label", "Initiative"),
        renderQuickMechanicalValue(findInitiativeValue(mechanics?.combatFundamentals)));

    quickGrid.append(movement, initiative);
    section.append(abilityGrid, quickGrid);

    const standardAbilityKeys = new Set(ABILITY_SCORE_DEFINITIONS.map(definition => definition.key));
    const additionalAbilityValues = mechanics?.abilityValues?.filter(value => !standardAbilityKeys.has(value.key as CharacterAbilityKey)) ?? [];
    if (additionalAbilityValues.length > 0) {
        section.append(renderAdditionalAbilityValues(additionalAbilityValues));
    }
    return section;
}

function renderAbilityScoreCard(
    definition: AbilityScoreDefinition,
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    handlers: StructuralCharacterHandlers,
    effectiveValue?: CalculatedMechanicalValueView
): HTMLElement {
    const display = getBaseAbilityScoreDisplay(builder, definition.key);
    const configured = display.status === "configured";
    const policy = getAbilityScoreActionPolicy(builder, readOnly, configured);
    const card = createElement("article", "dd-stat dd-stat--ability");
    card.setAttribute("data-ability-key", definition.key);
    card.setAttribute("data-ability-score-state", display.status);
    card.setAttribute("data-effective-ability-state", effectiveValue === undefined ? "unavailable" : "resolved");
    card.append(createElement("h3", "dd-stat__label", definition.label));

    if (effectiveValue === undefined) {
        card.append(
            createElement("p", "dd-stat__value", display.value),
            createElement("p", "dd-stat__detail", display.detail),
            createElement("p", "dd-stat__modifier", "Modifier -")
        );
    } else {
        card.setAttribute("data-effective-ability-key", effectiveValue.key);
        card.append(
            createElement("p", "dd-stat__value", formatMechanicalValue(effectiveValue)),
            createElement("p", "dd-stat__detail", "Effective value"),
            createElement(
                "p",
                "dd-stat__base-context",
                `Base input: ${display.value}`)
        );

        if (effectiveValue.relatedValues?.length) {
            const related = createElement("div", "dd-stat__related-values");
            for (const value of effectiveValue.relatedValues) {
                related.append(createElement(
                    "span",
                    "dd-stat__related-value",
                    `${value.label} ${formatMechanicalValue(value)}`));
            }
            card.append(related);
        } else {
            card.append(createElement("p", "dd-stat__modifier", "Modifier -"));
        }

        const details = renderAbilityMechanicalDetails(effectiveValue);
        if (details !== null) card.append(details);
    }

    if (structuralEditing && !readOnly && (display.status === "configured" || display.status === "unconfigured")) {
        const editor = createElement("div", "dd-stat__editor");
        const input = createElement("input", "dd-stat__input");
        input.type = "number";
        input.step = "1";
        input.inputMode = "numeric";
        input.autocomplete = "off";
        input.value = display.score === null ? "" : String(display.score);
        input.setAttribute("aria-label", `${definition.label} Base Score`);
        input.disabled = !policy.canSave;
        input.addEventListener("input", () => input.setCustomValidity(""));

        const actions = createElement("div", "dd-stat__actions");
        actions.append(createButton(
            policy.saveLabel,
            "dd-stat__button",
            () => {
                const parsed = parseBaseAbilityScoreInput(input.value);
                if (!parsed.ok) {
                    input.setCustomValidity(parsed.message);
                    input.reportValidity();
                    return;
                }
                input.setCustomValidity("");
                handlers.setBaseAbilityScore(definition.key, parsed.score);
            },
            !policy.canSave));

        if (configured) {
            actions.append(createButton(
                "Clear",
                "dd-stat__button dd-stat__button--secondary",
                () => handlers.clearBaseAbilityScore(definition.key),
                !policy.canClear));
        }

        editor.append(input, actions);
        card.append(editor);
    }

    if (builder.abilitySaveError?.abilityKey === definition.key) {
        const error = createElement("p", "dd-stat__error", builder.abilitySaveError.message);
        error.setAttribute("role", "alert");
        card.append(error);
    }

    return card;
}

function renderAbilityMechanicalDetails(value: CalculatedMechanicalValueView): HTMLElement | null {
    if (!(value.breakdown?.length || value.sourceAttributions?.length)) return null;

    const details = createElement("details", "dd-stat__mechanics-details");
    details.append(createElement("summary", "dd-stat__mechanics-details-toggle", "Details"));
    const body = createElement("div", "dd-stat__mechanics-details-body");
    const breakdown = renderFacts((value.breakdown ?? []).map(entry =>
        [entry.label, formatMechanicalValue(entry)] as const));
    if (breakdown !== null) body.append(breakdown);
    const sources = renderSourceAttributions(value.sourceAttributions, true);
    if (sources !== null) body.append(sources);
    details.append(body);
    return details;
}

function renderAdditionalAbilityValues(values: readonly CalculatedMechanicalValueView[]): HTMLElement {
    const section = createElement("section", "dd-core-stats__additional-abilities");
    section.setAttribute("aria-label", "Additional effective abilities");
    section.append(createElement("h3", "dd-core-stats__additional-title", "Additional Abilities"));
    const grid = createElement("div", "dd-core-stats__additional-grid");
    for (const value of values) {
        const card = createElement("article", "dd-additional-ability");
        card.setAttribute("data-additional-ability-key", value.key);
        card.append(renderMechanicalValue(value));
        grid.append(card);
    }
    section.append(grid);
    return section;
}

function renderCharacterMechanicsSources(mechanics: CharacterMechanicsView | null): HTMLElement | null {
    const sources = renderSourceAttributions(mechanics?.sourceAttributions, true);
    if (sources === null) return null;

    const surface = createElement("aside", "dd-character-mechanics-sources");
    surface.setAttribute("aria-label", "Character mechanics rule modules");
    surface.append(
        createElement("span", "dd-character-mechanics-sources__label", "Rules modules"),
        sources);
    return surface;
}

function renderSupportScaffoldCard(
    title: string,
    values: readonly (readonly [string, string])[]
): HTMLElement {
    const card = createSectionCard(title, "dd-support-card dd-support-scaffold");
    const list = createElement("div", "dd-support-scaffold__list");
    for (const [key, label] of values) {
        const row = createElement("div", "dd-support-scaffold__row");
        row.setAttribute("data-support-scaffold-key", key);
        row.append(
            createElement("span", "dd-support-scaffold__label", label),
            createElement("strong", "dd-support-scaffold__value", "-"));
        list.append(row);
    }
    card.append(list);
    return card;
}

function renderPrimaryContent(
    activeSection: SheetSection,
    builder: CharacterBuilderUiState,
    routine: CharacterRoutineUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const card = createElement("section", "dd-primary-content");
    const nav = createElement("nav", "dd-primary-nav");
    nav.setAttribute("aria-label", "Character sheet sections");
    for (const section of SHEET_SECTIONS) {
        const active = section.id === activeSection;
        const button = createButton(
            section.label,
            active ? "dd-primary-nav__button dd-primary-nav__button--active" : "dd-primary-nav__button",
            () => handlers.selectSection(section.id));
        if (active) button.setAttribute("aria-current", "page");
        nav.append(button);
    }

    const definition = SHEET_SECTIONS.find(value => value.id === activeSection) ?? SHEET_SECTIONS[0];
    const panel = createElement("div", "dd-primary-content__panel");
    panel.setAttribute("data-sheet-section", definition.id);
    panel.append(createElement("h2", "dd-primary-content__title", definition.label));
    if (definition.id === "notes") {
        panel.append(renderNotesSection(routine, readOnly, handlers.routine));
    } else if (definition.id === "inventory") {
        panel.append(renderInventorySection(routine, readOnly, handlers.routine, mechanics?.inventory));
    } else if (definition.id === "features") {
        panel.append(renderFeaturesSection(builder, structuralEditing, readOnly, handlers.feats));
    } else if (definition.id === "actions") {
        const presentation = createElement("div", "dd-action-workflows");
        presentation.append(
            renderActionsPresentation(mechanics?.actions),
            renderChecksAndProceduresPresentation(mechanics?.checks, mechanics?.procedures));
        panel.append(presentation);
    } else if (definition.id === "spells") {
        panel.append(renderSpellcastingPresentation(mechanics?.spellcastingProfiles));
    } else {
        panel.append(
            createElement("h3", "dd-primary-content__empty-title", definition.emptyTitle),
            createElement("p", "dd-primary-content__empty-message", definition.emptyMessage)
        );
    }
    card.append(nav, panel);
    return card;
}

function renderFeaturesSection(
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    handlers: FeatCharacterHandlers
): HTMLElement {
    const content = createElement("div", "dd-feature-sections");
    const feats = createElement("section", "dd-feature-subsection");
    feats.append(createElement("h3", "dd-feature-subsection__title", "Feats"));

    if (builder.status === "idle" || builder.status === "loading") {
        feats.append(createInlineState("Loading Character Feats…", "loading"));
    } else if (builder.status === "error" || builder.build === null) {
        feats.append(createInlineState(
            builder.message ?? "Character Feats are unavailable.",
            "error"));
    } else {
        const pending = hasPendingBuildMutation(builder);
        const editable = structuralEditing && !readOnly && !builder.build.readOnly;

        if (builder.featSaveError !== undefined) {
            feats.append(createInlineState(builder.featSaveError, "error"));
        }
        if (editable) {
            const actions = createElement("div", "dd-routine-actions");
            actions.append(createButton(
                "Add Feat",
                "dd-button dd-button--primary",
                handlers.openChooser,
                pending));
            feats.append(actions);
        }
        if (builder.featChooser.kind === "open" && editable) {
            feats.append(renderFeatChooser(builder, handlers));
        }

        const occurrences = builder.build.progressionEntries.filter(value => value.kind === "feat");
        if (occurrences.length === 0) {
            feats.append(createElement(
                "p",
                "dd-routine-empty",
                editable
                    ? "No Feat occurrences yet. Add one from the Rules Core catalog."
                    : "No Character-owned Feat occurrences have been recorded."));
        } else {
            const list = createElement("div", "dd-feat-list");
            for (const occurrence of occurrences) {
                const item = createElement("article", "dd-feat");
                item.setAttribute("data-feat-occurrence-id", occurrence.id);
                const reference = builder.featReferences[occurrence.id] ?? {
                    status: "loading" as const,
                    conceptKey: occurrence.ruleConceptKey
                };
                const display = toRuleReferenceDisplay(reference);
                const title = display.tone === "unavailable"
                    ? "Unavailable Feat reference"
                    : display.value;
                item.append(
                    createElement("h4", "dd-feat__name", title),
                    createElement(
                        "p",
                        "dd-routine-meta",
                        display.detail ?? occurrence.ruleConceptKey)
                );
                if (editable) {
                    item.append(createButton(
                        builder.savingFeat === occurrence.id ? "Removing…" : "Remove",
                        "dd-button dd-button--ghost",
                        () => {
                            if (window.confirm("Remove this Feat occurrence from the Character?")) {
                                handlers.remove(occurrence.id);
                            }
                        },
                        pending));
                }
                list.append(item);
            }
            feats.append(list);
        }
    }

    const other = createElement("section", "dd-feature-subsection");
    other.append(
        createElement("h3", "dd-feature-subsection__title", "Other Features & Traits"),
        createInlineState(
            "Class, Subclass, Species, and other granted features are not available yet because Rules Core does not expose the normalized ordinary-Character feature/effect consumer contract.",
            "neutral"));
    content.append(feats, other);
    return content;
}

function renderFeatChooser(
    builder: CharacterBuilderUiState,
    handlers: FeatCharacterHandlers
): HTMLElement {
    const chooser = builder.featChooser;
    const section = createElement("section", "dd-rule-chooser dd-feat-chooser");
    if (chooser.kind !== "open") return section;

    const heading = createElement("h4", "dd-rule-chooser__title", "Add Feat");
    const search = createElement("form", "dd-rule-chooser__search");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search Rules Core Feats";
    input.setAttribute("aria-label", "Search Rules Core Feats");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search");
    submit.type = "submit";
    const close = createButton("Close", "dd-button dd-button--ghost", handlers.closeChooser);
    search.append(input, submit, close);
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.search(input.value);
    });
    section.append(heading, search);

    if (chooser.status === "idle" || chooser.status === "loading") {
        section.append(createInlineState("Loading Rules Core Feats…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(chooser.message ?? "Rules Core Feat search failed.", "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching Rules Core Feats."));
        return section;
    }

    const results = createElement("div", "dd-rule-chooser__results");
    for (const rule of chooser.results) {
        const result = createElement("article", "dd-rule-chooser__result");
        result.append(
            createElement("strong", "dd-rule-chooser__result-name", rule.displayName),
            createElement(
                "span",
                "dd-rule-chooser__result-meta",
                [rule.editionDisplayName, rule.sourceCode].filter(Boolean).join(" • ")),
            createElement("span", "dd-rule-chooser__result-key", rule.conceptKey),
            createButton(
                builder.savingFeat === "add" ? "Adding…" : "Add",
                "dd-button dd-button--primary",
                () => handlers.add(rule.conceptKey),
                hasPendingBuildMutation(builder))
        );
        results.append(result);
    }
    section.append(results);
    return section;
}

function renderInventorySection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers,
    mechanics: InventoryMechanicsView | undefined
): HTMLElement {
    const content = createElement("div", "dd-routine-section dd-inventory");
    if (routine.status === "idle" || routine.status === "loading") {
        content.append(createInlineState("Loading Character inventory…", "loading"));
        appendInventoryMechanicsPresentation(content, mechanics);
        return content;
    }
    if (routine.status === "error" || routine.state === null) {
        content.append(createInlineState(
            routine.message ?? "Character inventory is unavailable.",
            "error"));
        appendInventoryMechanicsPresentation(content, mechanics);
        return content;
    }

    const pending = routine.mutation !== null;
    const editable = !readOnly && !routine.state.readOnly;
    if (routine.mutationError !== undefined) {
        content.append(createInlineState(routine.mutationError, "error"));
    }

    if (editable) {
        const actions = createElement("div", "dd-routine-actions");
        actions.append(createButton(
            "Add Item",
            "dd-button dd-button--primary",
            handlers.openInventoryChooser,
            pending));
        content.append(actions);
    }

    if (routine.inventoryChooser.kind === "open" && editable) {
        content.append(renderInventoryChooser(routine, handlers));
    }

    if (routine.state.inventoryItemOccurrences.length === 0) {
        content.append(createElement(
            "p",
            "dd-routine-empty",
            editable
                ? "No item occurrences yet. Add an item from the Rules Core catalog."
                : "No item occurrences have been recorded."));
    }

    const list = createElement("div", "dd-inventory-list");
    for (const occurrence of routine.state.inventoryItemOccurrences) {
        const item = createElement("article", "dd-inventory-item");
        item.setAttribute("data-inventory-occurrence-id", occurrence.id);
        const reference = routine.references[occurrence.id] ?? {
            status: "loading" as const,
            conceptKey: occurrence.ruleConceptKey
        };
        const display = toRuleReferenceDisplay(reference);
        const title = display.tone === "unavailable"
            ? "Unavailable item reference"
            : display.value;
        item.append(
            createElement("h3", "dd-inventory-item__name", title),
            createElement(
                "p",
                "dd-routine-meta",
                display.detail ?? occurrence.ruleConceptKey)
        );
        const occurrenceMechanics = renderItemOccurrenceMechanics(
            findItemOccurrenceMechanics(mechanics, occurrence.id));
        if (occurrenceMechanics !== null) item.append(occurrenceMechanics);
        if (editable) {
            item.append(createButton(
                routine.mutation?.kind === "inventory-delete"
                    && routine.mutation.entryId === occurrence.id
                    ? "Removing…"
                    : "Remove",
                "dd-button dd-button--ghost",
                () => {
                    if (window.confirm("Remove this item occurrence from the Character?")) {
                        handlers.removeInventoryItem(occurrence.id);
                    }
                },
                pending));
        }
        list.append(item);
    }
    if (list.children.length > 0) content.append(list);
    appendInventoryMechanicsPresentation(content, mechanics);
    return content;
}

function appendInventoryMechanicsPresentation(
    target: HTMLElement,
    mechanics: InventoryMechanicsView | undefined
): void {
    const rendered = renderInventoryMechanics(mechanics);
    if (rendered !== null) target.append(rendered);
}

function renderInventoryChooser(
    routine: CharacterRoutineUiState,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const chooser = routine.inventoryChooser;
    const section = createElement("section", "dd-rule-chooser dd-inventory-chooser");
    if (chooser.kind !== "open") return section;

    const heading = createElement("h3", "dd-rule-chooser__title", "Add Inventory Item");
    const search = createElement("form", "dd-rule-chooser__search");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search Rules Core items";
    input.setAttribute("aria-label", "Search Rules Core items");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search");
    submit.type = "submit";
    const close = createButton("Close", "dd-button dd-button--ghost", handlers.closeInventoryChooser);
    search.append(input, submit, close);
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.searchInventory(input.value);
    });
    section.append(heading, search);

    if (chooser.status === "idle" || chooser.status === "loading") {
        section.append(createInlineState("Loading Rules Core items…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(chooser.message ?? "Rules Core item search failed.", "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching Rules Core items."));
        return section;
    }

    const results = createElement("div", "dd-rule-chooser__results");
    for (const rule of chooser.results) {
        const result = createElement("article", "dd-rule-chooser__result");
        result.append(
            createElement("strong", "dd-rule-chooser__result-name", rule.displayName),
            createElement(
                "span",
                "dd-rule-chooser__result-meta",
                [rule.editionDisplayName, rule.sourceCode].filter(Boolean).join(" • ")),
            createElement("span", "dd-rule-chooser__result-key", rule.conceptKey),
            createButton(
                "Add",
                "dd-button dd-button--primary",
                () => handlers.addInventoryItem(rule.conceptKey),
                routine.mutation !== null)
        );
        results.append(result);
    }
    section.append(results);
    return section;
}

function renderNotesSection(
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RoutineCharacterHandlers
): HTMLElement {
    const content = createElement("div", "dd-routine-section dd-notes");
    if (routine.status === "idle" || routine.status === "loading") {
        content.append(createInlineState("Loading Character notes…", "loading"));
        return content;
    }
    if (routine.status === "error" || routine.state === null) {
        content.append(createInlineState(
            routine.message ?? "Character notes are unavailable.",
            "error"));
        return content;
    }

    const pending = routine.mutation !== null;
    const editable = !readOnly && !routine.state.readOnly;
    if (routine.mutationError !== undefined) {
        content.append(createInlineState(routine.mutationError, "error"));
    }

    if (editable) {
        const form = createElement("form", "dd-note-form");
        const label = createElement("label", "dd-routine-label", "Add note");
        const input = createElement("textarea", "dd-routine-textarea");
        input.rows = 4;
        input.maxLength = 10000;
        input.setAttribute("aria-label", "New Character note");
        const submit = createElement("button", "dd-button dd-button--primary", "Add Note");
        submit.type = "submit";
        submit.disabled = pending;
        form.append(label, input, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            if (pending || input.value.trim().length === 0) return;
            handlers.addNote(input.value);
        });
        content.append(form);
    }

    if (routine.state.notes.length === 0) {
        content.append(createElement(
            "p",
            "dd-routine-empty",
            editable
                ? "No notes yet. Add a Character-owned note above."
                : "No Character-owned notes have been recorded."));
        return content;
    }

    const list = createElement("div", "dd-note-list");
    for (const note of routine.state.notes) {
        const item = createElement("article", "dd-note");
        item.setAttribute("data-note-id", note.id);
        const meta = createElement(
            "p",
            "dd-routine-meta",
            `Updated ${new Date(note.updatedAt).toLocaleString()}`);

        if (editable) {
            const editor = createElement("textarea", "dd-routine-textarea dd-note__editor");
            editor.rows = Math.max(3, Math.min(12, note.content.split(/\r?\n/).length + 1));
            editor.maxLength = 10000;
            editor.value = note.content;
            editor.setAttribute("aria-label", "Character note");
            editor.disabled = pending;

            const actions = createElement("div", "dd-routine-actions");
            const save = createButton(
                routine.mutation?.kind === "note-update" && routine.mutation.entryId === note.id
                    ? "Saving…"
                    : "Save",
                "dd-button dd-button--secondary",
                () => handlers.updateNote(note.id, editor.value),
                pending);
            const remove = createButton(
                routine.mutation?.kind === "note-delete" && routine.mutation.entryId === note.id
                    ? "Deleting…"
                    : "Delete",
                "dd-button dd-button--ghost",
                () => {
                    if (window.confirm("Delete this Character note?")) {
                        handlers.deleteNote(note.id);
                    }
                },
                pending);
            actions.append(save, remove);
            item.append(editor, meta, actions);
        } else {
            const body = createElement("p", "dd-note__body", note.content);
            item.append(body, meta);
        }
        list.append(item);
    }
    content.append(list);
    return content;
}

function legacyAdvancementHeaderSummary(
    startingClass: string,
    subclass: string
): { value: string; detail?: string } {
    const values = [...new Set([startingClass, subclass].map(value => value.trim()).filter(Boolean))];
    return {
        value: values[0] ?? "-",
        detail: values.length > 1 ? values.slice(1).join(" • ") : undefined
    };
}

function headerSummaryItem(label: string, value: string, detail?: string): HTMLElement {
    const item = createElement("div", "dd-sheet-header__summary-item");
    const term = createElement("dt", "dd-sheet-header__summary-label", label);
    const description = createElement("dd", "dd-sheet-header__summary-value", value);
    item.append(term, description);
    if (detail !== undefined) {
        const meta = createElement("dd", "dd-sheet-header__summary-detail", detail);
        item.append(meta);
    }
    return item;
}

function characterInitials(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return "?";
    return parts.slice(0, 2).map(part => part[0]?.toUpperCase() ?? "").join("");
}
