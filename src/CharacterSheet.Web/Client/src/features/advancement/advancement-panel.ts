import type { CharacterBuilderUiState } from "../../app-state.js";
import type { CharacterAdvancementView } from "../../ui/character-advancement.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import type { PlayerAdvancementHandlers } from "../../ui/sheet-contracts.js";
import type {
    AdvancementCandidateTarget,
    AdvancementWorkflowState
} from "./advancement-workflow.js";
import type {
    CharacterAdvancementChoice,
    CharacterAdvancementEligibility,
    CharacterAdvancementPlan,
    CharacterAdvancementPrerequisite
} from "./advancement-api.js";

const MAX_NORMAL_CHARACTER_LEVEL = 20;

export function renderCharacterAdvancementPanel(
    builder: CharacterBuilderUiState,
    advancement: CharacterAdvancementView,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement | null {
    if (!state.open
        || builder.status !== "ready"
        || builder.build === null
        || builder.build.readOnly) {
        return null;
    }

    const overlay = createElement("div", "dd-character-advancement-overlay");
    overlay.setAttribute("data-player-advancement", "true");
    overlay.addEventListener("click", event => {
        if (event.target === overlay && state.status !== "applying") {
            handlers.close();
        }
    });

    const surface = createElement("section", "dd-character-advancement-surface");
    surface.setAttribute("role", "dialog");
    surface.setAttribute("aria-modal", "true");
    surface.setAttribute("aria-labelledby", "dd-character-advancement-title");
    surface.tabIndex = -1;
    surface.addEventListener("keydown", event => {
        if (event.key === "Escape" && state.status !== "applying") {
            event.preventDefault();
            handlers.close();
        }
    });
    queueMicrotask(() => {
        if (surface.isConnected) surface.focus();
    });

    const header = createElement("header", "dd-character-advancement-surface__header");
    const heading = createElement("div", "dd-character-advancement-surface__heading");
    const title = createElement("h2", "dd-character-advancement-surface__title", "Advance Character");
    title.id = "dd-character-advancement-title";
    heading.append(
        title,
        createElement(
            "span",
            "dd-character-advancement-surface__scope",
            "Global Dorks & Dice rules"));
    header.append(
        heading,
        createButton(
            "Close",
            "dd-button dd-button--ghost",
            handlers.close,
            state.status === "applying"));

    const body = createElement("div", "dd-character-advancement-surface__body");
    if (state.status === "previewing") {
        body.append(createInlineState("Checking the proposed advancement…", "loading"));
    } else if (state.status === "applying") {
        body.append(createInlineState("Applying the advancement…", "loading"));
    } else if (state.status === "error") {
        body.append(createInlineState(
            state.message ?? "Unable to review this advancement.",
            "error"));
    }

    if (state.chooser.kind === "open") {
        body.append(renderCandidateChooser(state, handlers));
    } else if (state.plan !== null) {
        body.append(renderPlan(state.plan, state, handlers));
    } else {
        body.append(renderProgressionSelection(builder, advancement, state, handlers));
    }

    surface.append(header, body);
    overlay.append(surface);
    return overlay;
}

function renderProgressionSelection(
    builder: CharacterBuilderUiState,
    advancement: CharacterAdvancementView,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const section = createElement("section", "dd-character-advancement__selection");
    section.append(
        createElement("h3", "dd-character-advancement__section-title", "Choose a progression"),
        createElement(
            "p",
            "dd-character-advancement__copy",
            "Advance an existing Class or Prestige Class, or add a new progression. Subclass decisions appear inside the parent Class advancement when the effective rules require them."));

    const totalLevel = builder.build?.progressionEntries
        .filter(value => value.kind === "class" || value.kind === "prestigeClass")
        .reduce((sum, value) => sum + Math.max(value.level ?? 0, 0), 0) ?? 0;
    const epicBoundary = totalLevel >= MAX_NORMAL_CHARACTER_LEVEL;
    if (epicBoundary) {
        section.append(createInlineState(
            "This Character is level 20. Level 21+ progression is reserved for the planned Epic advancement update.",
            "neutral"));
    }

    const pending = state.status === "previewing" || state.status === "applying";
    const progressions = builder.build?.progressionEntries.filter(value =>
        value.kind === "class" || value.kind === "prestigeClass") ?? [];
    const cards = createElement("div", "dd-character-advancement__progression-grid");
    for (const entry of progressions) {
        const occurrence = advancement.occurrences.find(value => value.occurrenceId === entry.id);
        const name = occurrence?.displayName ?? entry.ruleConceptKey;
        const kind = entry.kind === "prestigeClass" ? "Prestige Class" : "Class";
        const currentLevel = entry.level ?? 0;
        const card = createElement("article", "dd-character-advancement__progression-card");
        card.setAttribute("data-advancement-progression-kind", entry.kind);
        card.append(
            createElement("span", "dd-character-advancement__progression-kind", kind),
            createElement("h4", "dd-character-advancement__progression-name", name),
            createElement(
                "p",
                "dd-character-advancement__progression-level",
                currentLevel > 0
                    ? `Level ${currentLevel} → ${currentLevel + 1}`
                    : "Progression level unavailable"));

        if (entry.kind === "class") {
            const children = advancement.occurrences.filter(value =>
                value.parentOccurrenceId === entry.id
                && value.kind.trim().toLowerCase() === "subclass");
            for (const child of children) {
                card.append(createElement(
                    "p",
                    "dd-character-advancement__progression-child",
                    child.displayName));
            }
        }

        card.append(createButton(
            `Advance ${name}`,
            "dd-button dd-button--secondary",
            () => handlers.previewExistingProgression(entry.id),
            pending || epicBoundary || currentLevel <= 0));
        cards.append(card);
    }
    section.append(cards);

    const add = createElement("section", "dd-character-advancement__add");
    add.append(createElement("h3", "dd-character-advancement__section-title", "Add progression"));
    const actions = createElement("div", "dd-character-advancement__actions");
    actions.append(
        createButton(
            "Take a level in another Class",
            "dd-button dd-button--secondary",
            () => handlers.openCandidateChooser("class"),
            pending || epicBoundary),
        createButton(
            "Qualify for a Prestige Class",
            "dd-button dd-button--secondary",
            () => handlers.openCandidateChooser("prestigeClass"),
            pending || epicBoundary));
    add.append(actions);
    section.append(add);
    return section;
}

function renderPlan(
    plan: CharacterAdvancementPlan,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const container = createElement("section", "dd-character-advancement__plan");
    container.setAttribute("data-advancement-plan-status", plan.status);
    const prestige = plan.advancementKind === "prestigeClass"
        || plan.operation.startsWith("prestige-class");
    const progressionKind = prestige ? "Prestige Class" : "Class";
    const newProgression = plan.currentClassLevel === 0;

    const heading = createElement("div", "dd-character-advancement__plan-heading");
    heading.append(
        createElement("span", "dd-character-advancement__progression-kind", progressionKind),
        createElement("h3", "dd-character-advancement__plan-title", plan.classDisplayName),
        createElement(
            "p",
            "dd-character-advancement__summary",
            newProgression
                ? `Acquire level ${plan.targetClassLevel} · Character level ${plan.currentCharacterLevel} → ${plan.targetCharacterLevel}`
                : `Level ${plan.currentClassLevel} → ${plan.targetClassLevel} · Character level ${plan.currentCharacterLevel} → ${plan.targetCharacterLevel}`));
    container.append(heading);

    if (plan.status === "applied") {
        container.append(createInlineState(
            "The advancement was applied and the Character Sheet has been refreshed.",
            "neutral"));
        const actions = createElement("div", "dd-character-advancement__actions");
        actions.append(createButton("Done", "dd-button dd-button--primary", handlers.close));
        container.append(actions);
        return container;
    }

    if (plan.advancementEligibility !== null && plan.advancementEligibility !== undefined) {
        container.append(renderEligibility(plan.advancementEligibility));
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
        choices.append(createElement("h4", "dd-advancement-entry__subheading", "Choices from this advancement"));
        for (const choice of plan.requiredChoices) {
            choices.append(renderRequiredChoice(choice, handlers));
        }
        container.append(choices);
    }

    if (plan.hitPointGainRequired) {
        container.append(renderHitPointInput(plan, state, handlers));
    }

    if (plan.changes.length > 0) {
        const changes = createElement("section", "dd-character-advancement__changes");
        changes.append(createElement("h4", "dd-advancement-entry__subheading", "What will change"));
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

    if (!plan.canApply) {
        container.append(createInlineState(statusMessage(plan), "neutral"));
    }

    const actions = createElement("div", "dd-character-advancement__actions dd-character-advancement__actions--footer");
    actions.append(
        createButton(
            "Back",
            "dd-button dd-button--ghost",
            handlers.backToSelection,
            state.status === "applying"),
        createButton(
            state.status === "applying" ? "Applying…" : "Apply Advancement",
            "dd-button dd-button--primary",
            handlers.apply,
            state.status === "applying" || !plan.canApply));
    container.append(actions);
    return container;
}

function renderEligibility(eligibility: CharacterAdvancementEligibility): HTMLElement {
    const section = createElement("section", "dd-character-advancement__eligibility");
    section.setAttribute("data-advancement-eligibility", eligibility.state);
    const label = eligibility.eligible === true
        ? "Eligible"
        : eligibility.eligible === false
            ? "Requirements not met"
            : "Eligibility unresolved";
    section.append(
        createElement("h4", "dd-advancement-entry__subheading", "Prestige Class eligibility"),
        createElement("strong", "dd-character-advancement__eligibility-state", label));
    if (eligibility.eligible === null || eligibility.eligible === undefined) {
        section.append(createElement(
            "p",
            "dd-character-advancement__copy",
            "Rules Core does not yet have enough normalized prerequisite data to approve or reject this Prestige Class. Known requirements are shown below."));
    }
    return section;
}

function renderPrerequisites(
    prerequisites: CharacterAdvancementPrerequisite[]
): HTMLElement {
    const section = createElement("section", "dd-character-advancement__requirements");
    section.append(createElement("h4", "dd-advancement-entry__subheading", "Prerequisites"));
    const list = createElement("ul", "dd-character-advancement__requirement-list");
    for (const prerequisite of prerequisites) {
        for (const requirement of prerequisite.requirements) {
            const fallback = [
                requirement.kind,
                requirement.targetKey,
                requirement.operator,
                requirement.numericValue ?? requirement.textValue
            ].filter(value => value !== null && value !== undefined && String(value).length > 0).join(" ");
            const text = requirement.reason?.trim() || fallback || prerequisite.conceptKey;
            const state = requirement.satisfied === true
                ? "Met"
                : requirement.satisfied === false
                    ? "Not met"
                    : "Unresolved";
            const item = createElement(
                "li",
                requirement.satisfied === false
                    ? "dd-character-advancement__requirement dd-character-advancement__requirement--blocked"
                    : "dd-character-advancement__requirement");
            item.setAttribute(
                "data-prerequisite-satisfied",
                requirement.satisfied === true ? "true" : requirement.satisfied === false ? "false" : "unknown");
            item.append(
                createElement("span", "dd-character-advancement__requirement-state", state),
                createElement("span", "dd-character-advancement__requirement-text", text));
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
    card.append(
        createElement("h5", "dd-character-advancement__choice-title", choice.displayName),
        createElement("span", "dd-character-advancement__choice-kind", humanizeChoiceKind(choice.kind)));

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

    const controls = createElement("div", "dd-character-advancement__choice-controls");
    const select = createElement("select", "dd-rule-chooser__input");
    select.setAttribute("aria-label", choice.displayName);
    const placeholder = createElement("option");
    placeholder.value = "";
    placeholder.textContent = "Choose…";
    select.append(placeholder);
    for (const option of choice.options) {
        const element = createElement("option");
        element.value = option.value;
        element.textContent = option.displayName;
        if (choice.selectedValue === option.value) element.selected = true;
        select.append(element);
    }
    select.value = choice.selectedValue ?? "";
    controls.append(
        select,
        createButton(
            choice.selectedValue ? "Replace" : "Choose",
            "dd-button dd-button--secondary",
            () => {
                if (select.value.length > 0) {
                    handlers.resolveChoice(choice.choiceKey, select.value);
                }
            }));
    card.append(controls);
    return card;
}

function renderHitPointInput(
    plan: CharacterAdvancementPlan,
    state: AdvancementWorkflowState,
    handlers: PlayerAdvancementHandlers
): HTMLElement {
    const section = createElement("section", "dd-character-advancement__hit-points");
    section.append(
        createElement("h4", "dd-advancement-entry__subheading", "Hit Points"),
        createElement(
            "p",
            "dd-routine-meta",
            "Enter the raw hit-die outcome for this progression level. Rules Core applies the effective Hit Point rules."));
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
    const container = createElement("section", "dd-character-advancement__candidate-screen");
    container.setAttribute("data-advancement-candidate-chooser", chooser.target);

    const header = createElement("div", "dd-character-advancement__candidate-header");
    header.append(
        createElement(
            "div",
            "",
            ""),
        createButton("Back", "dd-button dd-button--ghost", handlers.closeCandidateChooser));
    const titleContainer = header.firstElementChild as HTMLElement;
    titleContainer.append(
        createElement("h3", "dd-character-advancement__section-title", candidateTitle(chooser.target)),
        createElement("p", "dd-character-advancement__copy", candidateHelp(chooser.target)));
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
        const list = createElement("ul", "dd-character-advancement__candidate-list");
        for (const rule of chooser.results) {
            const item = createElement("li", "dd-character-advancement__candidate");
            const text = createElement("div", "dd-character-advancement__candidate-copy");
            text.append(createElement("strong", "dd-character-advancement__candidate-name", rule.displayName));
            const metadata = [rule.editionDisplayName, rule.sourceCode, rule.packageDisplayName]
                .map(value => value.trim())
                .filter(value => value.length > 0);
            if (metadata.length > 0) {
                text.append(createElement("span", "dd-rule-result__meta", metadata.join(" • ")));
            }
            item.append(
                text,
                createButton(
                    chooser.target === "prestigeClass" ? "Check Requirements" : "Choose",
                    "dd-button dd-button--secondary",
                    () => handlers.selectCandidate(chooser.target, rule.conceptKey)));
            list.append(item);
        }
        container.append(list);
    }
    return container;
}

function candidateTitle(target: AdvancementCandidateTarget): string {
    switch (target) {
        case "class": return "Take a level in another Class";
        case "prestigeClass": return "Qualify for a Prestige Class";
        case "subclass": return "Choose Subclass";
    }
}

function candidateHelp(target: AdvancementCandidateTarget): string {
    switch (target) {
        case "class":
            return "Choose an effective Class to preview its first level before making any change.";
        case "prestigeClass":
            return "Browse Prestige Classes freely. Selecting one checks its effective prerequisites; ineligible and unresolved candidates remain inspectable.";
        case "subclass":
            return "Choose the Subclass required by this Class advancement.";
    }
}

function humanizeChoiceKind(kind: string): string {
    return kind
        .replace(/([a-z])([A-Z])/g, "$1 $2")
        .replace(/[-_]+/g, " ")
        .replace(/\b\w/g, value => value.toUpperCase());
}

function statusMessage(plan: CharacterAdvancementPlan): string {
    switch (plan.status) {
        case "blocked-prerequisite":
            return "One or more prerequisites are not satisfied.";
        case "choices-required":
            return "Complete the choices granted by this advancement before applying it.";
        case "blocked-conflict":
            return "The effective rules report a blocking conflict for this advancement.";
        case "blocked-eligibility":
            return plan.advancementEligibility?.eligible === null
                || plan.advancementEligibility?.eligible === undefined
                ? "Prestige Class eligibility is unresolved. Known requirements can be reviewed, but the Character can not acquire it until Rules Core can establish eligibility."
                : "The selected option is not eligible under the effective ruleset.";
        case "hit-points-required":
            return "Enter the Hit Point gain for this progression level and review again.";
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
