import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export interface FoundationalRuleSelectionResponse {
    id: string;
    category: string;
    ruleConceptKey: string;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterAdvancementEntryResponse {
    id: string;
    ordinal: number | null;
    kind: string;
    ruleConceptKey: string;
    parentAdvancementEntryId: string | null;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterBuildResponse {
    characterId: string;
    builderStatus: string;
    readOnly: boolean;
    foundationalSelections: FoundationalRuleSelectionResponse[];
    progressionEntries: CharacterAdvancementEntryResponse[];
}

export type CharacterBuilderChoice = "raceSpecies" | "startingClass";

export function buildCharacterBuildBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    choice?: CharacterBuilderChoice
): string {
    const base = `/api/characters/${encodeURIComponent(characterId)}/build`;
    if (choice === "raceSpecies") {
        return buildCharacterSheetApiUrl(environment, `${base}/race-species`);
    }
    if (choice === "startingClass") {
        return buildCharacterSheetApiUrl(environment, `${base}/starting-class`);
    }
    return buildCharacterSheetApiUrl(environment, base);
}

export async function loadCharacterBuild(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(buildCharacterBuildBackendUrl(environment, characterId), {
        method: "GET",
        headers: { Accept: "application/json" }
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to load Character build state."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export async function setCharacterBuildChoice(
    environment: HostEnvironment,
    characterId: string,
    choice: CharacterBuilderChoice,
    conceptKey: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(buildCharacterBuildBackendUrl(environment, characterId, choice), {
        method: "PUT",
        headers: {
            Accept: "application/json",
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ conceptKey })
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to save Character build choice."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export async function clearCharacterBuildChoice(
    environment: HostEnvironment,
    characterId: string,
    choice: CharacterBuilderChoice,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(buildCharacterBuildBackendUrl(environment, characterId, choice), {
        method: "DELETE",
        headers: { Accept: "application/json" }
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to clear Character build choice."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}
