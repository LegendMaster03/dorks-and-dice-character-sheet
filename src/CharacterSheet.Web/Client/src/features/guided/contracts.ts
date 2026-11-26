/**
 * Shared editor-facing contracts, not a replacement for native DTOs or the
 * application stores. Method adapters translate verified native responses into
 * these presentation models. No client grant or authority is represented here.
 */
export interface CatalogIdentity {
    readonly rulesetId: string;
    readonly fingerprint: string;
}
export interface NativePreviewEnvelope<T> extends CatalogIdentity {
    readonly contractVersion: 1;
    readonly previewOnly: true;
    readonly result: T;
}
export interface NativePreset {
    readonly id: string;
    readonly name: string;
    readonly description?: string | null;
    readonly kind: string;
    readonly parentId: string | null;
    readonly selectable: boolean;
    readonly defaultUpbringingId: string | null;
    readonly acquisitionMethodIds: readonly string[];
    readonly ancestrySelectionRequired: boolean;
}
export interface NativeMethodReference {
    readonly ruleId: string;
    readonly methodId: string;
}
export interface NativeMethod {
    readonly reference: NativeMethodReference;
    readonly name: string;
    readonly description?: string | null;
    readonly stage: string | null;
    readonly support: string;
    readonly defaultAvailability: string | null;
}
export interface NativeDiscoveryEnvelope extends CatalogIdentity {
    readonly contractVersion: 1;
    readonly previewOnly: true;
}
export interface NativePresetDiscovery extends NativeDiscoveryEnvelope {
    readonly presets: readonly NativePreset[];
}
export interface NativeMethodDiscovery extends NativeDiscoveryEnvelope {
    readonly methods: readonly NativeMethod[];
}
export interface Provenance {
    readonly label: string;
    readonly detail?: string;
}
export interface DecisionLink {
    readonly id: string;
    readonly label: string;
}
export interface ValidationIssue {
    readonly id: string;
    readonly severity: "incomplete" | "conflict" | "invalid" | "authorization-required" | "unavailable" | "warning";
    readonly message: string;
    readonly decision?: DecisionLink;
}
export interface RuleNode {
    readonly id: string;
    readonly parentId?: string | null;
    readonly label: string;
    readonly selectable: boolean;
    readonly reason?: string;
}
export interface DisplayFact {
    readonly label: string;
    readonly value: string;
    readonly source?: Provenance;
}
export interface RuleDetail {
    readonly title: string;
    readonly description?: string;
    readonly facts?: readonly DisplayFact[];
    readonly source?: Provenance;
    readonly features?: readonly RuleDetail[];
}
export interface ChoiceOption {
    readonly id: string;
    readonly label: string;
    readonly unavailableReason?: string;
    readonly source?: Provenance;
    readonly children?: readonly ChoiceRequirement[];
}
export interface ChoiceRequirement {
    readonly id: string;
    readonly label: string;
    readonly kind: "finite" | "counted" | "catalog";
    readonly selectedIds: readonly string[];
    readonly options: readonly ChoiceOption[];
    readonly minimum?: number;
    readonly maximum?: number;
    readonly catalogId?: string;
    readonly source?: Provenance;
    readonly issues?: readonly ValidationIssue[];
}
export interface AllocationView {
    readonly label: string;
    readonly used?: string;
    readonly allowance?: string;
    readonly remaining?: string;
    readonly status: "within" | "over" | "unresolved" | "unavailable";
    readonly detail?: string;
}
export interface DependencyChange {
    readonly retained: readonly string[];
    readonly reconsider: readonly string[];
    readonly available: readonly string[];
}
export type DraftSaveStatus = "unavailable" | "dirty" | "saving" | "saved" | "failed" | "conflict" | "stale" | "read-only";
export interface DraftView<T> extends CatalogIdentity {
    readonly revision: number;
    readonly proposal: T;
    readonly saveStatus: DraftSaveStatus;
    readonly readOnly: boolean;
}
export interface EvaluationView<T> extends CatalogIdentity {
    readonly draftRevision: number;
    readonly previewOnly: true;
    readonly result: T;
    readonly issues: readonly ValidationIssue[];
}
export interface EditorContext<TDraft, TPreview> {
    readonly characterId: string;
    readonly draft: DraftView<TDraft> | null;
    readonly preview: EvaluationView<TPreview> | null;
    readonly decisionId: string;
    readonly readOnly: boolean;
    readonly navigate: (decisionId: string) => void;
    readonly save: (proposal: TDraft) => Promise<void>;
    readonly retry: () => Promise<void>;
}
export interface MethodEditor<TContext> {
    readonly reference: NativeMethodReference;
    readonly decisions: readonly DecisionLink[];
    readonly render: (context: TContext) => HTMLElement;
}

/** Registration is explicit and local to the workspace, not a global state store.
 * Only native-discovered executable initial preview methods are dispatched.
 * A preview editor is never an activation capability.
 */
export function resolveMethodEditor<T>(method: NativeMethod, editors: readonly MethodEditor<T>[]): MethodEditor<T> | null {
    if (method.stage === "post-origination" ||
        (method.support !== "initial-preview" && method.support !== "assembly-preview")) return null;
    const matches = editors.filter(editor => editor.reference.ruleId === method.reference.ruleId
        && editor.reference.methodId === method.reference.methodId);
    return matches.length === 1 ? matches[0]! : null;
}

/** A late or stale response can never be presented as the current evaluation. */
export function isCurrentEvaluation<T>(draft: Pick<DraftView<unknown>, "revision" | "rulesetId" | "fingerprint">,
    evaluation: EvaluationView<T>): boolean {
    return evaluation.previewOnly === true && draft.revision === evaluation.draftRevision
        && draft.rulesetId === evaluation.rulesetId && draft.fingerprint === evaluation.fingerprint;
}

/** Search includes matching ancestors; identity and selectability are unchanged. */
export function matchingRuleNodes(nodes: readonly RuleNode[], query: string): readonly RuleNode[] {
    const term = query.trim().toLocaleLowerCase();
    if (!term) return nodes;
    const byId = new Map(nodes.map(node => [node.id, node]));
    const included = new Set<string>();
    for (const node of nodes) {
        if (!node.label.toLocaleLowerCase().includes(term)) continue;
        let current: RuleNode | undefined = node;
        while (current && !included.has(current.id)) {
            included.add(current.id);
            current = current.parentId ? byId.get(current.parentId) : undefined;
        }
    }
    return nodes.filter(node => included.has(node.id));
}
