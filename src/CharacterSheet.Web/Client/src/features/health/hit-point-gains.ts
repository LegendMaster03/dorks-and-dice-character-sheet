import type { CharacterRoutineUiState } from "../../app-state.js";
import type { AdvancementOccurrenceView } from "../../ui/character-advancement.js";
import { createButton, createElement, createInlineState, createSectionCard } from "../../ui/components.js";
import type { RulesInputCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderHitPointGainEditors(
    occurrences: readonly AdvancementOccurrenceView[],
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RulesInputCharacterHandlers
): HTMLElement | null {
    const levelOwners = occurrences
        .map(occurrence => ({
            occurrence,
            level: advancementLevel(occurrence)
        }))
        .filter(value =>
            value.level !== null
            && isLevelOwningKind(value.occurrence.kind));

    if (levelOwners.length === 0) return null;

    const section = createSectionCard(
        "Hit Point Gains",
        "dd-guided-builder__hit-point-gains");
    section.append(createElement(
        "p",
        "dd-guided-builder__section-copy",
        "Enter the raw hit-die outcome for each Class level. Rules Core applies the effective Hit Point rules and Constitution modifier."));

    if (routine.status !== "ready" || routine.state === null) {
        section.append(createInlineState(
            "Character runtime state is unavailable.",
            "warning"));
        return section;
    }

    const gains = routine.state.hitPointGains ?? [];
    for (const { occurrence, level } of levelOwners) {
        const group = createElement("section", "dd-hit-point-gain-group");
        group.setAttribute(
            "data-hit-point-gain-advancement",
            occurrence.occurrenceId);
        group.append(createElement(
            "h4",
            "dd-hit-point-gain-group__title",
            occurrence.displayName));

        const list = createElement("div", "dd-hit-point-gain-list");
        for (let classLevel = 1; classLevel <= level!; classLevel++) {
            const saved = gains.find(value =>
                value.advancementOccurrenceId === occurrence.occurrenceId
                && value.classLevel === classLevel);
            list.append(renderHitPointGainRow(
                occurrence.occurrenceId,
                classLevel,
                saved?.hitDieValue,
                routine,
                readOnly,
                handlers));
        }
        group.append(list);
        section.append(group);
    }

    return section;
}

function renderHitPointGainRow(
    occurrenceId: string,
    classLevel: number,
    hitDieValue: number | undefined,
    routine: CharacterRoutineUiState,
    readOnly: boolean,
    handlers: RulesInputCharacterHandlers
): HTMLElement {
    const row = createElement("div", "dd-hit-point-gain-row");
    row.setAttribute(
        "data-hit-point-gain-key",
        `${occurrenceId}:${classLevel}`);
    row.append(createElement(
        "span",
        "dd-hit-point-gain-row__level",
        `Level ${classLevel}`));

    if (readOnly) {
        row.append(createElement(
            "strong",
            "dd-hit-point-gain-row__value",
            hitDieValue === undefined ? "-" : String(hitDieValue)));
        return row;
    }

    const input = createElement("input", "dd-sheet-screen__input");
    input.type = "number";
    input.step = "1";
    input.inputMode = "numeric";
    input.value = hitDieValue === undefined ? "" : String(hitDieValue);
    input.setAttribute(
        "aria-label",
        `Level ${classLevel} hit-die outcome`);
    input.addEventListener("input", () => input.setCustomValidity(""));

    const mutationKey = `${occurrenceId}:${classLevel}`;
    const saving = routine.mutation?.entryId === mutationKey
        && (routine.mutation.kind === "hit-point-gain-update"
            || routine.mutation.kind === "hit-point-gain-delete");
    const pending = routine.mutation !== null;

    const actions = createElement("div", "dd-hit-point-gain-row__actions");
    actions.append(createButton(
        saving && routine.mutation?.kind === "hit-point-gain-update"
            ? "Saving…"
            : "Save",
        "dd-button dd-button--secondary",
        () => {
            const parsed = parseInteger(input.value);
            if (parsed === null) {
                input.setCustomValidity(
                    "Enter a whole number that can be represented by the Character Sheet API.");
                input.reportValidity();
                return;
            }
            input.setCustomValidity("");
            handlers.setHitPointGain(
                occurrenceId,
                classLevel,
                parsed);
        },
        pending));

    if (hitDieValue !== undefined) {
        actions.append(createButton(
            saving && routine.mutation?.kind === "hit-point-gain-delete"
                ? "Clearing…"
                : "Clear",
            "dd-button dd-button--ghost",
            () => handlers.clearHitPointGain(
                occurrenceId,
                classLevel),
            pending));
    }

    row.append(input, actions);
    return row;
}

function advancementLevel(
    occurrence: AdvancementOccurrenceView
): number | null {
    const value = occurrence.progression?.value;
    if (typeof value !== "number"
        || !Number.isSafeInteger(value)
        || value <= 0) {
        return null;
    }
    return value;
}

function isLevelOwningKind(kind: string): boolean {
    const normalized = kind.trim().toLowerCase();
    return normalized === "class"
        || normalized === "prestigeclass"
        || normalized === "prestige-class";
}

function parseInteger(value: string): number | null {
    const normalized = value.trim();
    if (!/^-?\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed)
        && parsed >= -2147483648
        && parsed <= 2147483647
        ? parsed
        : null;
}
