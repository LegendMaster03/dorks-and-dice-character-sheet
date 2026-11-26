import { buildCharacterSheetApiUrl } from "../../../character-api.js";
import type { HostEnvironment } from "../../../host-environment.js";
import { discoverNativeConstructConfiguration, previewNativeConstruct,
    type NativeRulesetMetadata } from "../../../rules-core-native-api.js";
import type { NativePreviewEnvelope } from "../../guided/contracts.js";
import { decodeConfiguration, decodeConstructPreview, namedOption, record, integer,
    type Configuration, type ConstructDraft, type ConstructPreview, type NamedOption } from "./model.js";

type Fetcher = typeof fetch;
export type ConstructCatalogKind = "construct-component" | "construct-material" | "origin-trait";
export interface ConstructCatalog {
    rulesetId: string; fingerprint: string;
    components: readonly NamedOption[]; materials: readonly NamedOption[]; traits: readonly NamedOption[];
}
/** Use the existing native identity-only option endpoint. A's shared client
 * category union does not yet include Construct categories; this adapter stays
 * narrow until A integrates it. No direct Core URL or service credentials. */
export async function loadConstructOptions(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    category: ConstructCatalogKind, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<NamedOption[]> {
    if (!metadata.capabilities.nativeOptionSearch) throw new Error("Native catalog discovery is unavailable.");
    const options: NamedOption[] = [], seen = new Set<string>(); let total: number | undefined;
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
            const option = namedOption(raw);
            if (seen.has(option.id)) throw new Error("Duplicate native option.");
            seen.add(option.id); options.push(option);
        }
        if (options.length > total || (!page.options.length && options.length < total))
            throw new Error("Incomplete native Construct catalog page.");
    } while (options.length < total);
    return options;
}
export async function loadConstructCatalog(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<ConstructCatalog> {
    const [components, materials, traits] = await Promise.all([
        loadConstructOptions(environment, metadata, "construct-component", fetcher),
        loadConstructOptions(environment, metadata, "construct-material", fetcher),
        loadConstructOptions(environment, metadata, "origin-trait", fetcher)]);
    return { rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint, components, materials, traits };
}
export async function loadComponentConfiguration(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    definitionId: string, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<NativePreviewEnvelope<Configuration>> {
    const envelope = await discoverNativeConstructConfiguration(environment, metadata, definitionId, fetcher);
    const configuration = decodeConfiguration(envelope.configuration);
    if (configuration.definitionId !== definitionId) throw new Error("Mismatched native component configuration.");
    return { contractVersion: 1, previewOnly: true, rulesetId: envelope.rulesetId,
        fingerprint: envelope.fingerprint, result: configuration };
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
