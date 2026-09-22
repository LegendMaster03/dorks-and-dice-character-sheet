import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState,
    GuidedBuilderUiState,
    SheetMode
} from "../app-state.js";
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
    type CharacterMechanicsView
} from "./character-mechanics.js";
import { renderRestControls } from "../features/health/health.js";
import { renderSavingThrowsCard } from "../features/saving-throws/saving-throws.js";
import {
    createButton,
    createElement,
    createSectionCard
} from "./components.js";
import { renderSkillsCard } from "./skills.js";
import {
    renderSensesSummaryCard,
    renderTrainingCard
} from "../features/support/support-values.js";
import {
    ABILITY_SCORE_DEFINITIONS,
    createCharacterHeaderModel,
    getGuidedBuilderSectionStates,
    humanizeBuilderStatus,
    type SheetSection
} from "./sheet-model.js";
import {
    findAbilitySavingThrow,
    renderAbilityScoreCard
} from "../features/abilities/ability-stats.js";
import { renderCharacterMechanicsSources } from "../core/mechanics/mechanics-sources.js";
import { renderCoreStats } from "./core-stats.js";
import { renderPrimaryContent } from "./primary-content.js";
import { renderCombatSummaryBand } from "../features/combat/combat-summary.js";
import { renderConditionsCard } from "../features/conditions/conditions.js";
import type { CharacterSheetHandlers } from "./sheet-contracts.js";

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
        const advancementEditing = structuralEditing
            || (guidedBuilder.open
                && editable
                && guidedBuilder.activeSection === "advancement");
        shell.append(renderAdvancementDetails(
            advancement,
            builder,
            advancementEditing,
            handlers.structural));
    }
    if (editable) {
        shell.append(renderModeControls(
            sheetMode,
            guidedBuilder,
            handlers,
            routine.mutation?.kind === "health-update"));
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

    const competencyPresentation = mechanics?.competencies === undefined
        ? null
        : buildCompetencyPresentation(mechanics.competencies);

    const topRow = createElement("div", "dd-sheet__top-row");
    topRow.append(renderCoreStats(
        builder,
        structuralEditing,
        readOnly,
        mechanics,
        {
            currentHitPoints: routine.status === "ready"
                && routine.state?.currentHitPoints !== null
                ? routine.state?.currentHitPoints
                : undefined,
            deathSaves: routine.status === "ready"
                ? routine.state?.deathSaves
                : undefined,
            readOnly: readOnly || routine.status !== "ready" || routine.state === null,
            saving: routine.mutation?.kind === "health-update"
                || routine.mutation?.kind === "death-saves-update",
            onSetCurrentHitPoints: handlers.routine.setCurrentHitPoints,
            onSetDeathSaves: handlers.routine.setDeathSaves
        },
        handlers.structural));

    const dashboard = createElement("div", "dd-sheet__dashboard");

    const detachedSavingThrows = mechanics?.savingThrows === undefined
        ? undefined
        : mechanics.savingThrows.filter(save =>
            !ABILITY_SCORE_DEFINITIONS.some(definition =>
                findAbilitySavingThrow([save], definition) === save));

    const referenceRail = createElement("aside", "dd-sheet__reference-rail");
    referenceRail.setAttribute("aria-label", "Saving throws, senses, and training");
    referenceRail.append(
        renderSavingThrowsCard(detachedSavingThrows, true),
        renderSensesSummaryCard(mechanics),
        renderTrainingCard(mechanics)
    );

    const skillsColumn = createElement("aside", "dd-sheet__skills");
    skillsColumn.setAttribute("aria-label", "Skills and competencies");
    skillsColumn.append(renderSkillsCard(competencyPresentation));

    const stage = createElement("div", "dd-sheet__stage");
    stage.append(renderCombatSummaryBand(
        mechanics,
        renderConditionsCard(routine, readOnly, handlers.routine)));

    const primary = createElement("section", "dd-sheet__main");
    primary.setAttribute("aria-label", "Character details and controls");
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
    stage.append(primary);

    dashboard.append(referenceRail, skillsColumn, stage);
    shell.append(topRow, dashboard);
    return shell;
}

function renderModeControls(
    sheetMode: SheetMode,
    guidedBuilder: GuidedBuilderUiState,
    handlers: CharacterSheetHandlers,
    restSaving: boolean
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

    controls.append(renderRestControls(
        false,
        restSaving,
        handlers.routine.rest));

    const configuration = createElement("div", "dd-sheet-mode-bar__configuration");
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
    configuration.append(editToggle, guided);
    controls.append(configuration);
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
                "Enter the base Ability Scores used for this Character. These values are saved with the sheet."));
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
                "This review shows the Character setup the sheet can currently verify. Edition-specific requirements that are not represented here are not marked complete."));
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
