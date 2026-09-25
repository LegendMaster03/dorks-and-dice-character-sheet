import {
    CharacterSheetApiError,
    buildCharacterSheetApiUrl,
    readApiError,
    type FetchLike
} from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export interface CraftingManualCompetencyInput {
    displayName: string;
    contribution: number;
    isQualified: boolean;
}

export interface CraftingCompetencyInput {
    competencyKey?: string | null;
    manual?: CraftingManualCompetencyInput | null;
}

export interface ManufacturingResolutionInput {
    competency: CraftingCompetencyInput;
    campaignId?: string | null;
    hasQualifiedGuidance?: boolean;
    d20Roll?: number | null;
    otherModifier?: number;
    targetDc?: number | null;
}

export interface EnchantingResolutionInput {
    campaignId?: string | null;
    creatureType?: string | null;
    competency?: CraftingCompetencyInput | null;
    spellcastingKey?: string | null;
    d20Roll?: number | null;
    otherModifier?: number;
    targetDc?: number | null;
}

export interface CraftingCheckResolutionResponse {
    procedureKey: string;
    displayName: string;
    competencyKey: string;
    competencyDisplayName: string;
    manualCompetency: boolean;
    isQualified: boolean;
    rollMode: string;
    competencyContribution: number;
    abilityContribution: number;
    otherModifier: number;
    d20Roll: number | null;
    total: number | null;
    targetDc: number | null;
    meetsTarget: boolean | null;
    outcome: "pending" | "completed" | "failed" | "completed-with-flaws" | "destroyed" | string;
    margin: number | null;
    flawCount: number | null;
    inputsConsumed: boolean;
    producesFunctionalOutput: boolean;
}

export async function resolveCharacterManufacturing(
    environment: HostEnvironment,
    characterId: string,
    input: ManufacturingResolutionInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CraftingCheckResolutionResponse> {
    return await resolveCrafting(
        environment,
        characterId,
        "manufacturing",
        input,
        fetcher);
}

export async function resolveCharacterEnchanting(
    environment: HostEnvironment,
    characterId: string,
    input: EnchantingResolutionInput,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CraftingCheckResolutionResponse> {
    return await resolveCrafting(
        environment,
        characterId,
        "enchanting",
        input,
        fetcher);
}

async function resolveCrafting(
    environment: HostEnvironment,
    characterId: string,
    procedure: "manufacturing" | "enchanting",
    input: object,
    fetcher: FetchLike
): Promise<CraftingCheckResolutionResponse> {
    const response = await fetcher(
        buildCharacterSheetApiUrl(
            environment,
            `/api/characters/${encodeURIComponent(characterId)}/crafting/${procedure}/resolve`),
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
            await readApiError(response, "Crafting resolution failed."),
            response.status);
    }

    return await response.json() as CraftingCheckResolutionResponse;
}
