import type { CatalogIdentity, ChoiceRequirement, RuleDetail, RuleNode, ValidationIssue } from "../guided/contracts.js";
import type { NativeLifeExperienceProposal, NativeLifeExperiencePreviewResult } from "../../rules-core-native-api.js";

export type ExperienceKind = "upbringing" | "background";
export interface ExperienceNode extends RuleNode {
    readonly kind: ExperienceKind;
    readonly detail: RuleDetail;
}
export interface ExperienceCatalog extends CatalogIdentity {
    readonly nodes: readonly ExperienceNode[];
    // True only for an independently discovered complete native hierarchy.
    readonly hierarchyComplete: boolean;
    readonly labels: Readonly<Record<string, string>>;
}
export interface ExperienceDraft {
    readonly upbringingId: string | null;
    readonly backgroundId: string | null;
    readonly selections: Readonly<Record<string, readonly string[]>>;
    readonly startingItemIds: Readonly<Record<string, string>>;
}
export const emptyExperienceDraft = (): ExperienceDraft => ({
    upbringingId: null, backgroundId: null, selections: {}, startingItemIds: {}
});
export type NativeChoice = NativeLifeExperiencePreviewResult["choices"][number];
export type NativeGrant = NativeLifeExperiencePreviewResult["candidateGrants"][number];
export interface ExperienceIssue { code: string; path: string; detail: string }
export interface QualificationSource { sourceId: string; sourceOccurrenceId: string; grantId: string }
export interface Qualification {
    qualificationId: string; equipmentCategory: string; provenance: QualificationSource[];
}
export interface EquipmentPreview extends CatalogIdentity {
    contractVersion: 1; status: string; qualifications: Qualification[];
    requiredChoices: { choiceKey: string; count: number; eligibleQualificationIds: string[] }[];
    issues: ExperienceIssue[];
}
export interface EquipmentReplacement {
    provenance: QualificationSource; originalQualificationId: string; replacementQualificationId: string;
}
export interface ExperiencePreview extends Omit<NativeLifeExperiencePreviewResult, "issues" | "candidateEquipment" | "equipmentReplacements"> {
    issues: ExperienceIssue[];
    candidateEquipment: EquipmentPreview | null;
    equipmentReplacements: EquipmentReplacement[];
}
function record(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}
function strings(value: unknown): value is string[] {
    return Array.isArray(value) && value.every(v => typeof v === "string" && v.length > 0);
}
function fields(value: unknown, keys: string[]): value is Record<string, unknown> {
    return record(value) && keys.every(k => typeof value[k] === "string" && (value[k] as string).length > 0);
}
function integer(value: unknown, min = 0): value is number {
    return typeof value === "number" && Number.isSafeInteger(value) && value >= min;
}
function issue(value: unknown): value is ExperienceIssue {
    return fields(value, ["code", "path", "detail"]);
}
function provenance(value: unknown): value is QualificationSource {
    return fields(value, ["sourceId", "sourceOccurrenceId", "grantId"]);
}
function equipment(value: unknown, identity: CatalogIdentity): value is EquipmentPreview {
    return record(value) && value.contractVersion === 1 && value.rulesetId === identity.rulesetId &&
        value.fingerprint === identity.fingerprint && typeof value.status === "string" &&
        Array.isArray(value.qualifications) && value.qualifications.every(q =>
            fields(q, ["qualificationId", "equipmentCategory"]) && Array.isArray(q.provenance) &&
            q.provenance.every(provenance)) && Array.isArray(value.requiredChoices) &&
        value.requiredChoices.every(c => fields(c, ["choiceKey"]) && integer(c.count, 1) &&
            strings(c.eligibleQualificationIds)) && Array.isArray(value.issues) && value.issues.every(issue);
}
/** Decode the nested DTOs deliberately left opaque in the shared preview bridge. */
export function decodeExperiencePreview(raw: NativeLifeExperiencePreviewResult, identity: CatalogIdentity): ExperiencePreview {
    if (raw.earnedProjection !== null || !["invalid", "needs-choices", "unavailable", "resolved"].includes(raw.status) ||
        !raw.sources.every(s => fields(s, ["sourceId", "occurrenceId"]) && typeof s.inherited === "boolean") ||
        new Set(raw.sources.map(s => s.sourceId)).size !== raw.sources.length ||
        !raw.choices.every(c => fields(c, ["key", "kind"]) && integer(c.count, 1) && c.count <= 32 &&
            strings(c.optionIds) && new Set(c.optionIds).size === c.optionIds.length &&
            (c.contextConstraint == null || typeof c.contextConstraint === "string")) ||
        new Set(raw.choices.map(c => c.key)).size !== raw.choices.length ||
        !raw.selectedPaths.every(p => fields(p, ["sourceId", "occurrenceId", "benefitId", "optionId"])) ||
        !raw.candidateGrants.every(g => fields(g, ["sourceId", "occurrenceId", "benefitId", "kind", "targetId"]) &&
            integer(g.ranks) && typeof g.proficient === "boolean" &&
            (g.originalTargetId == null || typeof g.originalTargetId === "string") &&
            (g.grantPath == null || typeof g.grantPath === "string")) ||
        !raw.startingItems.every(i => fields(i, ["key", "name"]) && integer(i.quantity, 1) &&
            strings(i.ordinaryOptions) && typeof i.requiresMundane === "boolean" &&
            typeof i.requiresInexpensive === "boolean" &&
            (i.expectedEquipmentTypeId == null || typeof i.expectedEquipmentTypeId === "string") &&
            (i.selectedItemId == null || typeof i.selectedItemId === "string")) ||
        !raw.currency.every(c => fields(c, ["sourceId", "occurrenceId", "currency"]) && integer(c.quantity)) ||
        !raw.issues.every(issue) ||
        !(raw.candidateEquipment === null || equipment(raw.candidateEquipment, identity)) ||
        !raw.equipmentReplacements.every(r => fields(r, ["originalQualificationId", "replacementQualificationId"]) &&
            provenance(r.provenance))) throw new Error("Invalid Life Experiences preview.");
    return raw as ExperiencePreview;
}
export function labelFor(catalog: ExperienceCatalog, id: string): string {
    return catalog.labels[id] ?? catalog.nodes.find(n => n.id === id)?.label ??
        id.split(".").at(-1)!.replaceAll("-", " ").replace(/^./, c => c.toUpperCase());
}
function sourceChain(catalog: ExperienceCatalog, id: string | null, kind: ExperienceKind): Set<string> {
    const ids = new Set<string>();
    while (id !== null) {
        const node = catalog.nodes.find(n => n.id === id && n.kind === kind);
        if (!node || ids.has(id)) throw new Error("Incomplete or invalid native source hierarchy.");
        ids.add(id);
        id = node.parentId ?? null;
    }
    return ids;
}
export interface SourceChange {
    draft: ExperienceDraft; retained: string[]; reconsider: string[];
}
/** Preserve both the independent source and common cultural parents. No
 * inferred ancestry or default upbringing is ever added. */
export function changeExperienceSource(draft: ExperienceDraft, kind: ExperienceKind, id: string,
    catalog: ExperienceCatalog): SourceChange {
    const node = catalog.nodes.find(n => n.id === id && n.kind === kind);
    if (!node?.selectable) throw new Error("This source is not selectable.");
    const field = kind === "upbringing" ? "upbringingId" : "backgroundId";
    if (draft[field] === id) return { draft, retained: Object.keys(draft.selections), reconsider: [] };
    const next = { ...draft, [field]: id };
    // Without hierarchy evidence do not guess which inherited choices disappear.
    // Keep the entire draft for review rather than erase an unrelated selection.
    if (!catalog.hierarchyComplete) return { draft: next, retained: [], reconsider: [
        ...Object.keys(draft.selections), ...Object.keys(draft.startingItemIds)] };
    const active = new Set([...sourceChain(catalog, next.upbringingId, "upbringing"),
        ...sourceChain(catalog, next.backgroundId, "background")]);
    const retained: string[] = [], reconsider: string[] = [];
    const keep = (key: string) => {
        const isActive = active.has(key.split("/")[0]!);
        (isActive ? retained : reconsider).push(key);
        return isActive;
    };
    return { draft: { ...next, selections: Object.fromEntries(Object.entries(draft.selections).filter(([k]) => keep(k))),
        startingItemIds: Object.fromEntries(Object.entries(draft.startingItemIds).filter(([k]) => keep(k))) }, retained, reconsider };
}
/** Only exact native choice identities enter the draft; changing an outer
 * option removes that option's descendants, not the independent baseline. */
export function changeExperienceChoice(draft: ExperienceDraft, choice: NativeChoice, id: string,
    selected: boolean): ExperienceDraft {
    if (!choice.optionIds.includes(id)) throw new Error("Option was not offered by Rules Core.");
    const previous = draft.selections[choice.key] ?? [];
    const next = selected ? choice.count === 1 ? [id] : [...new Set([...previous, id])]
        : previous.filter(x => x !== id);
    if (next.length > choice.count) throw new Error("The native choice count would be exceeded.");
    const removed = previous.filter(x => !next.includes(x));
    const selections = Object.fromEntries(Object.entries(draft.selections).filter(([key]) =>
        key !== choice.key && !removed.some(option => key.startsWith(choice.key + "/" + option + "/") ||
            key === choice.key + "/" + option)));
    if (next.length) selections[choice.key] = next;
    return { ...draft, selections };
}
/** Incomplete counted selections stay in the durable draft; they are not sent
 * as invalid complete selections. Unknown keys are retained for explicit review. */
export function toNativeProposal(draft: ExperienceDraft, choices: readonly NativeChoice[] = []): NativeLifeExperienceProposal | null {
    if (!draft.upbringingId || !draft.backgroundId) return null;
    const byKey = new Map(choices.map(c => [c.key, c]));
    return { upbringingId: draft.upbringingId, backgroundId: draft.backgroundId,
        selections: Object.fromEntries(Object.entries(draft.selections).filter(([key, ids]) => {
            const choice = byKey.get(key);
            return ids.length > 0 && (!choice || ids.length === choice.count);
        })), startingItemIds: draft.startingItemIds };
}
/** Inactive and conflicting selections remain visible until explicit review.
 * Never discard a choice simply because a preview failed or was incomplete. */
export function reconsiderChoices(draft: ExperienceDraft, preview: ExperiencePreview): string[] {
    const invalidKeys = new Set(preview.issues.filter(i => ["unexpected-life-experience-choice",
        "invalid-life-experience-selection", "unexpected-starting-item-choice"].includes(i.code)).map(i => i.path));
    return [...new Set([...Object.keys(draft.selections), ...Object.keys(draft.startingItemIds)])]
        .filter(key => invalidKeys.has(key));
}
export function removeReviewedChoice(draft: ExperienceDraft, key: string): ExperienceDraft {
    const selections = Object.fromEntries(Object.entries(draft.selections).filter(([k]) => k !== key && !k.startsWith(key + "/")));
    const startingItemIds = Object.fromEntries(Object.entries(draft.startingItemIds).filter(([k]) => k !== key));
    return { ...draft, selections, startingItemIds };
}
export function choiceTree(choices: readonly NativeChoice[], draft: ExperienceDraft, catalog: ExperienceCatalog): ChoiceRequirement[] {
    const parent = new Map<string, { key: string; option: string }>();
    for (const child of choices) {
        const candidates = choices.flatMap(p => p.optionIds.map(option => ({ key: p.key, option })))
            .filter(p => child.key === p.key + "/" + p.option || child.key.startsWith(p.key + "/" + p.option + "/"))
            .sort((a, b) => b.key.length - a.key.length);
        if (candidates[0]) parent.set(child.key, candidates[0]);
    }
    const build = (c: NativeChoice): ChoiceRequirement => ({
        id: c.key, label: (c.kind === "duplicate-skill" ? "Replace duplicate Skill proficiency" :
            c.kind === "duplicate-tool" ? "Replace duplicate tool qualification" :
            labelFor(catalog, c.key.split("/").at(-1)!)) + (c.contextConstraint ? " (context verification required)" : ""),
        kind: c.count > 1 ? "counted" : "finite", minimum: c.count, maximum: c.count,
        source: { label: labelFor(catalog, c.key.split("/")[0]!) },
        selectedIds: draft.selections[c.key] ?? [],
        options: c.optionIds.map(id => ({ id, label: labelFor(catalog, id),
            children: choices.filter(child => parent.get(child.key)?.key === c.key &&
                parent.get(child.key)?.option === id).map(build) })),
        issues: c.contextConstraint ? [{ id: c.key, severity: "authorization-required",
            message: "The source-specific context must be independently verified." }] : undefined
    });
    return choices.filter(c => !parent.has(c.key)).map(build);
}
export function validationIssues(preview: ExperiencePreview): ValidationIssue[] {
    return preview.issues.map((i, index) => ({ id: String(index),
        severity: i.code.includes("unverified") ? "authorization-required" :
            i.code.includes("required") ? "incomplete" :
            i.code.startsWith("invalid") || i.code.startsWith("unexpected") ? "invalid" : "unavailable",
        message: i.detail, decision: { id: i.path.includes("/item/") ? "equipment" : "benefits",
            label: i.path.includes("/item/") ? "Equipment" : "Benefits" } }));
}
