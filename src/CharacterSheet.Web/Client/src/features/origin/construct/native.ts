import { buildCharacterSheetApiUrl } from "../../../character-api.js";
import type { HostEnvironment } from "../../../host-environment.js";
import { discoverNativeConstructConfiguration, previewNativeConstruct,
    type NativeRulesetMetadata } from "../../../rules-core-native-api.js";
import type { NativePreviewEnvelope } from "../../guided/contracts.js";
import { decodeConfiguration, decodeConstructPreview, namedOption, record, integer,
    type Configuration, type ConstructDraft, type ConstructPreview, type NamedOption } from "./model.js";

type Fetcher = typeof fetch;
export type ConstructCatalogKind = "construct-component" | "construct-material" | "origin-trait";
export interface ConstructCatalogOption extends NamedOption { componentCategory: string | null }
export interface ConstructCatalog {
    rulesetId: string; fingerprint: string; initialAllowance: number | null;
    components: readonly ConstructCatalogOption[]; materials: readonly NamedOption[]; traits: readonly NamedOption[];
}
/** Use the existing native identity-only option endpoint. A's shared client
 * category union does not yet include Construct categories; this adapter stays
 * narrow until A integrates it. No direct Core URL or service credentials. */
export async function loadConstructOptions(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    category: ConstructCatalogKind, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<ConstructCatalogOption[]> {
    if (!metadata.capabilities.nativeOptionSearch) throw new Error("Native catalog discovery is unavailable.");
    const options: ConstructCatalogOption[] = [], seen = new Set<string>(); let total: number | undefined;
    do {
        const query = new URLSearchParams({ fingerprint: metadata.fingerprint, category,
            q: "", limit: "100", offset: String(options.length) });
        const response = await fetcher(buildCharacterSheetApiUrl(environment, "/api/native-rules/options?" + query),
            { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
        if (!response.ok) throw new Error("Native Construct catalog is unavailable (" + response.status + ").");
        const page = record(await response.json());
        if (page.contractVersion !== 1 || page.rulesetId !== metadata.rulesetId || page.fingerprint !== metadata.fingerprint ||
            page.category !== category || page.offset !== options.length || !Array.isArray(page.options) || page.options.length > 100)
            throw new Error("Invalid or stale Construct catalog response.");
        const count = integer(page.total);
        if (count < 0 || count > 10000 || (total !== undefined && total !== count))
            throw new Error("Native Construct catalog exceeds the supported complete discovery bound or changed.");
        total = count;
        for (const raw of page.options) {
            if (record(raw).kind !== category) throw new Error("Unexpected native category.");
            const option = namedOption(raw), role = record(raw).componentCategory;
            if (!/^[A-Za-z0-9][A-Za-z0-9_.-]{0,159}$/.test(option.id) || seen.has(option.id))
                throw new Error("Invalid or duplicate native option.");
            if (role != null && (category !== "construct-component" || typeof role !== "string" || !role.trim() || role.length > 160))
                throw new Error("Invalid native component category.");
            seen.add(option.id); options.push({ ...option, componentCategory: typeof role === "string" ? role : null });
        }
        if (options.length > total || (!page.options.length && options.length < total))
            throw new Error("Incomplete native Construct catalog page.");
    } while (options.length < total);
    return options;
}
/** Read the exact native method's authored allowance. Older Core builds
 * omit the field; keep it unavailable rather than choosing a default. */
export async function loadConstructAllowance(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<number | null> {
    const query = new URLSearchParams({ fingerprint: metadata.fingerprint });
    const response = await fetcher(buildCharacterSheetApiUrl(environment, "/api/native-rules/origins/methods?" + query),
        { credentials: "same-origin", cache: "no-store", headers: { Accept: "application/json" } });
    if (!response.ok) throw new Error("Native Construct method discovery failed (" + response.status + ").");
    const body = record(await response.json());
    if (body.contractVersion !== 1 || body.previewOnly !== true || body.rulesetId !== metadata.rulesetId ||
        body.fingerprint !== metadata.fingerprint || !Array.isArray(body.methods))
        throw new Error("Invalid or stale Construct method discovery.");
    const matches = body.methods.map(record).filter(method => {
        const reference = record(method.reference);
        return reference.ruleId === "rule.creature-origination" && reference.methodId === "construct-assembly";
    });
    if (matches.length !== 1 || matches[0]!.support !== "assembly-preview") return null;
    const amount = matches[0]!.initialConstructionAllowance;
    if (amount == null) return null;
    const allowance = integer(amount);
    if (allowance < 0) throw new Error("Invalid native Construct allowance.");
    return allowance;
}
export async function loadConstructCatalog(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<ConstructCatalog> {
    const [components, materials, traits, initialAllowance] = await Promise.all([
        loadConstructOptions(environment, metadata, "construct-component", fetcher),
        loadConstructOptions(environment, metadata, "construct-material", fetcher),
        loadConstructOptions(environment, metadata, "origin-trait", fetcher),
        loadConstructAllowance(environment, metadata, fetcher)]);
    return { rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint, components, materials, traits, initialAllowance };
}
export async function loadComponentConfiguration(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    definitionId: string, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<NativePreviewEnvelope<Configuration>> {
    const envelope = await discoverNativeConstructConfiguration(environment, metadata, definitionId, fetcher);
    const configuration = decodeConfiguration(envelope.configuration);
    if (configuration.definitionId !== definitionId) throw new Error("Mismatched native component configuration.");
    return { contractVersion: 1, previewOnly: true, rulesetId: envelope.rulesetId,
        fingerprint: envelope.fingerprint, result: configuration };
}
/** The saved response contains the native Construct envelope, never the
 * ordinary biological preview. Verify identity before decoding it. */
export function decodeSavedConstructPreview(raw: unknown, metadata: NativeRulesetMetadata): NativePreviewEnvelope<ConstructPreview> {
    const envelope = record(raw);
    if (envelope.contractVersion !== 1 || envelope.previewOnly !== true ||
        envelope.rulesetId !== metadata.rulesetId || envelope.fingerprint !== metadata.fingerprint)
        throw new Error("Invalid or stale saved Construct evaluation.");
    return { contractVersion: 1, previewOnly: true, rulesetId: metadata.rulesetId,
        fingerprint: metadata.fingerprint, result: decodeConstructPreview(envelope.result, envelope.movementModes) };
}
/** Stateless and initial-only. The native preview receives raw player choices,
 * not evaluated candidates, a profile, an operating session, or authority. */
export async function evaluateConstructDraft(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    draft: ConstructDraft, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<NativePreviewEnvelope<ConstructPreview>> {
    const { proposal, assembly } = draft;
    if (assembly.initialConstruction !== true || proposal.method.ruleId !== "rule.creature-origination" ||
        proposal.method.methodId !== "construct-assembly" || proposal.intent !== "Custom" || proposal.candidates.length ||
        proposal.contributors.length) throw new Error("Only an initial custom Construct player proposal may be previewed here.");
    const envelope = await previewNativeConstruct(environment, metadata, proposal, assembly, fetcher);
    return { contractVersion: 1, rulesetId: envelope.rulesetId, fingerprint: envelope.fingerprint,
        previewOnly: true, result: decodeConstructPreview(envelope.result, envelope.movementModes) };
}
