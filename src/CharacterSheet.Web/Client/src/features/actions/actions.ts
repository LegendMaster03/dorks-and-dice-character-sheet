import type { ActionAttackView } from "../../ui/character-mechanics.js";
import { createElement, createInlineState } from "../../ui/components.js";
import {
    appendSources,
    joinOptional,
    renderFacts,
    renderMechanicalValue
} from "../../core/mechanics/mechanic-value.js";

export function renderActionsPresentation(
    actions: readonly ActionAttackView[] | undefined
): HTMLElement {
    const root = createElement("div", "dd-action-list");
    root.setAttribute(
        "data-action-state",
        actions === undefined ? "unavailable" : "resolved");
    if (actions === undefined || actions.length === 0) {
        root.append(createInlineState("-", "neutral"));
        return root;
    }

    for (const action of actions) {
        const item = createElement("article", "dd-action-card");
        item.setAttribute("data-action-key", action.key);
        item.append(createElement("h3", "dd-action-card__name", action.name));
        if (action.actionType) {
            item.append(createElement(
                "span",
                "dd-action-card__type",
                action.actionType));
        }
        if (action.attackOrCheck) {
            item.append(renderMechanicalValue(action.attackOrCheck, true));
        }

        const facts = renderFacts([
            ["Damage", action.damage === undefined
                ? undefined
                : action.damageType === undefined
                    ? action.damage
                    : `${action.damage} ${action.damageType}`],
            ["Critical", joinOptional(
                action.criticalRange,
                action.criticalMultiplier,
                " / ")],
            ["Range", action.range],
            ["Reach", action.reach],
            ["Ammunition", action.ammunition],
            ["Target", action.target]
        ]);
        if (facts !== null) item.append(facts);

        if (action.notes?.length) {
            const notes = createElement("ul", "dd-action-card__notes");
            for (const note of action.notes) {
                notes.append(createElement("li", undefined, note));
            }
            item.append(notes);
        }
        appendSources(item, action.sourceAttributions);
        root.append(item);
    }
    return root;
}
