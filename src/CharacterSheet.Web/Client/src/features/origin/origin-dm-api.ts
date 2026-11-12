/**
 * Restricted campaign-DM Origin actions. These are not owner-scoped builder
 * reads, and neither preview offers nor client selections authorize grants.
 */
import { buildCharacterSheetApiUrl, CharacterSheetApiError,
    readApiError, type FetchLike } from "../../character-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { NativeOriginActivationResponse } from "./origin-api.js";

export interface MixedOriginDmCandidate {
    id: string;
    contributorId: string;
    sourceId: "species.human" | "species.elf";
    occurrencePath: string;
    kind: string;
    intrinsicSlotId: string | null;
    offeredValue: unknown;
    requiresChoices: boolean;
}

export interface MixedOriginDmReview {
    status: "ready";
    draftRevision: number;
    rulesetId: string;
    fingerprint: string;
    proposalId: string;
    candidates: readonly MixedOriginDmCandidate[];
    previewOnly: true;
    activeGrants: false;
}

function object(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validateId(id: string, label: string): void {
    if (!guidPattern.test(id) ||
        /^00000000-0000-0000-0000-000000000000$/i.test(id)) {
        throw new Error(label + " requires a valid Site UUID.");
    }
}

async function requestDm(
    environment: HostEnvironment, characterId: string, campaignId: string,
    action: "mixed-review" | "activate-mixed-approved",
    method: "GET" | "POST", body: object | undefined, fetcher: FetchLike
): Promise<unknown> {
    validateId(characterId, "DM target character");
    validateId(campaignId, "Campaign scope");
    const path = `/api/characters/${encodeURIComponent(characterId)}/origin/${action}` +
        (method === "GET" ? `?campaignId=${encodeURIComponent(campaignId)}` : "");
    const response = await fetcher(buildCharacterSheetApiUrl(environment, path), {
        method, credentials: "same-origin", cache: "no-store",
        headers: body === undefined
            ? { Accept: "application/json" }
            : { Accept: "application/json", "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Mixed Origin DM operation was denied."),
            response.status);
    }
    try {
        return await response.json() as unknown;
    } catch {
        throw new CharacterSheetApiError("Mixed Origin DM returned invalid JSON.", response.status);
    }
}

/**
 * Site checks the actor's current DM role and the exact active
 * campaign–character association before reading the owner's draft.
 * Offered candidates are not already approved or active grants.
 */
export async function reviewDmMixedOrigin(
    environment: HostEnvironment, characterId: string, campaignId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<MixedOriginDmReview> {
    const response = await requestDm(environment, characterId, campaignId,
        "mixed-review", "GET", undefined, fetcher);
    if (!object(response) || response.status !== "ready" ||
        response.previewOnly !== true || response.activeGrants !== false ||
        !Number.isSafeInteger(response.draftRevision) ||
        (response.draftRevision as number) < 1 ||
        typeof response.rulesetId !== "string" || !response.rulesetId ||
        typeof response.fingerprint !== "string" || !response.fingerprint ||
        typeof response.proposalId !== "string" || !response.proposalId ||
        !Array.isArray(response.candidates) || response.candidates.length > 256 ||
        Object.hasOwn(response, "profile") || Object.hasOwn(response, "grants") ||
        Object.hasOwn(response, "approvedDecisions")) {
        throw new Error("Mixed Origin DM review violated its read-only contract.");
    }
    const seen = new Set<string>();
    for (const offer of response.candidates) {
        if (!object(offer) || typeof offer.id !== "string" || !offer.id ||
            seen.has(offer.id) || typeof offer.contributorId !== "string" ||
            !offer.contributorId || (offer.sourceId !== "species.human" &&
            offer.sourceId !== "species.elf") ||
            typeof offer.occurrencePath !== "string" ||
            !offer.occurrencePath.startsWith("/") ||
            typeof offer.kind !== "string" ||
            !(offer.intrinsicSlotId === null ||
                typeof offer.intrinsicSlotId === "string") ||
            typeof offer.requiresChoices !== "boolean" ||
            Object.hasOwn(offer, "grant") || Object.hasOwn(offer, "authorization")) {
            throw new Error("Mixed Origin DM candidate violated its offer-only contract.");
        }
        seen.add(offer.id);
    }
    return response as unknown as MixedOriginDmReview;
}

/**
 * The browser submits only selected offer IDs and a reviewed revision.
 * The server rechecks Site authorization, fresh catalog content and the
 * immutable owner draft, then creates scoped decisions itself.
 * This activation currently supports only initial Human–Elf mixed Origins.
 */
export async function activateDmApprovedMixedOrigin(
    environment: HostEnvironment, characterId: string, campaignId: string,
    expectedDraftRevision: number, candidateIds: readonly string[],
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginActivationResponse> {
    if (!Number.isSafeInteger(expectedDraftRevision) || expectedDraftRevision < 1 ||
        !Array.isArray(candidateIds) || candidateIds.length < 1 ||
        candidateIds.length > 32 ||
        candidateIds.some(id => typeof id !== "string" || !id.trim() || id.length > 180) ||
        new Set(candidateIds).size !== candidateIds.length) {
        throw new Error("Mixed Origin DM activation requires a distinct, reviewed offer selection.");
    }
    const response = await requestDm(environment, characterId, campaignId,
        "activate-mixed-approved", "POST",
        { campaignId, expectedDraftRevision, candidateIds }, fetcher);
    if (!object(response) || response.status !== "applied" ||
        !Number.isSafeInteger(response.revision) ||
        (response.revision as number) < 1 ||
        response.effectiveMechanicsReady !== false) {
        throw new Error("Mixed Origin DM activation returned an invalid trust contract.");
    }
    return response as unknown as NativeOriginActivationResponse;
}
