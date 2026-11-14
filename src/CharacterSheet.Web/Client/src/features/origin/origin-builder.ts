/**
 * Native Origin editor for direct initial construction. The user can save
 * incomplete proposals; a preview profile is never displayed as active grants.
 * A separate, Site-scoped DM route handles the narrow Human–Elf approval;
 * this owner editor never grants mixed Origin authority or changes forms.
 */
import type { CharacterBuildResponse } from "../../builder-api.js";
import { createButton, createElement, createInlineState,
    createSectionCard } from "../../ui/components.js";
import type { OriginUiState } from "./origin-workflow.js";
import {
    canActivateDirectNativeOrigin,
    hasLegacySpeciesSelections,
    isEditableDirectNativeOrigin,
    isEditableMixedNativeOrigin,
    mixedNativeParentSources,
    requiredDirectOriginChoices,
    type MixedNativeParentSlot
} from "./origin-builder-model.js";
import type { NativeOriginRequiredChoice } from "./origin-builder-model.js";

export interface OriginBuilderHandlers {
    openChooser(category: "species" | "ancestry" | "skill",
        candidateId?: string, requirementId?: string,
        parentSlot?: MixedNativeParentSlot): void;
    searchChooser(query: string): void;
    closeChooser(): void;
    chooseOption(optionId: string): void;
    clearAncestry(): void;
    setMode(mode: "direct" | "mixed"): void;
    clearMixedParent(slot: MixedNativeParentSlot): void;
    setFiniteChoice(candidateId: string, requirementId: string,
        id: string | null): void;
    activate(): void;
    refreshDraft(): void;
    reload(): void;
    loadParentOptions(): void;
    selectParentSource(contributorId: string, parentCharacterId: string): void;
    reviewParentSources(): void;
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
    const direct = proposal === null || isEditableDirectNativeOrigin(proposal);
    const basicMixed = proposal !== null && isEditableMixedNativeOrigin(proposal);
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

    if (!direct && !basicMixed) {
        section.append(createInlineState(
            "This Origin has custom, nested, post-origination, or separately authorized evidence that the basic editor cannot alter. Existing evidence is preserved; use the future trusted editor.",
            "warning"));
        return section;
    }

    if (readOnly || origin.readOnly) {
        section.append(createInlineState("Origin is read-only for this Character.", "readonly"));
    }

    const nativeSnapshotChanged = origin.draft !== null &&
        (origin.draft.rulesetId !== origin.catalog.rulesetId ||
         origin.draft.fingerprint !== origin.catalog.fingerprint ||
         origin.preview?.contentChangedSinceRequest === true);
    if (nativeSnapshotChanged) {
        const refresh = createElement("div", "dd-origin-native-re-review");
        refresh.setAttribute("data-origin-native-re-review", "required");
        refresh.append(createInlineState(
            "The native catalog differs from the saved Origin draft. Previous DM offer selections must be reviewed again. Refreshing re-evaluates the same proposal, increments its draft revision, and does not approve grants. Any choices that no longer satisfy the rules remain unresolved.",
            "warning"));
        refresh.append(createButton(
            "Review draft against current native rules",
            "dd-button dd-button--secondary",
            handlers.refreshDraft, locked));
        section.append(refresh);
    }

    const mode = createElement("div", "dd-build-choice__actions");
    mode.setAttribute("data-origin-construction-mode", direct ? "direct" : "mixed");
    mode.append(createButton(
        "Direct first-party Origin", "dd-button dd-button--secondary",
        () => handlers.setMode("direct"), locked || direct));
    mode.append(createButton(
        "Mixed biological sources", "dd-button dd-button--secondary",
        () => handlers.setMode("mixed"), locked || basicMixed));
    section.append(mode);
    section.append(createElement("p", "dd-guided-builder__section-copy",
        "Changing modes discards the saved draft's source selections and required choices. It does not change an authorized Origin."));

    if (direct) {
        const identity = createElement("div", "dd-build__grid");
        identity.append(renderSelectedIdentity("Species",
            proposal?.selectedSpeciesId, "species", locked, handlers));
        identity.append(renderSelectedIdentity("Ancestry (optional)",
            proposal?.selectedAncestryId, "ancestry",
            locked || !proposal?.selectedSpeciesId, handlers));
        section.append(identity);
    } else if (proposal !== null) {
        // These are source exemplars, not trusted identities of existing
        // parent characters. Do not invent a manifested parent profile.
        section.append(createInlineState(
            "Mixed biological Origin is evidence-only. These two native source references are not verified parent-character profiles. Rules Core does not infer biological compatibility, ancestry inheritance or trait stacking. A campaign DM may separately review and approve exact offers through the restricted DM review route; this owner editor cannot activate mixed Origins.",
            "warning"));
        const parents = createElement("div", "dd-build__grid");
        for (const parent of mixedNativeParentSources(proposal)) {
            const parentCard = createElement("article", "dd-build-choice");
            parentCard.setAttribute("data-mixed-origin-parent", parent.slot);
            parentCard.append(createElement("h3", "dd-build-choice__label",
                parent.slot === "parent-one" ? "Biological source 1" : "Biological source 2"));
            parentCard.append(createElement("p", "dd-build-choice__value",
                parent.sourceId ?? "Not selected"));
            const actions = createElement("div", "dd-build-choice__actions");
            actions.append(createButton("Choose Species source",
                "dd-button dd-button--secondary",
                () => handlers.openChooser("species", undefined, undefined, parent.slot),
                locked));
            actions.append(createButton("Choose Ancestry source",
                "dd-button dd-button--secondary",
                () => handlers.openChooser("ancestry", undefined, undefined, parent.slot),
                locked));
            if (parent.sourceId !== null) {
                actions.append(createButton("Clear source",
                    "dd-button dd-button--ghost",
                    () => handlers.clearMixedParent(parent.slot), locked));
            }
            parentCard.append(actions);
            parents.append(parentCard);
        }
        section.append(parents);

        // A native Species exemplar is not a real parent Character. This
        // read-only inspection lets an owner compare the draft's source IDs
        // against their other Characters without saving a relationship.
        const proposedSources = mixedNativeParentSources(proposal);
        const chosenSources = proposedSources.filter(source => source.sourceId !== null);
        const inspection = origin.parentSources;
        const inspectionPanel = createElement("section", "dd-origin-parent-inspection");
        inspectionPanel.setAttribute("data-origin-parent-inspection", "inspection-only");
        inspectionPanel.append(
            createElement("h3", "dd-build-choice__label",
                "Compare with your existing Characters"),
            createElement("p", "dd-guided-builder__section-copy",
                "Compare the native Species or Ancestry sources in this saved draft with other Characters you own. This is a temporary inspection, not verified parentage, inheritance authorization, or a change to the Origin.")
        );

        if (chosenSources.length !== proposedSources.length ||
            proposedSources.length !== 2 || origin.draft === null) {
            inspectionPanel.append(createInlineState(
                "Select both native biological source references and save the draft before inspecting individual Characters.",
                "neutral"));
        } else {
            inspectionPanel.append(createButton(
                inspection.optionsStatus === "ready" ? "Refresh owned Characters" :
                    "Load owned Characters",
                "dd-button dd-button--secondary",
                handlers.loadParentOptions,
                origin.busy || inspection.busy || inspection.optionsStatus === "loading"));
            if (inspection.optionsStatus === "loading") {
                inspectionPanel.append(createInlineState(
                    "Loading your Site-owned Character list…", "loading"));
            }
            if (inspection.optionsStatus === "error") {
                inspectionPanel.append(createInlineState(
                    inspection.message ?? "Owner Character list is unavailable.",
                    "warning"));
            }
            if (inspection.optionsStatus === "ready") {
                if (inspection.options.length === 0) {
                    inspectionPanel.append(createInlineState(
                        "No other owned Characters are available for comparison.",
                        "neutral"));
                } else {
                    const sourceSelections = createElement("div", "dd-build__grid");
                    for (const source of chosenSources) {
                        const choice = createElement("label", "dd-rule-chooser__label");
                        choice.append(createElement("span", "",
                            `${source.slot === "parent-one" ? "Source 1" : "Source 2"} — ${source.sourceId}`));
                        const dropdown = createElement("select", "dd-rule-chooser__input");
                        dropdown.setAttribute("aria-label",
                            `Choose an owned Character for ${source.slot}`);
                        const empty = createElement("option", "", "Choose an owned Character");
                        empty.value = "";
                        dropdown.append(empty);
                        for (const character of inspection.options) {
                            const option = createElement("option", "",
                                `${character.name}${character.archived ? " (Archived)" : ""} · ${character.id.slice(0, 8)}`);
                            option.value = character.id;
                            dropdown.append(option);
                        }
                        dropdown.value = inspection.selected[source.slot] ?? "";
                        dropdown.disabled = origin.busy || inspection.busy;
                        dropdown.addEventListener("change", () =>
                            handlers.selectParentSource(source.slot, dropdown.value));
                        choice.append(dropdown);
                        sourceSelections.append(choice);
                    }
                    inspectionPanel.append(sourceSelections);
                    const readyForReview = chosenSources.every(source =>
                        inspection.selected[source.slot] !== undefined);
                    inspectionPanel.append(createButton(
                        inspection.busy ? "Reviewing native sources…" :
                            "Check parent-source alignment",
                        "dd-button dd-button--secondary",
                        handlers.reviewParentSources,
                        origin.busy || inspection.busy || !readyForReview));
                }
            }
            if (inspection.busy) {
                inspectionPanel.append(createInlineState(
                    "Revalidating each proposed parent Origin against current native rules…",
                    "loading"));
            }
            if (inspection.result !== null) {
                const matches = createElement("ul", "dd-guided-builder__review-list");
                for (const match of inspection.result.matches) {
                    const owner = inspection.options.find(candidate =>
                        candidate.id.toLowerCase() === match.parentCharacterId.toLowerCase());
                    matches.append(createElement("li", "dd-guided-builder__review-item",
                        `${match.contributorId}: ${owner?.name ?? "Owned Character"} — ${match.nativeSourceId} (Origin revision ${match.parentOriginRevision})`));
                }
                inspectionPanel.append(matches, createInlineState(
                    "The selected individual Origins match these native source identities. The Character relationships, heritable states, and any traits to manifest are not established or authorized.",
                    "warning"));
            } else if (inspection.optionsStatus === "ready" && inspection.message) {
                inspectionPanel.append(createInlineState(inspection.message, "warning"));
            }
        }
        section.append(inspectionPanel);
    }

    if (origin.draft !== null) {
        section.append(createElement("p", "dd-build__meta",
            `Draft revision ${origin.draft.revision} — not active`));
    }
    if (direct) {
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
    }

    if (origin.preview !== null) {
        const authorized = direct && canActivateDirectNativeOrigin(proposal, origin.preview);
        section.append(createInlineState(
            !direct
                ? "Native mixed-source preview preserves the offered candidates but does not authorize inheritance or activation."
                : authorized
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
        if (direct) {
            section.append(createButton(
                "Activate Native Origin", "dd-button dd-button--primary",
                handlers.activate,
                locked || legacy || !authorized || origin.draft === null));
        }
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
                chooser.category === "ancestry" ? "Ancestry" : "Species"}${chooser.parentSlot === undefined ? "" : " source for " + (chooser.parentSlot === "parent-one" ? "biological source 1" : "biological source 2")}`),
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
