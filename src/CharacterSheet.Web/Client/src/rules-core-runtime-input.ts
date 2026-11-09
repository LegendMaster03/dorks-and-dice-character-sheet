/**
 * Builds stable keys for transient Rules Core roll inputs so a runtime result does not collide
 * with other character-owned facts.
 */
export const RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX = "rules-core-roll:";

export function toRulesCoreRuntimeRollStateKey(rollKey: string): string {
    return `${RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX}${rollKey}`;
}
