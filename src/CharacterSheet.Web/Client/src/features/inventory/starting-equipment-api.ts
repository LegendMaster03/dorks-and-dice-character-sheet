import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "../../character-api.js";
import type { CharacterStateResponse } from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";

export async function applyStartingEquipment(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    const response = await fetcher(
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/state/starting-equipment/apply`),
        {
            method: "POST",
            headers: { Accept: "application/json" }
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to apply starting equipment."),
            response.status);
    }
    return await response.json() as CharacterStateResponse;
}
