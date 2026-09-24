import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState
} from "../app-state.js";
import type { CharacterMechanicsView } from "./character-mechanics.js";
import { createButton, createElement } from "./components.js";
import {
    builderReferenceDisplay,
    SHEET_SECTIONS,
    type SheetSection
} from "./sheet-model.js";
import type { CharacterSheetHandlers } from "./sheet-contracts.js";
import { renderActionsPresentation } from "../features/actions/actions.js";
import {
    renderChecksAndProceduresPresentation,
    renderSpellcastingPresentation
} from "./procedure-components.js";
import { renderFeaturesSection } from "../features/features/features-section.js";
import { renderInventorySection } from "../features/inventory/inventory-section.js";
import { renderNotesSection } from "../features/notes/notes-section.js";
import { renderKnownSpellsSection } from "../features/spells/known-spells.js";
import { renderProfileSection } from "../features/profile/profile-section.js";
import { renderHarvestingLauncher } from "../features/harvesting/harvesting-workspace.js";

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

        const summary = createElement(
            "span",
            "dd-primary-nav__summary",
            sectionOverviewSummary(section.id, builder, routine, mechanics));
        summary.setAttribute("data-sheet-section-summary", section.id);
        button.append(summary);

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
        case "details":
            panel.append(
                renderBackgroundIdentity(builder),
                renderProfileSection(
                    routine,
                    readOnly,
                    handlers.routine,
                    mechanics?.characterMetadata));
            break;
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
            if (handlers.harvestingCrafting !== undefined) {
                presentation.append(renderHarvestingLauncher(
                    handlers.harvestingCrafting,
                    readOnly));
            }
            panel.append(presentation);
            break;
        }
        case "spells": {
            const presentation = createElement("div", "dd-spell-workflows");
            presentation.append(
                renderSpellcastingPresentation(
                    mechanics?.spellcastingProfiles,
                    {
                        readOnly: readOnly || routine.status !== "ready" || routine.state === null,
                        savingResourceKey: routine.mutation?.kind === "rules-input-update"
                            && routine.mutation.entryId?.startsWith("resource:")
                            ? routine.mutation.entryId.slice("resource:".length)
                            : null,
                        onSetResource: handlers.rules.setResource
                    }),
                renderKnownSpellsSection(
                    routine,
                    readOnly,
                    handlers.spells));
            panel.append(presentation);
            break;
        }
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


function sectionOverviewSummary(
    section: SheetSection,
    builder: CharacterBuilderUiState,
    routine: CharacterRoutineUiState,
    mechanics: CharacterMechanicsView | null
): string {
    switch (section) {
        case "actions": {
            const actions = mechanics?.actions?.length ?? 0;
            const workflows = (mechanics?.checks?.length ?? 0)
                + (mechanics?.procedures?.length ?? 0);
            return `${formatOverviewCount(actions, "action")} · ${formatOverviewCount(workflows, "workflow")}`;
        }
        case "spells": {
            const knownSpells = routine.state?.rulesInputs
                ?.filter(input => input.kind === "knownSpell").length ?? 0;
            const profiles = mechanics?.spellcastingProfiles?.length ?? 0;
            return `${formatOverviewCount(knownSpells, "known spell")} · ${formatOverviewCount(profiles, "casting profile")}`;
        }
        case "inventory": {
            const entries = routine.state?.inventoryItemOccurrences.length ?? 0;
            const equipped = routine.state?.inventoryItemOccurrences
                .filter(item => item.isEquipped).length ?? 0;
            return `${formatOverviewCount(entries, "entry", "entries")} · ${equipped} equipped`;
        }
        case "features":
            return formatOverviewCount(mechanics?.features?.length ?? 0, "feature");
        case "details": {
            const background = builderReferenceDisplay(builder, "background");
            return background.value === "Not selected"
                ? "No background selected"
                : background.value;
        }
        case "notes":
            return formatOverviewCount(routine.state?.notes.length ?? 0, "note");
    }
}

function formatOverviewCount(
    count: number,
    singular: string,
    plural: string = `${singular}s`
): string {
    return `${count} ${count === 1 ? singular : plural}`;
}


function renderBackgroundIdentity(builder: CharacterBuilderUiState): HTMLElement {
    const identity = createElement("section", "dd-background-identity");
    identity.setAttribute("aria-label", "Background identity");
    identity.setAttribute("data-background-identity", "true");

    const grid = createElement("dl", "dd-background-identity__grid");
    grid.append(
        backgroundIdentityItem(
            "Background",
            builderReferenceDisplay(builder, "background")),
        backgroundIdentityItem(
            "Deity",
            builderReferenceDisplay(builder, "deity")));
    identity.append(grid);
    return identity;
}

function backgroundIdentityItem(
    label: string,
    display: ReturnType<typeof builderReferenceDisplay>
): HTMLElement {
    const item = createElement("div", "dd-background-identity__item");
    item.append(
        createElement("dt", "dd-background-identity__label", label),
        createElement("dd", "dd-background-identity__value", display.value));
    if (display.detail !== undefined) {
        item.append(createElement(
            "dd",
            "dd-background-identity__detail",
            display.detail));
    }
    return item;
}
