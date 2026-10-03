import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    type FetchLike
} from "../../character-api.js";
import type { CharacterStateResponse } from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";

export function buildStartingEquipmentMaterializationUrl(
    environment: HostEnvironment,
    characterId: string
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/state/inventory/starting-equipment`);
}

export async function materializeStartingEquipmentInventory(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    const response = await fetcher(
        buildStartingEquipmentMaterializationUrl(environment, characterId),
        {
            method: "POST",
            headers: { Accept: "application/json" }
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(
                response,
                "Unable to add starting items to Character inventory."),
            response.status);
    }
    return await response.json() as CharacterStateResponse;
}

async function readApiError(response: Response, fallback: string): Promise<string> {
    try {
        const payload = await response.json() as {
            error?: unknown;
            detail?: unknown;
            title?: unknown;
        };
        if (typeof payload.error === "string" && payload.error.trim().length > 0) {
            return payload.error;
        }
        if (typeof payload.detail === "string" && payload.detail.trim().length > 0) {
            return payload.detail;
        }
        if (typeof payload.title === "string" && payload.title.trim().length > 0) {
            return payload.title;
        }
        return fallback;
    } catch {
        return fallback;
    }
}
