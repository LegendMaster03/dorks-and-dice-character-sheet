import type {
    CharacterCheckView,
    CharacterProcedureView,
    DisplayFieldView,
    InventoryMechanicsView,
    ItemOccurrenceMechanicsView,
    SourceAttributionView,
    SpellcastingProfileView
} from "./character-mechanics.js";
import { createElement, createInlineState } from "./components.js";
import { renderDisplayFields, renderFacts, renderMechanicalValue } from "./mechanics-components.js";
import { renderSourceAttributions } from "./source-attribution.js";

export function renderCheck(check: CharacterCheckView): HTMLElement {
    const root = createElement("article", "dd-check-card");
    root.setAttribute("data-check-key", check.key);
    root.append(createElement("h4", "dd-check-card__name", check.name));
    const facts = renderFacts([fieldTuple(check.ability), fieldTuple(check.competencyOrTool), fieldTuple(check.target)]);
    if (facts !== null) root.append(facts);
    if (check.effectiveModifierOrResult) root.append(renderMechanicalValue(check.effectiveModifierOrResult, true));
    appendSources(root, check.sourceAttributions);
    return root;
}

export function renderProcedure(procedure: CharacterProcedureView): HTMLElement {
    const root = createElement("article", "dd-procedure-card");
    root.setAttribute("data-procedure-key", procedure.key);
    root.append(createElement("h3", "dd-procedure-card__name", procedure.name));
    const checks = createElement("div", "dd-procedure-card__checks");
    checks.append(...procedure.components.map(renderCheck));
    root.append(checks);
    const facts = renderFacts([fieldTuple(procedure.state), fieldTuple(procedure.result)]);
    if (facts !== null) root.append(facts);
    appendSources(root, procedure.sourceAttributions);
    return root;
}

export function renderChecksAndProceduresPresentation(
    checks: readonly CharacterCheckView[] | undefined,
    procedures: readonly CharacterProcedureView[] | undefined
): HTMLElement {
    const root = createElement("section", "dd-check-procedure-presentation");
    root.append(createElement("h3", "dd-check-procedure-presentation__title", "Checks & Procedures"));
    root.setAttribute(
        "data-check-procedure-state",
        checks === undefined && procedures === undefined ? "unavailable" : "resolved");

    if (checks === undefined && procedures === undefined) {
        root.append(createInlineState("-", "neutral"));
        return root;
    }

    const primaryChecks = (checks ?? []).filter(value => value.supplemental !== true);
    const primaryProcedures = (procedures ?? []).filter(value => value.supplemental !== true);
    const supplementalChecks = (checks ?? []).filter(value => value.supplemental === true);
    const supplementalProcedures = (procedures ?? []).filter(value => value.supplemental === true);

    appendCheckProcedureGroups(root, primaryChecks, primaryProcedures);

    if (supplementalChecks.length > 0 || supplementalProcedures.length > 0) {
        const sourceCredit = renderSourceAttributions(
            collectSourceAttributions(supplementalChecks, supplementalProcedures),
            true);
        if (sourceCredit !== null) {
            const credit = createElement("div", "dd-check-procedure-presentation__supplemental-credit");
            credit.append(sourceCredit);
            root.append(credit);
        }

        const disclosure = createElement("details", "dd-check-procedure-presentation__supplemental");
        disclosure.append(createElement(
            "summary",
            "dd-check-procedure-presentation__supplemental-toggle",
            "Supplemental checks & procedures"));
        const body = createElement("div", "dd-check-procedure-presentation__supplemental-body");
        appendCheckProcedureGroups(body, supplementalChecks, supplementalProcedures);
        disclosure.append(body);
        root.append(disclosure);
    }

    if (primaryChecks.length === 0
        && primaryProcedures.length === 0
        && supplementalChecks.length === 0
        && supplementalProcedures.length === 0) {
        root.append(createInlineState("-", "neutral"));
    }
    return root;
}

function appendCheckProcedureGroups(
    target: HTMLElement,
    checks: readonly CharacterCheckView[],
    procedures: readonly CharacterProcedureView[]
): void {
    if (checks.length > 0) {
        const group = createElement("div", "dd-check-procedure-presentation__checks");
        group.append(...checks.map(renderCheck));
        target.append(group);
    }
    if (procedures.length > 0) {
        const group = createElement("div", "dd-check-procedure-presentation__procedures");
        group.append(...procedures.map(renderProcedure));
        target.append(group);
    }
}

function collectSourceAttributions(
    checks: readonly CharacterCheckView[],
    procedures: readonly CharacterProcedureView[]
): readonly SourceAttributionView[] {
    const byKey = new Map<string, SourceAttributionView>();
    for (const source of [
        ...checks.flatMap(value => value.sourceAttributions ?? []),
        ...procedures.flatMap(value => value.sourceAttributions ?? [])
    ]) {
        if (source.presentationRequired !== true) continue;
        if (!byKey.has(source.key)) byKey.set(source.key, source);
    }
    return [...byKey.values()];
}

export function renderItemOccurrenceMechanics(mechanics: ItemOccurrenceMechanicsView | undefined): HTMLElement | null {
    if (mechanics === undefined || !(mechanics.values?.length || mechanics.facts?.length || mechanics.sourceAttributions?.length)) return null;
    const details = createElement("details", "dd-item-mechanics");
    details.append(createElement("summary", "dd-item-mechanics__toggle", "Mechanical details"));
    const body = createElement("div", "dd-item-mechanics__body");
    body.append(...(mechanics.values ?? []).map(value => renderMechanicalValue(value, true)));
    const facts = renderDisplayFields(mechanics.facts);
    if (facts !== null) body.append(facts);
    appendSources(body, mechanics.sourceAttributions);
    details.append(body);
    return details;
}

export function renderInventoryMechanics(mechanics: InventoryMechanicsView | undefined): HTMLElement | null {
    if (mechanics === undefined) return null;
    const root = createElement("div", "dd-inventory-mechanics");
    if (mechanics.carrying) {
        const section = subsection("Carrying & Load");
        const fields = [mechanics.carrying.carried, mechanics.carrying.load, ...(mechanics.carrying.thresholds ?? [])].filter((value): value is DisplayFieldView => value !== undefined);
        const facts = renderDisplayFields(fields);
        if (facts !== null) section.append(facts);
        appendSources(section, mechanics.carrying.sourceAttributions);
        root.append(section);
    }
    if (mechanics.components?.length) {
        const section = subsection("Components");
        for (const component of mechanics.components) {
            const item = createElement("article", "dd-component-card");
            item.setAttribute("data-component-key", component.key);
            item.append(createElement("h4", "dd-component-card__name", component.label));
            if (component.quantity) item.append(createElement("span", "dd-component-card__quantity", component.quantity));
            const facts = renderDisplayFields(component.fields);
            if (facts !== null) item.append(facts);
            appendSources(item, component.sourceAttributions);
            section.append(item);
        }
        root.append(section);
    }
    if (mechanics.procedures?.length) {
        const section = subsection("Procedures");
        section.append(...mechanics.procedures.map(renderProcedure));
        root.append(section);
    }
    if (mechanics.crafting?.length) {
        const section = subsection("Crafting");
        for (const crafting of mechanics.crafting) {
            const item = createElement("article", "dd-crafting-card");
            item.setAttribute("data-crafting-key", crafting.key);
            item.append(createElement("h4", "dd-crafting-card__name", crafting.name));
            const facts = renderFacts([fieldTuple(crafting.targetOutput), fieldTuple(crafting.progress), fieldTuple(crafting.result)]);
            if (facts !== null) item.append(facts);
            for (const [label, fields] of [["Required inputs", crafting.requiredInputs], ["Competencies & tools", crafting.competenciesOrTools]] as const) {
                const rendered = renderDisplayFields(fields, label);
                if (rendered !== null) item.append(rendered);
            }
            if (crafting.procedure) item.append(renderProcedure(crafting.procedure));
            appendSources(item, crafting.sourceAttributions);
            section.append(item);
        }
        root.append(section);
    }
    return root.children.length === 0 ? null : root;
}

export function renderSpellcastingPresentation(profiles: readonly SpellcastingProfileView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-spellcasting-profiles");
    if (profiles === undefined || profiles.length === 0) {
        root.append(createInlineState(profiles === undefined ? "Resolved spellcasting profiles are not available." : "No spellcasting profiles were supplied for this Character.", "neutral"));
        return root;
    }
    for (const profile of profiles) root.append(renderSpellcastingProfile(profile));
    return root;
}

function renderSpellcastingProfile(profile: SpellcastingProfileView): HTMLElement {
    const root = createElement("article", "dd-spellcasting-profile");
    root.setAttribute("data-spellcasting-profile-key", profile.key);
    root.append(createElement("h3", "dd-spellcasting-profile__name", profile.label));
    const facts = renderFacts([
        ["Source", profile.castingSource], ["Casting ability", profile.castingAbility], fieldTuple(profile.resourceSystem),
        ["Domains", profile.domains?.join(", ")], ["Specialty school", profile.specialtySchool],
        ["Prohibited schools", profile.prohibitedSchools?.join(", ")], fieldTuple(profile.arcaneSpellFailure)
    ]);
    if (facts !== null) root.append(facts);
    if (profile.saveDc) root.append(renderMechanicalValue(profile.saveDc, true));
    if (profile.spellAttack) root.append(renderMechanicalValue(profile.spellAttack, true));
    for (const [label, fields] of [["Bonus spells", profile.bonusSpells], [undefined, profile.metadata]] as const) {
        const rendered = renderDisplayFields(fields, label);
        if (rendered !== null) root.append(rendered);
    }
    appendSources(root, profile.sourceAttributions);
    return root;
}

function fieldTuple(field: DisplayFieldView | undefined): readonly [string, string] | undefined {
    return field === undefined ? undefined : [field.label, field.value];
}

function appendSources(target: HTMLElement, sources: Parameters<typeof renderSourceAttributions>[0]): void {
    const rendered = renderSourceAttributions(sources, true);
    if (rendered !== null) target.append(rendered);
}

function subsection(title: string): HTMLElement {
    const section = createElement("section", "dd-inventory-mechanics__section");
    section.append(createElement("h3", "dd-inventory-mechanics__title", title));
    return section;
}
