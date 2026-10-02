import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "../../character-api.js";
import type { CharacterBuildResponse } from "../../builder-api.js";
import type { HostEnvironment } from "../../host-environment.js";

export interface CharacterClassAdvancementRequest {
    classAdvancementEntryId?: string | null;
    classConceptKey?: string | null;
    hitDieValue?: number | null;
    subclassConceptKey?: string | null;
}

export interface CharacterAdvancementChoiceOption {
    value: string;
    displayName: string;
    conceptKey?: string | null;
}

export interface CharacterAdvancementChoice {
    choiceKey: string;
    groupKey: string;
    displayName: string;
    kind: string;
    state: string;
    options: CharacterAdvancementChoiceOption[];
    selectedValue?: string | null;
    sourceConceptKey?: string | null;
}

export interface CharacterAdvancementPrerequisiteRequirement {
    requirementKey: string;
    kind: string;
    targetKey?: string | null;
    operator?: string | null;
    numericValue?: number | null;
    textValue?: string | null;
    satisfied?: boolean | null;
    state: string;
    reason?: string | null;
    groupKey?: string | null;
    groupMatchCount?: number;
}

export interface CharacterAdvancementPrerequisite {
    conceptKey: string;
    state: string;
    satisfied?: boolean | null;
    requirements: CharacterAdvancementPrerequisiteRequirement[];
}

export interface CharacterAdvancementConflict {
    conflictKey: string;
    kind: string;
    message: string;
    relatedMechanicKeys: string[];
    relatedConceptKeys: string[];
}

export interface CharacterAdvancementChange {
    kind: string;
    label: string;
    detail?: string | null;
}

export interface CharacterAdvancementEligibility {
    candidateConceptKey: string;
    candidateDisplayName: string;
    candidateEntityType: string;
    eligible?: boolean | null;
    state: string;
    prerequisites?: CharacterAdvancementPrerequisite | null;
    conflicts: CharacterAdvancementConflict[];
}

export interface CharacterAdvancementPlan {
    operation: string;
    classAdvancementEntryId: string;
    classConceptKey: string;
    classDisplayName: string;
    startingClass: boolean;
    currentClassLevel: number;
    targetClassLevel: number;
    currentCharacterLevel: number;
    targetCharacterLevel: number;
    hitPointGainRequired: boolean;
    hitDieValue?: number | null;
    status: string;
    canApply: boolean;
    prerequisites: CharacterAdvancementPrerequisite[];
    requiredChoices: CharacterAdvancementChoice[];
    blockingConflicts: CharacterAdvancementConflict[];
    changes: CharacterAdvancementChange[];
    subclassConceptKey?: string | null;
    subclassDisplayName?: string | null;
    subclassEligibility?: CharacterAdvancementEligibility | null;
}

export interface CharacterAdvancementApplyResponse {
    plan: CharacterAdvancementPlan;
    build: CharacterBuildResponse;
}

export function buildCharacterAdvancementBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    operation: "preview" | "apply"
): string {
    return buildCharacterSheetApiUrl(
        environment,
        `/api/characters/${encodeURIComponent(characterId)}/advancement/${operation}`);
}

export async function previewCharacterAdvancement(
    environment: HostEnvironment,
    characterId: string,
    request: CharacterClassAdvancementRequest,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterAdvancementPlan> {
    const response = await fetcher(
        buildCharacterAdvancementBackendUrl(environment, characterId, "preview"),
        {
            method: "POST",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(request)
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to preview Character advancement."),
            response.status);
    }
    return await response.json() as CharacterAdvancementPlan;
}

export async function applyCharacterAdvancement(
    environment: HostEnvironment,
    characterId: string,
    request: CharacterClassAdvancementRequest,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CharacterAdvancementApplyResponse> {
    const response = await fetcher(
        buildCharacterAdvancementBackendUrl(environment, characterId, "apply"),
        {
            method: "POST",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(request)
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Unable to apply Character advancement."),
            response.status);
    }
    return await response.json() as CharacterAdvancementApplyResponse;
}
