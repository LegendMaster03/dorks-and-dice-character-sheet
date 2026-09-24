import type { CharacterBuilderUiState } from "../app-state.js";
import {
    advancementKindLabel,
    buildAdvancementPresentation,
    formatAdvancementProgression,
    type AdvancementGrantView,
    type CharacterAdvancementView
} from "./character-advancement.js";
import { createButton, createElement, createInlineState } from "./components.js";
import { hasPendingBuildMutation } from "./sheet-model.js";
import type { StructuralCharacterHandlers } from "./sheet-contracts.js";
import { renderSourceAttributions } from "./source-attribution.js";

export interface AdvancementProgressControl {
    value: number | null | undefined;
    readOnly: boolean;
    saving: boolean;
    onSet(value: number | null): void;
}

export function renderAdvancementDetails(
    advancement: CharacterAdvancementView | null,
    builder?: CharacterBuilderUiState,
    editing = false,
    handlers?: StructuralCharacterHandlers,
    progress?: AdvancementProgressControl
): HTMLElement {
    const details = createElement("details", "dd-advancement-overview");
    details.setAttribute("data-advancement-state", advancement === null ? "unavailable" : "resolved");
    details.append(createElement("summary", "dd-advancement-overview__toggle", "Advancement details"));
    const body = createElement("div", "dd-advancement-overview__body");
    if (progress !== undefined) {
        body.append(renderAdvancementProgress(progress));
    }

    if (advancement === null) {
        body.append(createInlineState("Advancement details are not available yet.", "neutral"));
        details.append(body);
        return details;
    }
    if (advancement.occurrences.length === 0) {
        body.append(createInlineState("No advancement details are available for this Character.", "neutral"));
        details.append(body);
        return details;
    }

    const list = createElement("div", "dd-advancement-list");
    for (const item of buildAdvancementPresentation(advancement)) {
        const occurrence = item.occurrence;
        const card = createElement("article", "dd-advancement-entry");
        card.setAttribute("data-advancement-occurrence-id", occurrence.occurrenceId);
        card.setAttribute("data-advancement-concept-key", occurrence.conceptKey);
        card.setAttribute("data-advancement-kind", occurrence.kind);
        if (occurrence.parentOccurrenceId !== undefined) {
            card.setAttribute("data-parent-advancement-occurrence-id", occurrence.parentOccurrenceId);
        }

        const heading = createElement("div", "dd-advancement-entry__heading");
        heading.append(createElement("h3", "dd-advancement-entry__name", occurrence.displayName));
        const meta = createElement("div", "dd-advancement-entry__meta");
        meta.append(createElement("span", "dd-advancement-entry__kind", advancementKindLabel(occurrence)));
        if (occurrence.progression !== undefined) {
            meta.append(createElement("span", "dd-advancement-entry__progression", formatAdvancementProgression(occurrence.progression)));
        }
        heading.append(meta);
        card.append(heading);

        const buildEntry = builder?.build?.progressionEntries.find(
            value => value.id === occurrence.occurrenceId);
        if (editing
            && handlers !== undefined
            && builder?.build !== null
            && builder?.build !== undefined
            && !builder.build.readOnly
            && buildEntry !== undefined
            && isLevelOwningAdvancementKind(buildEntry.kind)) {
            card.append(renderLevelEditor(
                occurrence.occurrenceId,
                buildEntry.level,
                builder,
                handlers));
        }

        if (item.parent !== undefined) {
            card.append(createElement("p", "dd-advancement-entry__relationship", `Parent: ${item.parent.displayName}`));
        } else if (item.unresolvedParent) {
            card.append(createElement("p", "dd-advancement-entry__relationship", "Parent advancement is unavailable."));
        }

        if (occurrence.progressionDetails?.length) {
            card.append(renderFields("Progression details", occurrence.progressionDetails));
        }
        if (occurrence.grantedFeaturesOrMechanics?.length) {
            card.append(renderGrants(occurrence.grantedFeaturesOrMechanics));
        }
        const sources = renderSourceAttributions(occurrence.sourceAttributions, true);
        if (sources !== null) card.append(sources);
        list.append(card);
    }

    body.append(list);
    details.append(body);
    return details;
}

function renderAdvancementProgress(
    control: AdvancementProgressControl
): HTMLElement {
    const section = createElement("section", "dd-advancement-progress");
    section.setAttribute("data-advancement-progress", "true");
    section.append(createElement(
        "h3",
        "dd-advancement-entry__name",
        "Advancement Progress"));

    if (control.readOnly) {
        section.append(createElement(
            "p",
            "dd-advancement-entry__progression",
            control.value === null || control.value === undefined
                ? "Not set"
                : String(control.value)));
    } else {
        const row = createElement("div", "dd-advancement-entry__level-editor");
        const label = createElement(
            "label",
            "dd-sheet-screen__label",
            "Current progress");
        const input = createElement("input", "dd-sheet-screen__input");
        input.type = "number";
        input.step = "1";
        input.min = "0";
        input.max = "2147483647";
        input.inputMode = "numeric";
        input.value = control.value === null || control.value === undefined
            ? ""
            : String(control.value);
        input.placeholder = "Not set";
        input.setAttribute("aria-label", "Advancement progress");
        input.addEventListener("input", () => input.setCustomValidity(""));
        label.append(input);

        const save = createButton(
            control.saving ? "Saving…" : "Save Progress",
            "dd-button dd-button--secondary",
            () => {
                const normalized = input.value.trim();
                if (normalized.length === 0) {
                    control.onSet(null);
                    return;
                }
                if (!/^\d+$/.test(normalized)) {
                    input.setCustomValidity("Progress must be a whole number from 0 through 2147483647.");
                    input.reportValidity();
                    return;
                }
                const parsed = Number(normalized);
                if (!Number.isSafeInteger(parsed) || parsed < 0 || parsed > 2147483647) {
                    input.setCustomValidity("Progress must be a whole number from 0 through 2147483647.");
                    input.reportValidity();
                    return;
                }
                input.setCustomValidity("");
                control.onSet(parsed);
            },
            control.saving);
        row.append(label, save);
        section.append(row);
    }

    section.append(createElement(
        "p",
        "dd-routine-meta",
        "Character-owned advancement progress. Rules Core has not supplied a ruleset-specific progression label for this Character."));
    return section;
}

function renderLevelEditor(
    occurrenceId: string,
    level: number | null | undefined,
    builder: CharacterBuilderUiState,
    handlers: StructuralCharacterHandlers
): HTMLElement {
    const editor = createElement("div", "dd-advancement-entry__level-editor");
    editor.setAttribute("data-advancement-level-editor", occurrenceId);

    const label = createElement("label", "dd-sheet-screen__label", "Level");
    const input = createElement("input", "dd-sheet-screen__input");
    input.type = "number";
    input.step = "1";
    input.min = "1";
    input.max = "1000";
    input.inputMode = "numeric";
    input.value = level === null || level === undefined ? "1" : String(level);
    input.setAttribute("aria-label", "Advancement level");
    input.addEventListener("input", () => input.setCustomValidity(""));
    label.append(input);

    const pending = hasPendingBuildMutation(builder);
    const saving = builder.savingAdvancementLevel === occurrenceId;
    const save = createButton(
        saving ? "Saving…" : "Save Level",
        "dd-button dd-button--secondary",
        () => {
            const parsed = parseAdvancementLevel(input.value);
            if (parsed === null) {
                input.setCustomValidity("Level must be a whole number from 1 through 1000.");
                input.reportValidity();
                return;
            }
            input.setCustomValidity("");
            handlers.setAdvancementLevel(occurrenceId, parsed);
        },
        pending);

    editor.append(label, save);
    if (builder.advancementLevelSaveError?.occurrenceId === occurrenceId) {
        editor.append(createInlineState(
            builder.advancementLevelSaveError.message,
            "error"));
    }
    return editor;
}

function isLevelOwningAdvancementKind(kind: string): boolean {
    const normalized = kind.trim().toLowerCase();
    return normalized === "class"
        || normalized === "prestigeclass"
        || normalized === "prestige-class";
}

function parseAdvancementLevel(value: string): number | null {
    const normalized = value.trim();
    if (!/^\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed) && parsed >= 1 && parsed <= 1000
        ? parsed
        : null;
}

function renderFields(
    title: string,
    fields: readonly { key: string; label: string; value: string }[]
): HTMLElement {
    const section = createElement("section", "dd-advancement-entry__details");
    section.append(createElement("h4", "dd-advancement-entry__subheading", title));
    const list = createElement("dl", "dd-advancement-entry__fields");
    for (const field of fields) {
        const row = createElement("div", "dd-definition-row");
        row.setAttribute("data-advancement-field-key", field.key);
        row.append(
            createElement("dt", "dd-definition-row__term", field.label),
            createElement("dd", "dd-definition-row__value", field.value)
        );
        list.append(row);
    }
    section.append(list);
    return section;
}

function renderGrants(grants: readonly AdvancementGrantView[]): HTMLElement {
    const section = createElement("section", "dd-advancement-entry__grants");
    section.append(createElement("h4", "dd-advancement-entry__subheading", "Granted Features & Mechanics"));
    const list = createElement("div", "dd-advancement-grant-list");
    for (const grant of grants) {
        const item = createElement("div", "dd-advancement-grant");
        item.setAttribute("data-advancement-grant-key", grant.key);
        if (grant.conceptKey !== undefined) {
            item.setAttribute("data-advancement-grant-concept-key", grant.conceptKey);
        }
        item.append(createElement("strong", "dd-advancement-grant__label", grant.label));
        if (grant.detail !== undefined) {
            item.append(createElement("span", "dd-advancement-grant__detail", grant.detail));
        }
        const sources = renderSourceAttributions(grant.sourceAttributions, true);
        if (sources !== null) item.append(sources);
        list.append(item);
    }
    section.append(list);
    return section;
}
