export const RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX = "rules-core-roll:";

export function toRulesCoreRuntimeRollStateKey(rollKey: string): string {
    return `${RULES_CORE_RUNTIME_ROLL_INPUT_PREFIX}${rollKey}`;
}
