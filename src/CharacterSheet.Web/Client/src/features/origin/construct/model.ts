import type { AllocationView, ValidationIssue } from "../../guided/contracts.js";
import type { NativeOriginDraftProposal, NativeOriginDraftManifestation } from "../origin-api.js";

/** Initial construction only. No fuel snapshot, resource debit, or trusted runtime
 * override is represented by this player-editable input. The host supplies allowance. */
export interface ComponentInstance {
    instanceId: string;
    definitionId: string;
    materials?: Readonly<Record<string, string>>;
    connectedTo?: readonly string[];
    enabled: boolean;
    modeId?: string | null;
    designId?: string | null;
    enhancementIds?: readonly string[];
    powerSourceId?: string | null;
    parentInstanceId?: string | null;
    mountInterfaceId?: string | null;
    constructionVariantId?: string | null;
    protectedRegionId?: string | null;
}
export interface PowerConnection { fromInstanceId: string; toInstanceId: string }
export interface AssemblyInput {
    components: readonly ComponentInstance[];
    initialAllowance: number | null;
    initialConstruction: true;
    powerConnections?: readonly PowerConnection[];
}
/** One method-specific payload alongside the canonical Origin proposal.
 * This is a transport contract for A's draft integration, not a separate store. */
export interface ConstructDraft { proposal: NativeOriginDraftProposal; assembly: AssemblyInput }
export const constructReference = { ruleId: "rule.creature-origination", methodId: "construct-assembly" } as const;
export function emptyConstructDraft(id: string, allowance: number | null): ConstructDraft {
    if (!id.trim() || (allowance !== null && (!Number.isSafeInteger(allowance) || allowance < 0)))
        throw new Error("Invalid initial Construct draft identity or allowance.");
    return { proposal: { id, intent: "Custom", method: constructReference,
        contributors: [], candidates: [], manifestations: [] },
        assembly: { components: [], initialAllowance: allowance, initialConstruction: true } };
}
export function addComponent(draft: ConstructDraft, instanceId: string, definitionId: string): ConstructDraft {
    if (!/^[A-Za-z0-9_.-]{1,96}$/.test(instanceId) || !definitionId.trim() ||
        draft.assembly.components.some(x => x.instanceId === instanceId))
        throw new Error("A component requires a distinct stable instance identity.");
    return { ...draft, assembly: { ...draft.assembly, components: [...draft.assembly.components,
        { instanceId, definitionId, enabled: true }] } };
}
export type ComponentPatch = Partial<Omit<ComponentInstance, "instanceId" | "definitionId">>;
export function editComponent(draft: ConstructDraft, instanceId: string, patch: ComponentPatch): ConstructDraft {
    if (!draft.assembly.components.some(x => x.instanceId === instanceId)) throw new Error("Component no longer exists.");
    return { ...draft, assembly: { ...draft.assembly, components: draft.assembly.components.map(x =>
        x.instanceId === instanceId ? { ...x, ...patch, instanceId: x.instanceId, definitionId: x.definitionId } : x) } };
}
/** Remove only exact-instance links that necessarily cease to exist. Keep all
 * other selections, including manifestations needing native revalidation. */
export function removeComponent(draft: ConstructDraft, instanceId: string): ConstructDraft {
    if (!draft.assembly.components.some(x => x.instanceId === instanceId)) throw new Error("Component no longer exists.");
    const components = draft.assembly.components.filter(x => x.instanceId !== instanceId).map(x => {
        const links = x.connectedTo?.filter(id => id !== instanceId);
        const changedLinks = links?.length !== x.connectedTo?.length;
        const removedParent = x.parentInstanceId === instanceId;
        return changedLinks || removedParent ? { ...x,
            ...(changedLinks ? { connectedTo: links } : {}),
            ...(removedParent ? { parentInstanceId: null, mountInterfaceId: null } : {}) } : x;
    });
    const powerConnections = draft.assembly.powerConnections?.filter(x =>
        x.fromInstanceId !== instanceId && x.toInstanceId !== instanceId);
    return { ...draft, assembly: { ...draft.assembly, components, powerConnections } };
}
export function setManifestation(draft: ConstructDraft, candidateId: string,
    selection: NativeOriginDraftManifestation | null): ConstructDraft {
    if (selection && selection.candidateId !== candidateId) throw new Error("Mismatched characteristic identity.");
    const existing = draft.proposal.manifestations.find(x => x.candidateId === candidateId);
    const manifestations = draft.proposal.manifestations.filter(x => x.candidateId !== candidateId);
    if (selection) manifestations.push({ ...selection, id: existing?.id ?? selection.id });
    return { ...draft, proposal: { ...draft.proposal, manifestations } };
}

export type NativeStatus = "Invalid" | "Incomplete" | "Unresolved" | "Conflicting" | "Authorized";
export interface NativeIssue { code: string; path: string; severity: "Incomplete" | "Unresolved" | "Conflict" | "Invalid"; detail: string }
export interface NamedOption { id: string; name: string }
export interface Configuration {
    definitionId: string; name: string; materialSlots: string[]; defaultRecipeId: string | null;
    constructionVariants: { id: string; recipeId: string; bodySize: string | null }[];
    modeIds: string[]; baseCapabilities: string[]; requiresAnyOf: string[][];
    mountInterfaces: string[]; acceptsMountInterfaces: string[];
    powerSourceRequired: boolean; requiresContinuousPower: boolean;
    designs: NamedOption[]; enhancements: NamedOption[]; powerSources: NamedOption[]; issues: NativeIssue[];
}
export interface ComponentState {
    instanceId: string; definitionId: string; installed: boolean; operational: boolean;
    activeCapabilities: string[]; requestedEnabled: boolean; modeId: string | null;
    activeOperationalOriginTraitIds: string[] | null; requiresContinuousPower: boolean;
    sourceSupplyingPower: boolean | null;
}
export interface Network {
    id: string; componentInstanceIds: string[]; requestedDemand: number; availableOutput: number;
    operationalDemand: number; operationalOutput: number; requestedHeadroom: number | null;
    isOverallocated: boolean | null; hasUnresolvedDemand: boolean; hasUnresolvedOutput: boolean;
}
export interface AssemblyResult {
    status: NativeStatus; knownConstructionCost: number; components: ComponentState[];
    shutdownConsequences: { instanceId: string; consequenceId: string }[];
    issues: NativeIssue[]; powerNetworks: Network[] | null;
    requiredConstructionResources: Record<string, number> | null;
    derivedCreatureSize: string | null; knownRetainedMaterialMassPounds: number | null;
    unresolvedRetainedMassMaterials: string[] | null; isBodyMassComplete: boolean;
    productsWithUncertifiedMass: string[] | null;
}
export interface Candidate {
    id: string; kind: "Intrinsic" | "Trait" | "Other"; labelId: string;
    offeredValue: unknown; unresolvedEvidence: string[];
    requiredChoices: { id: string; minimum: number; maximum: number;
        finiteOptions: string[] | null; optionCatalogId: string | null; parameterKey: string | null }[];
}
export interface ConstructPreview {
    assembly: AssemblyResult;
    origin: { status: NativeStatus; issues: NativeIssue[]; candidates: Candidate[] };
    movement: { status: string; modes: { mode: string; speedFeet: number }[] } | null;
}

// Decode the bounded native records before rendering. Reject malformed
// booleans/counts/status rather than coercing missing values to zero or false.
export function record(v: unknown): Record<string, unknown> {
    if (!v || typeof v !== "object" || Array.isArray(v)) throw new Error("Invalid native Construct record.");
    return v as Record<string, unknown>;
}
export function str(v: unknown): string {
    if (typeof v !== "string" || !v.trim()) throw new Error("Invalid native Construct text.");
    return v;
}
export function integer(v: unknown): number {
    if (typeof v !== "number" || !Number.isSafeInteger(v)) throw new Error("Invalid native Construct number.");
    return v;
}
function nonnegative(v: unknown): number {
    const n = integer(v); if (n < 0) throw new Error("Invalid negative native Construct quantity."); return n;
}
function bool(v: unknown): boolean {
    if (typeof v !== "boolean") throw new Error("Invalid native Construct flag."); return v;
}
function list<T>(v: unknown, decode: (v: unknown) => T): T[] {
    if (!Array.isArray(v)) throw new Error("Invalid native Construct list.");
    return v.map(decode);
}
function optional<T>(v: unknown, decode: (v: unknown) => T): T | null {
    return v === null || v === undefined ? null : decode(v);
}
const strings = (v: unknown) => list(v, str);
function enumValue<T extends string>(v: unknown, values: readonly T[]): T {
    const match = typeof v === "number" ? values[v] : values.find(x => x === v);
    if (!match) throw new Error("Unknown native Construct status."); return match;
}
const status = (v: unknown) => enumValue(v, ["Invalid", "Incomplete", "Unresolved", "Conflicting", "Authorized"] as const);
function issue(v: unknown): NativeIssue {
    const r = record(v); return { code: str(r.code), path: str(r.path),
        severity: enumValue(r.severity, ["Incomplete", "Unresolved", "Conflict", "Invalid"] as const), detail: str(r.detail) };
}
export function namedOption(v: unknown): NamedOption {
    const r = record(v); return { id: str(r.id), name: str(r.name) };
}
export function decodeConfiguration(v: unknown): Configuration {
    const r = record(v); return {
        definitionId: str(r.definitionId), name: str(r.name), materialSlots: strings(r.materialSlots),
        defaultRecipeId: optional(r.defaultRecipeId, str),
        constructionVariants: list(r.constructionVariants, x => {
            const t = record(x); return { id: str(t.id), recipeId: str(t.recipeId), bodySize: optional(t.bodySize, str) };
        }),
        modeIds: strings(r.modeIds), baseCapabilities: strings(r.baseCapabilities),
        requiresAnyOf: list(r.requiresAnyOf, strings), mountInterfaces: strings(r.mountInterfaces),
        acceptsMountInterfaces: strings(r.acceptsMountInterfaces), powerSourceRequired: bool(r.powerSourceRequired),
        requiresContinuousPower: bool(r.requiresContinuousPower), designs: list(r.designs, namedOption),
        enhancements: list(r.enhancements, namedOption), powerSources: list(r.powerSources, namedOption),
        issues: list(r.issues, issue)
    };
}
function componentState(v: unknown): ComponentState {
    const r = record(v); return { instanceId: str(r.instanceId), definitionId: str(r.definitionId),
        installed: bool(r.installed), operational: bool(r.operational), requestedEnabled: bool(r.requestedEnabled),
        activeCapabilities: strings(r.activeCapabilities), modeId: optional(r.modeId, str),
        activeOperationalOriginTraitIds: optional(r.activeOperationalOriginTraitIds, strings),
        requiresContinuousPower: bool(r.requiresContinuousPower), sourceSupplyingPower: optional(r.sourceSupplyingPower, bool) };
}
function network(v: unknown): Network {
    const r = record(v); return { id: str(r.id), componentInstanceIds: strings(r.componentInstanceIds),
        requestedDemand: nonnegative(r.requestedDemand), availableOutput: nonnegative(r.availableOutput),
        operationalDemand: nonnegative(r.operationalDemand), operationalOutput: nonnegative(r.operationalOutput),
        requestedHeadroom: optional(r.requestedHeadroom, integer), isOverallocated: optional(r.isOverallocated, bool),
        hasUnresolvedDemand: bool(r.hasUnresolvedDemand), hasUnresolvedOutput: bool(r.hasUnresolvedOutput) };
}
export function decodeAssembly(v: unknown): AssemblyResult {
    const r = record(v);
    return { status: status(r.status), knownConstructionCost: nonnegative(r.knownConstructionCost),
        components: list(r.components, componentState), shutdownConsequences: list(r.shutdownConsequences, x => {
            const t = record(x); return { instanceId: str(t.instanceId), consequenceId: str(t.consequenceId) };
        }), issues: list(r.issues, issue), powerNetworks: optional(r.powerNetworks, x => list(x, network)),
        requiredConstructionResources: optional(r.requiredConstructionResources, x =>
            Object.fromEntries(Object.entries(record(x)).map(([id, count]) => [id, nonnegative(count)]))),
        derivedCreatureSize: optional(r.derivedCreatureSize, str),
        knownRetainedMaterialMassPounds: optional(r.knownRetainedMaterialMassPounds, nonnegative),
        unresolvedRetainedMassMaterials: optional(r.unresolvedRetainedMassMaterials, strings),
        isBodyMassComplete: bool(r.isBodyMassComplete),
        productsWithUncertifiedMass: optional(r.productsWithUncertifiedMass, strings) };
}
function candidate(v: unknown): Candidate {
    const r = record(v), identity = record(r.identity);
    return { id: str(r.id), kind: enumValue(identity.kind, ["Intrinsic", "Trait", "Other"] as const),
        labelId: str(identity.intrinsicSlotId ?? identity.mechanicalDefinitionId ?? identity.traitConceptId),
        offeredValue: r.offeredValue, unresolvedEvidence: optional(r.unresolvedEvidence, strings) ?? [],
        requiredChoices: optional(r.requiredChoices, x => list(x, raw => {
            const t = record(raw); return { id: str(t.id), minimum: nonnegative(t.minimum), maximum: nonnegative(t.maximum),
                finiteOptions: optional(t.finiteOptions, strings), optionCatalogId: optional(t.optionCatalogId, str),
                parameterKey: optional(t.parameterKey, str) };
        })) ?? [] };
}
export function decodeConstructPreview(raw: unknown, movement: unknown): ConstructPreview {
    const r = record(raw), origin = record(r.origin), proposal = record(origin.evaluatedProposal);
    return { assembly: decodeAssembly(r.assembly),
        origin: { status: status(origin.status), issues: list(origin.issues, issue),
            candidates: list(proposal.candidates, candidate) },
        movement: optional(movement, x => { const t = record(x); return {
            status: str(t.status), modes: list(t.modes, y => {
                const m = record(y); return { mode: str(m.mode), speedFeet: nonnegative(m.speedFeet) };
            }) }; }) };
}
/** Cost is native; an over-limit lower bound is meaningful even if other
 * ingredients are unresolved. Do not announce unused points for partial quotes. */
export function constructionAllocation(allowance: number | null, result: AssemblyResult | null): AllocationView {
    const exceeded = result?.issues.some(x => x.code === "construct-allowance-exceeded") ?? false;
    const complete = result?.status === "Authorized";
    return { label: "Starting construction allowance", allowance: allowance === null ? undefined : String(allowance),
        used: result ? String(result.knownConstructionCost) : undefined,
        remaining: result && allowance !== null && complete ? String(allowance - result.knownConstructionCost) : undefined,
        status: exceeded ? "over" : complete && allowance !== null ? "within" : "unresolved",
        detail: !result ? "Awaiting native quotation." : exceeded ? "Known cost exceeds the allowance."
            : complete ? "Initial ingredient quotation; no inventory transaction." : "Known cost only; total and remaining allowance are not confirmed." };
}
export function toValidation(issues: readonly NativeIssue[]): ValidationIssue[] {
    const seen = new Set<string>(); const result: ValidationIssue[] = [];
    for (const x of issues) {
        const id = x.code + ":" + x.path + ":" + x.detail;
        if (seen.has(id)) continue; seen.add(id);
        const severity = x.severity === "Incomplete" ? "incomplete" : x.severity === "Unresolved" ? "unavailable"
            : x.severity === "Conflict" ? "conflict" : "invalid";
        result.push({ id, severity, message: x.detail, decision: { id: "configuration", label: "Review configuration" } });
    }
    return result;
}
