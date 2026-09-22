import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState
} from "../app-state.js";
import type { CharacterMechanicsView } from "./character-mechanics.js";
import { createButton, createElement } from "./components.js";
import { SHEET_SECTIONS, type SheetSection } from "./sheet-model.js";
import type { CharacterSheetHandlers } from "./sheet-contracts.js";
import { renderActionsPresentation } from "../features/actions/actions.js";
import {
    renderChecksAndProceduresPresentation,
    renderSpellcastingPresentation
} from "./procedure-components.js";
import { renderFeaturesSection } from "../features/features/features-section.js";
import { renderInventorySection } from "../features/inventory/inventory-section.js";
import { renderNotesSection } from "../features/notes/notes-section.js";

export function renderPrimaryContent(
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
    nav.setAttribute("role", "tablist");
    nav.setAttribute("aria-orientation", "horizontal");
    const tabs: Array<{ section: SheetSection; button: HTMLButtonElement }> = [];

    for (const section of SHEET_SECTIONS) {
        const active = section.id === activeSection;
        const button = createButton(
            section.label,
            active
                ? "dd-primary-nav__button dd-primary-nav__button--active"
                : "dd-primary-nav__button",
            () => handlers.selectSection(section.id));
        button.id = `dd-sheet-tab-${section.id}`;
        button.tabIndex = active ? 0 : -1;
        button.setAttribute("role", "tab");
        button.setAttribute("aria-selected", active ? "true" : "false");
        button.setAttribute("aria-controls", `dd-sheet-panel-${section.id}`);
        button.setAttribute("data-sheet-section-tab", section.id);
        if (active) button.setAttribute("aria-current", "page");
        tabs.push({ section: section.id, button });
        nav.append(button);
    }
    nav.addEventListener("keydown", event => {
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        const currentIndex = tabs.findIndex(tab => tab.button === event.target);
        if (currentIndex < 0) return;

        event.preventDefault();
        let nextIndex: number;
        if (event.key === "Home") {
            nextIndex = 0;
        } else if (event.key === "End") {
            nextIndex = tabs.length - 1;
        } else {
            const direction = event.key === "ArrowRight" ? 1 : -1;
            nextIndex = (currentIndex + direction + tabs.length) % tabs.length;
        }
        handlers.selectSection(tabs[nextIndex].section);
    });

    const definition = SHEET_SECTIONS.find(value => value.id === activeSection)
        ?? SHEET_SECTIONS[0];
    const panel = createElement("div", "dd-primary-content__panel");
    panel.id = `dd-sheet-panel-${definition.id}`;
    panel.setAttribute("role", "tabpanel");
    panel.setAttribute("aria-labelledby", `dd-sheet-tab-${definition.id}`);
    panel.setAttribute("data-sheet-section", definition.id);
    panel.append(createElement("h2", "dd-primary-content__title", definition.label));

    switch (definition.id) {
        case "notes":
            panel.append(renderNotesSection(routine, readOnly, handlers.routine));
            break;
        case "inventory":
            panel.append(renderInventorySection(
                routine,
                readOnly,
                handlers.routine,
                mechanics?.inventory));
            break;
        case "features":
            panel.append(renderFeaturesSection(
                builder,
                structuralEditing,
                readOnly,
                mechanics?.features,
                handlers.feats));
            break;
        case "actions": {
            const presentation = createElement("div", "dd-action-workflows");
            presentation.append(
                renderActionsPresentation(mechanics?.actions),
                renderChecksAndProceduresPresentation(
                    mechanics?.checks,
                    mechanics?.procedures));
            panel.append(presentation);
            break;
        }
        case "spells":
            panel.append(renderSpellcastingPresentation(
                mechanics?.spellcastingProfiles));
            break;
        default:
            panel.append(
                createElement(
                    "h3",
                    "dd-primary-content__empty-title",
                    definition.emptyTitle),
                createElement(
                    "p",
                    "dd-primary-content__empty-message",
                    definition.emptyMessage));
            break;
    }

    card.append(nav, panel);
    return card;
}
