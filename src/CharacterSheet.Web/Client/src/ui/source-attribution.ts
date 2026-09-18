import type { SourceAttributionView } from "./character-mechanics.js";
import { createElement } from "./components.js";

export function renderSourceAttributions(
    attributions: readonly SourceAttributionView[] | undefined,
    compact = false
): HTMLElement | null {
    if (attributions === undefined || attributions.length === 0) return null;

    const group = createElement(
        "div",
        compact ? "dd-source-attributions dd-source-attributions--compact" : "dd-source-attributions"
    );
    group.setAttribute("aria-label", "Rule sources");

    for (const attribution of attributions) {
        const item = createElement("div", "dd-source-attribution");
        item.setAttribute("data-source-attribution-key", attribution.key);
        item.append(createElement("span", "dd-source-attribution__label", attribution.label));
        if (attribution.detail !== undefined && attribution.detail.trim().length > 0) {
            item.append(createElement("span", "dd-source-attribution__detail", attribution.detail));
        }
        if (attribution.officialUrl !== undefined && attribution.officialUrl.trim().length > 0) {
            const link = createElement("a", "dd-source-attribution__link", attribution.linkLabel ?? "View official source");
            link.href = attribution.officialUrl;
            link.target = "_blank";
            link.rel = "noopener noreferrer";
            item.append(link);
        }
        group.append(item);
    }
    return group;
}
