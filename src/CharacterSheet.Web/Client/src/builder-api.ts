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
    level?: number | null;
}

export const CHARACTER_ABILITY_KEYS = [
    "strength",
    "dexterity",
    "constitution",
    "intelligence",
    "wisdom",
    "charisma"
] as const;

export type CharacterAbilityKey = typeof CHARACTER_ABILITY_KEYS[number];

export interface BaseAbilityScoreInputResponse {
    id: string;
    abilityKey: CharacterAbilityKey;
    score: number;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterBuildResponse {
    characterId: string;
    builderStatus: string;
    readOnly: boolean;
    foundationalSelections: FoundationalRuleSelectionResponse[];
    baseAbilityScoreInputs: BaseAbilityScoreInputResponse[];
    progressionEntries: CharacterAdvancementEntryResponse[];
}

export type CharacterBuilderChoice = "species" | "subspecies" | "background" | "deity" | "startingClass" | "subclass";

export function buildCharacterBuildBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    choice?: CharacterBuilderChoice,
    classAdvancementEntryId?: string
): string {
    const base = `/api/characters/${encodeURIComponent(characterId)}/build`;
    if (choice === "species") {
        return buildCharacterSheetApiUrl(environment, `${base}/species`);
    }
    if (choice === "subspecies") {
        return buildCharacterSheetApiUrl(environment, `${base}/subspecies`);
    }
    if (choice === "background") {
        return buildCharacterSheetApiUrl(environment, `${base}/background`);
    }
    if (choice === "deity") {
        return buildCharacterSheetApiUrl(environment, `${base}/deity`);
    }
    if (choice === "startingClass") {
        return buildCharacterSheetApiUrl(environment, `${base}/starting-class`);
    }
    if (choice === "subclass") {
        if (classAdvancementEntryId === undefined || classAdvancementEntryId.trim().length === 0) {
            throw new Error("A Class advancement entry is required for a Subclass selection.");
        }
        return buildCharacterSheetApiUrl(
            environment,
            `${base}/classes/${encodeURIComponent(classAdvancementEntryId)}/subclass`);
    }
    return buildCharacterSheetApiUrl(environment, base);
}

export function buildCharacterAbilityScoreBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    abilityKey: CharacterAbilityKey
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/build/ability-scores/${encodeURIComponent(abilityKey)}`);
}

export function buildCharacterFeatBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    featAdvancementEntryId?: string
): string {
    const base = `/api/characters/${encodeURIComponent(characterId)}/build/feats`;
    return buildCharacterSheetApiUrl(
        environment,
        featAdvancementEntryId === undefined
            ? base
            : `${base}/${encodeURIComponent(featAdvancementEntryId)}`);
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
    classAdvancementEntryId?: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterBuildBackendUrl(environment, characterId, choice, classAdvancementEntryId),
        {
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
    classAdvancementEntryId?: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterBuildBackendUrl(environment, characterId, choice, classAdvancementEntryId),
        {
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

export async function setCharacterBaseAbilityScore(
    environment: HostEnvironment,
    characterId: string,
    abilityKey: CharacterAbilityKey,
    score: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterAbilityScoreBackendUrl(environment, characterId, abilityKey),
        {
            method: "PUT",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ score })
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to save base Ability Score."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export async function clearCharacterBaseAbilityScore(
    environment: HostEnvironment,
    characterId: string,
    abilityKey: CharacterAbilityKey,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterAbilityScoreBackendUrl(environment, characterId, abilityKey),
        {
            method: "DELETE",
            headers: { Accept: "application/json" }
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to clear base Ability Score."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export function buildCharacterAdvancementLevelBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    advancementEntryId: string
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/build/advancements/${encodeURIComponent(advancementEntryId)}/level`);
}

export async function setCharacterAdvancementLevel(
    environment: HostEnvironment,
    characterId: string,
    advancementEntryId: string,
    level: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterAdvancementLevelBackendUrl(environment, characterId, advancementEntryId),
        {
            method: "PUT",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify({ level })
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to save advancement level."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export async function addCharacterFeatOccurrence(
    environment: HostEnvironment,
    characterId: string,
    conceptKey: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(buildCharacterFeatBackendUrl(environment, characterId), {
        method: "POST",
        headers: { Accept: "application/json", "Content-Type": "application/json" },
        body: JSON.stringify({ conceptKey })
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to add Feat occurrence."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}

export async function removeCharacterFeatOccurrence(
    environment: HostEnvironment,
    characterId: string,
    featAdvancementEntryId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterBuildResponse> {
    const response = await fetcher(
        buildCharacterFeatBackendUrl(environment, characterId, featAdvancementEntryId),
        { method: "DELETE", headers: { Accept: "application/json" } });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to remove Feat occurrence."),
            response.status);
    }
    return await response.json() as CharacterBuildResponse;
}
