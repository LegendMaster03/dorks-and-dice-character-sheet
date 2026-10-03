import {
    createInitialHarvestingCraftingState,
    type CharacterBuilderUiState,
    type CharacterRoutineUiState,
    type GuidedBuilderUiState,
    type HarvestingCraftingUiState,
    type SheetMode
} from "../app-state.js";
import type { CharacterSheetBootstrapResponse } from "../character-api.js";
import { renderCharacterBuilder } from "./builder.js";
import {
    createCompactAdvancementSummary,
    type CharacterAdvancementView
} from "./character-advancement.js";
import { renderAdvancementDetails } from "./advancement.js";
import {
    buildCompetencyPresentation,
    partitionCompetencyCollection,
    type CharacterMechanicsView
} from "./character-mechanics.js";
import { renderGuidedSourceFeatures } from "./guided-source-features.js";
import { renderRecoveryContinuation, renderRecoveryControls } from "../features/health/health.js";
import { renderHitPointGainEditors } from "../features/health/hit-point-gains.js";
import { renderSavingThrowsCard } from "../features/saving-throws/saving-throws.js";
import {
    createButton,
    createElement,
    createInlineState,
    createSectionCard
} from "./components.js";
import { renderSkillsCard } from "./skills.js";
import { renderCompetenciesCard } from "./competencies.js";
import {
    renderSensesSummaryCard,
    renderTrainingCard
} from "../features/support/support-values.js";
import {
    ABILITY_SCORE_DEFINITIONS,
    createCharacterHeaderModel,
    getGuidedBuilderChoiceSourceKeys,
    getGuidedBuilderOwnedChoiceSourceKeys,
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
import { CHARACTER_INSPIRATION_STATE_KEY } from "../features/inspiration/inspiration.js";
import { renderPrimaryContent } from "./primary-content.js";
import { renderCombatSummaryBand } from "../features/combat/combat-summary.js";
import { renderConditionsCard } from "../features/conditions/conditions.js";
import type { CharacterSheetHandlers } from "./sheet-contracts.js";
import { renderHarvestingCraftingOverlay } from "../features/harvesting/harvesting-workspace.js";

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
    handlers: CharacterSheetHandlers,
    harvestingCrafting: HarvestingCraftingUiState = createInitialHarvestingCraftingState()
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

    const portraitAsset = routine.status === "ready"
        ? (routine.state?.artAssets ?? []).find(asset => asset.isPortrait)
        : undefined;
    shell.append(renderCharacterHeader(
        character,
        builder,
        forceReadOnly,
        advancement,
        portraitAsset === undefined ? null : handlers.routine.artContentUrl(portraitAsset.id)));
    if (advancement !== null && advancement.occurrences.length > 0) {
        const advancementEditing = guidedBuilder.open
            && editable
            && guidedBuilder.activeSection === "class";
        shell.append(renderAdvancementDetails(
            advancement,
            builder,
            advancementEditing,
            handlers.structural,
            {
                value: routine.status === "ready"
                    ? routine.state?.advancementProgress
                    : undefined,
                readOnly: readOnly || routine.status !== "ready" || routine.state === null,
                saving: routine.mutation?.kind === "progression-update",
                onSet: handlers.routine.setAdvancementProgress
            }));
    }
    if (editable) {
        shell.append(renderModeControls(
            sheetMode,
            guidedBuilder,
            mechanics,
            routine,
            handlers));
    }
    if (readOnly) {
        shell.append(renderReadOnlyBanner(character.lifecycle === "Archived"));
    }

    if (guidedBuilder.open && editable) {
        shell.append(renderGuidedBuilder(
            character.characterId,
            builder,
            routine,
            guidedBuilder,
            advancement,
            mechanics,
            handlers));
        if (harvestingCrafting.open && handlers.harvestingCrafting !== undefined) {
            shell.append(renderHarvestingCraftingOverlay(
                character,
                harvestingCrafting,
                handlers.harvestingCrafting,
                readOnly,
                mechanics,
                routine));
        }
        return shell;
    }

    const mechanicsSources = renderCharacterMechanicsSources(mechanics);
    if (mechanicsSources !== null) shell.append(mechanicsSources);

    const competencyCollections = mechanics?.competencies === undefined
        ? null
        : partitionCompetencyCollection(mechanics.competencies);
    const skillPresentation = competencyCollections === null
        ? null
        : buildCompetencyPresentation(competencyCollections.skills);
    const broaderCompetencyPresentation = competencyCollections === null
        ? null
        : buildCompetencyPresentation(competencyCollections.competencies);

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
        {
            current: routine.status === "ready" && routine.state !== null
                ? (routine.state.rulesInputs ?? []).find(input =>
                    input.kind === "booleanFact"
                    && input.key === CHARACTER_INSPIRATION_STATE_KEY)?.booleanValue === true
                : undefined,
            readOnly: readOnly || routine.status !== "ready" || routine.state === null,
            saving: routine.mutation?.kind === "rules-input-update"
                && routine.mutation.entryId === `booleanFact:${CHARACTER_INSPIRATION_STATE_KEY}`,
            onSet: handlers.routine.setInspiration
        },
        handlers.structural));

    const dashboard = createElement("div", "dd-sheet__dashboard");

    const detachedSavingThrows = mechanics?.savingThrows === undefined
        ? undefined
        : mechanics.savingThrows.filter(save =>
            !ABILITY_SCORE_DEFINITIONS.some(definition =>
                findAbilitySavingThrow([save], definition) === save));

    const competencyRankControls = {
        readOnly: readOnly || routine.status !== "ready" || routine.state === null,
        savingKey: routine.mutation?.kind === "rules-input-update"
            && routine.mutation.entryId?.startsWith("competencyRank:")
            ? routine.mutation.entryId.slice("competencyRank:".length)
            : routine.mutation?.kind === "rules-input-delete"
                && routine.mutation.entryId?.startsWith("competencyRank:")
                ? routine.mutation.entryId.slice("competencyRank:".length)
                : null,
        onSetRank: handlers.rules.setCompetencyRank,
        onClearRank: handlers.rules.clearCompetencyRank
    };

    const referenceRail = createElement("aside", "dd-sheet__reference-rail");
    referenceRail.setAttribute("aria-label", "Saving throws, senses, and training");
    referenceRail.append(
        renderSavingThrowsCard(detachedSavingThrows, true),
        renderSensesSummaryCard(mechanics),
        renderTrainingCard(mechanics)
    );

    const skillsColumn = createElement("aside", "dd-sheet__skills");
    skillsColumn.setAttribute("aria-label", "Skills");
    skillsColumn.append(renderSkillsCard(skillPresentation, competencyRankControls));

    const competenciesColumn = createElement("aside", "dd-sheet__competencies");
    competenciesColumn.setAttribute("aria-label", "Competencies");
    competenciesColumn.append(renderCompetenciesCard(
        broaderCompetencyPresentation,
        competencyRankControls));

    const stage = createElement("div", "dd-sheet__stage");
    stage.append(renderCombatSummaryBand(
        mechanics,
        renderConditionsCard(routine, readOnly, handlers.routine)));

    const primary = createElement("section", "dd-sheet__main");
    primary.setAttribute("aria-label", "Character details and controls");
    primary.append(renderPrimaryContent(
        activeSection,
        builder,
        routine,
        structuralEditing,
        readOnly,
        mechanics,
        handlers));
    stage.append(primary);

    dashboard.append(skillsColumn, competenciesColumn, referenceRail, stage);

    const body = createElement("div", "dd-sheet__body");
    body.append(topRow, dashboard);
    shell.append(body);
    if (structuralEditing) {
        shell.append(renderCharacterEditorOverlay(
            character.characterId,
            builder,
            routine,
            mechanics,
            handlers));
    }
    if (harvestingCrafting.open && handlers.harvestingCrafting !== undefined) {
        shell.append(renderHarvestingCraftingOverlay(
            character,
            harvestingCrafting,
            handlers.harvestingCrafting,
            readOnly,
            mechanics,
            routine));
    }
    return shell;
}

function renderModeControls(
    sheetMode: SheetMode,
    guidedBuilder: GuidedBuilderUiState,
    mechanics: CharacterMechanicsView | null,
    routine: CharacterRoutineUiState,
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

    const recoveryState = routine.recovery ?? { kind: "closed" as const };
    const recoveryControls = renderRecoveryControls(
        mechanics?.recoveryProcedures,
        false,
        recoveryState.kind === "resolving",
        handlers.routine.recover);
    if (recoveryControls !== null) controls.append(recoveryControls);

    const recoveryContinuation = renderRecoveryContinuation(
        recoveryState,
        mechanics?.recoveryProcedures,
        handlers.routine.continueRecovery,
        handlers.routine.cancelRecovery);
    if (recoveryContinuation !== null) controls.append(recoveryContinuation);

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

function renderCharacterEditorOverlay(
    characterId: string,
    builder: CharacterBuilderUiState,
    routine: CharacterRoutineUiState,
    mechanics: CharacterMechanicsView | null,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const overlay = createElement("div", "dd-character-editor-overlay");
    overlay.setAttribute("data-character-editor-overlay", "true");

    const surface = createElement("section", "dd-character-editor-surface");
    surface.setAttribute("role", "dialog");
    surface.setAttribute("aria-modal", "true");
    surface.setAttribute("aria-labelledby", "dd-character-editor-title");

    const header = createElement("div", "dd-character-editor-surface__header");
    const title = createElement("h2", "dd-character-editor-surface__title", "Edit Character");
    title.id = "dd-character-editor-title";
    header.append(
        title,
        createButton("Done", "dd-button dd-button--ghost", handlers.leaveEditMode));

    const body = createElement("div", "dd-character-editor-surface__body");
    body.append(renderCharacterBuilder(characterId, builder, false, handlers.structural));
    const masteryChoices = renderRulesChoices(
        mechanics,
        routine,
        handlers,
        {
            choiceKinds: ["weapon-mastery"],
            includeConflicts: false,
            heading: "Weapon Mastery"
        });
    if (masteryChoices !== null) body.append(masteryChoices);
    surface.append(header, body);
    overlay.append(surface);
    return overlay;
}

function renderGuidedBuilder(
    characterId: string,
    builder: CharacterBuilderUiState,
    routine: CharacterRoutineUiState,
    guidedBuilder: GuidedBuilderUiState,
    advancement: CharacterAdvancementView | null,
    mechanics: CharacterMechanicsView | null,
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
            "Work through the sections in any order. The Character Sheet remains available even when setup is incomplete."));

    const sectionStates = getGuidedBuilderSectionStates(builder, mechanics);
    const nav = createElement("nav", "dd-guided-builder__nav");
    nav.setAttribute("aria-label", "Guided builder sections");
    for (const [index, section] of sectionStates.entries()) {
        const active = section.id === guidedBuilder.activeSection;
        const unavailable = section.status === "unavailable";
        const button = createButton(
            `${index + 1}. ${section.label} · ${guidedStatusLabel(section.status)}`,
            active
                ? "dd-guided-builder__nav-button dd-guided-builder__nav-button--active"
                : "dd-guided-builder__nav-button",
            () => handlers.selectGuidedBuilderSection(section.id),
            unavailable);
        button.setAttribute("data-guided-builder-section", section.id);
        button.setAttribute("data-guided-builder-step", String(index + 1));
        button.setAttribute("data-guided-builder-status", section.status);
        if (unavailable) button.setAttribute("aria-disabled", "true");
        if (active) button.setAttribute("aria-current", "page");
        nav.append(button);
    }
    container.append(nav);
    const panel = createElement("div", "dd-guided-builder__panel");
    panel.setAttribute("data-guided-builder-active-section", guidedBuilder.activeSection);

    switch (guidedBuilder.activeSection) {
        case "class": {
            panel.append(renderCharacterBuilder(
                characterId,
                builder,
                false,
                handlers.structural,
                { title: "Class", choices: ["startingClass", "subclass"] }));
            const classFeatures = renderGuidedSourceFeatures(
                mechanics,
                getGuidedBuilderChoiceSourceKeys(builder, "class"),
                "Class Features");
            if (classFeatures !== null) panel.append(classFeatures);
            const classChoices = renderRulesChoices(
                mechanics,
                routine,
                handlers,
                {
                    sourceConceptKeys: getGuidedBuilderChoiceSourceKeys(builder, "class"),
                    excludedChoiceKinds: ["subclass"],
                    includeConflicts: false,
                    heading: "Class Choices"
                });
            if (classChoices !== null) panel.append(classChoices);
            const hitPointGains = renderHitPointGainEditors(
                advancement?.occurrences ?? [],
                routine,
                false,
                handlers.rules);
            if (hitPointGains !== null) panel.append(hitPointGains);
            break;
        }
        case "background": {
            panel.append(renderCharacterBuilder(
                characterId,
                builder,
                false,
                handlers.structural,
                { title: "Background", choices: ["background", "deity"] }));
            const backgroundFeatures = renderGuidedSourceFeatures(
                mechanics,
                getGuidedBuilderChoiceSourceKeys(builder, "background"),
                "Background Features");
            if (backgroundFeatures !== null) panel.append(backgroundFeatures);
            const backgroundChoices = renderRulesChoices(
                mechanics,
                routine,
                handlers,
                {
                    sourceConceptKeys: getGuidedBuilderChoiceSourceKeys(builder, "background"),
                    includeConflicts: false,
                    heading: "Background Choices"
                });
            if (backgroundChoices !== null) panel.append(backgroundChoices);
            break;
        }
        case "species": {
            panel.append(renderCharacterBuilder(
                characterId,
                builder,
                false,
                handlers.structural,
                { title: "Species", choices: ["species", "subspecies"] }));
            const speciesFeatures = renderGuidedSourceFeatures(
                mechanics,
                getGuidedBuilderChoiceSourceKeys(builder, "species"),
                "Species Traits");
            if (speciesFeatures !== null) panel.append(speciesFeatures);
            const speciesChoices = renderRulesChoices(
                mechanics,
                routine,
                handlers,
                {
                    sourceConceptKeys: getGuidedBuilderChoiceSourceKeys(builder, "species"),
                    includeConflicts: false,
                    heading: "Species Choices"
                });
            if (speciesChoices !== null) panel.append(speciesChoices);
            break;
        }
        case "abilities": {
            const abilities = createSectionCard("Base Ability Scores", "dd-guided-builder__abilities");
            abilities.append(createElement(
                "p",
                "dd-guided-builder__section-copy",
                "Enter the base Ability Scores used for this Character. Ability choices granted by your Class, Background, or Species appear in those sections."));
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
            const abilityChoices = renderRulesChoices(
                mechanics,
                routine,
                handlers,
                {
                    choiceKinds: ["ability-score", "ability-score-set"],
                    excludedSourceConceptKeys: getGuidedBuilderOwnedChoiceSourceKeys(builder),
                    includeConflicts: false,
                    heading: "Required Ability Choices"
                });
            if (abilityChoices !== null) panel.append(abilityChoices);
            break;
        }
        case "equipment": {
            const equipment = createSectionCard("Starting Equipment", "dd-guided-builder__equipment");
            equipment.append(
                createElement(
                    "p",
                    "dd-guided-builder__section-copy",
                    "This step is staged for rule-guided starting equipment. It will be enabled when Rules Core exposes the Character's starting-equipment grants and choices."),
                createInlineState(
                    "Starting equipment is not available in Guided Setup yet. General inventory remains available on the Character Sheet.",
                    "warning"));
            panel.append(equipment);
            break;
        }
        case "review": {
            const review = createSectionCard("Review & What's Next", "dd-guided-builder__review");
            review.append(createElement(
                "p",
                "dd-guided-builder__section-copy",
                "Review what the Character Sheet can currently verify. Setup does not need to be complete before you return to the sheet."));
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
            const rulesChoices = renderRulesChoices(
                mechanics,
                routine,
                handlers);
            if (rulesChoices !== null) review.append(rulesChoices);
            const nextSteps = createElement("div", "dd-build-choice__actions");
            const viewSheet = createButton(
                "View Character Sheet",
                "dd-button dd-button--primary",
                handlers.closeGuidedBuilder);
            viewSheet.setAttribute("data-guided-builder-navigation-action", "sheet");
            nextSteps.append(viewSheet);
            review.append(nextSteps);
            panel.append(review);
            break;
        }
    }

    const activeIndex = sectionStates.findIndex(section => section.id === guidedBuilder.activeSection);
    const previousSection = activeIndex > 0
        ? sectionStates.slice(0, activeIndex).reverse().find(section => section.status !== "unavailable")
        : undefined;
    const nextSection = activeIndex >= 0
        ? sectionStates.slice(activeIndex + 1).find(section => section.status !== "unavailable")
        : undefined;
    if (previousSection !== undefined || nextSection !== undefined) {
        const navigation = createElement("div", "dd-build-choice__actions");
        navigation.setAttribute("data-guided-builder-navigation", "true");
        navigation.setAttribute("aria-label", "Guided setup navigation");
        if (previousSection !== undefined) {
            const previous = createButton(
                `Previous: ${previousSection.label}`,
                "dd-button dd-button--ghost",
                () => handlers.selectGuidedBuilderSection(previousSection.id));
            previous.setAttribute("data-guided-builder-navigation-action", "previous");
            navigation.append(previous);
        }
        if (nextSection !== undefined) {
            const next = createButton(
                nextSection.id === "review" ? "Review Character" : `Next: ${nextSection.label}`,
                "dd-button dd-button--primary",
                () => handlers.selectGuidedBuilderSection(nextSection.id));
            next.setAttribute("data-guided-builder-navigation-action", "next");
            navigation.append(next);
        }
        panel.append(navigation);
    }

    container.append(panel);
    return container;
}

interface RulesChoiceRenderOptions {
    choiceKinds?: readonly string[];
    excludedChoiceKinds?: readonly string[];
    sourceConceptKeys?: readonly string[];
    excludedSourceConceptKeys?: readonly string[];
    includeConflicts?: boolean;
    heading?: string;
}

function renderRulesChoices(
    mechanics: CharacterMechanicsView | null,
    routine: CharacterRoutineUiState,
    handlers: CharacterSheetHandlers,
    options: RulesChoiceRenderOptions = {}
): HTMLElement | null {
    if (mechanics === null) {
        return null;
    }

    const allChoices = mechanics.ruleChoices ?? [];
    const sourceConceptKeys = options.sourceConceptKeys === undefined
        ? null
        : new Set(options.sourceConceptKeys);
    const excludedSourceConceptKeys = options.excludedSourceConceptKeys === undefined
        ? null
        : new Set(options.excludedSourceConceptKeys);
    const choices = allChoices
        .filter(choice => options.choiceKinds === undefined || options.choiceKinds.includes(choice.kind))
        .filter(choice => options.excludedChoiceKinds === undefined || !options.excludedChoiceKinds.includes(choice.kind))
        .filter(choice => sourceConceptKeys === null
            || (choice.sourceConceptKey !== undefined && sourceConceptKeys.has(choice.sourceConceptKey)))
        .filter(choice => excludedSourceConceptKeys === null
            || choice.sourceConceptKey === undefined
            || !excludedSourceConceptKeys.has(choice.sourceConceptKey));
    const conflicts = options.includeConflicts === false
        ? []
        : mechanics.projectionConflicts ?? [];
    if (choices.length === 0 && conflicts.length === 0) {
        return null;
    }

    const section = createElement("section", "dd-guided-builder__rules");
    section.append(createElement(
        "h3",
        "dd-guided-builder__subheading",
        options.heading ?? "Rules Choices"));

    const pending = routine.mutation?.kind === "rules-input-update"
        || routine.mutation?.kind === "rules-input-delete";

    for (const choice of choices) {
        const card = createElement("article", "dd-build-choice");
        card.setAttribute("data-rule-choice-key", choice.choiceKey);
        card.setAttribute("data-rule-choice-state", choice.state);
        card.append(createElement("h4", "dd-build-choice__label", choice.displayName));

        const selectedOption = choice.options.find(option =>
            option.value === choice.selectedValue);
        const selectedLabel = selectedOption?.displayName
            ?? choice.selectedValue
            ?? "Not selected";
        card.append(createElement(
            "p",
            "dd-build-choice__value",
            selectedLabel));

        const selectedElsewhere = new Set(
            allChoices
                .filter(other =>
                    other.choiceKey !== choice.choiceKey
                    && other.groupKey === choice.groupKey
                    && other.selectedValue !== undefined)
                .map(other => other.selectedValue!.trim().toLowerCase()));
        const duplicateSelection = choice.selectedValue !== undefined
            && selectedElsewhere.has(choice.selectedValue.trim().toLowerCase());
        const sourceUnavailable = choice.state === "source-unavailable";

        if (sourceUnavailable) {
            card.append(createInlineState(
                "Rules Core can not fully represent the legal options for this choice.",
                "warning"));
        } else if (duplicateSelection) {
            card.append(createInlineState(
                "This option is already selected in another choice in the same group. Choose a different option.",
                "warning"));
        } else if (choice.selectedValue !== undefined && choice.state !== "resolved") {
            card.append(createInlineState(
                "The current selection does not satisfy this rules choice. Choose a different option or clear it.",
                "warning"));
        }

        if (choice.options.length > 0 && !sourceUnavailable) {
            const controls = createElement("div", "dd-build-choice__actions");
            const select = createElement("select", "dd-rule-chooser__input");
            select.setAttribute("aria-label", choice.displayName);

            const placeholder = createElement("option");
            placeholder.value = "";
            placeholder.textContent = "Choose…";
            select.append(placeholder);

            for (const option of choice.options) {
                if (selectedElsewhere.has(option.value.trim().toLowerCase())) {
                    continue;
                }
                const element = createElement("option");
                element.value = option.value;
                element.textContent = option.displayName;
                if (option.value === choice.selectedValue) {
                    element.selected = true;
                }
                select.append(element);
            }
            select.value = duplicateSelection ? "" : choice.selectedValue ?? "";

            controls.append(
                select,
                createButton(
                    choice.selectedValue === undefined ? "Choose" : "Replace",
                    "dd-button dd-button--secondary",
                    () => {
                        if (select.value.length > 0) {
                            handlers.rules.setChoice(choice.choiceKey, select.value);
                        }
                    },
                    pending));
            if (choice.selectedValue !== undefined) {
                controls.append(createButton(
                    "Clear",
                    "dd-button dd-button--ghost",
                    () => handlers.rules.clearChoice(choice.choiceKey),
                    pending));
            }
            card.append(controls);
        } else if (!sourceUnavailable && choice.selectedValue === undefined) {
            card.append(createInlineState(
                "Rules Core requires this choice but did not provide selectable options.",
                "warning"));
        }

        section.append(card);
    }

    if (conflicts.length > 0) {
        const conflictSection = createElement("section", "dd-guided-builder__conflicts");
        conflictSection.append(createElement(
            "h3",
            "dd-guided-builder__subheading",
            "Rules Conflicts"));
        for (const conflict of conflicts) {
            const item = createInlineState(conflict.message, "warning");
            item.setAttribute("data-rule-conflict-key", conflict.conflictKey);
            conflictSection.append(item);
        }
        section.append(conflictSection);
    }

    return section;
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
    advancement: CharacterAdvancementView | null = null,
    portraitUrl: string | null = null
): HTMLElement {
    const model = createCharacterHeaderModel(character, builder, forceReadOnly);
    const header = createElement("header", "dd-sheet-header");

    const identity = createElement("div", "dd-sheet-header__identity");
    const portrait = createElement("div", "dd-sheet-header__portrait");
    if (portraitUrl === null) {
        portrait.textContent = characterInitials(character.name);
        portrait.setAttribute("aria-hidden", "true");
    } else {
        const image = document.createElement("img");
        image.className = "dd-sheet-header__portrait-image";
        image.src = portraitUrl;
        image.alt = `${character.name} portrait`;
        portrait.append(image);
    }

    const text = createElement("div", "dd-sheet-header__text");
    const name = createElement("h1", "dd-sheet-header__name", model.name);

    const advancementSummary = advancement === null
        ? legacyAdvancementHeaderSummary(model.startingClass.value, model.subclass.value)
        : createCompactAdvancementSummary(advancement);
    const headline = createElement("div", "dd-sheet-header__headline");
    const species = createElement(
        "span",
        "dd-sheet-header__headline-item",
        `Species: ${model.species.value}`);
    species.setAttribute("data-sheet-header-identity", "species");
    species.setAttribute("aria-label", `Species: ${model.species.value}`);
    if (model.species.detail !== undefined) {
        species.setAttribute("title", model.species.detail);
    }

    const subspecies = createElement(
        "span",
        "dd-sheet-header__headline-item",
        `Subspecies: ${model.subspecies.value}`);
    subspecies.setAttribute("data-sheet-header-identity", "subspecies");
    subspecies.setAttribute("aria-label", `Subspecies: ${model.subspecies.value}`);
    if (model.subspecies.detail !== undefined) {
        subspecies.setAttribute("title", model.subspecies.detail);
    }

    const advancementText = advancementSummary.detail === undefined
        ? advancementSummary.value
        : `${advancementSummary.value} • ${advancementSummary.detail}`;
    const advancementItem = createElement(
        "span",
        "dd-sheet-header__headline-item",
        advancementText);
    advancementItem.setAttribute("data-sheet-header-identity", "advancement");
    advancementItem.setAttribute("aria-label", `Advancement: ${advancementText}`);

    headline.append(
        species,
        createElement("span", "dd-sheet-header__headline-separator", "•"),
        subspecies,
        createElement("span", "dd-sheet-header__headline-separator", "•"),
        advancementItem);

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

    text.append(name, headline, statusLine);
    identity.append(portrait, text);
    header.append(identity);
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

function characterInitials(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return "?";
    return parts.slice(0, 2).map(part => part[0]?.toUpperCase() ?? "").join("");
}
