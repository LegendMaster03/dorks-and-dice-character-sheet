/**
 * Standalone DM-only Origin review panel. It renders only campaign-scoped
 * native offers, not the target's private owner-editable Character Sheet.
 */
import { createButton, createElement, createInlineState,
    createSectionCard } from "../../ui/components.js";
import type { OriginDmReviewState } from "./origin-dm-workflow.js";
import type { MixedOriginDmCandidate } from "./origin-dm-api.js";

export interface OriginDmReviewHandlers {
    reload(): void;
    toggle(candidateId: string): void;
    apply(): void;
}

function offeredValue(value: unknown): string {
    if (value === null || value === undefined) return "No offered value";
    if (typeof value === "string") return value;
    try {
        const rendered = JSON.stringify(value);
        return rendered.length > 250 ? rendered.slice(0, 250) + "…" : rendered;
    } catch {
        return "Unrenderable native offer";
    }
}

function renderCandidate(
    candidate: MixedOriginDmCandidate,
    selected: boolean,
    locked: boolean,
    onToggle: (id: string) => void
): HTMLElement {
    const item = createElement("li", "dd-build-choice");
    item.setAttribute("data-dm-origin-candidate", candidate.id);
    const label = createElement("label", "dd-rule-chooser__label");
    const input = createElement("input");
    input.type = "checkbox";
    input.checked = selected;
    input.disabled = locked || candidate.requiresChoices;
    input.setAttribute("aria-label",
        `Select ${candidate.sourceId} ${candidate.occurrencePath}`);
    input.addEventListener("change", () => onToggle(candidate.id));
    const title = createElement("span", "dd-build-choice__label",
        `${candidate.sourceId} — ${candidate.intrinsicSlotId ??
            candidate.occurrencePath}`);
    label.append(input, title);
    item.append(label,
        createElement("p", "dd-build-choice__value", offeredValue(candidate.offeredValue)),
        createElement("p", "dd-build__meta",
            `Source occurrence: ${candidate.occurrencePath} | Contributor: ${candidate.contributorId} | Offer ID: ${candidate.id}`));
    if (candidate.requiresChoices) {
        item.append(createInlineState(
            "This candidate requires a separate authored choice and can not be approved by this review screen.",
            "warning"));
    }
    return item;
}

export function renderDmOriginReview(
    campaignId: string, characterId: string,
    state: OriginDmReviewState, handlers: OriginDmReviewHandlers
): HTMLElement {
    const screen = createElement("main", "dd-sheet-screen");
    screen.setAttribute("data-dm-origin-review", characterId);
    screen.setAttribute("aria-busy", state.busy || state.status === "loading"
        ? "true" : "false");
    const panel = createSectionCard("Mixed Origin — DM review",
        "dd-build dd-origin-builder");
    panel.append(createElement("p", "dd-guided-builder__section-copy",
        "This is a restricted review of a character's proposed native Human–Elf source offers. It does not provide access to the owner's character build, and native source exemplars do not establish real parentage. A reviewed offer is not a grant."));
    panel.append(createElement("p", "dd-build__meta",
        `Campaign: ${campaignId} | Target character: ${characterId}`));

    if (state.status === "idle" || state.status === "loading") {
        panel.append(createInlineState("Checking campaign DM authorization and native offers…", "loading"));
    } else if (state.status === "error") {
        panel.append(createInlineState(state.message ??
            "The DM review could not be loaded. No authorization is inferred.", "warning"),
        createButton("Retry DM review", "dd-button dd-button--secondary", handlers.reload));
    } else if (state.status === "applied") {
        panel.append(createInlineState(
            `Origin construction committed at revision ${state.committedRevision}. This does not certify full cross-pillar character mechanics.`,
            "neutral"));
    } else if (state.review) {
        panel.append(createElement("p", "dd-build__meta",
            `Proposal: ${state.review.proposalId} | Draft revision: ${state.review.draftRevision} | Catalog: ${state.review.rulesetId}`));
        panel.append(createInlineState(
            "Select the five exact native occurrences for the current Human–Elf approval example: creature type, size, walking speed, and the two individually adjudicated traits. The server will reject incompatible or stale selections. No parent package is automatically stacked.",
            "warning"));
        const list = createElement("ul", "dd-guided-builder__review-list");
        for (const candidate of state.review.candidates) {
            list.append(renderCandidate(candidate,
                state.selectedCandidateIds.includes(candidate.id),
                state.busy, handlers.toggle));
        }
        panel.append(list);
        panel.append(createElement("p", "dd-build__meta",
            `Selected offers: ${state.selectedCandidateIds.length} of 5 required by this review screen.`));
        const actions = createElement("div", "dd-build-choice__actions");
        actions.append(
            createButton("Refresh native offers", "dd-button dd-button--secondary",
                handlers.reload, state.busy),
            createButton("Commit DM-approved mixed Origin", "dd-button dd-button--primary",
                handlers.apply,
                state.busy || state.selectedCandidateIds.length !== 5));
        panel.append(actions);
        if (state.message) {
            panel.append(createInlineState(state.message, "warning"));
        }
    }
    screen.append(panel);
    return screen;
}
