import type { CharacterBuilderUiState } from "../app-state.js";
import type { CharacterBuilderChoice } from "../builder-api.js";
import { getStartingClassEntry, getStoredChoiceConceptKey } from "../builder-rules.js";
import type { ResolvedRuleCatalogItem } from "../rules-core-api.js";
import { createButton, createElement, createInlineState, createSectionCard } from "./components.js";
import {
    getChoiceActionPolicy,
    hasPendingBuildMutation,
    humanizeBuilderStatus,
    toRuleReferenceDisplay
} from "./sheet-model.js";

export interface CharacterBuilderHandlers {
    openChooser(target: CharacterBuilderChoice): void;
    clearChoice(target: CharacterBuilderChoice): void;
    submitChooserSearch(target: CharacterBuilderChoice, query: string): void;
    closeChooser(): void;
    saveChoice(target: CharacterBuilderChoice, conceptKey: string): void;
}

export interface CharacterBuilderRenderOptions {
    title?: string;
    choices?: readonly CharacterBuilderChoice[];
}

const ALL_CHARACTER_BUILDER_CHOICES: readonly CharacterBuilderChoice[] = [
    "species",
    "subspecies",
    "background",
    "deity",
    "startingClass",
    "subclass"
];

export function renderCharacterBuilder(
    characterId: string,
    builder: CharacterBuilderUiState,
    forceReadOnly: boolean,
    handlers: CharacterBuilderHandlers,
    options: CharacterBuilderRenderOptions = {}
): HTMLElement {
    const choices = options.choices ?? ALL_CHARACTER_BUILDER_CHOICES;
    const section = createSectionCard(options.title ?? "Character Setup", "dd-build");
    section.setAttribute("data-character-builder", characterId);
    const mutationPending = hasPendingBuildMutation(builder);
    section.setAttribute("aria-busy", builder.status === "loading" || mutationPending ? "true" : "false");

    if (builder.status === "idle" || builder.status === "loading") {
        section.append(createInlineState("Loading Character setup…", "loading"));
        return section;
    }
    if (builder.status === "error") {
        section.append(createInlineState(builder.message ?? "Unable to load Character setup.", "error"));
        return section;
    }
    if (builder.build === null) {
        section.append(createInlineState("Character setup is unavailable.", "warning"));
        return section;
    }

    const readOnly = forceReadOnly || builder.build.readOnly;
    const status = humanizeBuilderStatus(builder.build.builderStatus);
    const headingMeta = createElement("div", "dd-build__meta");
    if (status !== null) {
        headingMeta.append(createElement("span", "dd-status-pill", status));
    }
    if (readOnly) {
        headingMeta.append(createElement("span", "dd-status-pill dd-status-pill--readonly", "Read-only"));
    }
    section.append(headingMeta);

    const grid = createElement("div", "dd-build__grid");
    const startingClass = getStartingClassEntry(builder.build);
    const speciesSelected = getStoredChoiceConceptKey(builder.build, "species") !== null;
    if (choices.includes("species")) {
        grid.append(renderChoice("species", "Species", builder, readOnly, true, undefined, handlers));
    }
    if (choices.includes("subspecies")) {
        grid.append(renderChoice(
            "subspecies",
            "Subspecies",
            builder,
            readOnly,
            speciesSelected,
            "Choose a Species before selecting a Subspecies.",
            handlers));
    }
    if (choices.includes("background")) {
        grid.append(renderChoice("background", "Background", builder, readOnly, true, undefined, handlers));
    }
    if (choices.includes("deity")) {
        grid.append(renderChoice("deity", "Deity", builder, readOnly, true, undefined, handlers));
    }
    if (choices.includes("startingClass")) {
        grid.append(renderChoice("startingClass", "Starting Class", builder, readOnly, true, undefined, handlers));
    }
    if (choices.includes("subclass")) {
        grid.append(renderChoice(
            "subclass",
            "Subclass",
            builder,
            readOnly,
            startingClass !== null,
            "Choose a Class before selecting a Subclass.",
            handlers));
    }
    section.append(grid);

    if (builder.saveError !== undefined) {
        section.append(createInlineState(builder.saveError, "error"));
    }

    if (builder.chooser.kind === "open" && choices.includes(builder.chooser.target)) {
        section.append(renderRuleChooser(builder, handlers));
    }

    return section;
}

function renderChoice(
    target: CharacterBuilderChoice,
    label: string,
    builder: CharacterBuilderUiState,
    readOnly: boolean,
    available: boolean,
    unavailableMessage: string | undefined,
    handlers: CharacterBuilderHandlers
): HTMLElement {
    const reference = builder.references[target] ?? { status: "none" as const };
    const display = toRuleReferenceDisplay(reference);
    const policy = getChoiceActionPolicy(reference, readOnly, available, hasPendingBuildMutation(builder));
    const card = createElement("article", "dd-build-choice");
    card.setAttribute("data-builder-choice", target);

    const heading = createElement("h3", "dd-build-choice__label", label);
    const value = createElement("p", `dd-build-choice__value dd-build-choice__value--${display.tone}`, display.value);
    card.append(heading, value);
    if (display.detail !== undefined) {
        card.append(createElement("p", "dd-build-choice__detail", display.detail));
    }

    if (!available) {
        card.append(createInlineState(unavailableMessage ?? "This selection is not available yet.", "neutral"));
        return card;
    }

    if (readOnly) {
        card.append(createElement("span", "dd-build-choice__readonly", "Read-only"));
        return card;
    }

    const actions = createElement("div", "dd-build-choice__actions");
    actions.append(createButton(
        policy.chooseLabel,
        "dd-button dd-button--secondary",
        () => handlers.openChooser(target),
        !policy.canChoose));
    if (reference.status !== "none") {
        actions.append(createButton(
            "Clear",
            "dd-button dd-button--ghost",
            () => handlers.clearChoice(target),
            !policy.canClear));
    }
    card.append(actions);
    return card;
}

function renderRuleChooser(
    builder: CharacterBuilderUiState,
    handlers: CharacterBuilderHandlers
): HTMLElement {
    const chooser = builder.chooser;
    if (chooser.kind !== "open") {
        throw new Error("Rule chooser renderer requires an open chooser state.");
    }

    const target = chooser.target;
    const container = createElement("aside", "dd-rule-chooser");
    container.setAttribute("data-rule-chooser", target);
    container.setAttribute("role", "dialog");
    container.setAttribute("aria-modal", "false");
    const headingId = `dd-rule-chooser-${target}`;
    container.setAttribute("aria-labelledby", headingId);

    const header = createElement("div", "dd-rule-chooser__header");
    const heading = createElement(
        "h3",
        "dd-rule-chooser__title",
        target === "species"
            ? "Choose Species"
            : target === "subspecies"
                ? "Choose Subspecies"
                : target === "background"
                    ? "Choose Background"
                    : target === "deity"
                        ? "Choose Deity"
                        : target === "startingClass" ? "Choose Starting Class" : "Choose Subclass");
    heading.id = headingId;
    header.append(heading, createButton("Close", "dd-button dd-button--ghost", handlers.closeChooser));
    container.append(header);

    const form = createElement("form", "dd-rule-chooser__search");
    const label = createElement("label", "dd-rule-chooser__label", "Search available rules");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.name = "ruleSearch";
    input.value = chooser.query;
    input.autocomplete = "off";
    input.placeholder = "Search by name";
    input.autofocus = true;
    queueMicrotask(() => {
        if (input.isConnected) input.focus();
    });
    label.append(input);
    const submit = createElement("button", "dd-button dd-button--primary", "Search");
    submit.type = "submit";
    form.append(label, submit);
    form.addEventListener("submit", event => {
        event.preventDefault();
        handlers.submitChooserSearch(target, input.value);
    });
    container.append(form);

    if (chooser.status === "idle" || chooser.status === "loading") {
        container.append(createInlineState("Loading rule catalog…", "loading"));
    } else if (chooser.status === "error") {
        container.append(createInlineState(chooser.message ?? "Rule catalog is unavailable.", "error"));
    } else if (chooser.results.length === 0) {
        container.append(createInlineState("No matching rules are available.", "neutral"));
    } else {
        const list = createElement("ul", "dd-rule-chooser__results");
        for (const rule of chooser.results) {
            list.append(renderRuleChooserResult(target, rule, hasPendingBuildMutation(builder), handlers));
        }
        container.append(list);
    }

    return container;
}

function renderRuleChooserResult(
    target: CharacterBuilderChoice,
    rule: ResolvedRuleCatalogItem,
    disabled: boolean,
    handlers: CharacterBuilderHandlers
): HTMLLIElement {
    const item = createElement("li", "dd-rule-result");
    const button = createButton(
        rule.displayName,
        "dd-rule-result__button",
        () => handlers.saveChoice(target, rule.conceptKey),
        disabled);
    item.append(button);

    const metadata = [rule.editionDisplayName, rule.sourceCode, rule.packageDisplayName]
        .map(value => value.trim())
        .filter(value => value.length > 0);
    if (metadata.length > 0) {
        item.append(createElement("span", "dd-rule-result__meta", metadata.join(" • ")));
    }
    return item;
}
