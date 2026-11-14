/**
 * Temporary owner-side source inspection. The selected character IDs never
 * enter the saved Origin draft and never establish biological relationships.
 * Every result is tied to the current character, draft revision, and inputs.
 */
import type { HostEnvironment } from "../../host-environment.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import {
    loadOwnerOriginParentOptions,
    reviewOwnerVisibleParentSourceAlignment,
    type NativeOriginParentSourceAlignmentResponse,
    type NativeOriginParentSourceBinding,
    type OwnerOriginParentOption
} from "./origin-api.js";

export interface OriginNativeContributorSource {
    contributorId: string;
    sourceId: string;
}

export interface OriginParentSourceInspectionState {
    optionsStatus: "idle" | "loading" | "ready" | "error";
    options: readonly OwnerOriginParentOption[];
    sources: readonly OriginNativeContributorSource[];
    selected: Readonly<Record<string, string>>;
    busy: boolean;
    result: NativeOriginParentSourceAlignmentResponse | null;
    message?: string;
}

export interface OriginParentSourceInspectionWorkflow {
    current(): Readonly<OriginParentSourceInspectionState>;
    reset(): void;
    prepare(characterId: string, draftRevision: number,
        sources: readonly OriginNativeContributorSource[]): void;
    loadOptions(): Promise<void>;
    select(contributorId: string, parentCharacterId: string): void;
    review(): Promise<void>;
}

interface OriginParentSourceDependencies {
    loadOptions: (environment: HostEnvironment) => Promise<readonly OwnerOriginParentOption[]>;
    review: (environment: HostEnvironment, childId: string, draftRevision: number,
        bindings: readonly NativeOriginParentSourceBinding[]) =>
        Promise<NativeOriginParentSourceAlignmentResponse>;
}

const defaultDependencies: OriginParentSourceDependencies = {
    loadOptions: loadOwnerOriginParentOptions,
    review: reviewOwnerVisibleParentSourceAlignment
};

function empty(): OriginParentSourceInspectionState {
    return {
        optionsStatus: "idle", options: [], sources: [],
        selected: {}, busy: false, result: null
    };
}

export function createOriginParentSourceInspectionWorkflow(
    environment: HostEnvironment,
    notify: () => void,
    dependencies: OriginParentSourceDependencies = defaultDependencies
): OriginParentSourceInspectionWorkflow {
    let state = empty();
    let childId: string | null = null;
    let revision: number | null = null;
    let generation = 0;

    function update(partial: Partial<OriginParentSourceInspectionState>): void {
        state = { ...state, ...partial };
        notify();
    }

    function reset(): void {
        ++generation;
        childId = null;
        revision = null;
        state = empty();
        notify();
    }

    function prepare(characterId: string, draftRevision: number,
        sources: readonly OriginNativeContributorSource[]): void {
        reset();
        if (characterId.trim().length === 0 ||
            !Number.isSafeInteger(draftRevision) || draftRevision < 1 ||
            sources.length < 1 || sources.length > 16 ||
            sources.some(source => !source.contributorId || !source.sourceId) ||
            new Set(sources.map(source => source.contributorId)).size !== sources.length) {
            return;
        }
        childId = characterId;
        revision = draftRevision;
        update({ sources: sources.map(source => ({ ...source })) });
    }

    async function loadOptions(): Promise<void> {
        if (childId === null || revision === null || state.busy ||
            state.optionsStatus === "loading" || state.sources.length === 0) return;
        const token = ++generation;
        const target = childId;
        update({
            optionsStatus: "loading", options: [], selected: {},
            result: null, message: undefined
        });
        try {
            const options = await dependencies.loadOptions(environment);
            if (token !== generation || target !== childId) return;
            update({
                optionsStatus: "ready",
                options: options.filter(option =>
                    option.id.toLowerCase() !== target.toLowerCase()),
                selected: {}, result: null, message: undefined
            });
        } catch (error) {
            if (token !== generation || target !== childId) return;
            update({
                optionsStatus: "error", options: [], selected: {},
                result: null, message: requestErrorMessage(error)
            });
        }
    }

    function select(contributorId: string, parentCharacterId: string): void {
        if (state.optionsStatus !== "ready" || state.busy ||
            !state.sources.some(source => source.contributorId === contributorId)) return;
        if (parentCharacterId.length > 0 &&
            !state.options.some(option => option.id === parentCharacterId)) return;
        const next = { ...state.selected };
        if (parentCharacterId.length === 0) {
            delete next[contributorId];
        } else {
            // Two sources must not silently bind to one purported individual.
            // These bindings are still only transient proposed identities.
            if (Object.entries(next).some(([source, parent]) =>
                source !== contributorId && parent === parentCharacterId)) {
                update({ message: "Choose a different Character for each proposed source." });
                return;
            }
            next[contributorId] = parentCharacterId;
        }
        ++generation;
        update({ selected: next, result: null, message: undefined });
    }

    async function review(): Promise<void> {
        if (childId === null || revision === null ||
            state.optionsStatus !== "ready" || state.busy ||
            state.sources.length === 0) return;
        const sources = state.sources.map(source => ({ ...source }));
        const bindings = sources.map(source => ({
            contributorId: source.contributorId,
            parentCharacterId: state.selected[source.contributorId] ?? ""
        }));
        if (bindings.some(binding => !state.options.some(option =>
                option.id === binding.parentCharacterId)) ||
            new Set(bindings.map(binding => binding.parentCharacterId)).size !==
                bindings.length) {
            update({ message: "Select a different owned Character for every native source." });
            return;
        }
        const token = ++generation;
        const target = childId;
        const expectedRevision = revision;
        update({ busy: true, result: null, message: undefined });
        try {
            const result = await dependencies.review(environment,
                target, expectedRevision, bindings);
            if (token !== generation || target !== childId ||
                expectedRevision !== revision) return;
            // Verify the response remains an exact comparison to the current
            // source references rather than trusting a stale or partial list.
            if (result.status !== "ready" ||
                result.inspectionOnly !== true ||
                result.relationshipVerified !== false ||
                result.inheritanceAuthorized !== false ||
                result.activeGrants !== false ||
                result.effectiveMechanicsReady !== false ||
                result.draftRevision !== expectedRevision ||
                result.matches.length !== bindings.length ||
                !result.matches.every((match, index) =>
                    match.contributorId === bindings[index].contributorId &&
                    match.parentCharacterId.toLowerCase() ===
                        bindings[index].parentCharacterId.toLowerCase() &&
                    match.nativeSourceId === sources[index].sourceId)) {
                throw new Error("Parent source inspection does not match this saved Origin draft.");
            }
            update({ busy: false, result, message: undefined });
        } catch (error) {
            if (token !== generation || target !== childId) return;
            update({ busy: false, result: null, message: requestErrorMessage(error) });
        }
    }

    return {
        current: () => state,
        reset, prepare, loadOptions, select, review
    };
}
