import type { CharacterBuilderUiState } from "../../app-state.js";
import {
    formatMechanicalValue,
    type CalculatedMechanicalValueView,
    type SavingThrowView
} from "../../ui/character-mechanics.js";
import { createButton, createElement } from "../../ui/components.js";
import { renderSplitStat } from "../../ui/split-stat.js";
import { renderSourceAttributions } from "../../ui/source-attribution.js";
import {
    getAbilityScoreActionPolicy,
    getBaseAbilityScoreDisplay,
    parseBaseAbilityScoreInput,
    type AbilityScoreDefinition
} from "../../ui/sheet-model.js";
import type { StructuralCharacterHandlers } from "../../ui/sheet-contracts.js";
import {
    renderFacts,
    renderMechanicalValue
} from "../../core/mechanics/mechanic-value.js";

export function renderAbilityScoreCard(
    definition: AbilityScoreDefinition,
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    handlers: StructuralCharacterHandlers,
    effectiveValue?: CalculatedMechanicalValueView,
    savingThrow?: SavingThrowView
): HTMLElement {
    const display = getBaseAbilityScoreDisplay(builder, definition.key);
    const configured = display.status === "configured";
    const policy = getAbilityScoreActionPolicy(builder, readOnly, configured);
    const card = createElement("article", "dd-stat dd-stat--ability");
    card.setAttribute("data-ability-key", definition.key);
    card.setAttribute("data-ability-score-state", display.status);
    card.setAttribute("data-effective-ability-state", effectiveValue === undefined ? "unavailable" : "resolved");

    const modifier = findAbilityModifier(effectiveValue);
    if (effectiveValue !== undefined) {
        card.setAttribute("data-effective-ability-key", effectiveValue.key);
    }

    const saveAttributes: Record<string, string> = {
        "data-ability-save": definition.key
    };
    if (savingThrow !== undefined) {
        saveAttributes["data-saving-throw-key"] = savingThrow.key;
    }

    card.append(renderSplitStat({
        label: definition.label,
        primaryValue: effectiveValue === undefined
            ? display.value
            : formatMechanicalValue(effectiveValue),
        primaryDetail: effectiveValue === undefined
            ? display.detail
            : "Effective value",
        primaryContext: effectiveValue === undefined
            ? undefined
            : `Base input: ${display.value}`,
        className: "dd-ability-stat__presentation",
        secondary: [
            {
                label: "Modifier",
                value: modifier === undefined ? "-" : formatMechanicalValue(modifier),
                className: "dd-ability-stat__modifier",
                attributes: { "data-ability-modifier": definition.key }
            },
            {
                label: abilitySaveLabel(savingThrow, definition),
                value: savingThrow === undefined ? "-" : formatMechanicalValue(savingThrow),
                className: "dd-ability-stat__save",
                attributes: saveAttributes
            }
        ]
    }));

    if (effectiveValue !== undefined) {
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

function findAbilityModifier(
    value: CalculatedMechanicalValueView | undefined
): NonNullable<CalculatedMechanicalValueView["relatedValues"]>[number] | undefined {
    return value?.relatedValues?.find(related =>
        related.key.toLowerCase() === "modifier"
        || related.label.trim().toLowerCase() === "modifier");
}

export function findAbilitySavingThrow(
    saves: readonly SavingThrowView[] | undefined,
    definition: AbilityScoreDefinition
): SavingThrowView | undefined {
    const abilityKey = definition.key.toLowerCase();
    const abilityLabel = definition.label.trim().toLowerCase();
    return saves?.find(save => {
        const governing = save.governingAbility?.trim().toLowerCase();
        if (governing === abilityKey || governing === abilityLabel) return true;

        const key = save.key.trim().toLowerCase();
        if (key === `save.${abilityKey}`
            || key === `saving-throw.${abilityKey}`) return true;

        return (abilityKey === "constitution"
                && (key === "save.fortitude" || key === "saving-throw.fortitude"))
            || (abilityKey === "dexterity"
                && (key === "save.reflex" || key === "saving-throw.reflex"))
            || (abilityKey === "wisdom"
                && (key === "save.will" || key === "saving-throw.will"));
    });
}

function abilitySaveLabel(
    savingThrow: SavingThrowView | undefined,
    definition: AbilityScoreDefinition
): string {
    if (savingThrow === undefined) return "Save";
    const key = savingThrow.key.trim().toLowerCase();
    if (key === `save.${definition.key}`
        || key === `saving-throw.${definition.key}`) {
        return "Save";
    }
    if (key === "save.fortitude" || key === "saving-throw.fortitude") return "Fort Save";
    if (key === "save.reflex" || key === "saving-throw.reflex") return "Ref Save";
    if (key === "save.will" || key === "saving-throw.will") return "Will Save";
    return savingThrow.label;
}

function renderAbilityMechanicalDetails(value: CalculatedMechanicalValueView): HTMLElement | null {
    const related = (value.relatedValues ?? []).filter(entry => entry !== findAbilityModifier(value));
    if (!(related.length || value.breakdown?.length || value.sourceAttributions?.length)) return null;

    const details = createElement("details", "dd-stat__mechanics-details");
    details.append(createElement("summary", "dd-stat__mechanics-details-toggle", "Details"));
    const body = createElement("div", "dd-stat__mechanics-details-body");
    const relatedFacts = renderFacts(related.map(entry =>
        [entry.label, formatMechanicalValue(entry)] as const));
    if (relatedFacts !== null) body.append(relatedFacts);
    const breakdown = renderFacts((value.breakdown ?? []).map(entry =>
        [entry.label, formatMechanicalValue(entry)] as const));
    if (breakdown !== null) body.append(breakdown);
    const sources = renderSourceAttributions(value.sourceAttributions, true);
    if (sources !== null) body.append(sources);
    details.append(body);
    return details;
}

export function renderAdditionalAbilityValues(values: readonly CalculatedMechanicalValueView[]): HTMLElement {
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

