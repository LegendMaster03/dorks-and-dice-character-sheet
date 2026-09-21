import {
    clearCharacterBaseAbilityScore,
    setCharacterBaseAbilityScore,
    type CharacterAbilityKey
} from "../../builder-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type { BuildStateWorkflow } from "../../core/application/build-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface AbilityWorkflow {
    save(
        characterId: string,
        abilityKey: CharacterAbilityKey,
        score: number
    ): Promise<void>;
    clear(characterId: string, abilityKey: CharacterAbilityKey): Promise<void>;
}

export function createAbilityWorkflow(
    application: CharacterSheetApplication,
    buildState: BuildStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): AbilityWorkflow {
    async function persist(
        characterId: string,
        abilityKey: CharacterAbilityKey,
        operation: () => ReturnType<typeof setCharacterBaseAbilityScore>
    ): Promise<void> {
        application.dispatch({ type: "ability-save-started", abilityKey });
        try {
            const build = await operation();
            application.dispatch({ type: "ability-saved", build });
            await buildState.resolveReferences(build);
            await presentation.load(characterId);
        } catch (error) {
            application.dispatch({
                type: "ability-save-failed",
                abilityKey,
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        save(characterId, abilityKey, score): Promise<void> {
            return persist(
                characterId,
                abilityKey,
                () => setCharacterBaseAbilityScore(
                    environment,
                    characterId,
                    abilityKey,
                    score));
        },

        clear(characterId, abilityKey): Promise<void> {
            return persist(
                characterId,
                abilityKey,
                () => clearCharacterBaseAbilityScore(
                    environment,
                    characterId,
                    abilityKey));
        }
    };
}
