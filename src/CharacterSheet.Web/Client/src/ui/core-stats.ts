import type { CharacterBuilderUiState } from "../app-state.js";
import type { CharacterAbilityKey } from "../builder-api.js";
import {
    findAbilityValue,
    type CharacterMechanicsView
} from "./character-mechanics.js";
import { createElement } from "./components.js";
import {
    ABILITY_SCORE_DEFINITIONS
} from "./sheet-model.js";
import type { StructuralCharacterHandlers } from "./sheet-contracts.js";
import {
    findAbilitySavingThrow,
    renderAbilityScoreCard,
    renderAdditionalAbilityValues
} from "../features/abilities/ability-stats.js";
import { findInitiativeValue } from "../features/initiative/initiative.js";
import { renderHealthQuickCard, type HealthControlOptions } from "../features/health/health.js";
import { renderInspirationQuickCard } from "../features/inspiration/inspiration.js";
import { renderArmorClassQuickCard } from "../features/defense/defense.js";
import { renderMovementValues } from "../features/movement/movement.js";
import { renderProficiencyQuickCard } from "../features/proficiency/proficiency.js";
import { renderQuickMechanicalValue } from "../core/mechanics/mechanic-value.js";

export function renderCoreStats(
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null,
    healthControl: HealthControlOptions,
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
            findAbilityValue(mechanics?.abilityValues, definition.key),
            findAbilitySavingThrow(mechanics?.savingThrows, definition)));
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

    quickGrid.append(
        renderProficiencyQuickCard(mechanics?.combatFundamentals),
        movement,
        renderInspirationQuickCard(mechanics?.inspiration),
        renderHealthQuickCard(mechanics, healthControl),
        initiative,
        renderArmorClassQuickCard(mechanics)
    );
    section.append(abilityGrid, quickGrid);

    const standardAbilityKeys = new Set(ABILITY_SCORE_DEFINITIONS.map(definition => definition.key));
    const additionalAbilityValues = mechanics?.abilityValues?.filter(value => !standardAbilityKeys.has(value.key as CharacterAbilityKey)) ?? [];
    if (additionalAbilityValues.length > 0) {
        section.append(renderAdditionalAbilityValues(additionalAbilityValues));
    }
    return section;
}

