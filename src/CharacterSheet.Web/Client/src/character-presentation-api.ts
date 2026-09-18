import type { HostEnvironment } from "./host-environment.js";
import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "./character-api.js";
import type { CharacterAdvancementView } from "./ui/character-advancement.js";
import type { CharacterMechanicsView } from "./ui/character-mechanics.js";

export interface CharacterPresentationResponse {
    advancement: CharacterAdvancementView;
    mechanics: CharacterMechanicsView | null;
}

export function buildCharacterPresentationUrl(
    environment: HostEnvironment,
    characterId: string
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/presentation`);
}

export async function loadCharacterPresentation(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterPresentationResponse> {
    const response = await fetcher(buildCharacterPresentationUrl(environment, characterId), {
        method: "GET",
        headers: { Accept: "application/json" }
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Character mechanics presentation is unavailable."),
            response.status);
    }
    return await response.json() as CharacterPresentationResponse;
}
