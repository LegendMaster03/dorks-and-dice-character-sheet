import type { CharacterBuilderUiState } from "../../app-state.js";
import type { CharacterAdvancementView } from "../../ui/character-advancement.js";
import {
    createButton,
    createElement,
    createInlineState,
    createSectionCard
} from "../../ui/components.js";
import type { PlayerAdvancementHandlers } from "../../ui/sheet-contracts.js";
import type {
    AdvancementCandidateTarget,
    AdvancementWorkflowState
} from "./advancement-workflow.js";
import type {
    CharacterAdvancementChoice,
    CharacterAdvancementPlan,
    CharacterAdvancementPrerequisite
} from "./advancement-api.js";

export function renderCharacterAdvancementPanel(
    builder: CharacterBuilderUiState,
    advancement: CharacterAdvancementView,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement | null {
    if (builder.status !== "ready" || builder.build === null || builder.build.readOnly) {
        return null;
    }

    const section = createSectionCard("Advance Character", "dd-character-advancement");
    section.setAttribute("data-player-advancement", "true");
    section.append(createElement(
        "p",
        "dd-character-advancement__copy",
        "Advance one Class level at a time. The preview shows the effective requirements, choices, Hit Point input, and features before anything is applied."));

    const pending = state.status === "previewing" || state.status === "applying";
    const classEntries = builder.build.progressionEntries.filter(value => value.kind === "class");
    const classActions = createElement("div", "dd-character-advancement__classes");
    for (const entry of classEntries) {
        const occurrence = advancement.occurrences.find(value => value.occurrenceId === entry.id);
        const label = occurrence?.displayName ?? entry.ruleConceptKey;
        const currentLevel = entry.level ?? 0;
        classActions.append(createButton(
            `Advance ${label}${currentLevel > 0 ? ` · Level ${currentLevel}` : ""}`,
            "dd-button dd-button--secondary",
            () => handlers.previewExistingClass(entry.id),
            pending));
    }
    classActions.append(createButton(
        "Add Another Class",
        "dd-button dd-button--secondary",
        () => handlers.openCandidateChooser("class"),
        pending));
    section.append(classActions);

    if (state.status === "previewing") {
        section.append(createInlineState("Checking the proposed advancement…", "loading"));
    } else if (state.status === "applying") {
        section.append(createInlineState("Applying the advancement…", "loading"));
    } else if (state.status === "error") {
        section.append(createInlineState(
            state.message ?? "Unable to review this advancement.",
            "error"));
    }

    if (state.plan !== null) {
        section.append(renderPlan(state.plan, state, handlers));
    }
    if (state.chooser.kind === "open") {
        section.append(renderCandidateChooser(state, handlers));
    }

    return section;
}

function renderPlan(
    plan: CharacterAdvancementPlan,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const container = createElement("section", "dd-character-advancement__plan");
    container.setAttribute("data-advancement-plan-status", plan.status);
    container.append(createElement(
        "h3",
        "dd-character-advancement__plan-title",
        plan.operation === "multiclass"
            ? `${plan.classDisplayName} · Level 1`
            : `${plan.classDisplayName} · Level ${plan.currentClassLevel} → ${plan.targetClassLevel}`));
    container.append(createElement(
        "p",
        "dd-character-advancement__summary",
        `Character level ${plan.currentCharacterLevel} → ${plan.targetCharacterLevel}`));

    if (plan.status === "applied") {
        container.append(createInlineState(
            "The advancement was applied and the Character Sheet has been refreshed.",
            "neutral"));
        container.append(createButton(
            "Done",
            "dd-button dd-button--secondary",
            handlers.cancel));
        return container;
    }

    if (plan.changes.length > 0) {
        const changes = createElement("section", "dd-character-advancement__changes");
        changes.append(createElement("h4", "dd-advancement-entry__subheading", "This advancement"));
        const list = createElement("ul", "dd-character-advancement__list");
        for (const change of plan.changes) {
            const item = createElement("li", "dd-character-advancement__list-item");
            item.append(createElement("strong", "", change.label));
            if (change.detail) {
                item.append(createElement("span", "dd-routine-meta", ` · ${change.detail}`));
            }
            list.append(item);
        }
        changes.append(list);
        container.append(changes);
    }

    if (plan.prerequisites.length > 0) {
        container.append(renderPrerequisites(plan.prerequisites));
    }
    if (plan.blockingConflicts.length > 0) {
        const conflicts = createElement("section", "dd-character-advancement__requirements");
        conflicts.append(createElement("h4", "dd-advancement-entry__subheading", "Blocking conflicts"));
        for (const conflict of plan.blockingConflicts) {
            conflicts.append(createInlineState(conflict.message, "warning"));
        }
        container.append(conflicts);
    }

    if (plan.requiredChoices.length > 0) {
        const choices = createElement("section", "dd-character-advancement__requirements");
        choices.append(createElement("h4", "dd-advancement-entry__subheading", "Required choices"));
        for (const choice of plan.requiredChoices) {
            choices.append(renderRequiredChoice(choice, handlers));
        }
        container.append(choices);
    }

    if (plan.hitPointGainRequired) {
        container.append(renderHitPointInput(plan, state, handlers));
    }

    const actions = createElement("div", "dd-character-advancement__actions");
    actions.append(createButton(
        state.status === "applying" ? "Applying…" : "Apply Advancement",
        "dd-button dd-button--primary",
        handlers.apply,
        state.status === "applying" || !plan.canApply));
    actions.append(createButton(
        "Cancel",
        "dd-button dd-button--ghost",
        handlers.cancel,
        state.status === "applying"));
    container.append(actions);

    if (!plan.canApply) {
        container.append(createInlineState(statusMessage(plan.status), "neutral"));
    }
    return container;
}

function renderPrerequisites(
    prerequisites: CharacterAdvancementPrerequisite[]
): HTMLElement {
    const section = createElement("section", "dd-character-advancement__requirements");
    section.append(createElement("h4", "dd-advancement-entry__subheading", "Prerequisites"));
    const list = createElement("ul", "dd-character-advancement__list");
    for (const prerequisite of prerequisites) {
        for (const requirement of prerequisite.requirements) {
            const fallback = [
                requirement.kind,
                requirement.targetKey,
                requirement.operator,
                requirement.numericValue ?? requirement.textValue
            ].filter(value => value !== null && value !== undefined && String(value).length > 0).join(" ");
            const text = requirement.reason?.trim() || fallback || prerequisite.conceptKey;
            const item = createElement(
                "li",
                requirement.satisfied === false
                    ? "dd-character-advancement__list-item dd-character-advancement__list-item--blocked"
                    : "dd-character-advancement__list-item",
                text);
            item.setAttribute(
                "data-prerequisite-satisfied",
                requirement.satisfied === true ? "true" : requirement.satisfied === false ? "false" : "unknown");
            list.append(item);
        }
    }
    section.append(list);
    return section;
}

function renderRequiredChoice(
    choice: CharacterAdvancementChoice,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const card = createElement("article", "dd-character-advancement__choice");
    card.setAttribute("data-advancement-choice", choice.choiceKey);
    card.append(createElement("strong", "", choice.displayName));

    if (choice.kind.trim().toLowerCase() === "subclass") {
        card.append(createButton(
            "Choose Subclass",
            "dd-button dd-button--secondary",
            () => handlers.openCandidateChooser("subclass")));
        return card;
    }

    if (choice.options.length === 0) {
        card.append(createInlineState(
            "Rules Core requires this choice but did not provide selectable options.",
            "warning"));
        return card;
    }

    const options = createElement("div", "dd-character-advancement__choice-options");
    for (const option of choice.options) {
        options.append(createButton(
            option.displayName,
            "dd-button dd-button--secondary",
            () => handlers.resolveChoice(choice.choiceKey, option.value)));
    }
    card.append(options);
    return card;
}

function renderHitPointInput(
    plan: CharacterAdvancementPlan,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const section = createElement("section", "dd-character-advancement__hit-points");
    section.append(
        createElement("h4", "dd-advancement-entry__subheading", "Hit Point gain"),
        createElement(
            "p",
            "dd-routine-meta",
            "Enter the raw hit-die outcome for this Class level. Rules Core applies the effective Hit Point rules."));
    const row = createElement("div", "dd-advancement-entry__level-editor");
    const label = createElement("label", "dd-sheet-screen__label", "Hit-die outcome");
    const input = createElement("input", "dd-sheet-screen__input");
    input.type = "number";
    input.step = "1";
    input.inputMode = "numeric";
    input.value = state.request?.hitDieValue === null || state.request?.hitDieValue === undefined
        ? ""
        : String(state.request.hitDieValue);
    input.addEventListener("input", () => input.setCustomValidity(""));
    label.append(input);
    row.append(label, createButton(
        plan.hitDieValue === null || plan.hitDieValue === undefined ? "Review" : "Review Again",
        "dd-button dd-button--secondary",
        () => {
            const parsed = parseApiInteger(input.value);
            if (parsed === null) {
                input.setCustomValidity("Enter a whole number that can be represented by the Character Sheet API.");
                input.reportValidity();
                return;
            }
            input.setCustomValidity("");
            handlers.reviewHitPointGain(parsed);
        },
        state.status === "previewing" || state.status === "applying"));
    section.append(row);
    return section;
}

function renderCandidateChooser(
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const chooser = state.chooser;
    if (chooser.kind !== "open") {
        throw new Error("Advancement candidate chooser requires an open state.");
    }
    const container = createElement("aside", "dd-rule-chooser dd-character-advancement__chooser");
    container.setAttribute("role", "dialog");
    container.setAttribute("aria-modal", "false");
    container.setAttribute("data-advancement-candidate-chooser", chooser.target);

    const header = createElement("div", "dd-rule-chooser__header");
    header.append(
        createElement(
            "h3",
            "dd-rule-chooser__title",
            chooser.target === "class" ? "Choose Another Class" : "Choose Subclass"),
        createButton("Close", "dd-button dd-button--ghost", handlers.closeCandidateChooser));
    container.append(header);

    const form = createElement("form", "dd-rule-chooser__search");
    const label = createElement("label", "dd-rule-chooser__label", "Search available rules");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.autocomplete = "off";
    input.placeholder = "Search by name";
    label.append(input);
    const submit = createElement("button", "dd-button dd-button--primary", "Search");
    submit.type = "submit";
    form.append(label, submit);
    form.addEventListener("submit", event => {
        event.preventDefault();
        handlers.searchCandidates(chooser.target, input.value);
    });
    container.append(form);

    if (chooser.status === "idle" || chooser.status === "loading") {
        container.append(createInlineState("Loading available choices…", "loading"));
    } else if (chooser.status === "error") {
        container.append(createInlineState(
            chooser.message ?? "Available choices could not be loaded.",
            "error"));
    } else if (chooser.results.length === 0) {
        container.append(createInlineState("No matching choices are available.", "neutral"));
    } else {
        const list = createElement("ul", "dd-rule-chooser__results");
        for (const rule of chooser.results) {
            const item = createElement("li", "dd-rule-result");
            item.append(createButton(
                rule.displayName,
                "dd-rule-result__button",
                () => handlers.selectCandidate(chooser.target, rule.conceptKey)));
            list.append(item);
        }
        container.append(list);
    }
    return container;
}

function statusMessage(status: string): string {
    switch (status) {
        case "blocked-prerequisite":
            return "One or more prerequisites are not satisfied.";
        case "choices-required":
            return "Complete the required choices before applying this advancement.";
        case "blocked-conflict":
            return "Rules Core reported a blocking conflict for this advancement.";
        case "blocked-eligibility":
            return "The selected option is not eligible under the effective ruleset.";
        case "hit-points-required":
            return "Enter the Hit Point gain for this Class level and review again.";
        default:
            return "Review the unresolved requirements before applying this advancement.";
    }
}

function parseApiInteger(value: string): number | null {
    const normalized = value.trim();
    if (!/^-?\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed)
        && parsed >= -2147483648
        && parsed <= 2147483647
        ? parsed
        : null;
}
