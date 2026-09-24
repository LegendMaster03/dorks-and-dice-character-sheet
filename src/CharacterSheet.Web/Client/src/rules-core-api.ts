import type { FetchLike } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export interface ResolvedRulesCatalogResponse {
    scope: string;
    campaignId: string | null;
    revisionNumber: number | null;
    publishedAt: string | null;
    rules: ResolvedRuleCatalogItem[];
}

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

export function buildRulesCoreUrl(
    environment: HostEnvironment,
    apiPath: string,
    query?: URLSearchParams
): string {
    if (!apiPath.startsWith("/api/")) {
        throw new Error("Rules Core API paths must begin with /api/.");
    }

    let baseUrl: string;
    if (environment.embedded) {
        // Rules Core receives its own Site-issued Tool Host context. Never derive this from or
        // forward Character Sheet's Tool Host ticket/context path; tickets are Tool-scoped.
        baseUrl = "/tool-host/rules-core/api/upstream";
    } else {
        if (!environment.standaloneDevelopment || environment.rulesCoreDevelopmentBaseUrl === null) {
            throw new Error(
                "Standalone Rules Core access is available only through the development-only RulesCore:DevelopmentBaseUrl adapter.");
        }
        baseUrl = environment.rulesCoreDevelopmentBaseUrl;
    }

    const suffix = query !== undefined && query.size > 0 ? `?${query.toString()}` : "";
    return `${baseUrl}${apiPath}${suffix}`;
}

export async function searchResolvedRules(
    environment: HostEnvironment,
    entityType: string,
    search: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<ResolvedRulesCatalogResponse> {
    const query = new URLSearchParams();
    query.set("entityType", entityType);
    const normalizedSearch = search.trim();
    if (normalizedSearch.length > 0) {
        query.set("q", normalizedSearch);
    }
    query.set("limit", "200");

    const response = await fetcher(buildRulesCoreUrl(environment, "/api/rules", query), {
        method: "GET",
        headers: { Accept: "application/json" }
    });
    if (!response.ok) {
        throw new RulesCoreApiError(
            await readRulesCoreError(response, "Rules Core catalog is unavailable."),
            response.status);
    }

    return await response.json() as ResolvedRulesCatalogResponse;
}

export async function resolveRuleConcept(
    environment: HostEnvironment,
    conceptKey: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<ResolvedRuleDetail | null> {
    const normalizedKey = conceptKey.trim();
    if (normalizedKey.length === 0) {
        throw new Error("Rules Core concept key is required.");
    }

    const response = await fetcher(
        buildRulesCoreUrl(environment, `/api/rules/${encodeURIComponent(normalizedKey)}`),
        {
            method: "GET",
            headers: { Accept: "application/json" }
        });
    if (response.status === 404) {
        return null;
    }
    if (!response.ok) {
        throw new RulesCoreApiError(
            await readRulesCoreError(response, "Rules Core rule resolution failed."),
            response.status);
    }

    return await response.json() as ResolvedRuleDetail;
}

async function readRulesCoreError(response: Response, fallback: string): Promise<string> {
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

export async function loadHarvestingRules(
    environment: HostEnvironment,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<HarvestingRulesCatalogResponse> {
    return await requestRulesCoreJson<HarvestingRulesCatalogResponse>(
        environment,
        "/api/rules/harvesting",
        "GET",
        undefined,
        "Harvesting rules are unavailable.",
        fetcher);
}

export async function resolveHarvestingTable(
    environment: HostEnvironment,
    input: HarvestingTableResolutionInput,
    campaignId: string | null = null,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<HarvestingResolvedTableResponse> {
    const path = campaignId === null
        ? "/api/rules/harvesting/resolve"
        : `/api/campaigns/${encodeURIComponent(campaignId)}/rules/harvesting/resolve`;
    return await requestRulesCoreJson<HarvestingResolvedTableResponse>(
        environment,
        path,
        "POST",
        input,
        "Harvesting table resolution failed.",
        fetcher);
}

export async function resolveHarvestingOutcome(
    environment: HostEnvironment,
    input: HarvestingOutcomeInput,
    campaignId: string | null = null,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<HarvestingOutcomeResponse> {
    const path = campaignId === null
        ? "/api/rules/harvesting/outcome"
        : `/api/campaigns/${encodeURIComponent(campaignId)}/rules/harvesting/outcome`;
    return await requestRulesCoreJson<HarvestingOutcomeResponse>(
        environment,
        path,
        "POST",
        input,
        "Harvesting outcome resolution failed.",
        fetcher);
}

async function requestRulesCoreJson<T>(
    environment: HostEnvironment,
    path: string,
    method: "GET" | "POST",
    body: object | undefined,
    fallback: string,
    fetcher: FetchLike
): Promise<T> {
    const response = await fetcher(buildRulesCoreUrl(environment, path), {
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
        throw new RulesCoreApiError(
            await readRulesCoreError(response, fallback),
            response.status);
    }
    return await response.json() as T;
}
