import { buildCharacterSheetApiUrl } from "../../character-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import { previewNativeLifeExperiences, type NativeRulesetMetadata } from "../../rules-core-native-api.js";
import type { DraftView, EvaluationView } from "../guided/contracts.js";
import { decodeExperiencePreview, toNativeProposal, replayExperienceProposal, reconsiderChoices, validationIssues, type ExperienceCatalog, type ExperienceDraft,
    type ExperiencePreview, type ExperienceNode } from "./model.js";

type Fetcher = typeof globalThis.fetch;

/** Older Core builds return titles alone. Selection requires both complete
 * native source catalogs and an intact hierarchy at the requested fingerprint. */
export async function loadExperienceTitleCatalog(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    fetcher: Fetcher = globalThis.fetch.bind(globalThis),
    categories: readonly ("upbringing" | "background" | "skill" | "competency")[] =
        ["upbringing", "background", "skill", "competency"]): Promise<ExperienceCatalog> {
    if (!metadata.capabilities.nativeOptionSearch) throw new Error("Native catalog discovery is unavailable.");
    const nodes: ExperienceNode[] = [];
    const labels: Record<string, string> = Object.create(null);
    let hierarchyComplete = true;
    for (const category of categories) {
        const experienceCategory = category === "upbringing" || category === "background";
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
            const hasHierarchy = page.includesLifeExperienceHierarchy === true;
            if (experienceCategory && !hasHierarchy) hierarchyComplete = false;
            for (const option of page.options) {
                if (typeof option?.id !== "string" || !option.id || typeof option.name !== "string" ||
                    !option.name.trim() || option.kind !== category || seen.has(option.id))
                    throw new Error("Invalid or repeated native catalog identity.");
                seen.add(option.id);
                labels[option.id] = option.name;
                if (!experienceCategory) continue;
                const source = option.lifeExperience;
                if (hasHierarchy && (!source || typeof source.selectable !== "boolean" ||
                    (source.parentId !== null && (typeof source.parentId !== "string" ||
                        !source.parentId.trim() || source.parentId.length > 160)) ||
                    (source.description !== null && (typeof source.description !== "string" ||
                        !source.description.trim() || source.description.length > 4000))))
                    throw new Error("Invalid native Life Experiences metadata.");
                nodes.push({
                    id: option.id, label: option.name, kind: category,
                    parentId: hasHierarchy ? source.parentId : null,
                    selectable: hasHierarchy && source.selectable,
                    detail: { title: option.name, description: hasHierarchy ? source.description ?? undefined : undefined }
                });
            }
            offset += page.options.length;
            if (offset === total) break;
            if (!page.options.length) throw new Error("Incomplete native catalog page.");
        }
    }
    if (hierarchyComplete) {
        const byId = new Map(nodes.map(node => [node.id, node]));
        hierarchyComplete = byId.size === nodes.length && nodes.every(node => {
            const seen = new Set([node.id]);
            let parentId = node.parentId;
            while (parentId) {
                const parent = byId.get(parentId);
                if (node.kind !== "upbringing" || parent?.kind !== node.kind ||
                    seen.has(parentId) || seen.size >= 16) return false;
                seen.add(parentId);
                parentId = parent.parentId;
            }
            return true;
        });
    }
    return { rulesetId: metadata.rulesetId, fingerprint: metadata.fingerprint, labels, hierarchyComplete,
        nodes: hierarchyComplete ? nodes : nodes.map(node => ({
            ...node, parentId: null, selectable: false, reason: "Native selection and hierarchy metadata unavailable."
        })) };
}

/** A draft can exist with only one selected source. The native resolver needs
 * both, so do not send a placeholder ID. Evaluation has no state side effects. */
export async function evaluateExperienceDraft(environment: HostEnvironment, metadata: NativeRulesetMetadata,
    draft: DraftView<ExperienceDraft>, fetcher: Fetcher = globalThis.fetch.bind(globalThis)): Promise<EvaluationView<ExperiencePreview> | null> {
    if (draft.rulesetId !== metadata.rulesetId || draft.fingerprint !== metadata.fingerprint)
        throw new Error("Draft rules changed; explicit review is required.");
    let proposal = toNativeProposal(draft.proposal);
    if (!proposal) return null;
    // Discover the actual active branches before replaying complete selections.
    // This supports reload of partial counted choices and changed sources without
    // ever feeding an incomplete count or guessing the native option structure.
    const rejected = new Set<string>();
    const signature = (input: typeof proposal) => JSON.stringify([
        Object.entries(input?.selections ?? {}).sort(([a], [b]) => a.localeCompare(b)),
        Object.entries(input?.startingItemIds ?? {}).sort(([a], [b]) => a.localeCompare(b))
    ]);
    for (let round = 0; round < 12; round++) {
        const envelope = await previewNativeLifeExperiences(environment, metadata, proposal, fetcher);
        const result = decodeExperiencePreview(envelope.result, metadata);
        for (const issue of result.issues) if (["unexpected-life-experience-choice", "invalid-life-experience-selection",
            "unexpected-starting-item-choice"].includes(issue.code)) rejected.add(issue.path);
        const next = replayExperienceProposal(draft.proposal, result);
        const filtered = { ...next, selections: Object.fromEntries(Object.entries(next.selections ?? {}).filter(([key]) => !rejected.has(key))),
            startingItemIds: Object.fromEntries(Object.entries(next.startingItemIds ?? {}).filter(([key]) => !rejected.has(key))) };
        const invalidSource = result.issues.some(i => i.code === "invalid-life-experience-source" ||
            i.code === "invalid-life-experience-parent");
        if (invalidSource || signature(filtered) === signature(proposal)) {
            const review = invalidSource ? [] : [...new Set([...reconsiderChoices(draft.proposal, result), ...rejected])];
            return { rulesetId: envelope.rulesetId, fingerprint: envelope.fingerprint, draftRevision: draft.revision,
                previewOnly: true, result: { ...result, draftReviewKeys: review }, issues: [...validationIssues(result), ...review.map((_, index) => ({
                    id: "draft-review-" + index, severity: "conflict" as const,
                    message: "A saved choice needs review against the current active native options.",
                    decision: { id: "benefits", label: "Benefits" }
                }))] };
        }
        proposal = filtered;
    }
    throw new Error("Native choice discovery did not stabilize. The draft remains unchanged.");
}

/** The host owns revisioned persistence, requests and stale-response handling.
 * Do not store a second mutable draft or substitute local storage for access checks. */
export interface ExperienceDraftHost {
    load(characterId: string): Promise<DraftView<ExperienceDraft> | null>;
    save(characterId: string, expectedRevision: number, proposal: ExperienceDraft): Promise<DraftView<ExperienceDraft>>;
}
