import type { SourceAttributionView } from "./character-mechanics.js";
import { createElement } from "./components.js";

export function getSafeExternalSourceUrl(value: string | undefined): string | null {
    if (value === undefined || value.trim().length === 0) return null;
    try {
        const parsed = new URL(value);
        return parsed.protocol === "https:" ? parsed.href : null;
    } catch {
        return null;
    }
}

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
        const safeUrl = getSafeExternalSourceUrl(attribution.officialUrl);
        if (safeUrl !== null) {
            const link = createElement("a", "dd-source-attribution__link", attribution.linkLabel ?? "View official source");
            link.href = safeUrl;
            link.target = "_blank";
            link.rel = "noopener noreferrer";
            item.append(link);
        }
        group.append(item);
    }
    return group;
}
