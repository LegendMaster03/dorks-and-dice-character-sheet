import {
    removeCharacterHitPointGain,
    removeCharacterRulesInput,
    setCharacterHitPointGain,
    setCharacterRulesInput,
    type CharacterRulesInputKind,
    type CharacterRulesInputStateInput
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { RoutineStateWorkflow } from "./routine-state-workflow.js";
import type { PresentationWorkflow } from "./presentation-workflow.js";

export interface RulesInputMutationOptions {
    refreshPresentation?: boolean;
    renderMutation?: boolean;
}

export interface RulesInputWorkflow {
    set(
        characterId: string,
        input: CharacterRulesInputStateInput,
        options?: RulesInputMutationOptions
    ): Promise<boolean>;
    remove(
        characterId: string,
        kind: CharacterRulesInputKind,
        key: string
    ): Promise<boolean>;
    setChoice(characterId: string, choiceKey: string, value: string): Promise<boolean>;
    clearChoice(characterId: string, choiceKey: string): Promise<boolean>;
    setResource(characterId: string, resourceKey: string, currentValue: number): Promise<boolean>;
    setBooleanFact(
        characterId: string,
        factKey: string,
        value: boolean,
        options?: RulesInputMutationOptions
    ): Promise<boolean>;
    setHitPointGain(
        characterId: string,
        advancementOccurrenceId: string,
        classLevel: number,
        hitDieValue: number
    ): Promise<boolean>;
    clearHitPointGain(
        characterId: string,
        advancementOccurrenceId: string,
        classLevel: number
    ): Promise<boolean>;
}

export function createRulesInputWorkflow(
    routine: RoutineStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): RulesInputWorkflow {
    async function refresh(
        characterId: string,
        changed: boolean,
        refreshPresentation = true
    ): Promise<boolean> {
        if (changed && refreshPresentation) await presentation.load(characterId);
        return changed;
    }

    async function set(
        characterId: string,
        input: CharacterRulesInputStateInput,
        options: RulesInputMutationOptions = {}
    ): Promise<boolean> {
        return await refresh(
            characterId,
            await routine.mutate(
                "rules-input-update",
                () => setCharacterRulesInput(
                    environment,
                    characterId,
                    input),
                `${input.kind}:${input.key}`,
                options.renderMutation ?? true),
            options.refreshPresentation ?? true);
    }

    async function remove(
        characterId: string,
        kind: CharacterRulesInputKind,
        key: string
    ): Promise<boolean> {
        return await refresh(
            characterId,
            await routine.mutate(
                "rules-input-delete",
                () => removeCharacterRulesInput(
                    environment,
                    characterId,
                    kind,
                    key),
                `${kind}:${key}`));
    }

    return {
        set,
        remove,

        setChoice(characterId, choiceKey, value): Promise<boolean> {
            return set(characterId, {
                kind: "choice",
                key: choiceKey,
                textValue: value
            });
        },

        clearChoice(characterId, choiceKey): Promise<boolean> {
            return remove(characterId, "choice", choiceKey);
        },

        setResource(characterId, resourceKey, currentValue): Promise<boolean> {
            return set(characterId, {
                kind: "resource",
                key: resourceKey,
                integerValue: currentValue
            });
        },

        setBooleanFact(characterId, factKey, value, options): Promise<boolean> {
            return set(characterId, {
                kind: "booleanFact",
                key: factKey,
                booleanValue: value
            }, options);
        },

        async setHitPointGain(
            characterId,
            advancementOccurrenceId,
            classLevel,
            hitDieValue
        ): Promise<boolean> {
            return await refresh(
                characterId,
                await routine.mutate(
                    "hit-point-gain-update",
                    () => setCharacterHitPointGain(
                        environment,
                        characterId,
                        advancementOccurrenceId,
                        classLevel,
                        hitDieValue),
                    `${advancementOccurrenceId}:${classLevel}`));
        },

        async clearHitPointGain(
            characterId,
            advancementOccurrenceId,
            classLevel
        ): Promise<boolean> {
            return await refresh(
                characterId,
                await routine.mutate(
                    "hit-point-gain-delete",
                    () => removeCharacterHitPointGain(
                        environment,
                        characterId,
                        advancementOccurrenceId,
                        classLevel),
                    `${advancementOccurrenceId}:${classLevel}`));
        }
    };
}
