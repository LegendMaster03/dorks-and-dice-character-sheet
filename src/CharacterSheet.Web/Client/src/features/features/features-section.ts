import type { CharacterBuilderUiState } from "../../app-state.js";
import {
    createButton,
    createElement,
    createInlineState
} from "../../ui/components.js";
import {
    hasPendingBuildMutation,
    toRuleReferenceDisplay
} from "../../ui/sheet-model.js";
import type { FeatCharacterHandlers } from "../../ui/sheet-contracts.js";

export function renderFeaturesSection(
    builder: CharacterBuilderUiState,
    structuralEditing: boolean,
    readOnly: boolean,
    handlers: FeatCharacterHandlers
): HTMLElement {
    const content = createElement("div", "dd-feature-sections");
    const feats = createElement("section", "dd-feature-subsection");
    feats.append(createElement("h3", "dd-feature-subsection__title", "Feats"));

    if (builder.status === "idle" || builder.status === "loading") {
        feats.append(createInlineState("Loading Character Feats…", "loading"));
    } else if (builder.status === "error" || builder.build === null) {
        feats.append(createInlineState(
            builder.message ?? "Character Feats are unavailable.",
            "error"));
    } else {
        const pending = hasPendingBuildMutation(builder);
        const editable = structuralEditing && !readOnly && !builder.build.readOnly;

        if (builder.featSaveError !== undefined) {
            feats.append(createInlineState(builder.featSaveError, "error"));
        }
        if (editable) {
            const actions = createElement("div", "dd-routine-actions");
            actions.append(createButton(
                "Add Feat",
                "dd-button dd-button--primary",
                handlers.openChooser,
                pending));
            feats.append(actions);
        }
        if (builder.featChooser.kind === "open" && editable) {
            feats.append(renderFeatChooser(builder, handlers));
        }

        const occurrences = builder.build.progressionEntries.filter(value => value.kind === "feat");
        if (occurrences.length === 0) {
            feats.append(createElement(
                "p",
                "dd-routine-empty",
                editable
                    ? "No Feat occurrences yet. Add one from the Rules Core catalog."
                    : "No Character-owned Feat occurrences have been recorded."));
        } else {
            const list = createElement("div", "dd-feat-list");
            for (const occurrence of occurrences) {
                const item = createElement("article", "dd-feat");
                item.setAttribute("data-feat-occurrence-id", occurrence.id);
                const reference = builder.featReferences[occurrence.id] ?? {
                    status: "loading" as const,
                    conceptKey: occurrence.ruleConceptKey
                };
                const display = toRuleReferenceDisplay(reference);
                const title = display.tone === "unavailable"
                    ? "Unavailable Feat reference"
                    : display.value;
                item.append(
                    createElement("h4", "dd-feat__name", title),
                    createElement(
                        "p",
                        "dd-routine-meta",
                        display.detail ?? occurrence.ruleConceptKey)
                );
                if (editable) {
                    item.append(createButton(
                        builder.savingFeat === occurrence.id ? "Removing…" : "Remove",
                        "dd-button dd-button--ghost",
                        () => {
                            if (window.confirm("Remove this Feat occurrence from the Character?")) {
                                handlers.remove(occurrence.id);
                            }
                        },
                        pending));
                }
                list.append(item);
            }
            feats.append(list);
        }
    }

    const other = createElement("section", "dd-feature-subsection");
    other.append(
        createElement("h3", "dd-feature-subsection__title", "Other Features & Traits"),
        createInlineState(
            "Class, Subclass, Species, and other granted features are not available yet because Rules Core does not expose the normalized ordinary-Character feature/effect consumer contract.",
            "neutral"));
    content.append(feats, other);
    return content;
}

function renderFeatChooser(
    builder: CharacterBuilderUiState,
    handlers: FeatCharacterHandlers
): HTMLElement {
    const chooser = builder.featChooser;
    const section = createElement("section", "dd-rule-chooser dd-feat-chooser");
    if (chooser.kind !== "open") return section;

    const heading = createElement("h4", "dd-rule-chooser__title", "Add Feat");
    const search = createElement("form", "dd-rule-chooser__search");
    const input = createElement("input", "dd-rule-chooser__input");
    input.type = "search";
    input.value = chooser.query;
    input.placeholder = "Search Rules Core Feats";
    input.setAttribute("aria-label", "Search Rules Core Feats");
    const submit = createElement("button", "dd-button dd-button--secondary", "Search");
    submit.type = "submit";
    const close = createButton("Close", "dd-button dd-button--ghost", handlers.closeChooser);
    search.append(input, submit, close);
    search.addEventListener("submit", event => {
        event.preventDefault();
        handlers.search(input.value);
    });
    section.append(heading, search);

    if (chooser.status === "idle" || chooser.status === "loading") {
        section.append(createInlineState("Loading Rules Core Feats…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(chooser.message ?? "Rules Core Feat search failed.", "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching Rules Core Feats."));
        return section;
    }

    const results = createElement("div", "dd-rule-chooser__results");
    for (const rule of chooser.results) {
        const result = createElement("article", "dd-rule-chooser__result");
        result.append(
            createElement("strong", "dd-rule-chooser__result-name", rule.displayName),
            createElement(
                "span",
                "dd-rule-chooser__result-meta",
                [rule.editionDisplayName, rule.sourceCode].filter(Boolean).join(" • ")),
            createElement("span", "dd-rule-chooser__result-key", rule.conceptKey),
            createButton(
                builder.savingFeat === "add" ? "Adding…" : "Add",
                "dd-button dd-button--primary",
                () => handlers.add(rule.conceptKey),
                hasPendingBuildMutation(builder))
        );
        results.append(result);
    }
    section.append(results);
    return section;
}

