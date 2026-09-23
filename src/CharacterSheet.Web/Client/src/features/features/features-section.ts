import type { CharacterBuilderUiState } from "../../app-state.js";
import type { CharacterFeatureView } from "../../ui/character-mechanics.js";
import { renderSourceAttributions } from "../../ui/source-attribution.js";
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
    projectedFeatures: readonly CharacterFeatureView[] | undefined,
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
                    ? "No feats yet. Add one from the rule catalog."
                    : "No feats have been added."));
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
                            if (window.confirm("Remove this feat from the Character?")) {
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
    other.append(createElement("h3", "dd-feature-subsection__title", "Other Features & Traits"));
    if (projectedFeatures === undefined) {
        other.append(createInlineState(
            "Rules-derived features are unavailable for this Character.",
            "neutral"));
    } else if (projectedFeatures.length === 0) {
        other.append(createElement("p", "dd-routine-empty", "No additional granted features."));
    } else {
        const list = createElement("div", "dd-feat-list");
        for (const feature of projectedFeatures) {
            const item = createElement("article", "dd-feat");
            item.setAttribute("data-feature-key", feature.key);
            item.setAttribute("data-feature-state", feature.state);
            item.append(createElement("h4", "dd-feat__name", feature.label));

            const meta = [
                feature.grantingSourceKind,
                feature.acquisitionLevel === undefined
                    ? undefined
                    : `Level ${feature.acquisitionLevel}`,
                feature.sourceConceptKey
            ].filter((value): value is string => value !== undefined && value.length > 0);
            if (meta.length > 0) {
                item.append(createElement("p", "dd-routine-meta", meta.join(" • ")));
            }

            for (const effect of feature.effects ?? []) {
                item.append(createElement(
                    "p",
                    "dd-routine-meta",
                    `${effect.label}: ${effect.value}`));
            }
            const sources = renderSourceAttributions(feature.sourceAttributions, true);
            if (sources !== null) item.append(sources);
            list.append(item);
        }
        other.append(list);
    }
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
    input.placeholder = "Search available feats";
    input.setAttribute("aria-label", "Search available feats");
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
        section.append(createInlineState("Loading available feats…", "loading"));
        return section;
    }
    if (chooser.status === "error") {
        section.append(createInlineState(chooser.message ?? "Feat search failed.", "error"));
        return section;
    }
    if (chooser.results.length === 0) {
        section.append(createElement("p", "dd-routine-empty", "No matching feats."));
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

