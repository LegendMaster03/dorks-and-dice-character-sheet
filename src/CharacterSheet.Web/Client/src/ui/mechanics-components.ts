/**
 * Compatibility exports for existing consumers.
 *
 * New feature code should import the owning core/feature module directly. This
 * file intentionally contains no feature implementation.
 */
export {
    renderMechanicalValue,
    renderQuickMechanicalValue,
    renderDisplayFields,
    renderFacts
} from "../core/mechanics/mechanic-value.js";
export {
    renderSavingThrowsCard
} from "../features/saving-throws/saving-throws.js";
export {
    findInitiativeValue
} from "../features/initiative/initiative.js";
export {
    renderArmorClassQuickCard,
    renderDefenseMechanicsCard
} from "../features/defense/defense.js";
export {
    adjustCurrentHitPoints,
    renderHealthQuickCard,
    renderHealthMechanicsCard,
    renderRecoveryContinuation,
    renderRecoveryControls,
    type HealthControlOptions
} from "../features/health/health.js";
export {
    renderMovementValues
} from "../features/movement/movement.js";
export {
    renderActionsPresentation
} from "../features/actions/actions.js";
export {
    renderCombatFundamentalsCard,
    renderCombatMechanicsSummary
} from "../features/combat/combat.js";
