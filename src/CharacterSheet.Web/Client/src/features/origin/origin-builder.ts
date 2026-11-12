/**
 * Native Origin editor for direct initial construction. The user can save
 * incomplete proposals; a preview profile is never displayed as active grants.
 * Mixed/nested Origin, transformation and DM controls require later contracts.
 */
import type { CharacterBuildResponse } from "../../builder-api.js";
import { createButton, createElement, createInlineState,
    createSectionCard } from "../../ui/components.js";
import type { OriginUiState } from "./origin-workflow.js";
import {
    hasLegacySpeciesSelections,
    isAuthorizedInitialNativeOrigin,
    isDirectOriginProposal,
    requiredDirectOriginChoices
} from "./origin-builder-model.js";
import type { NativeOriginRequiredChoice } from "./origin-builder-model.js";

export interface OriginBuilderHandlers {
    openChooser(category: "species" | "ancestry" | "skill",
        candidateId?: string, requirementId?: string): void;
    searchChooser(query: string): void;
    closeChooser(): void;
    chooseOption(optionId: string): void;
    clearAncestry(): void;
    setFiniteChoice(candidateId: string, requirementId: string,
        id: string | null): void;
    activate(): void;
    reload(): void;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}

function savedUnderlyingLabel(value: unknown, field: string): string {
    if (!isRecord(value) || !isRecord(value.underlying)) return "Unresolved";
    const found = value.underlying[field];
    return typeof found === "string" && found.length > 0 ? found : "None";
}

function renderSelectedIdentity(
    label: string,
    selectedId: string | null | undefined,
    category: "species" | "ancestry",
    disabled: boolean,
    handlers: OriginBuilderHandlers
): HTMLElement {
    const row = createElement("article", "dd-build-choice");
    row.setAttribute("data-origin-identity", category);
    row.append(
        createElement("h3", "dd-build-choice__label", label),
        createElement("p", "dd-build-choice__value", selectedId ?? "Not selected"));
    const actions = createElement("div", "dd-build-choice__actions");
    actions.append(createButton(
        selectedId ? "Change" : "Choose",
        "dd-button dd-button--secondary",
        () => handlers.openChooser(category), disabled));
    if (category === "ancestry" && selectedId) {
        actions.append(createButton("Clear", "dd-button dd-button--ghost",
            handlers.clearAncestry, disabled));
    }
    row.append(actions);
    return row;
}

function renderRequiredChoice(
    choice: NativeOriginRequiredChoice,
    disabled: boolean,
    handlers: OriginBuilderHandlers
): HTMLElement {
    const article = createElement("article", "dd-build-choice");
    article.setAttribute("data-origin-choice", choice.requirementId);
    article.append(createElement("h3", "dd-build-choice__label", choice.label));

    if (choice.minimum !== 1 || choice.maximum !== 1) {
        article.append(createInlineState(
            "This choice requires a multiple-selection editor, which is not available yet.",
            "warning"));
        return article;
    }
    if (choice.finiteOptions !== null) {
        const label = createElement("label", "dd-rule-chooser__label", "Select an authored option");
        const select = createElement("select", "dd-rule-chooser__input");
        select.setAttribute("aria-label", choice.label);
        const placeholder = createElement("option", "", "Not selected");
        placeholder.value = "";
        select.append(placeholder);
        for (const id of choice.finiteOptions) {
            const option = createElement("option", "", id);
            option.value = id;
            select.append(option);
        }
        select.value = choice.selectedOptionIds[0] ?? "";
        select.disabled = disabled;
        select.addEventListener("change", () => handlers.setFiniteChoice(
            choice.candidateId, choice.requirementId, select.value || null));
        label.append(select);
        article.append(label);
        return article;
    }
    if (choice.optionCatalogId === "any-native-skill") {
        article.append(createElement("p", "dd-build-choice__value",
            choice.selectedOptionIds.length === 0
                ? "No Skill selected"
                : choice.selectedOptionIds.join(", ")));
        article.append(createButton(
            "Choose native Skill", "dd-button dd-button--secondary",
            () => handlers.openChooser("skill", choice.candidateId,
                choice.requirementId), disabled));
        return article;
    }
    article.append(createInlineState(
        choice.optionCatalogId === "feat:origin"
            ? "Origin Feat selection awaits an executable native Feat eligibility contract. No Feat is granted."
            : "This catalog choice is not yet mechanically authorized.",
        "warning"));
    return article;
}

export function renderOriginBuilder(
    characterId: string,
    origin: OriginUiState,
    build: CharacterBuildResponse | null,
    readOnly: boolean,
    handlers: OriginBuilderHandlers
): HTMLElement {
    const section = createSectionCard("Origin — Species & Ancestry", "dd-build dd-origin-builder");
    section.setAttribute("data-native-origin-builder", characterId);
    section.setAttribute("aria-busy", origin.busy || origin.status === "loading"
        ? "true" : "false");

    if (origin.status === "idle" || origin.status === "loading") {
        section.append(createInlineState("Loading Origin state and native rules…", "loading"));
        return section;
    }
    if (origin.status === "error" || origin.catalog === null) {
        section.append(createInlineState(origin.message ??
            "Origin state or native rules are unavailable. No fallback Species selection is applied.",
        "warning"));
        section.append(createButton("Retry Origin load", "dd-button dd-button--secondary",
            handlers.reload));
        return section;
    }

    const legacy = hasLegacySpeciesSelections(build?.foundationalSelections ?? []);
    const locked = readOnly || origin.readOnly || origin.busy ||
        origin.savedState !== null;
    const proposal = origin.draft?.proposal ?? null;
    const direct = proposal === null || isDirectOriginProposal(proposal);
    section.append(createElement("p", "dd-guided-builder__section-copy",
        "An Origin describes the underlying Species and Ancestry configuration. Save a draft for native review; a draft does not grant any traits. Class, Background, and Upbringing benefits are independent."));

    if (legacy) {
        section.append(createInlineState(
            "This Character has earlier Species/Subspecies selections. They remain in the original builder until explicitly reconciled. Native Origin activation is blocked; existing selections are not copied or deleted.",
            "warning"));
    }

    if (origin.savedState !== null) {
        const saved = origin.savedState;
        section.append(createInlineState(
            `Origin construction is committed at revision ${saved.revision}. New construction can not replace it; permanent reconstruction requires its own authorized method.`,
            "neutral"));
        const baseline = createElement("dl", "dd-origin-builder__baseline");
        baseline.append(createElement("dt", "", "Underlying Species"),
            createElement("dd", "", savedUnderlyingLabel(saved.profile, "speciesId")),
            createElement("dt", "", "Underlying Ancestry"),
            createElement("dd", "", savedUnderlyingLabel(saved.profile, "ancestryId")));
        section.append(baseline, createInlineState(
            "The effective cross-pillar character projection is still under development. This saved Origin is not a complete gameplay calculation.",
            "warning"));
        return section;
    }

    if (!direct) {
        section.append(createInlineState(
            "This saved proposal contains a mixed, nested, or custom Origin. Its evidence is preserved, but the direct first-party editor can not modify or activate it. A separately authorized editor is required.",
            "warning"));
        return section;
    }

    if (readOnly || origin.readOnly) {
        section.append(createInlineState("Origin is read-only for this Character.", "readonly"));
    }

    const identity = createElement("div", "dd-build__grid");
    identity.append(renderSelectedIdentity("Species",
        proposal?.selectedSpeciesId, "species", locked, handlers));
    identity.append(renderSelectedIdentity("Ancestry (optional)",
        proposal?.selectedAncestryId, "ancestry",
        locked || !proposal?.selectedSpeciesId, handlers));
    section.append(identity);

    if (origin.draft !== null) {
        section.append(createElement("p", "dd-build__meta",
            `Draft revision ${origin.draft.revision} — not active`));
    }
    const choices = requiredDirectOriginChoices(origin.preview, proposal);
    if (choices.length > 0) {
        const group = createElement("div", "dd-origin-builder__choices");
        group.append(createElement("h3", "dd-build-choice__label",
            "Required native creation choices"));
        for (const choice of choices) {
            group.append(renderRequiredChoice(choice, locked, handlers));
        }
        section.append(group);
    }
    if (origin.preview !== null) {
        const authorized = isAuthorizedInitialNativeOrigin(origin.preview);
        section.append(createInlineState(
            authorized
                ? "Native first-party evaluation is complete. This is still a preview until activated."
                : "Native evaluation is incomplete or unresolved. The saved draft does not grant traits.",
            authorized ? "neutral" : "warning"));
        const issues = origin.preview.result.issues ?? [];
        if (issues.length > 0) {
            const list = createElement("ul", "dd-guided-builder__review-list");
            for (const issue of issues.slice(0, 8)) {
                list.append(createElement("li", "dd-guided-builder__review-item",
                    `${issue.code}: ${issue.detail}`));
            }
            if (issues.length > 8) list.append(createElement("li", "",
                `And ${issues.length - 8} additional native issues.`));
            section.append(list);
        }
        section.append(createButton(
            "Activate Native Origin", "dd-button dd-button--primary",
            handlers.activate,
            locked || legacy || !authorized || origin.draft === null));
    } else if (origin.draft !== null) {
        section.append(createInlineState(
            "The draft is saved, but its current native evaluation is unavailable. Reload before activation.",
            "warning"));
    }

    if (origin.message) {
        section.append(createInlineState(origin.message, "warning"));
    }

    if (origin.chooser.kind === "open") {
        const chooser = origin.chooser;
        const aside = createElement("aside", "dd-rule-chooser");
        aside.setAttribute("data-origin-chooser", chooser.category);
        aside.setAttribute("role", "dialog");
        aside.setAttribute("aria-modal", "false");
        const header = createElement("div", "dd-rule-chooser__header");
        header.append(createElement("h3", "dd-rule-chooser__title",
            `Choose native ${chooser.category === "skill" ? "Skill" :
                chooser.category === "ancestry" ? "Ancestry" : "Species"}`),
            createButton("Close", "dd-button dd-button--ghost", handlers.closeChooser));
        aside.append(header);
        const form = createElement("form", "dd-rule-chooser__search");
        const label = createElement("label", "dd-rule-chooser__label",
            "Search native options");
        const input = createElement("input", "dd-rule-chooser__input");
        input.type = "search";
        input.value = chooser.query;
        input.autocomplete = "off";
        label.append(input);
        const submit = createElement("button", "dd-button dd-button--secondary",
            "Search");
        submit.type = "submit";
        form.append(label, submit);
        form.addEventListener("submit", event => {
            event.preventDefault();
            handlers.searchChooser(input.value);
        });
        aside.append(form);
        if (chooser.status === "loading") {
            aside.append(createInlineState("Loading native choices…", "loading"));
        } else if (chooser.status === "error") {
            aside.append(createInlineState(chooser.error ??
                "Native choices are not available.", "warning"));
        } else if (chooser.options.length === 0) {
            aside.append(createInlineState("No native options matched.", "neutral"));
        } else {
            const list = createElement("ul", "dd-rule-chooser__results");
            for (const item of chooser.options) {
                const row = createElement("li", "dd-rule-result");
                row.append(createButton(item.name, "dd-rule-result__button",
                    () => handlers.chooseOption(item.id), origin.busy));
                row.append(createElement("span", "dd-rule-result__meta", item.id));
                list.append(row);
            }
            aside.append(list);
        }
        section.append(aside);
    }
    return section;
}
