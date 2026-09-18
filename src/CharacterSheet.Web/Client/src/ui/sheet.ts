import type { CharacterBuilderUiState } from "../app-state.js";
import type { CharacterSheetBootstrapResponse } from "../character-api.js";
import type { CharacterBuilderHandlers } from "./builder.js";
import { renderCharacterBuilder } from "./builder.js";
import { createButton, createElement, createPlaceholder, createSectionCard } from "./components.js";
import {
    createCharacterHeaderModel,
    humanizeBuilderStatus,
    MECHANIC_PLACEHOLDERS,
    SHEET_SECTIONS,
    type MechanicPlaceholderDefinition,
    type SheetSection
} from "./sheet-model.js";

export interface CharacterSheetHandlers extends CharacterBuilderHandlers {
    selectSection(section: SheetSection): void;
}

export function renderCharacterWorkspace(
    character: CharacterSheetBootstrapResponse,
    builder: CharacterBuilderUiState,
    activeSection: SheetSection,
    forceReadOnly: boolean,
    handlers: CharacterSheetHandlers
): HTMLElement {
    const shell = createElement("article", "dd-sheet");
    shell.setAttribute("data-character-sheet-shell", "true");
    const readOnly = forceReadOnly || builder.build?.readOnly === true || character.lifecycle === "Archived";
    shell.setAttribute("data-read-only", readOnly ? "true" : "false");

    shell.append(renderCharacterHeader(character, builder, forceReadOnly));
    if (forceReadOnly || character.lifecycle === "Archived") {
        shell.append(renderReadOnlyBanner());
    }

    shell.append(renderCoreStats());

    const workspace = createElement("div", "dd-sheet__workspace");
    const support = createElement("aside", "dd-sheet__support dd-sheet__support--left");
    support.setAttribute("aria-label", "Character supporting statistics");
    support.append(
        renderPlaceholderCard("Saving Throws", placeholders("saving-throws")),
        renderPlaceholderCard("Passive Values", placeholders("passive-values")),
        renderPlaceholderCard("Proficiencies & Training", placeholders("training"))
    );

    const skills = createElement("section", "dd-sheet__skills");
    skills.setAttribute("aria-label", "Character skills");
    skills.append(renderPlaceholderCard("Skills", placeholders("skills")));

    const main = createElement("main", "dd-sheet__main");
    main.append(
        renderCombatSummary(),
        renderCharacterBuilder(character.characterId, builder, forceReadOnly, handlers),
        renderPrimaryContent(activeSection, handlers)
    );

    workspace.append(support, skills, main);
    shell.append(workspace);
    return shell;
}

export function renderCharacterHeader(
    character: CharacterSheetBootstrapResponse,
    builder: CharacterBuilderUiState,
    forceReadOnly: boolean
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

    const summary = createElement("dl", "dd-sheet-header__summary");
    summary.append(
        headerSummaryItem("Race / Species", model.raceSpecies.value, model.raceSpecies.detail),
        headerSummaryItem("Class", model.startingClass.value, model.startingClass.detail),
        headerSummaryItem("Subclass", model.subclass.value, model.subclass.detail),
        headerSummaryItem(
            "Advancement",
            "Not yet available",
            "Level, multiclass, and Prestige Class summaries are not implemented yet.")
    );
    if (model.readOnly) {
        summary.append(headerSummaryItem("Sheet state", "Read-only", "Restore the Character through the Site to edit."));
    }

    header.append(identity, summary);
    return header;
}

function renderReadOnlyBanner(): HTMLElement {
    const banner = createElement("section", "dd-readonly-banner");
    banner.setAttribute("role", "status");
    const title = createElement("strong", "dd-readonly-banner__title", "Archived Character — read-only");
    const message = createElement(
        "span",
        "dd-readonly-banner__message",
        "The digital Character Sheet is preserved. Restore the Character through the Dorks & Dice Site before making changes.");
    banner.append(title, message);
    return banner;
}

function renderCoreStats(): HTMLElement {
    const section = createElement("section", "dd-core-stats");
    section.setAttribute("aria-labelledby", "dd-core-stats-heading");
    const heading = createElement("h2", "dd-visually-hidden", "Core statistics");
    heading.id = "dd-core-stats-heading";
    section.append(heading);

    const abilityGrid = createElement("div", "dd-core-stats__abilities");
    for (const placeholder of MECHANIC_PLACEHOLDERS.filter(value => value.group === "ability")) {
        abilityGrid.append(createStatPlaceholder(placeholder));
    }

    const quickGrid = createElement("div", "dd-core-stats__quick");
    for (const placeholder of MECHANIC_PLACEHOLDERS.filter(value => value.group === "quick")) {
        quickGrid.append(createStatPlaceholder(placeholder));
    }
    section.append(abilityGrid, quickGrid);
    return section;
}

function createStatPlaceholder(definition: MechanicPlaceholderDefinition): HTMLElement {
    const card = createElement("article", "dd-stat");
    card.setAttribute("data-unimplemented-mechanic", definition.id);
    card.append(
        createElement("h3", "dd-stat__label", definition.label),
        createElement("p", "dd-stat__value", "Not configured"),
        createElement("p", "dd-stat__detail", definition.message)
    );
    return card;
}

function renderPlaceholderCard(title: string, definitions: readonly MechanicPlaceholderDefinition[]): HTMLElement {
    const card = createSectionCard(title, "dd-support-card");
    for (const definition of definitions) {
        card.append(createPlaceholder(definition.label, definition.message, true));
    }
    return card;
}

function renderCombatSummary(): HTMLElement {
    const section = createElement("section", "dd-combat-summary");
    section.setAttribute("aria-labelledby", "dd-combat-heading");
    const heading = createElement("h2", "dd-visually-hidden", "Combat summary");
    heading.id = "dd-combat-heading";
    section.append(heading);
    for (const placeholder of MECHANIC_PLACEHOLDERS.filter(value => value.group === "combat")) {
        section.append(createPlaceholder(placeholder.label, placeholder.message, true));
    }
    return section;
}

function renderPrimaryContent(activeSection: SheetSection, handlers: CharacterSheetHandlers): HTMLElement {
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
    panel.append(
        createElement("h2", "dd-primary-content__title", definition.label),
        createElement("h3", "dd-primary-content__empty-title", definition.emptyTitle),
        createElement("p", "dd-primary-content__empty-message", definition.emptyMessage)
    );
    card.append(nav, panel);
    return card;
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

function placeholders(id: string): MechanicPlaceholderDefinition[] {
    return MECHANIC_PLACEHOLDERS.filter(value => value.id === id);
}

function characterInitials(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    if (parts.length === 0) return "?";
    return parts.slice(0, 2).map(part => part[0]?.toUpperCase() ?? "").join("");
}
