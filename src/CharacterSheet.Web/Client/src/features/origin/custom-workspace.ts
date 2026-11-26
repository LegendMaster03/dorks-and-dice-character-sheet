/** Application composition for the native custom Origin editors.
 * Canonical persisted state belongs to OriginWorkflow. This host retains only
 * method navigation, fingerprint-scoped discovery, and failed unsaved edits. */
import type { HostEnvironment } from "../../host-environment.js";
import { createButton, createElement, createInlineState } from "../../ui/components.js";
import type { DraftSaveStatus, DraftView, EvaluationView, MethodEditor, NativeMethod, NativePreset,
    NativePreviewEnvelope } from "../guided/contracts.js";
import type { NativeOriginDraftProposal } from "./origin-api.js";
import type { OriginUiState, OriginWorkflow } from "./origin-workflow.js";
import { createInitialOriginProposal } from "./origin-builder-model.js";
import { editableConventional } from "./conventional/model.js";
import { biologicalEditor, type BiologicalEditorContext } from "./biological/editor.js";
import { supportsBiologicalDraft } from "./biological/model.js";
import { constructMethodEditor, type ConstructEditorContext } from "./construct/editor.js";
import { emptyConstructDraft, packConstructDraft, unpackConstructDraft, type Configuration,
    type ConstructDraft, type ConstructPreview } from "./construct/model.js";
import { loadComponentConfiguration, loadConstructCatalog, type ConstructCatalog } from "./construct/native.js";

export type CustomOriginContext =
    | { kind: "biological"; editor: BiologicalEditorContext }
    | { kind: "construct"; editor: ConstructEditorContext };

type PendingEdit = { proposal: NativeOriginDraftProposal; revision: number; fingerprint: string };
const button = "dd-button dd-button--secondary";
const errorText = (error: unknown) => error instanceof Error ? error.message : "Native Origin request failed.";
const sameMethod = (a: NativeOriginDraftProposal, b: NativeOriginDraftProposal) =>
    a.intent === b.intent && a.method.ruleId === b.method.ruleId && a.method.methodId === b.method.methodId;

/** Keep the guided workspace mounted after initial saves and during stale-rule
 * review. Unsupported legacy drafts and established Origins retain their
 * existing read-only/reconciliation route. This is not an authorization gate. */
export function canMountGuidedOriginWorkspace(state: OriginUiState | undefined): state is OriginUiState {
    if (!state || state.status !== "ready" || !state.catalog || state.savedState) return false;
    const draft = state.draft;
    if (!draft) return true;
    if (supportsBiologicalDraft(draft.proposal) || unpackConstructDraft(draft.proposal)) return true;
    return editableConventional(draft.proposal) && draft.rulesetId === state.catalog.rulesetId &&
        draft.fingerprint === state.catalog.fingerprint && !state.preview?.contentChangedSinceRequest;
}

export function createCustomOriginHost(environment: HostEnvironment, characterId: string,
    workflow: OriginWorkflow, notify: () => void) {
    let biologicalDecision = "contributors", constructDecision = "assembly";
    let query = "", selectedInstanceId: string | null = null;
    let catalog: ConstructCatalog | null = null, catalogKey: string | null = null;
    let attemptedCatalogKey: string | null = null, catalogError: string | null = null;
    let requestGeneration = 0, loadingCatalog = false;
    let pending: PendingEdit | null = null, saving = false, error: string | null = null;
    const configurations = new Map<string, NativePreviewEnvelope<Configuration>>();
    const loadingConfigurations = new Set<string>();
    const state = workflow.current;
    const metadataKey = () => state().catalog ? state().catalog!.rulesetId + ":" + state().catalog!.fingerprint : null;
    const editable = () => state().status === "ready" && !state().readOnly && !state().savedState &&
        !state().busy && !saving && !!state().catalog &&
        (!state().draft || state().draft!.characterId === characterId &&
            state().draft!.rulesetId === state().catalog!.rulesetId &&
            state().draft!.fingerprint === state().catalog!.fingerprint);

    function draftStatus(): DraftSaveStatus {
        const s = state();
        if (s.readOnly || s.savedState) return "read-only";
        if (saving || s.busy) return "saving";
        if (pending) return (s.draft?.revision ?? 0) !== pending.revision ||
            s.catalog?.fingerprint !== pending.fingerprint ||
            s.message?.includes("changed in another session") ? "conflict" : "failed";
        if (s.draft && (s.draft.rulesetId !== s.catalog?.rulesetId || s.draft.fingerprint !== s.catalog?.fingerprint))
            return "stale";
        return s.draft ? "saved" : "unavailable";
    }
    async function persist(edit: PendingEdit): Promise<void> {
        if (!editable() || (state().draft?.revision ?? 0) !== edit.revision ||
            state().catalog?.fingerprint !== edit.fingerprint)
            throw new Error("The Origin draft changed. Reload before editing.");
        pending = edit; saving = true; error = null; notify();
        try {
            await workflow.saveCustomDraft(characterId, edit.proposal, edit.revision);
            pending = null;
        } catch (e) {
            error = errorText(e);
            throw e;
        } finally {
            saving = false; notify();
        }
    }
    async function save(proposal: NativeOriginDraftProposal, revision: number, fingerprint: string): Promise<void> {
        if (pending) throw new Error("Resolve the previous unsaved change first.");
        if (!editable() || (state().draft?.revision ?? 0) !== revision || state().catalog?.fingerprint !== fingerprint)
            throw new Error("The Origin draft changed. Reload before editing.");
        const previous = state().draft?.proposal;
        if (previous && !sameMethod(previous, proposal) &&
            !window.confirm("Replace the current Origin draft? Its existing configuration will be removed only if the new draft saves successfully."))
            return;
        await persist({ proposal, revision, fingerprint });
    }
    async function retry(): Promise<void> {
        if (!pending) { await workflow.load(characterId); return; }
        if (draftStatus() === "conflict")
            throw new Error("The saved Origin changed. Discard the pending change and reload before editing.");
        await persist(pending);
    }
    async function discoverCatalog(): Promise<void> {
        const metadata = state().catalog, key = metadataKey();
        if (!metadata || !key || loadingCatalog) return;
        const generation = ++requestGeneration;
        attemptedCatalogKey = key; loadingCatalog = true; catalogError = null; notify();
        try {
            const result = await loadConstructCatalog(environment, metadata);
            if (generation === requestGeneration && metadataKey() === key) {
                catalog = result; catalogKey = key;
            }
        } catch (e) {
            if (generation === requestGeneration && metadataKey() === key) catalogError = errorText(e);
        } finally {
            if (generation === requestGeneration) { loadingCatalog = false; notify(); }
        }
    }
    function syncDiscovery(): void {
        const key = metadataKey();
        if (catalogKey !== key && attemptedCatalogKey !== key) {
            ++requestGeneration;
            catalog = null; catalogKey = null; catalogError = null; loadingCatalog = false;
            configurations.clear(); loadingConfigurations.clear();
            // Rendering must not synchronously mutate application state.
            if (key) queueMicrotask(() => {
                if (metadataKey() === key && attemptedCatalogKey !== key) void discoverCatalog();
            });
        }
    }
    async function requestConfiguration(definitionId: string): Promise<void> {
        const metadata = state().catalog, key = metadataKey(), generation = requestGeneration;
        if (!metadata || !key || loadingConfigurations.has(definitionId) ||
            configurations.has(definitionId) || catalogKey !== key) return;
        loadingConfigurations.add(definitionId);
        try {
            const result = await loadComponentConfiguration(environment, metadata, definitionId);
            if (metadataKey() === key && requestGeneration === generation) configurations.set(definitionId, result);
        } catch (e) {
            if (metadataKey() === key && requestGeneration === generation) error = errorText(e);
        } finally {
            if (requestGeneration === generation) { loadingConfigurations.delete(definitionId); notify(); }
        }
    }
    function commonDraft<T>(proposal: T | null): DraftView<T> | null {
        const d = state().draft;
        return d && d.fingerprint && proposal !== null ? { proposal, revision: d.revision,
            rulesetId: d.rulesetId, fingerprint: d.fingerprint, saveStatus: draftStatus(),
            readOnly: !editable() || !!pending } : null;
    }
    function renderCustomStatus(): HTMLElement {
        const area = createElement("div");
        if (error) area.append(createInlineState(error, "error"));
        if (pending) {
            const conflict = draftStatus() === "conflict";
            area.append(createInlineState(conflict ? "The saved Origin changed. Your pending edit has not been applied." :
                saving ? "Saving Origin draft…" : "Your pending edit has not been saved.", "warning"),
                createButton("Retry draft save", button, () => void retry().catch(e => {
                    error = errorText(e); notify();
                }), saving || conflict),
                createButton("Discard pending change and reload", button, () => {
                    if (window.confirm("Discard the unsaved Origin change?")) {
                        pending = null; error = null; void workflow.load(characterId);
                    }
                }, saving));
        }
        const s = state();
        const d = s.draft;
        if (d && s.catalog && d.rulesetId === s.catalog.rulesetId &&
            d.fingerprint !== s.catalog.fingerprint && s.status === "ready" &&
            !s.readOnly && !s.savedState && !pending &&
            (supportsBiologicalDraft(d.proposal) || unpackConstructDraft(d.proposal))) {
            area.append(createInlineState("Native rules changed. Review the saved choices against the current catalog before editing.", "warning"),
                createButton("Review with current rules", button, () => {
                    if (!window.confirm("Re-evaluate this saved Origin under current rules? Existing choices will be preserved; native costs, requirements and allowance may change.")) return;
                    saving = true; error = null; notify();
                    void workflow.renewCustomDraft(characterId, d.revision, d.fingerprint).catch(e => {
                        error = errorText(e);
                    }).finally(() => { saving = false; notify(); });
                }, saving || s.busy));
        }
        return area;
    }
    const methodEditors: readonly MethodEditor<CustomOriginContext>[] = [
        { reference: biologicalEditor.reference, decisions: biologicalEditor.decisions,
            render: context => {
                if (context.kind !== "biological") throw new Error("Invalid Biological editor context.");
                return biologicalEditor.render(context.editor);
            } },
        { reference: constructMethodEditor.reference, decisions: constructMethodEditor.decisions,
            render: context => {
                if (context.kind !== "construct") throw new Error("Invalid Construct editor context.");
                const c = context.editor;
                const content = createElement("div");
                if (catalogError) content.append(createInlineState(catalogError, "error"),
                    createButton("Retry Construct catalog", button, () => void discoverCatalog(), loadingCatalog));
                if (loadingCatalog || !catalog && !catalogError)
                    content.append(createInlineState("Loading native Construct catalog…", "loading"));
                if (!c.draft) {
                    content.append(createButton("Start Construct draft", button, () => {
                        if (!catalog) return;
                        void c.save(emptyConstructDraft("initial-" + characterId, catalog.initialAllowance)).catch(e => {
                            error = errorText(e); notify();
                        });
                    }, c.readOnly || !c.catalog));
                } else content.append(constructMethodEditor.render(c));
                return content;
            } }
    ];
    function methodContext(method: NativeMethod, presets: readonly NativePreset[]): CustomOriginContext {
        const s = state(), revision = s.draft?.revision ?? 0, fingerprint = s.catalog?.fingerprint ?? "";
        const readOnly = !editable() || !!pending;
        const stored = s.draft?.proposal;
        if (method.reference.methodId === biologicalEditor.reference.methodId) {
            const draft = commonDraft(stored && !editableConventional(stored) &&
                stored.method.methodId === method.reference.methodId ? stored : null);
            const p = s.preview;
            const preview: EvaluationView<unknown> | null = draft && p && p.previewOnly &&
                !p.contentChangedSinceRequest && s.previewDraftRevision === draft.revision ?
                { draftRevision: draft.revision, rulesetId: p.rulesetId, fingerprint: p.fingerprint,
                    previewOnly: true, result: p, issues: [] } : null;
            return { kind: "biological", editor: { characterId, draft, preview, decisionId: biologicalDecision,
                readOnly, navigate: id => { biologicalDecision = id; notify(); },
                save: proposal => save(proposal, revision, fingerprint), retry, presets } };
        }
        syncDiscovery();
        const draft = commonDraft(stored ? unpackConstructDraft(stored) : null);
        const p = s.constructPreview;
        const preview: EvaluationView<ConstructPreview> | null = draft && p &&
            s.previewDraftRevision === draft.revision ? { draftRevision: draft.revision,
                rulesetId: p.rulesetId, fingerprint: p.fingerprint, previewOnly: true,
                result: p.result, issues: [] } : null;
        return { kind: "construct", editor: { characterId, draft, preview, decisionId: constructDecision,
            readOnly, navigate: id => { constructDecision = id; notify(); },
            save: (proposal: ConstructDraft) => save(packConstructDraft(proposal), revision, fingerprint), retry,
            catalog: catalogKey === metadataKey() ? catalog : null, configurations, query, selectedInstanceId,
            setQuery: value => {
                const input = document.activeElement instanceof HTMLInputElement ? document.activeElement : null;
                const start = input?.selectionStart, end = input?.selectionEnd;
                query = value; notify();
                const replacement = document.querySelector<HTMLInputElement>('input[aria-label="Search components"]');
                replacement?.focus();
                if (replacement && start != null && end != null) replacement.setSelectionRange(start, end);
            },
            inspectInstance: id => { selectedInstanceId = id; },
            requestConfiguration: id => void requestConfiguration(id),
            reportError: message => { error = message; notify(); } } };
    }
    return { methodEditors, methodContext, renderCustomStatus,
        hasPendingCustomEdit: () => pending !== null || saving,
        canResetToConventional: () => {
            const p = state().draft?.proposal;
            return editable() && !pending && !!p &&
                (supportsBiologicalDraft(p) || unpackConstructDraft(p) !== null);
        },
        resetToConventional: async () => {
            const s = state();
            if (!s.catalog || !s.draft) return;
            await save(createInitialOriginProposal(characterId), s.draft.revision, s.catalog.fingerprint);
        }
    };
}
