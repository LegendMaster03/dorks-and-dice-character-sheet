/** Conventional Origin selection is an adapter over the canonical direct proposal.
 * Discovery controls navigation; only a current Core evaluation can validate choices.
 */
import type { NativePreset, NativeMethod, RuleNode, DependencyChange } from "../../guided/contracts.js";
import type { NativeOriginDraftProposal, NativeOriginPreview } from "../origin-api.js";
import { isEditableDirectNativeOrigin, originPreviewCandidates, requiredDirectOriginChoices,
    changeDirectOriginChoice } from "../origin-builder-model.js";

export interface PresetSelection {
    speciesId: string;
    ancestryId: string | null;
}
export interface PresetEntry {
    preset: NativePreset;
    path: readonly NativePreset[];
    selection: PresetSelection | null;
    reason?: string;
}

/** Resolve arbitrary ancestry depth, including browse-only intermediates.
 * A gated ancestor can restrict its leaf; a nonselectable ancestor alone can not.
 * Fail closed for unknown kinds, missing parents and cycles.
 */
export function presetEntries(presets: readonly NativePreset[], methodId: string): readonly PresetEntry[] {
    const byId = new Map(presets.map(p => [p.id, p]));
    if (byId.size !== presets.length) throw new Error("Duplicate native preset identities.");
    return presets.map(preset => {
        const path: NativePreset[] = [];
        const seen = new Set<string>();
        let node: NativePreset | undefined = preset;
        while (node && !seen.has(node.id)) {
            seen.add(node.id);
            path.unshift(node);
            if (node.parentId === null) break;
            node = byId.get(node.parentId);
        }
        const root = path[0];
        let reason: string | undefined;
        if (!root || root.kind !== "species" || root.parentId !== null ||
            path.slice(1).some(p => p.kind !== "ancestry"))
            reason = "This native hierarchy is unavailable.";
        else if (path.some(p => p.acquisitionMethodIds.length > 0 &&
            !p.acquisitionMethodIds.includes(methodId)))
            reason = "Not available through ordinary initial creation.";
        else if (!preset.selectable || preset.ancestrySelectionRequired)
            reason = preset.ancestrySelectionRequired ? "Choose an Ancestry." : "Browse only.";
        return { preset, path, reason,
            selection: reason || !root ? null : { speciesId: root.id,
                ancestryId: preset.kind === "ancestry" ? preset.id : null } };
    });
}
export function presetRuleNodes(entries: readonly PresetEntry[]): readonly RuleNode[] {
    return entries.map(e => ({ id: e.preset.id, parentId: e.preset.parentId,
        label: e.preset.name, selectable: e.selection !== null, reason: e.reason }));
}
export function suggestedUpbringing(entry: PresetEntry): string | null {
    // A cultural suggestion is not persisted as part of the Origin proposal.
    return entry.preset.defaultUpbringingId;
}
export function initialMethod(method: NativeMethod): boolean {
    return method.stage !== "post-origination" &&
        (method.support === "initial-preview" || method.support === "assembly-preview");
}

/** Restrict this editor to simple choice-only direct drafts. Unknown evidence
 * must remain intact rather than being removed by the conventional editor.
 */
export function editableConventional(proposal: NativeOriginDraftProposal): boolean {
    return isEditableDirectNativeOrigin(proposal) &&
        Object.keys(proposal).every(k => ["id", "intent", "method", "contributors", "candidates",
            "manifestations", "selectedSpeciesId", "selectedAncestryId"].includes(k) ||
            proposal[k] == null || Array.isArray(proposal[k]) && (proposal[k] as unknown[]).length === 0) &&
        proposal.manifestations.every(m => Object.keys(m).every(k =>
            ["id", "candidateId", "choices"].includes(k)) && m.id === "selection:" + m.candidateId &&
            (m.choices ?? []).every(c => Object.keys(c).every(k =>
                ["requirementId", "selectedOptionIds"].includes(k))));
}
export function selectPreset(proposal: NativeOriginDraftProposal, selection: PresetSelection): NativeOriginDraftProposal {
    if (!editableConventional(proposal)) throw new Error("This Origin requires its own editor.");
    if (proposal.selectedSpeciesId === selection.speciesId &&
        (proposal.selectedAncestryId ?? null) === selection.ancestryId) return proposal;
    return { ...proposal, selectedSpeciesId: selection.speciesId,
        selectedAncestryId: selection.ancestryId, manifestations: [] };
}
function record(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}
/** Verify the response belongs to the requested direct proposal, not merely
 * another proposal with the same character-local ID.
 */
export function matchingPreview(preview: NativeOriginPreview, proposal: NativeOriginDraftProposal,
    rulesetId: string, fingerprint: string): boolean {
    const evaluated = preview.result.evaluatedProposal;
    return preview.previewOnly === true && !preview.contentChangedSinceRequest &&
        preview.rulesetId === rulesetId && preview.fingerprint === fingerprint &&
        record(evaluated) && evaluated.id === proposal.id &&
        (evaluated.intent === 0 || evaluated.intent === "Direct") &&
        record(evaluated.method) && evaluated.method.ruleId === proposal.method.ruleId &&
        evaluated.method.methodId === proposal.method.methodId &&
        (evaluated.selectedSpeciesId ?? null) === (proposal.selectedSpeciesId ?? null) &&
        (evaluated.selectedAncestryId ?? null) === (proposal.selectedAncestryId ?? null) &&
        Array.isArray(evaluated.candidates);
}

export interface PresetChange {
    proposal: NativeOriginDraftProposal;
    review: DependencyChange;
}
/** Retention is based on exact source occurrence, requirement and compatible
 * options from two native previews of the same snapshot. The caller must
 * preview the retained result again before persisting it.
 */
export function reconcilePreset(previous: NativeOriginDraftProposal, target: NativeOriginDraftProposal,
    before: NativeOriginPreview, after: NativeOriginPreview,
    rulesetId: string, fingerprint: string,
    catalogOptions: ReadonlyMap<string, readonly string[]>): PresetChange {
    if (!editableConventional(previous) || !editableConventional(target) ||
        !matchingPreview(before, previous, rulesetId, fingerprint) ||
        !matchingPreview(after, target, rulesetId, fingerprint))
        throw new Error("Current native evaluations are required to review a preset change.");
    const oldChoices = requiredDirectOriginChoices(before, previous);
    const newChoices = requiredDirectOriginChoices(after, target);
    const oldCandidates = originPreviewCandidates(before, previous);
    const newCandidates = originPreviewCandidates(after, target);
    const retained: string[] = [], reconsider: string[] = [];
    let proposal = target;
    for (const m of previous.manifestations) {
        for (const choice of m.choices ?? []) {
            const old = oldChoices.find(c => c.candidateId === m.candidateId && c.requirementId === choice.requirementId);
            const next = newChoices.find(c => c.candidateId === m.candidateId && c.requirementId === choice.requirementId);
            const oldSource = oldCandidates.find(c => c.id === m.candidateId);
            const newSource = newCandidates.find(c => c.id === m.candidateId);
            const options = next?.optionCatalogId ? catalogOptions.get(next.optionCatalogId) : next?.finiteOptions;
            const label = oldSource?.displayName ?? "Previously configured characteristic";
            if (old && next && oldSource && newSource && newSource.eligibility === "Eligible" &&
                oldSource.sourceId === newSource.sourceId && oldSource.occurrencePath === newSource.occurrencePath &&
                oldSource.characteristic === newSource.characteristic &&
                old.optionCatalogId === next.optionCatalogId && options &&
                choice.selectedOptionIds.length <= next.maximum &&
                choice.selectedOptionIds.every(id => options.includes(id))) {
                proposal = changeDirectOriginChoice(proposal, m.candidateId, choice.requirementId, choice.selectedOptionIds);
                retained.push(label);
            } else {
                reconsider.push(label);
            }
        }
    }
    const available = newChoices.filter(c => !oldChoices.some(o =>
        o.candidateId === c.candidateId && o.requirementId === c.requirementId))
        .map(c => newCandidates.find(n => n.id === c.candidateId)?.displayName ?? "New required choice");
    return { proposal, review: { retained, reconsider, available } };
}
