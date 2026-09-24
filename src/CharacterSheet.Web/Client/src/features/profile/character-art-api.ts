import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "../../character-api.js";
import { loadCharacterState, type CharacterStateResponse } from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";

function artBase(environment: HostEnvironment, characterId: string): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/state/art`);
}

async function mutate(
    environment: HostEnvironment,
    characterId: string,
    url: string,
    init: RequestInit,
    fallback: string,
    fetcher: FetchLike
): Promise<CharacterStateResponse> {
    const response = await fetcher(url, init);
    if (!response.ok) {
        throw new CharacterSheetApiError(await readApiError(response, fallback), response.status);
    }
    return await loadCharacterState(environment, characterId, fetcher);
}

export function buildCharacterArtContentUrl(
    environment: HostEnvironment,
    characterId: string,
    assetId: string
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/state/art/${encodeURIComponent(assetId)}/content`);
}

export async function uploadCharacterArt(
    environment: HostEnvironment,
    characterId: string,
    file: File,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    const form = new FormData();
    form.append("file", file, file.name);
    return await mutate(
        environment,
        characterId,
        artBase(environment, characterId),
        { method: "POST", headers: { Accept: "application/json" }, body: form },
        "Unable to upload Character art.",
        fetcher);
}

export async function setCharacterPortrait(
    environment: HostEnvironment,
    characterId: string,
    assetId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await mutate(
        environment,
        characterId,
        `${artBase(environment, characterId)}/${encodeURIComponent(assetId)}/portrait`,
        { method: "PUT", headers: { Accept: "application/json" } },
        "Unable to select Character portrait.",
        fetcher);
}

export async function clearCharacterPortrait(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await mutate(
        environment,
        characterId,
        `${artBase(environment, characterId)}/portrait`,
        { method: "DELETE", headers: { Accept: "application/json" } },
        "Unable to clear Character portrait.",
        fetcher);
}

export async function deleteCharacterArt(
    environment: HostEnvironment,
    characterId: string,
    assetId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await mutate(
        environment,
        characterId,
        `${artBase(environment, characterId)}/${encodeURIComponent(assetId)}`,
        { method: "DELETE", headers: { Accept: "application/json" } },
        "Unable to delete Character art.",
        fetcher);
}
