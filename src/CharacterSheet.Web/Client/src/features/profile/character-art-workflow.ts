import type { HostEnvironment } from "../../host-environment.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";
import {
    clearCharacterPortrait,
    deleteCharacterArt,
    setCharacterPortrait,
    uploadCharacterArt
} from "./character-art-api.js";

export interface CharacterArtWorkflow {
    upload(characterId: string, file: File): Promise<void>;
    setPortrait(characterId: string, assetId: string): Promise<void>;
    clearPortrait(characterId: string): Promise<void>;
    remove(characterId: string, assetId: string): Promise<void>;
}

export function createCharacterArtWorkflow(
    routine: RoutineStateWorkflow,
    environment: HostEnvironment
): CharacterArtWorkflow {
    return {
        async upload(characterId, file) {
            await routine.mutate(
                "art-update",
                () => uploadCharacterArt(environment, characterId, file),
                undefined,
                { resolveReferences: false });
        },
        async setPortrait(characterId, assetId) {
            await routine.mutate(
                "art-update",
                () => setCharacterPortrait(environment, characterId, assetId),
                assetId,
                { resolveReferences: false });
        },
        async clearPortrait(characterId) {
            await routine.mutate(
                "art-update",
                () => clearCharacterPortrait(environment, characterId),
                undefined,
                { resolveReferences: false });
        },
        async remove(characterId, assetId) {
            await routine.mutate(
                "art-delete",
                () => deleteCharacterArt(environment, characterId, assetId),
                assetId,
                { resolveReferences: false });
        }
    };
}
