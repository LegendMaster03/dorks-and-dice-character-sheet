import type { FetchLike } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

export interface ResolvedRulesCatalogResponse {
    scope: string;
    campaignId: string | null;
    revisionNumber: number | null;
    publishedAt: string | null;
    rules: ResolvedRuleCatalogItem[];
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
