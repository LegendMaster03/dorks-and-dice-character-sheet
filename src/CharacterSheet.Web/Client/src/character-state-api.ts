import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    type FetchLike
} from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export type CharacterRulesInputKind =
    | "choice"
    | "competencyRank"
    | "training"
    | "classSkill"
    | "knownSpell"
    | "resource"
    | "integerFact"
    | "booleanFact"
    | "stringFact";

export interface CharacterRulesInputStateResponse {
    id: string;
    kind: CharacterRulesInputKind;
    key: string;
    integerValue: number | null;
    booleanValue: boolean | null;
    textValue: string | null;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterRulesInputStateInput {
    kind: CharacterRulesInputKind;
    key: string;
    integerValue?: number | null;
    booleanValue?: boolean | null;
    textValue?: string | null;
}

export interface CharacterHitPointGainStateResponse {
    id: string;
    advancementOccurrenceId: string;
    classLevel: number;
    hitDieValue: number;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterInventoryItemOccurrenceResponse {
    id: string;
    ruleConceptKey: string | null;
    customName: string | null;
    createdAt: string;
    quantity: number;
    isCarried: boolean;
    isEquipped: boolean;
    isAttuned: boolean;
    containerOccurrenceId: string | null;
    updatedAt: string;
}

export interface CharacterInventoryItemOccurrenceStateInput {
    quantity: number;
    isCarried: boolean;
    isEquipped: boolean;
    isAttuned: boolean;
    containerOccurrenceId?: string | null;
}

export interface CharacterNoteResponse {
    id: string;
    content: string;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterDeathSavesResponse {
    successes: number;
    failures: number;
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

export interface CharacterRecoveryChoiceOptionResponse {
    key: string;
    displayName: string;
    value?: string | null;
}

export interface CharacterRecoveryChoiceResponse {
    key: string;
    prompt: string;
    required: boolean;
    options: CharacterRecoveryChoiceOptionResponse[];
}

export interface CharacterRecoveryRollResponse {
    key: string;
    rollKind: string;
    prompt: string;
    required: boolean;
    mechanicKey?: string | null;
    rollMode?: string | null;
}

export interface CharacterRecoveryEffectResponse {
    effectKey: string;
    targetKind: string;
    targetKey: string;
    operation: string;
    amount?: number | null;
    value?: string | null;
    referenceKey?: string | null;
}

export interface CharacterRecoveryResolutionResponse {
    procedureKey: string;
    displayName: string;
    presentationRole?: string | null;
    status: string;
    missingCapabilityKeys: string[];
    missingInputKeys: string[];
    pendingChoices: CharacterRecoveryChoiceResponse[];
    pendingRolls: CharacterRecoveryRollResponse[];
    consequences: CharacterRecoveryEffectResponse[];
}

export interface CharacterRecoveryRequestInput {
    integerInputs?: Record<string, number>;
    booleanInputs?: Record<string, boolean>;
    stringInputs?: Record<string, string>;
    choices?: Record<string, string>;
    rolls?: Record<string, number>;
}

export interface CharacterRecoveryResponse {
    resolution: CharacterRecoveryResolutionResponse;
    state: CharacterStateResponse;
}

export interface CharacterCurrencyBalanceResponse {
    id: string;
    currencyKey: string;
    amount: number;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterArtAssetResponse {
    id: string;
    originalFileName: string;
    contentType: string;
    byteLength: number;
    isPortrait: boolean;
    createdAt: string;
    updatedAt: string;
}

export interface CharacterProfileResponse {
    alignment: string | null;
    deity: string | null;
    age: string | null;
    height: string | null;
    weight: string | null;
    appearance: string | null;
    personalityTraits: string | null;
    ideals: string | null;
    bonds: string | null;
    flaws: string | null;
    backstory: string | null;
    alliesAndOrganizations: string | null;
    symbol: string | null;
    createdAt: string;
    updatedAt: string;
}

export type CharacterProfileInput = Omit<CharacterProfileResponse, "createdAt" | "updatedAt">;

export interface CharacterStateResponse {
    characterId: string;
    readOnly: boolean;
    currentHitPoints: number | null;
    advancementProgress: number | null;
    deathSaves: CharacterDeathSavesResponse;
    inventoryItemOccurrences: CharacterInventoryItemOccurrenceResponse[];
    notes: CharacterNoteResponse[];
    conditions: CharacterConditionOccurrenceResponse[];
    rulesInputs?: CharacterRulesInputStateResponse[];
    hitPointGains?: CharacterHitPointGainStateResponse[];
    profile?: CharacterProfileResponse | null;
    currencyBalances?: CharacterCurrencyBalanceResponse[];
    artAssets?: CharacterArtAssetResponse[];
}

export function buildCharacterStateBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    resource?: "progression" | "currency" | "profile" | "health" | "death-saves" | "inventory" | "notes" | "conditions",
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

export async function setCharacterAdvancementProgress(
    environment: HostEnvironment,
    characterId: string,
    value: number | null,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "progression"),
        "PUT",
        { value },
        "Unable to update Character advancement progress.");
}

export async function setCharacterCurrencyBalance(
    environment: HostEnvironment,
    characterId: string,
    key: string,
    amount: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "currency"),
        "PUT",
        { key, amount },
        "Unable to update Character currency.");
}

export async function removeCharacterCurrencyBalance(
    environment: HostEnvironment,
    characterId: string,
    key: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    const base = buildCharacterStateBackendUrl(environment, characterId, "currency");
    return await requestState(
        fetcher,
        `${base}?key=${encodeURIComponent(key)}`,
        "DELETE",
        undefined,
        "Unable to remove Character currency.");
}

export async function setCharacterProfile(
    environment: HostEnvironment,
    characterId: string,
    profile: CharacterProfileInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "profile"),
        "PUT",
        profile,
        "Unable to update Character details.");
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

export async function setCharacterDeathSaves(
    environment: HostEnvironment,
    characterId: string,
    successes: number,
    failures: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "death-saves"),
        "PUT",
        { successes, failures },
        "Unable to update Character death saves.");
}

export async function setCharacterRulesInput(
    environment: HostEnvironment,
    characterId: string,
    input: CharacterRulesInputStateInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/state/rules-inputs`),
        "PUT",
        input,
        "Unable to update Character rules input.");
}

export async function removeCharacterRulesInput(
    environment: HostEnvironment,
    characterId: string,
    kind: CharacterRulesInputKind,
    key: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    const base = `/api/characters/${encodeURIComponent(characterId)}/state/rules-inputs/${encodeURIComponent(kind)}`;
    return await requestState(
        fetcher,
        buildCharacterSheetApiUrl(
            environment,
            `${base}?key=${encodeURIComponent(key)}`),
        "DELETE",
        undefined,
        "Unable to clear Character rules input.");
}

export async function setCharacterHitPointGain(
    environment: HostEnvironment,
    characterId: string,
    advancementOccurrenceId: string,
    classLevel: number,
    hitDieValue: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/state/hit-point-gains/${encodeURIComponent(advancementOccurrenceId)}/${classLevel}`),
        "PUT",
        { hitDieValue },
        "Unable to update Character hit point gain.");
}

export async function removeCharacterHitPointGain(
    environment: HostEnvironment,
    characterId: string,
    advancementOccurrenceId: string,
    classLevel: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/state/hit-point-gains/${encodeURIComponent(advancementOccurrenceId)}/${classLevel}`),
        "DELETE",
        undefined,
        "Unable to clear Character hit point gain.");
}

export async function resolveCharacterRecovery(
    environment: HostEnvironment,
    characterId: string,
    procedureKey: string,
    input: CharacterRecoveryRequestInput = {},
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterRecoveryResponse> {
    const response = await fetcher(
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/state/recovery/${encodeURIComponent(procedureKey)}`),
        {
            method: "POST",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(input)
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to resolve Character recovery."),
            response.status);
    }
    return await response.json() as CharacterRecoveryResponse;
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

export async function addCustomInventoryItemOccurrence(
    environment: HostEnvironment,
    characterId: string,
    customName: string,
    quantity = 1,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "inventory"),
        "POST",
        { customName, quantity },
        "Unable to add custom inventory item.");
}

export async function updateInventoryItemOccurrence(
    environment: HostEnvironment,
    characterId: string,
    occurrenceId: string,
    input: CharacterInventoryItemOccurrenceStateInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterStateResponse> {
    return await requestState(
        fetcher,
        buildCharacterStateBackendUrl(environment, characterId, "inventory", occurrenceId),
        "PUT",
        input,
        "Unable to update inventory item.");
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
