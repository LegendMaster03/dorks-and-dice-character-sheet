import type { CharacterMechanicsView } from "./ui/character-mechanics.js";

export interface RulesCoreContextualHelpView {
    topicKey: string;
    displayName: string;
    shortText: string;
    fullText?: string | null;
    prominence: string;
}

export interface RulesCoreCharacterAttackResolutionView {
    targetDefenseKey?: string | null;
    rollMode: string;
    targetStateKeys: readonly string[];
}

export interface RulesCoreCharacterResolvedMechanicContextView {
    mechanicKey: string;
    displayName: string;
    help?: RulesCoreContextualHelpView | null;
}

export interface RulesCoreCharacterActionContextView {
    actionKey: string;
    attackResolution?: RulesCoreCharacterAttackResolutionView | null;
}

export interface RulesCoreCharacterProjectionContextView {
    mechanics?: readonly RulesCoreCharacterResolvedMechanicContextView[];
    actions?: readonly RulesCoreCharacterActionContextView[];
    helpTopics?: readonly RulesCoreContextualHelpView[] | null;
}

const rulesCoreContext = new WeakMap<
    CharacterMechanicsView,
    RulesCoreCharacterProjectionContextView
>();

export function attachRulesCoreContext(
    mechanics: CharacterMechanicsView | null,
    projection: RulesCoreCharacterProjectionContextView | null | undefined
): CharacterMechanicsView | null {
    if (mechanics === null) return null;
    if (projection === null || projection === undefined) {
        rulesCoreContext.delete(mechanics);
        return mechanics;
    }

    rulesCoreContext.set(mechanics, projection);
    return mechanics;
}

export function getRulesCoreContext(
    mechanics: CharacterMechanicsView | null
): RulesCoreCharacterProjectionContextView | undefined {
    return mechanics === null ? undefined : rulesCoreContext.get(mechanics);
}
