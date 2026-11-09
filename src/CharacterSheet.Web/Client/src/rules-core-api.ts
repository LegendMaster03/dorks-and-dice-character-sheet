/**
 * Defines the Rules Core-related transport contracts used by sheet workflows. Responses are
 * resolved upstream and are not local rule computations.
 */
import { buildCharacterSheetApiUrl, readApiError, type FetchLike } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

// The old /api/rules application interface is no longer part of Rules Core.
// Harvesting shapes are retained for the prior guided workflow while
// transport now uses the native authenticated mechanics contract.
// Data-only relationship shape retained for authored selection compatibility checks.
// It no longer represents a callable Rules Core API.
export interface ResolvedRuleRelationship {
    kind: string;
    relatedRuleConceptId: string;
    relatedConceptKey: string;
    relatedEntityType: string;
    relatedDisplayName: string;
}

export interface ResolvedRuleCatalogItem {
    ruleConceptId: string;
    conceptKey: string;
    entityType: string;
    displayName: string;
    effectiveDecisionKind: string;
    hasCampaignOverride: boolean;
    sourceEntityId: string;
    sourceEntityRevisionId: string;
    sourceRevisionNumber: number;
    sourceEntityName: string;
    sourceCode: string;
    packageKey: string;
    packageDisplayName: string;
    editionKey: string;
    editionDisplayName: string;
    relationships: ResolvedRuleRelationship[];
}

export interface ResolvedRuleDetail {
    ruleConceptId: string;
    conceptKey: string;
    entityType: string;
    displayName: string;
    sourceEntityName: string;
    sourceCode: string;
    packageKey: string;
    packageDisplayName: string;
    editionKey: string;
    editionDisplayName: string;
}

export class RulesCoreApiError extends Error {
    constructor(message: string, public readonly status: number) {
        super(message);
        this.name = "RulesCoreApiError";
    }
}

export interface HarvestingSourceResponse {
    workKey: string;
    workDisplayName: string;
    provider: string;
    gameEdition: string;
    releaseKind: string;
    publicationDate: string;
    referenceUri: string;
}

export interface HarvestingComponentResponse {
    key: string;
    displayName: string;
    componentDc: number;
    quantity: number | null;
    origin: string | null;
}

export interface HarvestingCreatureTypeResponse {
    key: string;
    displayName: string;
    competencyKey: string;
    competencyDisplayName: string;
    defaultComponents: HarvestingComponentResponse[];
}

export interface HarvestingProcedureCheckResponse {
    mechanicKey: string;
    abilityKey: string;
    abilityDisplayName: string;
    defaultRollMode: string;
}

export interface HarvestingProcedureResponse {
    assessment: HarvestingProcedureCheckResponse;
    carving: HarvestingProcedureCheckResponse;
    totalMechanicKey: string;
    sameActorRollMode: string;
    componentDcAggregation: string;
    awardMode: string;
    helpers: {
        maximumByCreatureSize: Record<string, number>;
        standardHelpActionApplies: boolean;
    };
}

export interface HarvestingRulesCatalogResponse {
    source: HarvestingSourceResponse;
    creatureTypes: HarvestingCreatureTypeResponse[];
    procedure: HarvestingProcedureResponse;
}

export interface HarvestingComponentEditInput {
    key: string;
    displayName?: string | null;
    componentDc?: number | null;
    quantity?: number | null;
}

export interface HarvestingTableResolutionInput {
    creatureConceptKey?: string | null;
    creatureType?: string | null;
    manualEdits?: {
        removeComponentKeys?: string[] | null;
        upsertComponents?: HarvestingComponentEditInput[] | null;
    } | null;
}

export interface HarvestingResolvedTableResponse {
    source: HarvestingSourceResponse;
    creatureType: string;
    creatureTypeDisplayName: string;
    competencyKey: string;
    competencyDisplayName: string;
    components: HarvestingComponentResponse[];
    creatureConceptKey: string | null;
    creatureDisplayName: string | null;
    creatureSize: string | null;
    creatureOverridesApplied: boolean;
    manualEditsApplied: boolean;
}

export interface HarvestingHelperInput {
    proficiencyBonus: number;
    isProficient: boolean;
    participatedForEntireDuration?: boolean;
    isAssessmentParticipant?: boolean;
    isCarvingParticipant?: boolean;
}

export interface HarvestingOutcomeInput {
    table: HarvestingTableResolutionInput;
    assessmentResult: number;
    carvingResult: number;
    sameActor: boolean;
    harvestOrderComponentKeys: string[];
    creatureSize?: string | null;
    helpers?: HarvestingHelperInput[] | null;
}

export interface HarvestingComponentOutcomeResponse {
    key: string;
    displayName: string;
    componentDc: number;
    harvestDc: number;
    quantity: number | null;
    origin: string | null;
    awarded: boolean;
}

export interface HarvestingOutcomeResponse {
    table: HarvestingResolvedTableResponse;
    assessmentRollMode: string;
    carvingRollMode: string;
    creatureSize: string | null;
    assessmentResult: number;
    carvingResult: number;
    helperCount: number;
    helperContribution: number;
    harvestingResult: number;
    components: HarvestingComponentOutcomeResponse[];
}


// Harvesting tables and outcomes are served by the authenticated native
// mechanics gateway. Each request pins only the snapshot used for that one
// operation; a ruleset content revision never requires client redeployment.
type HarvestFetcher = FetchLike;
interface HarvestingSnapshot {
    rulesetId: string;
    fingerprint: string;
}

async function requestHarvestingJson(
    environment: HostEnvironment,
    path: string,
    fetcher: HarvestFetcher,
    body?: object
): Promise<unknown> {
    const response = await fetcher(buildCharacterSheetApiUrl(environment, path), {
        method: body === undefined ? "GET" : "POST",
        credentials: "same-origin",
        headers: body === undefined
            ? { Accept: "application/json" }
            : { Accept: "application/json", "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    if (!response.ok) {
        const fallback = response.status === 409
            ? "The active ruleset changed. Retry the Harvesting operation."
            : "Native Harvesting is unavailable for this request.";
        throw new RulesCoreApiError(await readApiError(response, fallback), response.status);
    }
    return await response.json();
}

async function activeHarvestingSnapshot(
    environment: HostEnvironment, fetcher: HarvestFetcher
): Promise<HarvestingSnapshot> {
    const raw = await requestHarvestingJson(
        environment, "/api/native-rules/ruleset", fetcher);
    const metadata = raw as {
        contractVersion?: number;
        rulesetId?: string;
        fingerprint?: string;
        capabilities?: { harvestingResolution?: string };
    };
    if (metadata.contractVersion !== 1 ||
        !metadata.rulesetId ||
        !/^[0-9a-f]{64}$/.test(metadata.fingerprint ?? "") ||
        metadata.capabilities?.harvestingResolution !== "type-monster-manual-edits") {
        throw new RulesCoreApiError(
            "The active Rules Core does not advertise native Harvesting.", 503);
    }
    return {
        rulesetId: metadata.rulesetId,
        fingerprint: metadata.fingerprint!
    };
}

function ensureGlobalHarvesting(campaignId: string | null): void {
    if (campaignId !== null) {
        throw new RulesCoreApiError(
            "Campaign-specific Harvesting overrides are not available in the native rules contract.", 501);
    }
}

export async function loadHarvestingRules(
    environment: HostEnvironment,
    fetcher: HarvestFetcher = globalThis.fetch.bind(globalThis)
): Promise<HarvestingRulesCatalogResponse> {
    const snapshot = await activeHarvestingSnapshot(environment, fetcher);
    const data = await requestHarvestingJson(environment,
        "/api/native-rules/harvesting?fingerprint=" +
            encodeURIComponent(snapshot.fingerprint), fetcher);
    const catalog = data as HarvestingRulesCatalogResponse & HarvestingSnapshot & {
        contractVersion: number;
    };
    if (catalog.contractVersion !== 1 ||
        catalog.rulesetId !== snapshot.rulesetId ||
        catalog.fingerprint !== snapshot.fingerprint ||
        !Array.isArray(catalog.creatureTypes) ||
        catalog.source?.provider !== "Loot Tavern" ||
        !catalog.procedure) {
        throw new RulesCoreApiError("Native Harvesting catalog is invalid.", 502);
    }
    return catalog;
}

export async function resolveHarvestingTable(
    environment: HostEnvironment,
    input: HarvestingTableResolutionInput,
    campaignId: string | null = null,
    fetcher: HarvestFetcher = globalThis.fetch.bind(globalThis)
): Promise<HarvestingResolvedTableResponse> {
    ensureGlobalHarvesting(campaignId);
    const snapshot = await activeHarvestingSnapshot(environment, fetcher);
    const table = await requestHarvestingJson(environment,
        "/api/native-rules/harvesting/resolve", fetcher, { ...input, ...snapshot });
    if (!Array.isArray((table as HarvestingResolvedTableResponse)?.components)) {
        throw new RulesCoreApiError("Native Harvesting table is invalid.", 502);
    }
    return table as HarvestingResolvedTableResponse;
}

export async function resolveHarvestingOutcome(
    environment: HostEnvironment,
    input: HarvestingOutcomeInput,
    campaignId: string | null = null,
    fetcher: HarvestFetcher = globalThis.fetch.bind(globalThis)
): Promise<HarvestingOutcomeResponse> {
    ensureGlobalHarvesting(campaignId);
    const snapshot = await activeHarvestingSnapshot(environment, fetcher);
    const result = await requestHarvestingJson(environment,
        "/api/native-rules/harvesting/outcome", fetcher,
        { ...input, table: { ...input.table, ...snapshot } });
    if (!Array.isArray((result as HarvestingOutcomeResponse)?.components) ||
        !Number.isFinite((result as HarvestingOutcomeResponse)?.harvestingResult)) {
        throw new RulesCoreApiError("Native Harvesting outcome is invalid.", 502);
    }
    return result as HarvestingOutcomeResponse;
}
