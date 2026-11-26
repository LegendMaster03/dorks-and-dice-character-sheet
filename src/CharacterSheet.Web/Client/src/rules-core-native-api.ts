import type { NativeDiscoveryEnvelope, NativeMethod, NativeMethodDiscovery, NativePreset, NativePresetDiscovery, NativePreviewEnvelope } from "./features/guided/contracts.js";
/**
 * Calls native Rules Core mechanics through the authenticated same-origin bridge and validates
 * snapshot identities before accepting results.
 */
import { RulesCoreApiError } from "./rules-core-api.js";
import { buildCharacterSheetApiUrl } from "./character-api.js";
import type { HostEnvironment } from "./host-environment.js";

// This client does not reinterpret legacy RuleConceptKey values. Native
// IDs, ruleset identity and the catalog fingerprint are explicit inputs.
export const nativeOptionCategories = [
    "species", "ancestry", "class", "subclass", "patron", "pact", "invocation", "condition", "leveled-condition",
    "skill", "competency", "feat", "spell", "item", "monster"
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

/** Match the fingerprint and preview marker before exposing bounded native data.
 * Method-specific adapters validate their result fields; discovery never grants authority. */
async function guidedDiscovery(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    path: string, params: Record<string, string>, fetcher: NativeFetcher): Promise<Record<string, unknown>> {
    assertRuleset(metadata);
    const query = new URLSearchParams({ ...params, fingerprint: metadata.fingerprint });
    const result = await requestJson(environment, path + "?" + query.toString(), "GET", undefined, fetcher);
    assertMatchingSnapshot(metadata, result);
    if (result.previewOnly !== true) throw new Error("Invalid native discovery response.");
    return result;
}
function discoveryIdentity(metadata: NativeRulesetMetadata): NativeDiscoveryEnvelope {
    return { contractVersion: 1, rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint, previewOnly: true };
}
export async function discoverNativeOriginPresets(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)): Promise<NativePresetDiscovery> {
    const result = await guidedDiscovery(environment, metadata, "/api/native-rules/origins/presets", {}, fetcher);
    if (!Array.isArray(result.presets)) throw new Error("Invalid native preset discovery.");
    const presets = result.presets.map((value: unknown): NativePreset => {
        if (!isObject(value) || typeof value.id !== "string" || typeof value.name !== "string" ||
            typeof value.kind !== "string" || (value.parentId !== null && typeof value.parentId !== "string") ||
            typeof value.selectable !== "boolean" || typeof value.ancestrySelectionRequired !== "boolean" ||
            (value.defaultUpbringingId !== null && typeof value.defaultUpbringingId !== "string") ||
            !Array.isArray(value.acquisitionMethodIds) || !value.acquisitionMethodIds.every(id => typeof id === "string"))
            throw new Error("Invalid native preset.");
        return { id: value.id, name: value.name, kind: value.kind, parentId: value.parentId as string | null,
            selectable: value.selectable, ancestrySelectionRequired: value.ancestrySelectionRequired,
            defaultUpbringingId: value.defaultUpbringingId as string | null,
            acquisitionMethodIds: value.acquisitionMethodIds as string[] };
    });
    return { ...discoveryIdentity(metadata), presets };
}
export async function discoverNativeOriginMethods(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    creatureTypeId?: string, fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)): Promise<NativeMethodDiscovery> {
    const result = await guidedDiscovery(environment, metadata, "/api/native-rules/origins/methods",
        creatureTypeId ? { creatureTypeId } : {}, fetcher);
    if (!Array.isArray(result.methods)) throw new Error("Invalid native method discovery.");
    const methods = result.methods.map((value: unknown): NativeMethod => {
        if (!isObject(value) || !isObject(value.reference) || typeof value.reference.ruleId !== "string" ||
            typeof value.reference.methodId !== "string" || typeof value.name !== "string" ||
            (value.stage !== null && typeof value.stage !== "string") || typeof value.support !== "string" ||
            (value.defaultAvailability !== null && typeof value.defaultAvailability !== "string"))
            throw new Error("Invalid native method.");
        return { reference: { ruleId: value.reference.ruleId, methodId: value.reference.methodId },
            name: value.name, stage: value.stage as string | null, support: value.support,
            defaultAvailability: value.defaultAvailability as string | null };
    });
    return { ...discoveryIdentity(metadata), methods };
}
export interface NativeChoiceCatalogDiscovery extends NativeDiscoveryEnvelope {
    choices: { catalogId: string; status: string; requiresCharacterAuthority: boolean;
        options: readonly { id: string; name: string }[] };
}
export async function discoverNativeOriginChoiceCatalog(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    catalogId: string, fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)): Promise<NativeChoiceCatalogDiscovery> {
    const response = await guidedDiscovery(environment, metadata, "/api/native-rules/origins/choice-catalog", { catalogId }, fetcher);
    const c = response.choices;
    if (!isObject(c) || c.catalogId !== catalogId || typeof c.status !== "string" ||
        typeof c.requiresCharacterAuthority !== "boolean" || !Array.isArray(c.options))
        throw new Error("Invalid native choice catalog.");
    const options = c.options.map((option: unknown) => {
        if (!isObject(option) || typeof option.id !== "string" || typeof option.name !== "string")
            throw new Error("Invalid native catalog option.");
        return { id: option.id, name: option.name };
    });
    return { ...discoveryIdentity(metadata), choices: { catalogId, status: c.status,
        requiresCharacterAuthority: c.requiresCharacterAuthority, options } };
}
export async function discoverNativeConstructConfiguration(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    definitionId: string, fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)):
    Promise<NativeDiscoveryEnvelope & { configuration: Record<string, unknown> }> {
    const response = await guidedDiscovery(environment, metadata,
        "/api/native-rules/origins/construct/components/" + encodeURIComponent(definitionId) + "/configuration", {}, fetcher);
    if (!isObject(response.configuration)) throw new Error("Invalid native construct configuration.");
    return { ...discoveryIdentity(metadata), configuration: response.configuration };
}
export interface NativeLifeExperienceProposal {
    upbringingId: string;
    backgroundId: string;
    selections?: Readonly<Record<string, readonly string[]>>;
    startingItemIds?: Readonly<Record<string, string>>;
}
export interface NativeLifeExperiencePreviewResult {
    status: string;
    sources: readonly { sourceId: string; occurrenceId: string; inherited: boolean }[];
    choices: readonly { key: string; kind: string; count: number; optionIds: readonly string[]; contextConstraint?: string | null }[];
    selectedPaths: readonly { sourceId: string; occurrenceId: string; benefitId: string; optionId: string }[];
    candidateGrants: readonly { sourceId: string; occurrenceId: string; benefitId: string; optionId?: string | null;
        kind: string; targetId: string; ranks: number; proficient: boolean; originalTargetId?: string | null;
        authoredEffect?: unknown; grantPath?: string | null }[];
    startingItems: readonly { key: string; name: string; quantity: number; ordinaryOptions: readonly string[];
        expectedEquipmentTypeId?: string | null; requiresMundane: boolean; requiresInexpensive: boolean; selectedItemId?: string | null }[];
    currency: readonly { sourceId: string; occurrenceId: string; currency: string; quantity: number }[];
    candidateEquipment: unknown;
    equipmentReplacements: readonly unknown[];
    issues: readonly unknown[];
    earnedProjection: null;
}
async function guidedPreview(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    path: string, input: object, fetcher: NativeFetcher): Promise<Record<string, unknown>> {
    assertRuleset(metadata);
    const response = await requestJson(environment, path, "POST",
        { ...input, rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint }, fetcher);
    assertMatchingSnapshot(metadata, response);
    if (response.previewOnly !== true || !isObject(response.result)) throw new Error("Invalid native preview.");
    return response;
}
export async function previewNativeLifeExperiences(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    proposal: NativeLifeExperienceProposal, fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)):
    Promise<NativePreviewEnvelope<NativeLifeExperiencePreviewResult> & { committed: false }> {
    const response = await guidedPreview(environment, metadata, "/api/native-rules/origins/life-experiences/preview", { proposal }, fetcher);
    const result = response.result as Record<string, unknown>;
    if (response.committed !== false || result.earnedProjection != null || typeof result.status !== "string" ||
        !["sources", "choices", "selectedPaths", "candidateGrants", "startingItems", "currency", "equipmentReplacements", "issues"]
            .every(key => Array.isArray(result[key]))) throw new Error("Invalid native Life Experiences preview.");
    return { ...discoveryIdentity(metadata), committed: false,
        result: result as unknown as NativeLifeExperiencePreviewResult };
}
/** The assembly adapter owns its native DTO and result decoding (Workstream D).
 * Accept only preview data here, never a verified operating or modification plan. */
export async function previewNativeConstruct(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    proposal: import("./features/origin/origin-api.js").NativeOriginDraftProposal, assembly: object,
    fetcher: NativeFetcher = globalThis.fetch.bind(globalThis)):
    Promise<NativePreviewEnvelope<Record<string, unknown>> & { movementModes: unknown }> {
    const response = await guidedPreview(environment, metadata, "/api/native-rules/origins/construct/preview",
        { proposal, assembly }, fetcher);
    return { ...discoveryIdentity(metadata), result: response.result as Record<string, unknown>,
        movementModes: response.movementModes };
}
