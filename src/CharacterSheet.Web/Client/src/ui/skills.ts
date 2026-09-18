import { createElement, createPlaceholder, createSectionCard } from "./components.js";

export interface SkillPresentationSkill {
    id: string;
    label: string;
    displayValue: string;
}

export type SkillPresentationItem =
    | {
        kind: "standalone";
        skill: SkillPresentationSkill;
    }
    | {
        kind: "composite";
        parent: SkillPresentationSkill;
        components: readonly [SkillPresentationSkill, ...SkillPresentationSkill[]];
    };

type SkillRelationshipRole = "standalone" | "parent" | "component";

const unavailableMessage = "Skill values and resolved skill relationships are not available yet.";

export function renderSkillsCard(items: readonly SkillPresentationItem[] | null): HTMLElement {
    const card = createSectionCard("Skills", "dd-support-card dd-skills-card");

    if (items === null) {
        card.setAttribute("data-skills-state", "unavailable");
        card.append(createPlaceholder("Skills", unavailableMessage, true));
        return card;
    }

    card.setAttribute("data-skills-state", "resolved");
    const list = createElement("div", "dd-skill-list");

    items.forEach((item, index) => {
        if (item.kind === "standalone") {
            list.append(renderSkillRow(item.skill, "standalone"));
            return;
        }

        list.append(renderCompositeSkill(item, index));
    });

    card.append(list);
    return card;
}

function renderCompositeSkill(
    item: Extract<SkillPresentationItem, { kind: "composite" }>,
    index: number
): HTMLElement {
    const group = createElement("div", "dd-skill-group dd-skill-group--composite");
    group.setAttribute("role", "group");
    group.setAttribute("data-component-count", String(item.components.length));
    group.style.setProperty("--dd-skill-component-count", String(item.components.length));

    const parentLabelId = "dd-skill-group-" + index + "-parent-label";
    group.setAttribute("aria-labelledby", parentLabelId);
    group.append(renderSkillRow(item.parent, "parent", parentLabelId));

    for (const component of item.components) {
        group.append(renderSkillRow(component, "component"));
    }

    return group;
}

function renderSkillRow(
    skill: SkillPresentationSkill,
    relationship: SkillRelationshipRole,
    labelId?: string
): HTMLElement {
    const row = createElement(
        "div",
        "dd-skill-row dd-skill-row--" + relationship + (
            relationship === "parent"
                ? " dd-skill-group__parent"
                : relationship === "component"
                    ? " dd-skill-group__component"
                    : ""
        )
    );
    row.setAttribute("data-skill-row", "true");
    row.setAttribute("data-skill-id", skill.id);
    row.setAttribute("data-skill-role", relationship);

    const name = createElement("span", "dd-skill-row__name", skill.label);
    if (labelId !== undefined) {
        name.id = labelId;
    }

    const value = createElement("span", "dd-skill-row__value", skill.displayValue);
    row.append(name, value);
    return row;
}
