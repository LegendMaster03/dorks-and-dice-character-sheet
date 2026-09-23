import {
    setCharacterProfile,
    type CharacterProfileInput
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";

export interface ProfileWorkflow {
    save(characterId: string, input: CharacterProfileInput): Promise<void>;
}

export function createProfileWorkflow(
    routine: RoutineStateWorkflow,
    environment: HostEnvironment
): ProfileWorkflow {
    return {
        async save(characterId: string, input: CharacterProfileInput): Promise<void> {
            await routine.mutate(
                "profile-update",
                () => setCharacterProfile(environment, characterId, input));
        }
    };
}
