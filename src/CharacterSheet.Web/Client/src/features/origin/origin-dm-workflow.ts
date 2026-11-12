/**
 * Dedicated DM mixed-Origin review state. It deliberately does not load the
 * character owner editor, build, inventory, Class, or full character profile.
 */
import type { FetchLike } from "../../character-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import {
    activateDmApprovedMixedOrigin, reviewDmMixedOrigin,
    type MixedOriginDmReview
} from "./origin-dm-api.js";

export interface OriginDmReviewState {
    status: "idle" | "loading" | "ready" | "error" | "applied";
    review: MixedOriginDmReview | null;
    selectedCandidateIds: readonly string[];
    busy: boolean;
    committedRevision?: number;
    message?: string;
}

export interface OriginDmWorkflow {
    current(): Readonly<OriginDmReviewState>;
    load(characterId: string, campaignId: string): Promise<void>;
    toggleCandidate(candidateId: string): void;
    apply(characterId: string, campaignId: string): Promise<void>;
}

/**
 * Selection is local UI state, never approval evidence. A refresh clears it,
 * and the server independently reauthorizes scope and exact source candidates
 * before applying any grant.
 */
export function createOriginDmWorkflow(
    environment: HostEnvironment,
    notify: () => void,
    fetcher: FetchLike = window.fetch.bind(window)
): OriginDmWorkflow {
    let state: OriginDmReviewState = {
        status: "idle", review: null, selectedCandidateIds: [], busy: false
    };
    let currentTarget: string | null = null;
    let sequence = 0;

    function update(change: Partial<OriginDmReviewState>): void {
        state = { ...state, ...change };
        notify();
    }

    async function load(characterId: string, campaignId: string): Promise<void> {
        const target = campaignId + ":" + characterId;
        currentTarget = target;
        const token = ++sequence;
        update({ status: "loading", review: null, selectedCandidateIds: [],
            busy: false, message: undefined, committedRevision: undefined });
        try {
            const review = await reviewDmMixedOrigin(environment,
                characterId, campaignId, fetcher);
            if (token !== sequence || currentTarget !== target) return;
            update({ status: "ready", review, busy: false });
        } catch (error) {
            if (token !== sequence || currentTarget !== target) return;
            update({ status: "error", busy: false, message: requestErrorMessage(error) });
        }
    }

    function toggleCandidate(candidateId: string): void {
        if (state.status !== "ready" || state.busy || state.review === null) return;
        const candidate = state.review.candidates.find(x => x.id === candidateId);
        if (!candidate || candidate.requiresChoices) return;
        const next = new Set(state.selectedCandidateIds);
        if (next.has(candidateId)) next.delete(candidateId);
        else next.add(candidateId);
        update({ selectedCandidateIds: [...next], message: undefined });
    }

    async function apply(characterId: string, campaignId: string): Promise<void> {
        const target = campaignId + ":" + characterId;
        if (state.status !== "ready" || state.busy || !state.review ||
            state.selectedCandidateIds.length !== 5 || currentTarget !== target) return;
        const review = state.review;
        const candidateIds = [...state.selectedCandidateIds];
        // Check the set against this exact review, never a stale character.
        if (candidateIds.some(id => !review.candidates.some(x =>
            x.id === id && !x.requiresChoices))) return;
        const token = ++sequence;
        update({ busy: true, message: undefined });
        try {
            const applied = await activateDmApprovedMixedOrigin(
                environment, characterId, campaignId,
                review.draftRevision, candidateIds, fetcher);
            if (token !== sequence || currentTarget !== target) return;
            update({ status: "applied", review: null, busy: false,
                selectedCandidateIds: [], committedRevision: applied.revision });
        } catch (error) {
            if (token !== sequence || currentTarget !== target) return;
            update({ busy: false, message: requestErrorMessage(error) });
        }
    }

    return {
        current: () => state, load, toggleCandidate, apply
    };
}
