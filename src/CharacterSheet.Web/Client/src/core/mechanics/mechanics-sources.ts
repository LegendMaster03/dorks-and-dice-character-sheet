import type { CharacterMechanicsView } from "../../ui/character-mechanics.js";
import { createElement } from "../../ui/components.js";
import { renderSourceAttributionDisclosure } from "../../ui/source-attribution.js";

export function renderCharacterMechanicsSources(
    mechanics: CharacterMechanicsView | null
): HTMLElement | null {
    const sources = renderSourceAttributionDisclosure(mechanics?.sourceAttributions);
    if (sources === null) return null;

    const surface = createElement("aside", "dd-character-mechanics-sources");
    surface.setAttribute("aria-label", "Character mechanics rule modules");
    surface.append(
        createElement(
            "span",
            "dd-character-mechanics-sources__label",
            "Rules modules"),
        sources);
    return surface;
}
