import {
    setCharacterCurrentHitPoints,
    setCharacterDeathSaves
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";

export interface HealthWorkflow {
    setCurrentHitPoints(
        characterId: string,
        currentHitPoints: number | null
    ): Promise<void>;
    setDeathSaves(
        characterId: string,
        successes: number,
        failures: number
    ): Promise<void>;
}

export function createHealthWorkflow(
    routine: RoutineStateWorkflow,
    environment: HostEnvironment
): HealthWorkflow {
    return {
        async setCurrentHitPoints(
            characterId: string,
            currentHitPoints: number | null
        ): Promise<void> {
            await routine.mutate(
                "health-update",
                () => setCharacterCurrentHitPoints(
                    environment,
                    characterId,
                    currentHitPoints));
        },
        async setDeathSaves(
            characterId: string,
            successes: number,
            failures: number
        ): Promise<void> {
            await routine.mutate(
                "death-saves-update",
                () => setCharacterDeathSaves(
                    environment,
                    characterId,
                    successes,
                    failures));
        }
    };
}
