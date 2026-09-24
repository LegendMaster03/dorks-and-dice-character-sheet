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

interface CheckProcedureRenderOptions {
    compact?: boolean;
    showSources?: boolean;
}

export function renderCheck(
    check: CharacterCheckView,
    options: CheckProcedureRenderOptions = {}
): HTMLElement {
    const root = createElement(
        "article",
        options.compact ? "dd-check-card dd-check-card--compact" : "dd-check-card");
    root.setAttribute("data-check-key", check.key);
    root.append(createElement("h4", "dd-check-card__name", check.name));
    const facts = renderFacts([fieldTuple(check.ability), fieldTuple(check.competencyOrTool), fieldTuple(check.target)]);
    if (facts !== null) root.append(facts);
    if (check.effectiveModifierOrResult) root.append(renderMechanicalValue(check.effectiveModifierOrResult, true));
    if (options.showSources !== false) appendSources(root, check.sourceAttributions);
    return root;
}

export function renderProcedure(
    procedure: CharacterProcedureView,
    options: CheckProcedureRenderOptions = {}
): HTMLElement {
    const root = createElement(
        "article",
        options.compact ? "dd-procedure-card dd-procedure-card--compact" : "dd-procedure-card");
    root.setAttribute("data-procedure-key", procedure.key);
    root.append(createElement("h3", "dd-procedure-card__name", procedure.name));
    const checks = createElement("div", "dd-procedure-card__checks");
    checks.append(...procedure.components.map(component => renderCheck(component, options)));
    root.append(checks);
    const facts = renderFacts([fieldTuple(procedure.state), fieldTuple(procedure.result)]);
    if (facts !== null) root.append(facts);
    if (options.showSources !== false) appendSources(root, procedure.sourceAttributions);
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
        const supplementalSources = collectSourceAttributions(
            supplementalChecks,
            supplementalProcedures);
        const nestedCheckKeys = new Set(
            supplementalProcedures.flatMap(procedure =>
                procedure.components.map(component => component.key)));
        const standaloneSupplementalChecks = supplementalChecks.filter(
            check => !nestedCheckKeys.has(check.key));

        const disclosure = createElement("details", "dd-check-procedure-presentation__supplemental");
        disclosure.append(createElement(
            "summary",
            "dd-check-procedure-presentation__supplemental-toggle",
            supplementalDisclosureLabel(
                supplementalSources,
                supplementalChecks.length,
                supplementalProcedures.length)));
        const body = createElement("div", "dd-check-procedure-presentation__supplemental-body");

        const sourceCredit = renderSourceAttributions(supplementalSources, true);
        if (sourceCredit !== null) {
            const credit = createElement("div", "dd-check-procedure-presentation__supplemental-credit");
            credit.append(sourceCredit);
            body.append(credit);
        }

        appendCheckProcedureGroups(
            body,
            standaloneSupplementalChecks,
            supplementalProcedures,
            { compact: true, showSources: false });
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
    procedures: readonly CharacterProcedureView[],
    options: CheckProcedureRenderOptions = {}
): void {
    if (checks.length > 0) {
        const group = createElement("div", "dd-check-procedure-presentation__checks");
        group.append(...checks.map(check => renderCheck(check, options)));
        target.append(group);
    }
    if (procedures.length > 0) {
        const group = createElement("div", "dd-check-procedure-presentation__procedures");
        group.append(...procedures.map(procedure => renderProcedure(procedure, options)));
        target.append(group);
    }
}

function supplementalDisclosureLabel(
    sources: readonly SourceAttributionView[],
    checkCount: number,
    procedureCount: number
): string {
    const kind = checkCount > 0 && procedureCount > 0
        ? "checks & procedures"
        : checkCount > 0
            ? "checks"
            : "procedures";
    const labels = [...new Set(
        sources
            .map(source => source.label.trim())
            .filter(label => label.length > 0))];

    if (labels.length === 1) return `${labels[0]} ${kind}`;
    if (labels.length > 1) return `Additional ${kind} · ${labels.join(" • ")}`;
    return `Additional ${kind}`;
}

function collectSourceAttributions(
    checks: readonly CharacterCheckView[],
    procedures: readonly CharacterProcedureView[]
): readonly SourceAttributionView[] {
    const byKey = new Map<string, SourceAttributionView>();
    for (const source of [
        ...checks.flatMap(value => value.sourceAttributions ?? []),
        ...procedures.flatMap(value => value.sourceAttributions ?? []),
        ...procedures.flatMap(value =>
            value.components.flatMap(component => component.sourceAttributions ?? []))
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

export interface SpellcastingResourceControlOptions {
    readOnly?: boolean;
    savingResourceKey?: string | null;
    onSetResource?: (resourceKey: string, currentValue: number) => void;
}

export function renderSpellcastingPresentation(
    profiles: readonly SpellcastingProfileView[] | undefined,
    control: SpellcastingResourceControlOptions = {}
): HTMLElement {
    const root = createElement("div", "dd-spellcasting-profiles");
    root.setAttribute("data-spellcasting-state", profiles === undefined ? "unavailable" : "resolved");
    if (profiles === undefined || profiles.length === 0) {
        root.append(createInlineState("-", "neutral"));
        return root;
    }
    for (const profile of profiles) root.append(renderSpellcastingProfile(profile, control));
    return root;
}

function renderSpellcastingProfile(
    profile: SpellcastingProfileView,
    control: SpellcastingResourceControlOptions
): HTMLElement {
    const root = createElement("article", "dd-spellcasting-profile");
    root.setAttribute("data-spellcasting-profile-key", profile.key);
    root.append(createElement("h3", "dd-spellcasting-profile__name", profile.label));
    const facts = renderFacts([
        ["Source", profile.castingSource], ["Casting ability", profile.castingAbility], fieldTuple(profile.resourceSystem),
        ["Domains", profile.domains?.join(", ")], ["Specialty school", profile.specialtySchool],
        ["Prohibited schools", profile.prohibitedSchools?.join(", ")], fieldTuple(profile.arcaneSpellFailure)
    ]);
    if (facts !== null) root.append(facts);
    if (profile.resources !== undefined && profile.resources.length > 0) {
        root.append(renderSpellcastingResources(profile.resources, control));
    }
    if (profile.saveDc) root.append(renderMechanicalValue(profile.saveDc, true));
    if (profile.spellAttack) root.append(renderMechanicalValue(profile.spellAttack, true));
    for (const [label, fields] of [["Bonus spells", profile.bonusSpells], [undefined, profile.metadata]] as const) {
        const rendered = renderDisplayFields(fields, label);
        if (rendered !== null) root.append(rendered);
    }
    appendSources(root, profile.sourceAttributions);
    return root;
}

function renderSpellcastingResources(
    resources: NonNullable<SpellcastingProfileView["resources"]>,
    control: SpellcastingResourceControlOptions
): HTMLElement {
    const section = createElement("section", "dd-spellcasting-profile__resources");
    section.append(createElement("h4", "dd-spellcasting-profile__resources-title", "Resources"));

    for (const resource of resources) {
        const row = createElement("div", "dd-definition-row");
        row.setAttribute("data-spellcasting-resource-key", resource.key);
        const label = createElement("span", "dd-definition-row__term", resource.label);
        const value = resource.current !== undefined && resource.maximum !== undefined
            ? `${resource.current} / ${resource.maximum}`
            : resource.current !== undefined
                ? String(resource.current)
                : resource.maximum !== undefined
                    ? `Max ${resource.maximum}`
                    : "-";
        const valueElement = createElement("span", "dd-definition-row__value", value);
        row.append(label, valueElement);

        if (resource.recoveryProcedureKey !== undefined) {
            row.append(createElement(
                "span",
                "dd-routine-meta",
                `Recovery: ${resource.recoveryProcedureKey}`));
        }

        if (control.readOnly !== true && control.onSetResource !== undefined) {
            const editor = createElement("div", "dd-spellcasting-profile__resource-editor");
            const input = createElement("input", "dd-sheet-screen__input");
            input.type = "number";
            input.step = "1";
            input.inputMode = "numeric";
            input.value = resource.current === undefined ? "" : String(resource.current);
            input.setAttribute("aria-label", `${resource.label} current value`);
            input.addEventListener("input", () => input.setCustomValidity(""));

            const saving = control.savingResourceKey === resource.key;
            const save = createElement(
                "button",
                "dd-button dd-button--secondary",
                saving ? "Saving…" : "Set") as HTMLButtonElement;
            save.type = "button";
            save.disabled = control.savingResourceKey !== null
                && control.savingResourceKey !== undefined;
            save.addEventListener("click", () => {
                const parsed = parseResourceValue(input.value);
                if (parsed === null) {
                    input.setCustomValidity(
                        "Enter a whole number that can be represented by the Character Sheet API.");
                    input.reportValidity();
                    return;
                }
                input.setCustomValidity("");
                control.onSetResource!(resource.key, parsed);
            });
            editor.append(input, save);
            row.append(editor);
        }

        section.append(row);
    }
    return section;
}

function parseResourceValue(value: string): number | null {
    const normalized = value.trim();
    if (!/^-?\d+$/.test(normalized)) return null;
    const parsed = Number(normalized);
    return Number.isSafeInteger(parsed)
        && parsed >= -2147483648
        && parsed <= 2147483647
        ? parsed
        : null;
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
