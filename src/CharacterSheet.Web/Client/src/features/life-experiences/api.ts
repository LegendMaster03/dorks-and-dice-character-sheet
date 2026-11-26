import { buildCharacterSheetApiUrl } from "../../character-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import { previewNativeLifeExperiences, type NativeRulesetMetadata } from "../../rules-core-native-api.js";
import type { DraftView, EvaluationView } from "../guided/contracts.js";
import { decodeExperiencePreview, toNativeProposal, validationIssues, type ExperienceCatalog, type ExperienceDraft,
    type ExperiencePreview, type NativeChoice, type ExperienceNode } from "./model.js";

type Fetcher = typeof globalThis.fetch;

/** The public native options route currently returns only identities and titles.
 * Do not infer selectability, parentage, or descriptions from these records. */
export async function loadExperienceTitleCatalog(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<ExperienceCatalog> {
    if (!metadata.capabilities.nativeOptionSearch) throw new Error("Native catalog discovery is unavailable.");
    const nodes: ExperienceNode[] = [];
    const labels: Record<string, string> = Object.create(null);
    for (const category of ["upbringing", "background", "skill", "competency", "item", "spell"] as const) {
        const seen = new Set<string>();
        let total: number | null = null;
        for (let offset = 0; ; ) {
            const query = new URLSearchParams({ fingerprint: metadata.fingerprint, category, q: "", limit: "100", offset: String(offset) });
            const response = await fetcher(buildCharacterSheetApiUrl(environment, "/api/native-rules/options?" + query.toString()),
                { method: "GET", credentials: "same-origin", headers: { Accept: "application/json" } });
            if (!response.ok) throw new Error(response.status === 409 ? "Rules changed; reload the catalog." :
                "Native catalog discovery is unavailable.");
            const page = await response.json();
            if (page?.contractVersion !== 1 || page.rulesetId !== metadata.rulesetId ||
                page.fingerprint !== metadata.fingerprint || page.category !== category ||
                !Number.isSafeInteger(page.total) || page.total < 0 || page.total > 100000 ||
                (total !== null && total !== page.total) || page.offset !== offset ||
                !Array.isArray(page.options) || page.options.length > 100 ||
                offset + page.options.length > page.total)
                throw new Error("Invalid native catalog page.");
            total = page.total as number;
            for (const option of page.options) {
                if (typeof option?.id !== "string" || !option.id || typeof option.name !== "string" ||
                    !option.name.trim() || option.kind !== category || seen.has(option.id))
                    throw new Error("Invalid or repeated native catalog identity.");
                seen.add(option.id);
                labels[option.id] = option.name;
                if (category === "upbringing" || category === "background") nodes.push({
                    id: option.id, label: option.name, kind: category, selectable: false,
                    reason: "Native selection and hierarchy metadata unavailable.",
                    detail: { title: option.name, description: "Native definition details are not exposed by title discovery." }
                });
            }
            offset += page.options.length;
            if (offset === total) break;
            if (!page.options.length) throw new Error("Incomplete native catalog page.");
        }
    }
    return { rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint, nodes, labels, hierarchyComplete: false };
}

/** A draft can exist with only one selected source. The native resolver needs
 * both, so do not send a placeholder ID. Evaluation has no state side effects. */
export async function evaluateExperienceDraft(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    draft: DraftView<ExperienceDraft>, previousChoices: readonly NativeChoice[],
    fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<EvaluationView<ExperiencePreview> | null> {
    if (draft.rulesetId !== metadata.rulesetId || draft.fingerprint !== metadata.fingerprint)
        throw new Error("Draft rules changed; explicit review is required.");
    const proposal = toNativeProposal(draft.proposal, previousChoices);
    if (!proposal) return null;
    const envelope = await previewNativeLifeExperiences(environment, metadata, proposal, fetcher);
    const result = decodeExperiencePreview(envelope.result, metadata);
    return { rulesetId: envelope.rulesetId, fingerprint: envelope.fingerprint, draftRevision: draft.revision,
        previewOnly: true, result, issues: validationIssues(result) };
}

/** The host owns revisioned persistence, requests and stale-response handling.
 * Do not store a second mutable draft or substitute local storage for access checks. */
export interface ExperienceDraftHost {
    load(characterId: string): Promise<DraftView<ExperienceDraft> | null>;
    save(characterId: string, expectedRevision: number, proposal: ExperienceDraft): Promise<DraftView<ExperienceDraft>>;
}
