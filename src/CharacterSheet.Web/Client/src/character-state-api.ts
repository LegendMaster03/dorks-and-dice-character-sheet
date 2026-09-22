import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    type FetchLike
} from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export interface CharacterInventoryItemOccurrenceResponse {
    id: string;
    ruleConceptKey: string;
    createdAt: string;
}

export interface CharacterNoteResponse {
    id: string;
    content: string;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterConditionOccurrenceResponse {
    id: string;
    ruleConceptKey: string | null;
    customName: string | null;
    level: number | null;
    counterCurrent: number | null;
    counterMaximum: number | null;
    duration: string | null;
    notes: string | null;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterConditionStateInput {
    customName?: string | null;
    level?: number | null;
    counterCurrent?: number | null;
    counterMaximum?: number | null;
    duration?: string | null;
    notes?: string | null;
}

export interface CharacterConditionCreateInput extends CharacterConditionStateInput {
    conceptKey?: string | null;
}

export interface CharacterStateResponse {
    characterId: string;
    readOnly: boolean;
    currentHitPoints: number | null;
    inventoryItemOccurrences: CharacterInventoryItemOccurrenceResponse[];
    notes: CharacterNoteResponse[];
    conditions: CharacterConditionOccurrenceResponse[];
}

export function buildCharacterStateBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    resource?: "health" | "inventory" | "notes" | "conditions",
    entryId?: string
): string {
    let path = `/api/characters/${encodeURIComponent(characterId)}/state`;
    if (resource !== undefined) {
        path += `/${resource}`;
    }
    if (entryId !== undefined) {
        path += `/${encodeURIComponent(entryId)}`;
    }
    return buildCharacterSheetApiUrl(environment, path);
}

export async function loadCharacterState(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId),
        "GET",
        undefined,
        "Unable to load Character routine state.");
}

export async function setCharacterCurrentHitPoints(
    environment: HostEnvironment,
    characterId: string,
    currentHitPoints: number | null,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "health"),
        "PUT",
        { currentHitPoints },
        "Unable to update Character hit points.");
}

export async function addInventoryItemOccurrence(
    environment: HostEnvironment,
    characterId: string,
    conceptKey: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "inventory"),
        "POST",
        { conceptKey },
        "Unable to add inventory item.");
}

export async function removeInventoryItemOccurrence(
    environment: HostEnvironment,
    characterId: string,
    occurrenceId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "inventory", occurrenceId),
        "DELETE",
        undefined,
        "Unable to remove inventory item.");
}

export async function addCharacterNote(
    environment: HostEnvironment,
    characterId: string,
    content: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "notes"),
        "POST",
        { content },
        "Unable to add Character note.");
}

export async function updateCharacterNote(
    environment: HostEnvironment,
    characterId: string,
    noteId: string,
    content: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "notes", noteId),
        "PUT",
        { content },
        "Unable to update Character note.");
}

export async function removeCharacterNote(
    environment: HostEnvironment,
    characterId: string,
    noteId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "notes", noteId),
        "DELETE",
        undefined,
        "Unable to delete Character note.");
}

export async function addCharacterCondition(
    environment: HostEnvironment,
    characterId: string,
    input: CharacterConditionCreateInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "conditions"),
        "POST",
        input,
        "Unable to add Character condition.");
}

export async function updateCharacterCondition(
    environment: HostEnvironment,
    characterId: string,
    conditionId: string,
    input: CharacterConditionStateInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "conditions", conditionId),
        "PUT",
        input,
        "Unable to update Character condition.");
}

export async function removeCharacterCondition(
    environment: HostEnvironment,
    characterId: string,
    conditionId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "conditions", conditionId),
        "DELETE",
        undefined,
        "Unable to remove Character condition.");
}

async function requestState(
    fetcher: FetchLike,
    url: string,
    method: "GET" | "POST" | "PUT" | "DELETE",
    body: object | undefined,
    fallback: string
): Promise<CharacterStateResponse> {
    const response = await fetcher(url, {
        method,
        headers: body === undefined
            ? { Accept: "application/json" }
            : {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(await readApiError(response, fallback), response.status);
    }
    return await response.json() as CharacterStateResponse;
}

async function readApiError(response: Response, fallback: string): Promise<string> {
    try {
        const payload = await response.json() as { error?: unknown; detail?: unknown; title?: unknown };
        if (typeof payload.error === "string" && payload.error.trim().length > 0) return payload.error;
        if (typeof payload.detail === "string" && payload.detail.trim().length > 0) return payload.detail;
        if (typeof payload.title === "string" && payload.title.trim().length > 0) return payload.title;
        return fallback;
    } catch {
        return fallback;
    }
}
