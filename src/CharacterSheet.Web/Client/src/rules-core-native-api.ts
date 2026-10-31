import { RulesCoreApiError } from "./rules-core-api.js";
import { buildCharacterSheetApiUrl } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

// This client does not reinterpret legacy RuleConceptKey values. Native
// IDs, ruleset identity and the catalog fingerprint are explicit inputs.
export const nativeOptionCategories = [
    "class", "subclass", "patron", "pact", "invocation", "condition", "leveled-condition"
] as const;
export type NativeOptionCategory = typeof nativeOptionCategories[number];

export interface NativeRulesetMetadata {
    contractVersion: number;
    rulesetId: string;
    schemaVersion: number;
    fingerprint: string;
    capabilities: {
        characterProjection: boolean;
        resourceResolution: boolean;
        nativeOptionSearch: boolean;
        advancementEligibility: string;
        conditionRollMode: string;
        campaignEffectiveRules: boolean;
    };
}

export interface NativeOptionSummary {
    id: string;
    name: string;
    kind: NativeOptionCategory;
}

export interface NativeOptionPage {
    contractVersion: number;
    rulesetId: string;
    fingerprint: string;
    category: NativeOptionCategory;
    total: number;
    offset: number;
    limit: number;
    options: NativeOptionSummary[];
}

export interface NativeAdvancementInput {
    classId: string;
    level: number;
    occurrenceId: string;
}

export interface NativeSkillTraining {
    ranks?: number;
    proficient?: boolean;
    miscModifier?: number;
    ability?: string | null;
}

export interface NativeCharacterInput {
    rulesetId: string;
    fingerprint: string;
    abilityScores: Record<string, number>;
    advancements: NativeAdvancementInput[];
    skills?: Record<string, NativeSkillTraining>;
    competencies?: Record<string, NativeSkillTraining>;
    defenseContributions?: Record<string, number>;
    selectedOptions?: string[];
    currentResources?: Record<string, number>;
    conditionIds?: string[];
    hitPointGains?: Record<string, number>;
    baseWalkSpeed?: number;
}

export interface NativeNumeric {
    status: string;
    value: number | null;
    reason: string | null;
}

export interface NativeCharacterProjection {
    contractVersion: number;
    rulesetId: string;
    fingerprint: string;
    status: "resolved" | "partial" | "invalid";
    values: Record<string, NativeNumeric>;
    featureIds: string[];
    resources: Array<{
        id: string;
        maximum: number;
        current: number | null;
        recovery: string;
        spellLevel: number | null;
    }>;
    requiredChoices: string[];
    issues: Array<{ code: string; path: string; detail: string }>;
}

export interface NativeEligibilityResult {
    contractVersion: number;
    rulesetId: string;
    fingerprint: string;
    candidateId: string;
    candidateKind: string | null;
    status: "eligible" | "ineligible" | "unresolved" | "invalid";
    eligible: boolean | null;
    requirements: Array<{ key: string; status: string; detail: string }>;
}

type NativeFetcher = (url: string, init?: RequestInit) => Promise<Response>;
const validNativeId = /^[a-z0-9-]+(?:\.[a-z0-9-]+)+$/;
const validRulesetId = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const validFingerprint = /^[0-9a-f]{64}$/;

function assertRuleset(metadata: NativeRulesetMetadata): void {
    if (metadata.contractVersion !== 1 ||
        !validRulesetId.test(metadata.rulesetId) ||
        !validFingerprint.test(metadata.fingerprint)) {
        throw new Error("Rules Core returned an invalid native ruleset identity.");
    }
}

function assertCharacter(metadata: NativeRulesetMetadata, character: NativeCharacterInput): void {
    assertRuleset(metadata);
    if (character.rulesetId !== metadata.rulesetId ||
        character.fingerprint !== metadata.fingerprint) {
        throw new Error("Character projection uses a different Rules Core snapshot.");
    }
    if (character.advancements.some(entry =>
        !validNativeId.test(entry.classId) || !entry.classId.startsWith("class.") ||
        !Number.isInteger(entry.level) || entry.level < 1 || !entry.occurrenceId.trim())) {
        throw new Error("Character advancements must use native class IDs and occurrence IDs.");
    }
    if (character.selectedOptions?.some(id => !validNativeId.test(id)) ||
        character.conditionIds?.some(id => !id.startsWith("condition.") || !validNativeId.test(id))) {
        throw new Error("Character options and conditions must use native IDs.");
    }
}

async function requestJson(
    environment: HostEnvironment,
    path: string,
    method: "GET" | "POST",
    body: object | undefined,
    fetcher: NativeFetcher
): Promise<unknown> {
    let response: Response;
    try {
        response = await fetcher(buildCharacterSheetApiUrl(environment, path), {
            method,
            credentials: "same-origin",
            headers: body === undefined
                ? { Accept: "application/json" }
                : { Accept: "application/json", "Content-Type": "application/json" },
            body: body === undefined ? undefined : JSON.stringify(body)
        });
    } catch {
        throw new RulesCoreApiError("Rules Core native mechanics are unavailable.", 0);
    }
    if (!response.ok) {
        throw new RulesCoreApiError(
            response.status === 409
                ? "The Rules Core native catalog changed. Refresh the ruleset before continuing."
                : response.status === 401 || response.status === 403
                    ? "The current session is not authorized for native Rules Core mechanics."
                    : `Rules Core returned HTTP ${response.status}.`,
            response.status);
    }
    try {
        return await response.json() as unknown;
    } catch {
        throw new RulesCoreApiError("Rules Core returned an unreadable native response.", response.status);
    }
}

function isObject(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

function assertMatchingSnapshot(metadata: NativeRulesetMetadata, value: unknown):
    asserts value is Record<string, unknown> {
    if (!isObject(value) || value.contractVersion !== 1 ||
        value.rulesetId !== metadata.rulesetId || value.fingerprint !== metadata.fingerprint) {
        throw new Error("Rules Core returned a projection from a different native snapshot.");
    }
}

export async function loadNativeRuleset(
    environment: HostEnvironment,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)
): Promise<NativeRulesetMetadata> {
    const result = await requestJson(environment, "/api/native-rules/ruleset", "GET", undefined, fetcher);
    if (!isObject(result)) throw new Error("Rules Core native ruleset metadata is invalid.");
    const metadata = result as unknown as NativeRulesetMetadata;
    assertRuleset(metadata);
    return metadata;
}

export async function searchNativeOptions(
    environment: HostEnvironment,
    metadata: NativeRulesetMetadata,
    category: NativeOptionCategory,
    query: string,
    limit = 50,
    offset = 0,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)
): Promise<NativeOptionPage> {
    assertRuleset(metadata);
    if (!metadata.capabilities?.nativeOptionSearch) {
        throw new Error("Rules Core does not expose native option discovery.");
    }
    if (!nativeOptionCategories.includes(category) || query.length > 200 ||
        !Number.isInteger(limit) || limit < 1 || limit > 100 ||
        !Number.isInteger(offset) || offset < 0 || offset > 1_000_000) {
        throw new Error("Invalid native option search parameters.");
    }
    const params = new URLSearchParams({
        fingerprint: metadata.fingerprint, category, q: query.trim(),
        limit: String(limit), offset: String(offset)
    });
    const result = await requestJson(environment,
        `/api/native-rules/options?${params.toString()}`, "GET", undefined, fetcher);
    assertMatchingSnapshot(metadata, result);
    if (result.category !== category || !Array.isArray(result.options) ||
        typeof result.total !== "number" || result.total < 0 || !Number.isInteger(result.total)) {
        throw new Error("Rules Core returned invalid native option search results.");
    }
    // Do not pass through unexpected source-body fields even if a response
    // schema is accidentally broadened by a future server implementation.
    const options: NativeOptionSummary[] = result.options.map((entry: unknown) => {
        if (!isObject(entry) || typeof entry.id !== "string" ||
            typeof entry.name !== "string" || !entry.name.trim() ||
            entry.kind !== category || !validNativeId.test(entry.id)) {
            throw new Error("Rules Core returned an invalid native option identity.");
        }
        return { id: entry.id, name: entry.name, kind: category };
    });
    if (options.length > limit) throw new Error("Rules Core option search exceeded its requested limit.");
    return { contractVersion: 1, rulesetId: metadata.rulesetId,
        fingerprint: metadata.fingerprint, category,
        total: result.total, limit, offset, options };
}

export async function evaluateNativeAdvancement(
    environment: HostEnvironment,
    metadata: NativeRulesetMetadata,
    candidateId: string,
    character: NativeCharacterInput,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)
): Promise<NativeEligibilityResult> {
    assertCharacter(metadata, character);
    if (!validNativeId.test(candidateId)) throw new Error("Native candidate ID is invalid.");
    const result = await requestJson(environment, "/api/native-rules/advancements/eligibility",
        "POST", { rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint,
            candidateId, character }, fetcher);
    assertMatchingSnapshot(metadata, result);
    if (result.candidateId !== candidateId || !Array.isArray(result.requirements) ||
        !["eligible", "ineligible", "unresolved", "invalid"].includes(String(result.status)) ||
        (result.status === "eligible" && result.eligible !== true) ||
        (result.status === "ineligible" && result.eligible !== false) ||
        ((result.status === "unresolved" || result.status === "invalid") && result.eligible !== null)) {
        throw new Error("Rules Core returned an inconsistent eligibility decision.");
    }
    return result as unknown as NativeEligibilityResult;
}

export async function projectNativeCharacter(
    environment: HostEnvironment,
    metadata: NativeRulesetMetadata,
    character: NativeCharacterInput,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)
): Promise<NativeCharacterProjection> {
    assertCharacter(metadata, character);
    const result = await requestJson(environment, "/api/native-rules/characters/project",
        "POST", character, fetcher);
    assertMatchingSnapshot(metadata, result);
    if (!["resolved", "partial", "invalid"].includes(String(result.status)) ||
        !isObject(result.values) || !Array.isArray(result.featureIds) ||
        !Array.isArray(result.resources) || !Array.isArray(result.requiredChoices) ||
        !Array.isArray(result.issues)) {
        throw new Error("Rules Core returned an invalid character projection.");
    }
    // Preserve unsupported/partial values rather than converting missing
    // numerical outcomes to zero. This is not a legacy DTO adapter.
    return result as unknown as NativeCharacterProjection;
}
