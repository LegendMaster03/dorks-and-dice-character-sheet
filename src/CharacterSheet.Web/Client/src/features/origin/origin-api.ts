/**
 * Origin draft and saved-state operations are Character Sheet operations.
 * Rules Core owns eligibility; the browser never submits a prior profile,
 * independently earned grants, completed-event history, or DM authority.
 */
import {
    buildCharacterSheetApiUrl,
    CharacterSheetApiError,
    readApiError,
    type FetchLike
} from "../../character-api.js";
import type { HostEnvironment } from "../../host-environment.js";

export interface NativeOriginDraftProposal {
    id: string;
    intent: "Direct" | "Mixed" | "Custom" | "Transformation" | 0 | 1 | 2 | 3;
    method: { ruleId: string; methodId: string };
    contributors: readonly unknown[];
    candidates: readonly unknown[];
    manifestations: readonly NativeOriginDraftManifestation[];
    selectedSpeciesId?: string | null;
    selectedAncestryId?: string | null;
    [key: string]: unknown;
}

export interface NativeOriginDraftChoice {
    requirementId: string;
    selectedOptionIds: readonly string[];
    // No CatalogEligibilityEvidenceId here. Only Rules Core may mint
    // first-party catalog proof after checking the current native source.
}

export interface NativeOriginDraftManifestation {
    id: string;
    candidateId: string;
    selectedValue?: unknown;
    choices?: readonly NativeOriginDraftChoice[];
    selectedParameters?: Readonly<Record<string, unknown>>;
}

export interface NativeOriginDraftView {
    characterId: string;
    rulesetId: string;
    fingerprint: string | null;
    proposal: NativeOriginDraftProposal;
    revision: number;
    updatedAt: string;
    active: false;
}

export interface NativeOriginContractIssue {
    code: string;
    path: string;
    severity: "Incomplete" | "Unresolved" | "Conflict" | "Invalid" | number;
    detail: string;
}

export interface NativeOriginPreview {
    contractVersion: 1;
    rulesetId: string;
    fingerprint: string;
    contentChangedSinceRequest: boolean;
    previewOnly: true;
    result: {
        status: "Invalid" | "Incomplete" | "Unresolved" | "Conflicting" | "Authorized" | number;
        evaluatedProposal: unknown;
        profile: unknown;
        issues: NativeOriginContractIssue[];
    };
}

export interface NativeOriginDraftResponse {
    status: "ready";
    draft: NativeOriginDraftView | null;
    preview?: NativeOriginPreview | null;
    activeGrants: false;
}

export interface NativeOriginSavedState {
    characterId: string;
    rulesetId: string;
    fingerprint: string;
    profile: unknown;
    revision: number;
    sourceDraftRevision: number | null;
    updatedAt: string;
}

export interface NativeOriginCompletedEventSummary {
    eventId: string;
    committedRevision: number;
    methodRuleId: string;
    methodId: string;
    createdAt: string;
}

export interface NativeOriginStateResponse {
    status: "ready";
    savedState: NativeOriginSavedState | null;
    events: NativeOriginCompletedEventSummary[] | null;
    readOnly: boolean;
    effectiveMechanicsReady: false;
}

/**
 * A stored parent snapshot is useful for inspection by the same owner, but
 * does not establish the actual parent relationship, ruleset freshness,
 * eligibility or an inheritable grant list.
 */
export interface NativeOriginParentSnapshotResponse {
    status: "ready";
    snapshot: {
        parentCharacterId: string;
        rulesetId: string;
        fingerprint: string;
        revision: number;
        snapshotUpdatedAt: string;
        underlying: {
            speciesId: string | null;
            ancestryId: string | null;
            profileId: string | null;
        };
        current: {
            speciesId: string | null;
            ancestryId: string | null;
            profileId: string | null;
        };
    };
    inspectionOnly: true;
    inheritanceAuthorized: false;
    effectiveMechanicsReady: false;
}

/**
 * Fresh verification of only a committed direct native initial Origin.
 * It is still not biological parentage, inheritability, a campaign ruling,
 * or a verification of independently earned/mechanically effective grants.
 */
export interface NativeOriginParentNativeReviewResponse {
    status: "ready";
    review: {
        parentCharacterId: string;
        rulesetId: string;
        storedRevision: number;
        latestNativeFingerprint: string;
        verifiedProposalId: string;
    };
    inspectedOriginMatchesLatestNativeCatalog: true;
    inspectionOnly: true;
    inheritanceAuthorized: false;
    effectiveMechanicsReady: false;
}

/**
 * Site returns the current user's own Character summaries. No other owner's
 * private sheet or Origin profile is loaded. Character Sheet only uses these
 * identities as temporary candidate bindings for native-source comparison.
 */
export interface OwnerOriginParentOption {
    id: string;
    name: string;
    archived: boolean;
}

export async function loadOwnerOriginParentOptions(
    environment: HostEnvironment,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<readonly OwnerOriginParentOption[]> {
    if (!environment.embedded) {
        throw new Error("Owned Character selection requires the authenticated Dorks & Dice Site.");
    }
    const response = await fetcher("/characters/api?includeArchived=true", {
        method: "GET", credentials: "same-origin", cache: "no-store",
        headers: { Accept: "application/json" }
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "The Site owner Character list is unavailable."),
            response.status);
    }
    let data: unknown;
    try {
        data = await response.json() as unknown;
    } catch {
        throw new Error("The Site Character list returned invalid JSON.");
    }
    const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!Array.isArray(data) || !data.every(item =>
        isObject(item) && typeof item.id === "string" && guid.test(item.id) &&
        typeof item.name === "string" &&
        item.name.trim().length > 0 && item.name.length <= 160 &&
        (item.status === "Active" || item.status === "Archived")) ||
        new Set(data.map(item => (item as Record<string, unknown>).id
            ?.toString().toLowerCase())).size !== data.length) {
        throw new Error("The Site Character list returned an invalid owner identity contract.");
    }
    // Deliberately narrow the Site response before any Origin UI sees it.
    return data.map(item => ({
        id: item.id as string,
        name: item.name as string,
        archived: item.status === "Archived"
    }));
}

/** Proposed links only. These IDs never establish biological parentage. */
export interface NativeOriginParentSourceBinding {
    contributorId: string;
    parentCharacterId: string;
}

export interface NativeOriginParentSourceAlignmentResponse {
    status: "ready";
    draftRevision: number;
    matches: readonly {
        contributorId: string;
        parentCharacterId: string;
        nativeSourceId: string;
        parentOriginRevision: number;
    }[];
    inspectionOnly: true;
    relationshipVerified: false;
    inheritanceAuthorized: false;
    activeGrants: false;
    effectiveMechanicsReady: false;
}

export interface NativeOriginActivationResponse {
    status: "applied";
    revision: number;
    effectiveMechanicsReady: false;
}

function originPath(characterId: string,
    suffix: "draft" | "state" | "activate-native" | `parent-snapshot/${string}` | `parent-native-review/${string}` | "parent-source-alignment-review"): string {
    if (characterId.trim().length === 0) {
        throw new Error("A Site Character ID is required for Origin state.");
    }
    return `/api/characters/${encodeURIComponent(characterId)}/origin/${suffix}`;
}

export function originBackendUrl(
    environment: HostEnvironment,
    characterId: string,
    suffix: "draft" | "state" | "activate-native" | `parent-snapshot/${string}` | `parent-native-review/${string}` | "parent-source-alignment-review"
): string {
    return buildCharacterSheetApiUrl(environment, originPath(characterId, suffix));
}

async function requestOrigin(
    environment: HostEnvironment,
    characterId: string,
    suffix: "draft" | "state" | "activate-native" | `parent-snapshot/${string}` | `parent-native-review/${string}` | "parent-source-alignment-review",
    method: "GET" | "PUT" | "POST",
    body: object | undefined,
    fetcher: FetchLike
): Promise<unknown> {
    const response = await fetcher(originBackendUrl(environment, characterId, suffix), {
        method,
        credentials: "same-origin",
        cache: "no-store",
        headers: body === undefined
            ? { Accept: "application/json" }
            : { Accept: "application/json", "Content-Type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body)
    });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Origin operation could not be completed."),
            response.status);
    }
    try {
        return await response.json() as unknown;
    } catch {
        throw new CharacterSheetApiError("Origin returned an invalid response.", response.status);
    }
}

function isObject(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

function assertDraftResponse(value: unknown): asserts value is NativeOriginDraftResponse {
    if (!isObject(value) || value.status !== "ready" ||
        value.activeGrants !== false ||
        !(value.draft === null || isObject(value.draft)) ||
        (isObject(value.draft) && (
            value.draft.active !== false ||
            !Number.isSafeInteger(value.draft.revision) ||
            (value.draft.revision as number) < 1)) ||
        !(value.preview === undefined || value.preview === null ||
            isObject(value.preview) && value.preview.previewOnly === true)) {
        throw new Error("The Origin draft endpoint returned an invalid trust contract.");
    }
}

function assertStateResponse(value: unknown): asserts value is NativeOriginStateResponse {
    if (!isObject(value) || value.status !== "ready" ||
        value.effectiveMechanicsReady !== false ||
        typeof value.readOnly !== "boolean" ||
        !(value.savedState === null || isObject(value.savedState)) ||
        !(value.events === null || Array.isArray(value.events))) {
        throw new Error("The Origin state endpoint returned an invalid trust contract.");
    }
}

/** Preview is stateless. It never refreshes or commits authorized state. */
export async function previewOriginProposal(
    environment: HostEnvironment,
    rulesetId: string,
    fingerprint: string | null,
    proposal: NativeOriginDraftProposal,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginPreview> {
    const response = await fetcher(
        buildCharacterSheetApiUrl(environment, "/api/native-rules/origins/preview"), {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store",
            headers: { Accept: "application/json", "Content-Type": "application/json" },
            body: JSON.stringify({ rulesetId, fingerprint, proposal })
        });
    if (!response.ok) {
        throw new CharacterSheetApiError(
            await readApiError(response, "Native Origin preview is unavailable."),
            response.status);
    }
    const result: unknown = await response.json();
    if (!isObject(result) || result.previewOnly !== true ||
        result.rulesetId !== rulesetId || typeof result.fingerprint !== "string" ||
        !isObject(result.result) || !Array.isArray(result.result.issues)) {
        throw new Error("Rules Core did not return a stateless Origin preview.");
    }
    return result as unknown as NativeOriginPreview;
}

export async function loadOriginDraft(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginDraftResponse> {
    const result = await requestOrigin(environment, characterId, "draft", "GET",
        undefined, fetcher);
    assertDraftResponse(result);
    return result;
}

export async function saveOriginDraft(
    environment: HostEnvironment,
    characterId: string,
    rulesetId: string,
    fingerprint: string | null,
    proposal: NativeOriginDraftProposal,
    expectedRevision: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginDraftResponse> {
    if (!Number.isSafeInteger(expectedRevision) || expectedRevision < 0 ||
        rulesetId.trim().length === 0 ||
        !proposal || typeof proposal.id !== "string" ||
        !Array.isArray(proposal.contributors) ||
        !Array.isArray(proposal.candidates) ||
        !Array.isArray(proposal.manifestations)) {
        throw new Error("The Origin draft has an invalid proposal or revision.");
    }

    // Accept only raw player selections: the saved proposal does not carry
    // previously authorized state, table decisions, or catalog-proof receipts.
    // The backend independently enforces these restrictions as well.
    const forbidden = [
        "existingConfiguration", "existingEffectiveConfiguration",
        "existingEffectiveConfigurationBasis", "activeTemporaryTransformation",
        "otherActiveTemporaryTransformations", "temporaryTransformationPrecedence",
        "tableDecisions", "history"
    ];
    if (forbidden.some(field =>
        Object.hasOwn(proposal, field) &&
        proposal[field] !== null && proposal[field] !== undefined) ||
        proposal.manifestations.some((manifestation: NativeOriginDraftManifestation) =>
            manifestation.choices?.some((choice: NativeOriginDraftChoice) =>
                Object.hasOwn(choice, "catalogEligibilityEvidenceId")))) {
        throw new Error("The Origin draft contains server-only state or authority.");
    }

    const result = await requestOrigin(environment, characterId, "draft", "PUT",
        { rulesetId, fingerprint, proposal, expectedRevision }, fetcher);
    assertDraftResponse(result);
    return result;
}

export async function loadOriginState(
    environment: HostEnvironment,
    characterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginStateResponse> {
    const result = await requestOrigin(environment, characterId, "state", "GET",
        undefined, fetcher);
    assertStateResponse(result);
    return result;
}

/** Read metadata for a second character after Site independently checks both owners. */
export async function inspectOwnerVisibleParentOrigin(
    environment: HostEnvironment,
    characterId: string,
    parentCharacterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginParentSnapshotResponse> {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(parentCharacterId)) {
        throw new Error("Parent snapshot inspection requires a Site Character ID.");
    }
    const suffix = `parent-snapshot/${encodeURIComponent(parentCharacterId)}` as const;
    const result = await requestOrigin(environment, characterId,
        suffix, "GET", undefined, fetcher);
    if (!isObject(result) || result.status !== "ready" ||
        result.inspectionOnly !== true || result.inheritanceAuthorized !== false ||
        result.effectiveMechanicsReady !== false ||
        !isObject(result.snapshot) ||
        result.snapshot.parentCharacterId !== parentCharacterId ||
        !Number.isSafeInteger(result.snapshot.revision) ||
        (result.snapshot.revision as number) < 1 ||
        !isObject(result.snapshot.underlying) || !isObject(result.snapshot.current) ||
        Object.hasOwn(result.snapshot, "independentGrants") ||
        Object.hasOwn(result.snapshot.underlying, "inherentGrants") ||
        Object.hasOwn(result.snapshot.current, "inherentGrants")) {
        throw new Error("Parent Origin inspection returned an invalid trust contract.");
    }
    return result as unknown as NativeOriginParentSnapshotResponse;
}

/**
 * Reviews an owner-visible parent's original construction through the
 * current native Rules Core. This does not attach the parent to any draft.
 */
export async function reviewOwnerVisibleParentNativeOrigin(
    environment: HostEnvironment,
    characterId: string,
    parentCharacterId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginParentNativeReviewResponse> {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(parentCharacterId)) {
        throw new Error("Parent native review requires a Site Character ID.");
    }
    const suffix = `parent-native-review/${encodeURIComponent(parentCharacterId)}` as const;
    const result = await requestOrigin(environment, characterId,
        suffix, "GET", undefined, fetcher);
    if (!isObject(result) || result.status !== "ready" ||
        result.inspectedOriginMatchesLatestNativeCatalog !== true ||
        result.inspectionOnly !== true || result.inheritanceAuthorized !== false ||
        result.effectiveMechanicsReady !== false || !isObject(result.review) ||
        result.review.parentCharacterId !== parentCharacterId ||
        !Number.isSafeInteger(result.review.storedRevision) ||
        (result.review.storedRevision as number) < 1 ||
        typeof result.review.rulesetId !== "string" ||
        typeof result.review.latestNativeFingerprint !== "string" ||
        typeof result.review.verifiedProposalId !== "string" ||
        Object.hasOwn(result.review, "grants") ||
        Object.hasOwn(result.review, "profile") ||
        Object.hasOwn(result.review, "independentGrants")) {
        throw new Error("Parent native Origin review returned an invalid trust contract.");
    }
    return result as unknown as NativeOriginParentNativeReviewResponse;
}

/**
 * Inspect a complete list of individual-parent/native-source associations.
 * Only Site-owner-approved IDs, a previously saved mixed draft, and fresh
 * Rules Core evaluations can satisfy this read-only request. Nothing here
 * authorizes an actual parent-child relationship or an inherited grant.
 */
export async function reviewOwnerVisibleParentSourceAlignment(
    environment: HostEnvironment,
    characterId: string,
    expectedDraftRevision: number,
    bindings: readonly NativeOriginParentSourceBinding[],
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginParentSourceAlignmentResponse> {
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!uuid.test(characterId) ||
        !Number.isSafeInteger(expectedDraftRevision) ||
        expectedDraftRevision < 1 ||
        !Array.isArray(bindings) || bindings.length < 1 || bindings.length > 16 ||
        bindings.some(binding => !binding ||
            typeof binding.contributorId !== "string" ||
            binding.contributorId.trim().length === 0 ||
            binding.contributorId.length > 180 ||
            typeof binding.parentCharacterId !== "string" ||
            !uuid.test(binding.parentCharacterId) ||
            binding.parentCharacterId.toLowerCase() === characterId.toLowerCase()) ||
        new Set(bindings.map(binding => binding.contributorId)).size !== bindings.length ||
        new Set(bindings.map(binding => binding.parentCharacterId.toLowerCase())).size !==
            bindings.length) {
        throw new Error("Parent source alignment requires valid, distinct Site Character IDs and a saved draft revision.");
    }

    // Do not send a manifested parent, choice of heritable state, grant, or
    // relationship assertion. The server independently loads the saved draft.
    const payload = {
        expectedDraftRevision,
        bindings: bindings.map(binding => ({
            contributorId: binding.contributorId,
            parentCharacterId: binding.parentCharacterId
        }))
    };
    const result = await requestOrigin(environment, characterId,
        "parent-source-alignment-review", "POST", payload, fetcher);
    if (!isObject(result) || result.status !== "ready" ||
        result.inspectionOnly !== true || result.relationshipVerified !== false ||
        result.inheritanceAuthorized !== false || result.activeGrants !== false ||
        result.effectiveMechanicsReady !== false ||
        result.draftRevision !== expectedDraftRevision ||
        !Array.isArray(result.matches) || result.matches.length !== bindings.length ||
        ["verifiedSources", "verifiedInitialProfile", "profile", "grants",
            "relationships", "independentGrants", "effectiveGrants"].some(key =>
                Object.hasOwn(result, key)) ||
        !result.matches.every((entry: unknown, index: number) =>
            isObject(entry) &&
            entry.contributorId === bindings[index].contributorId &&
            typeof entry.parentCharacterId === "string" &&
            entry.parentCharacterId.toLowerCase() ===
                bindings[index].parentCharacterId.toLowerCase() &&
            typeof entry.nativeSourceId === "string" &&
            /^(species|ancestry)\.[a-z0-9.-]+$/.test(entry.nativeSourceId) &&
            Number.isSafeInteger(entry.parentOriginRevision) &&
            (entry.parentOriginRevision as number) >= 1 &&
            !["underlying", "current", "inherentGrants", "manifestedProfile",
                "verifiedInitialProfile", "independentGrants"].some(key =>
                    Object.hasOwn(entry, key)))) {
        throw new Error("Parent source alignment returned an invalid inspection-only trust contract.");
    }
    return result as unknown as NativeOriginParentSourceAlignmentResponse;
}

export async function activateNativeOrigin(
    environment: HostEnvironment,
    characterId: string,
    expectedDraftRevision: number,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<NativeOriginActivationResponse> {
    if (!Number.isSafeInteger(expectedDraftRevision) || expectedDraftRevision < 1) {
        throw new Error("Initial Origin activation requires an existing saved draft revision.");
    }
    // Do not add arbitrary request options. A caller cannot submit method,
    // DM/campaign claims, profile JSON, history, or independently earned grants.
    const result = await requestOrigin(environment, characterId, "activate-native",
        "POST", { expectedDraftRevision }, fetcher);
    if (!isObject(result) || result.status !== "applied" ||
        !Number.isSafeInteger(result.revision) || (result.revision as number) < 1 ||
        result.effectiveMechanicsReady !== false) {
        throw new Error("Initial Origin activation returned an invalid trust contract.");
    }
    return result as unknown as NativeOriginActivationResponse;
}
