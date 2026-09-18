import {
    advancementKindLabel,
    buildAdvancementPresentation,
    formatAdvancementProgression,
    type AdvancementGrantView,
    type CharacterAdvancementView
} from "./character-advancement.js";
import { createElement, createInlineState } from "./components.js";
import { renderSourceAttributions } from "./source-attribution.js";

export function renderAdvancementDetails(advancement: CharacterAdvancementView | null): HTMLElement {
    const details = createElement("details", "dd-advancement-overview");
    details.setAttribute("data-advancement-state", advancement === null ? "unavailable" : "resolved");
    details.append(createElement("summary", "dd-advancement-overview__toggle", "Advancement details"));
    const body = createElement("div", "dd-advancement-overview__body");

    if (advancement === null) {
        body.append(createInlineState("Resolved advancement occurrences are not available yet.", "neutral"));
        details.append(body);
        return details;
    }
    if (advancement.occurrences.length === 0) {
        body.append(createInlineState("No advancement occurrences were supplied for this Character.", "neutral"));
        details.append(body);
        return details;
    }

    const list = createElement("div", "dd-advancement-list");
    for (const item of buildAdvancementPresentation(advancement)) {
        const occurrence = item.occurrence;
        const card = createElement("article", "dd-advancement-entry");
        card.setAttribute("data-advancement-occurrence-id", occurrence.occurrenceId);
        card.setAttribute("data-advancement-concept-key", occurrence.conceptKey);
        card.setAttribute("data-advancement-kind", occurrence.kind);
        if (occurrence.parentOccurrenceId !== undefined) {
            card.setAttribute("data-parent-advancement-occurrence-id", occurrence.parentOccurrenceId);
        }

        const heading = createElement("div", "dd-advancement-entry__heading");
        heading.append(createElement("h3", "dd-advancement-entry__name", occurrence.displayName));
        const meta = createElement("div", "dd-advancement-entry__meta");
        meta.append(createElement("span", "dd-advancement-entry__kind", advancementKindLabel(occurrence)));
        if (occurrence.progression !== undefined) {
            meta.append(createElement("span", "dd-advancement-entry__progression", formatAdvancementProgression(occurrence.progression)));
        }
        heading.append(meta);
        card.append(heading);

        if (item.parent !== undefined) {
            card.append(createElement("p", "dd-advancement-entry__relationship", `Parent: ${item.parent.displayName}`));
        } else if (item.unresolvedParent) {
            card.append(createElement("p", "dd-advancement-entry__relationship", "Parent advancement is unavailable."));
        }

        if (occurrence.progressionDetails?.length) {
            card.append(renderFields("Progression details", occurrence.progressionDetails));
        }
        if (occurrence.grantedFeaturesOrMechanics?.length) {
            card.append(renderGrants(occurrence.grantedFeaturesOrMechanics));
        }
        const sources = renderSourceAttributions(occurrence.sourceAttributions, true);
        if (sources !== null) card.append(sources);
        list.append(card);
    }

    body.append(list);
    details.append(body);
    return details;
}

function renderFields(
    title: string,
    fields: readonly { key: string; label: string; value: string }[]
): HTMLElement {
    const section = createElement("section", "dd-advancement-entry__details");
    section.append(createElement("h4", "dd-advancement-entry__subheading", title));
    const list = createElement("dl", "dd-advancement-entry__fields");
    for (const field of fields) {
        const row = createElement("div", "dd-definition-row");
        row.setAttribute("data-advancement-field-key", field.key);
        row.append(
            createElement("dt", "dd-definition-row__term", field.label),
            createElement("dd", "dd-definition-row__value", field.value)
        );
        list.append(row);
    }
    section.append(list);
    return section;
}

function renderGrants(grants: readonly AdvancementGrantView[]): HTMLElement {
    const section = createElement("section", "dd-advancement-entry__grants");
    section.append(createElement("h4", "dd-advancement-entry__subheading", "Granted Features & Mechanics"));
    const list = createElement("div", "dd-advancement-grant-list");
    for (const grant of grants) {
        const item = createElement("div", "dd-advancement-grant");
        item.setAttribute("data-advancement-grant-key", grant.key);
        if (grant.conceptKey !== undefined) {
            item.setAttribute("data-advancement-grant-concept-key", grant.conceptKey);
        }
        item.append(createElement("strong", "dd-advancement-grant__label", grant.label));
        if (grant.detail !== undefined) {
            item.append(createElement("span", "dd-advancement-grant__detail", grant.detail));
        }
        const sources = renderSourceAttributions(grant.sourceAttributions, true);
        if (sources !== null) item.append(sources);
        list.append(item);
    }
    section.append(list);
    return section;
}
