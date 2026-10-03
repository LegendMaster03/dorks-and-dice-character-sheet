import type {
    CharacterFeatureView,
    CharacterMechanicsView
} from "./character-mechanics.js";
import { createElement, createSectionCard } from "./components.js";
import { renderSourceAttributions } from "./source-attribution.js";

export function getGuidedSourceFeatures(
    mechanics: CharacterMechanicsView | null,
    sourceConceptKeys: readonly string[]
): readonly CharacterFeatureView[] {
    if (mechanics === null || sourceConceptKeys.length === 0) {
        return [];
    }

    const sourceKeys = new Set(sourceConceptKeys);
    return (mechanics.features ?? [])
        .filter(feature =>
            feature.sourceConceptKey !== undefined
            && sourceKeys.has(feature.sourceConceptKey))
        .sort((left, right) => {
            const leftLevel = left.acquisitionLevel ?? Number.MAX_SAFE_INTEGER;
            const rightLevel = right.acquisitionLevel ?? Number.MAX_SAFE_INTEGER;
            return leftLevel - rightLevel || left.label.localeCompare(right.label);
        });
}

export function renderGuidedSourceFeatures(
    mechanics: CharacterMechanicsView | null,
    sourceConceptKeys: readonly string[],
    heading: string
): HTMLElement | null {
    const features = getGuidedSourceFeatures(mechanics, sourceConceptKeys);
    if (features.length === 0) {
        return null;
    }

    const section = createSectionCard(heading, "dd-guided-builder__features");
    section.setAttribute("data-guided-builder-features", heading);
    const grid = createElement("div", "dd-build__grid");

    for (const feature of features) {
        grid.append(renderGuidedFeature(feature));
    }

    section.append(grid);
    return section;
}

function renderGuidedFeature(feature: CharacterFeatureView): HTMLElement {
    const hasDetails = (feature.effects?.length ?? 0) > 0
        || (feature.sourceAttributions?.length ?? 0) > 0;
    const card = hasDetails
        ? createElement("details", "dd-build-choice dd-guided-builder__feature")
        : createElement("article", "dd-build-choice dd-guided-builder__feature");
    card.setAttribute("data-guided-builder-feature", feature.key);

    const titleText = feature.acquisitionLevel === undefined
        ? feature.label
        : `${feature.label} · Level ${feature.acquisitionLevel}`;

    if (hasDetails) {
        card.append(createElement(
            "summary",
            "dd-build-choice__label dd-guided-builder__feature-summary",
            titleText));
    } else {
        card.append(createElement(
            "h3",
            "dd-build-choice__label",
            titleText));
    }

    if ((feature.effects?.length ?? 0) > 0) {
        const effects = createElement("dl", "dd-guided-builder__feature-effects");
        for (const effect of feature.effects ?? []) {
            effects.append(
                createElement("dt", "dd-build-choice__detail", effect.label),
                createElement("dd", "dd-build-choice__value", effect.value));
        }
        card.append(effects);
    }

    const sources = renderSourceAttributions(feature.sourceAttributions, true);
    if (sources !== null) {
        card.append(sources);
    }

    return card;
}
