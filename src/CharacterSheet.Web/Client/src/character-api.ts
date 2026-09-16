import type { HostEnvironment } from "./host-environment.js";

export interface CharacterSheetRootState {
    schemaVersion: number;
    builderStatus: string;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterSheetBootstrapResponse {
    characterId: string;
    name: string;
    lifecycle: "Active" | "Archived";
    archivedAt: string | null;
    campaignIds: string[];
    hasRichSheet: boolean;
    sheet: CharacterSheetRootState | null;
}

export type FetchLike = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

export class CharacterSheetApiError extends Error {
    constructor(message: string, public readonly status: number) {
        super(message);
        this.name = "CharacterSheetApiError";
    }
}

export class RichSheetInitializationError extends Error {
    constructor(public readonly characterId: string, cause: unknown) {
        super("The Site Character was created, but its digital Character Sheet could not be initialized.", { cause });
        this.name = "RichSheetInitializationError";
    }
}

export function buildCharacterSheetBackendUrl(
    environment: HostEnvironment,
    characterId: string
): string {
    const backendPath = `/api/characters/${encodeURIComponent(characterId)}/sheet`;
    if (!environment.embedded) {
        return backendPath;
    }

    if (environment.contextUrl === null) {
        throw new Error("Embedded Character Sheet is missing a valid Tool Host context URL.");
    }

    const queryOrFragmentIndex = environment.contextUrl.search(/[?#]/);
    const contextPath = queryOrFragmentIndex >= 0
        ? environment.contextUrl.slice(0, queryOrFragmentIndex)
        : environment.contextUrl;
    if (!contextPath.endsWith("/context")) {
        throw new Error("Embedded Character Sheet is missing a valid Tool Host context URL.");
    }

    const hostApiBase = contextPath.slice(0, -"/context".length);
    return `${hostApiBase}/api/upstream${backendPath}`;
}

export function buildCharacterRouteUrl(environment: HostEnvironment, characterId: string): string {
    const route = `/characters/${encodeURIComponent(characterId)}`;
    return environment.embedded ? `${environment.toolBasePath}${route}` : route;
}

export async function loadCharacterSheet(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterSheetBootstrapResponse | null> {
    const response = await fetcher(buildCharacterSheetBackendUrl(environment, characterId), {
        method: "GET",
        headers: { Accept: "application/json" }
    });

    if (response.status === 404) {
        return null;
    }

    if (!response.ok) {
        throw new CharacterSheetApiError(await readError(response, "Unable to open Character Sheet."), response.status);
    }

    return await response.json() as CharacterSheetBootstrapResponse;
}

export async function initializeCharacterSheet(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterSheetBootstrapResponse> {
    const response = await fetcher(buildCharacterSheetBackendUrl(environment, characterId), {
        method: "POST",
        headers: { Accept: "application/json" }
    });

    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readError(response, "Unable to initialize digital Character Sheet."),
            response.status);
    }

    return await response.json() as CharacterSheetBootstrapResponse;
}

export async function createNewCharacterAndSheet(
    environment: HostEnvironment,
    name: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<string> {
    const trimmedName = name.trim();
    if (trimmedName.length === 0) {
        throw new Error("Character name is required.");
    }

    if (!environment.embedded) {
        throw new Error("New Site Characters can only be created while Character Sheet is hosted by Dorks & Dice.");
    }

    const createResponse = await fetcher("/characters/api", {
        method: "POST",
        headers: {
            Accept: "application/json",
            "Content-Type": "application/json"
        },
        body: JSON.stringify({ name: trimmedName })
    });

    if (!createResponse.ok) {
        throw new CharacterSheetApiError(await readError(createResponse, "Unable to create Site Character."), createResponse.status);
    }

    const created = await createResponse.json() as { id?: unknown };
    if (typeof created.id !== "string" || created.id.length === 0) {
        throw new Error("Site Character creation did not return a CharacterId.");
    }

    try {
        await initializeCharacterSheet(environment, created.id, fetcher);
    } catch (error) {
        throw new RichSheetInitializationError(created.id, error);
    }

    return created.id;
}

async function readError(response: Response, fallback: string): Promise<string> {
    try {
        const payload = await response.json() as { error?: unknown };
        return typeof payload.error === "string" && payload.error.trim().length > 0
            ? payload.error
            : fallback;
    } catch {
        return fallback;
    }
}
