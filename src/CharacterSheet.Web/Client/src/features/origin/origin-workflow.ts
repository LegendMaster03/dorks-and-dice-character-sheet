/**
 * Coordinates owner-scoped Origin drafts and native-only option discovery.
 * No mutation uses browser-provided grants or an evaluated preview as authority.
 */
import type { HostEnvironment } from "../../host-environment.js";
import { loadNativeRuleset, searchNativeOptions,
    type NativeOptionSummary, type NativeRulesetMetadata } from "../../rules-core-native-api.js";
import { CharacterSheetApiError } from "../../character-api.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import {
    activateNativeOrigin,
    loadOriginDraft,
    loadOriginState,
    previewOriginProposal,
    saveOriginDraft,
    type NativeOriginDraftView,
    type NativeOriginPreview,
    type NativeOriginSavedState
} from "./origin-api.js";
import {
    createOriginParentSourceInspectionWorkflow,
    type OriginParentSourceInspectionState
} from "./origin-parent-source-workflow.js";
import {
    changeDirectOriginChoice,
    changeDirectOriginIdentity,
    changeMixedNativeParent,
    changeNativeOriginMode,
    createInitialOriginProposal,
    canActivateDirectNativeOrigin,
    isEditableDirectNativeOrigin,
    isEditableMixedNativeOrigin,
    mixedNativeParentSources,
    type MixedNativeParentSlot
} from "./origin-builder-model.js";

export type OriginChooser =
    | { kind: "closed" }
    | { kind: "open"; category: "species" | "ancestry" | "skill";
        candidateId?: string; requirementId?: string;
        parentSlot?: MixedNativeParentSlot;
        query: string; status: "loading" | "ready" | "error";
        options: readonly NativeOptionSummary[]; error?: string };

export interface OriginUiState {
    status: "idle" | "loading" | "ready" | "error";
    draft: NativeOriginDraftView | null;
    savedState: NativeOriginSavedState | null;
    preview: NativeOriginPreview | null;
    catalog: NativeRulesetMetadata | null;
    readOnly: boolean;
    chooser: OriginChooser;
    parentSources: OriginParentSourceInspectionState;
    busy: boolean;
    message?: string;
}

export interface OriginWorkflow {
    current(): Readonly<OriginUiState>;
    load(characterId: string): Promise<void>;
    openChooser(category: "species" | "ancestry" | "skill",
        candidateId?: string, requirementId?: string,
        parentSlot?: MixedNativeParentSlot): Promise<void>;
    searchChooser(query: string): Promise<void>;
    closeChooser(): void;
    chooseOption(characterId: string, id: string): Promise<void>;
    clearAncestry(characterId: string): Promise<void>;
    setMode(characterId: string, mode: "direct" | "mixed"): Promise<void>;
    clearMixedParent(characterId: string, slot: MixedNativeParentSlot): Promise<void>;
    setFiniteChoice(characterId: string, candidateId: string,
        requirementId: string, id: string | null): Promise<void>;
    activate(characterId: string): Promise<void>;
    loadParentOptions(characterId: string): Promise<void>;
    selectParentSource(characterId: string, contributorId: string,
        parentCharacterId: string): void;
    reviewParentSources(characterId: string): Promise<void>;
}

export function createOriginWorkflow(
    environment: HostEnvironment, notify: () => void
): OriginWorkflow {
    const parentSources = createOriginParentSourceInspectionWorkflow(
        environment, () => update({ parentSources: parentSources.current() }));
    let state: OriginUiState = {
        status: "idle", draft: null, savedState: null, preview: null, catalog: null,
        readOnly: false, chooser: { kind: "closed" },
        parentSources: parentSources.current(), busy: false
    };
    let currentCharacterId: string | null = null;
    let sequence = 0;
    let searchSequence = 0;

    function update(partial: Partial<OriginUiState>): void {
        state = { ...state, ...partial };
        notify();
    }

    async function load(characterId: string): Promise<void> {
        parentSources.reset();
        const token = ++sequence;
        currentCharacterId = characterId;
        update({
            status: "loading", draft: null, savedState: null, preview: null,
            catalog: null, chooser: { kind: "closed" }, message: undefined
        });
        try {
            const [draft, authorized, catalog] = await Promise.all([
                loadOriginDraft(environment, characterId),
                loadOriginState(environment, characterId),
                loadNativeRuleset(environment)
            ]);
            if (token !== sequence) return;
            let preview = draft.preview ?? null;
            let previewError: string | undefined;
            if (draft.draft !== null && authorized.savedState === null && preview === null) {
                try {
                    preview = await previewOriginProposal(environment,
                        draft.draft.rulesetId, draft.draft.fingerprint,
                        draft.draft.proposal);
                } catch (error) {
                    previewError = requestErrorMessage(error);
                }
            }
            if (token !== sequence) return;
            update({
                status: "ready", draft: draft.draft,
                savedState: authorized.savedState, readOnly: authorized.readOnly,
                catalog, preview, message: previewError,
                chooser: { kind: "closed" }, busy: false
            });
            if (draft.draft !== null && authorized.savedState === null &&
                isEditableMixedNativeOrigin(draft.draft.proposal)) {
                parentSources.prepare(characterId, draft.draft.revision,
                    mixedNativeParentSources(draft.draft.proposal)
                        .filter(source => source.sourceId !== null)
                        .map(source => ({
                            contributorId: source.slot, sourceId: source.sourceId!
                        })));
            }
        } catch (error) {
            if (token !== sequence) return;
            update({
                status: "error", draft: null, savedState: null,
                preview: null, catalog: null, message: requestErrorMessage(error),
                chooser: { kind: "closed" }, busy: false
            });
        }
    }

    async function searchChooser(query: string): Promise<void> {
        const chooser = state.chooser;
        if (chooser.kind !== "open" || state.catalog === null) return;
        const token = ++searchSequence;
        const key = `${chooser.category}:${chooser.candidateId ?? ""}:${chooser.requirementId ?? ""}:${chooser.parentSlot ?? ""}`;
        update({
            chooser: { ...chooser, query, status: "loading", options: [], error: undefined }
        });
        try {
            const page = await searchNativeOptions(environment, state.catalog,
                chooser.category, query, 100);
            const current = state.chooser;
            if (token !== searchSequence || current.kind !== "open" ||
                `${current.category}:${current.candidateId ?? ""}:${current.requirementId ?? ""}:${current.parentSlot ?? ""}` !== key) return;
            update({ chooser: { ...current, query, status: "ready",
                options: page.options, error: undefined } });
        } catch (error) {
            const current = state.chooser;
            if (token !== searchSequence || current.kind !== "open") return;
            update({ chooser: { ...current, query, status: "error",
                options: [], error: requestErrorMessage(error) } });
        }
    }

    async function openChooser(category: "species" | "ancestry" | "skill",
        candidateId?: string, requirementId?: string,
        parentSlot?: MixedNativeParentSlot): Promise<void> {
        const proposal = state.draft?.proposal ?? createInitialOriginProposal(
            currentCharacterId ?? "");
        const editableDirect = isEditableDirectNativeOrigin(proposal);
        const editableMixed = isEditableMixedNativeOrigin(proposal);
        if (state.status !== "ready" || state.busy || state.readOnly ||
            state.savedState !== null || state.catalog === null ||
            (!editableDirect && !editableMixed) ||
            (editableMixed && (category === "skill" || parentSlot === undefined)) ||
            (editableDirect && parentSlot !== undefined)) return;
        ++searchSequence;
        update({ chooser: { kind: "open", category, candidateId, requirementId,
            parentSlot, query: "", status: "loading", options: [] },
            message: undefined });
        await searchChooser("");
    }

    function closeChooser(): void {
        ++searchSequence;
        update({ chooser: { kind: "closed" } });
    }

    async function saveProposal(characterId: string,
        proposal: NonNullable<OriginUiState["draft"]>["proposal"]): Promise<void> {
        if (currentCharacterId !== characterId || state.status !== "ready" ||
            state.busy || state.readOnly || state.savedState !== null ||
            state.catalog === null) return;
        const token = ++sequence;
        const expectedRevision = state.draft?.revision ?? 0;
        // Editing a draft invalidates every previous proposed parent binding,
        // including responses to native requests already in flight.
        parentSources.reset();
        update({ busy: true, message: undefined });
        try {
            const response = await saveOriginDraft(environment, characterId,
                state.catalog.rulesetId, state.catalog.fingerprint,
                proposal, expectedRevision);
            if (token !== sequence) return;
            update({ busy: false, draft: response.draft,
                preview: response.preview ?? null,
                chooser: { kind: "closed" },
                message: response.preview === null || response.preview === undefined
                    ? "Origin saved, but the native evaluation is not available."
                    : undefined });
            if (response.draft !== null &&
                isEditableMixedNativeOrigin(response.draft.proposal)) {
                parentSources.prepare(characterId, response.draft.revision,
                    mixedNativeParentSources(response.draft.proposal)
                        .filter(source => source.sourceId !== null)
                        .map(source => ({
                            contributorId: source.slot, sourceId: source.sourceId!
                        })));
            }
        } catch (error) {
            if (token !== sequence) return;
            // A 409 is an optimistic-concurrency conflict, not an instruction
            // to automatically overwrite a newer independently edited draft.
            update({ busy: false, message: error instanceof CharacterSheetApiError
                && error.status === 409
                    ? "Origin changed in another session. Reload before editing."
                    : requestErrorMessage(error) });
        }
    }

    async function chooseOption(characterId: string, id: string): Promise<void> {
        if (state.status !== "ready" || state.chooser.kind !== "open" ||
            state.catalog === null || state.savedState !== null ||
            !state.chooser.options.some(option => option.id === id)) return;
        const chooser = state.chooser;
        const draft = state.draft?.proposal ??
            createInitialOriginProposal(characterId);
        if (isEditableMixedNativeOrigin(draft)) {
            if (!chooser.parentSlot || chooser.category === "skill" ||
                !id.startsWith(chooser.category + ".")) return;
            await saveProposal(characterId, changeMixedNativeParent(
                draft, chooser.parentSlot, id));
            return;
        }
        if (!isEditableDirectNativeOrigin(draft)) return;
        if (chooser.category === "skill") {
            if (!chooser.candidateId || !chooser.requirementId) return;
            await saveProposal(characterId, changeDirectOriginChoice(draft,
                chooser.candidateId, chooser.requirementId, [id]));
        } else {
            if (chooser.category === "ancestry" && !draft.selectedSpeciesId) return;
            await saveProposal(characterId, changeDirectOriginIdentity(draft,
                chooser.category, id));
        }
    }

    async function clearAncestry(characterId: string): Promise<void> {
        const proposal = state.draft?.proposal;
        if (!proposal || !isEditableDirectNativeOrigin(proposal)) return;
        await saveProposal(characterId, changeDirectOriginIdentity(
            proposal, "ancestry", null));
    }

    async function setMode(characterId: string, mode: "direct" | "mixed"): Promise<void> {
        if (state.status !== "ready" || state.busy || state.savedState !== null ||
            state.readOnly) return;
        const proposal = state.draft?.proposal ??
            createInitialOriginProposal(characterId);
        if (!isEditableDirectNativeOrigin(proposal) &&
            !isEditableMixedNativeOrigin(proposal)) return;
        await saveProposal(characterId, changeNativeOriginMode(proposal, mode));
    }

    async function clearMixedParent(characterId: string,
        slot: MixedNativeParentSlot): Promise<void> {
        const proposal = state.draft?.proposal;
        if (proposal === undefined || !isEditableMixedNativeOrigin(proposal)) return;
        await saveProposal(characterId, changeMixedNativeParent(
            proposal, slot, null));
    }

    async function setFiniteChoice(characterId: string, candidateId: string,
        requirementId: string, id: string | null): Promise<void> {
        const proposal = state.draft?.proposal;
        if (!proposal || !isEditableDirectNativeOrigin(proposal)) return;
        await saveProposal(characterId, changeDirectOriginChoice(
            proposal, candidateId, requirementId, id === null ? [] : [id]));
    }

    async function activate(characterId: string): Promise<void> {
        if (state.status !== "ready" || state.savedState !== null || state.readOnly ||
            state.busy || state.draft === null ||
            !canActivateDirectNativeOrigin(state.draft.proposal, state.preview) ||
            currentCharacterId !== characterId) return;
        const token = ++sequence;
        update({ busy: true, message: undefined });
        try {
            await activateNativeOrigin(environment, characterId, state.draft.revision);
            if (token !== sequence) return;
            await load(characterId);
        } catch (error) {
            if (token !== sequence) return;
            update({ busy: false, message: requestErrorMessage(error) });
        }
    }

    async function loadParentOptions(characterId: string): Promise<void> {
        if (currentCharacterId !== characterId || state.status !== "ready" ||
            state.busy || state.savedState !== null ||
            !state.draft || !isEditableMixedNativeOrigin(state.draft.proposal)) return;
        await parentSources.loadOptions();
    }

    function selectParentSource(characterId: string, contributorId: string,
        parentCharacterId: string): void {
        if (currentCharacterId !== characterId || state.status !== "ready" ||
            state.busy || state.savedState !== null ||
            !state.draft || !isEditableMixedNativeOrigin(state.draft.proposal)) return;
        parentSources.select(contributorId, parentCharacterId);
    }

    async function reviewParentSources(characterId: string): Promise<void> {
        if (currentCharacterId !== characterId || state.status !== "ready" ||
            state.busy || state.savedState !== null ||
            !state.draft || !isEditableMixedNativeOrigin(state.draft.proposal)) return;
        await parentSources.review();
    }

    return {
        current: () => state, load, openChooser, searchChooser, closeChooser,
        chooseOption, clearAncestry, setMode, clearMixedParent,
        setFiniteChoice, activate, loadParentOptions, selectParentSource,
        reviewParentSources
    };
}
