import {
    formatHealthTrack,
    formatMechanicalValue,
    hasMechanicalDetails,
    type ActionAttackView,
    type CalculatedMechanicalValueView,
    type CharacterMechanicsView,
    type DisplayFieldView,
    type SavingThrowView
} from "./character-mechanics.js";
import { createElement, createInlineState, createSectionCard } from "./components.js";
import {
    renderSourceAttributionDisclosure,
    renderSourceAttributions
} from "./source-attribution.js";

export function renderMechanicalValue(value: CalculatedMechanicalValueView, compact = false): HTMLElement {
    const root = createElement("div", `dd-mechanic-value${compact ? " dd-mechanic-value--compact" : ""}`);
    root.setAttribute("data-mechanic-key", value.key);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(createElement("span", "dd-mechanic-value__label", value.label), createElement("strong", "dd-mechanic-value__value", formatMechanicalValue(value)));
    root.append(head);
    if (!hasMechanicalDetails(value)) return root;
    const details = createElement("details", "dd-mechanic-value__details");
    details.append(createElement("summary", "dd-mechanic-value__details-toggle", "Details"));
    const body = createElement("div", "dd-mechanic-value__details-body");
    const facts = renderFacts([
        ...(value.breakdown ?? []).map(entry => [entry.label, formatMechanicalValue(entry)] as const),
        ...(value.relatedValues ?? []).map(entry => [entry.label, formatMechanicalValue(entry)] as const)
    ]);
    if (facts !== null) body.append(facts);
    appendSources(body, value.sourceAttributions);
    details.append(body);
    root.append(details);
    return root;
}

export function renderSavingThrowsCard(saves: readonly SavingThrowView[] | undefined): HTMLElement {
    const card = createSectionCard("Saving Throws", "dd-support-card dd-saving-throws-card");
    card.setAttribute("data-saving-throws-state", saves === undefined ? "unavailable" : "resolved");
    const values = saves ?? [];
    const grid = createElement("div", "dd-saving-throws-card__grid");
    grid.append(...(
        values.length === 0 || usesThreeXSaveScaffold(values)
            ? renderSavingThrowScaffold(values)
            : values.map(renderSavingThrowCell)
    ));
    card.append(grid);
    return card;
}

export function findInitiativeValue(
    values: readonly CalculatedMechanicalValueView[] | undefined
): CalculatedMechanicalValueView | undefined {
    return values?.find(isInitiativeValue);
}

export function renderQuickMechanicalValue(
    value: CalculatedMechanicalValueView | undefined
): HTMLElement {
    const root = createElement("div", "dd-quick-mechanic");
    if (value === undefined) {
        root.setAttribute("data-quick-mechanic-state", "unavailable");
        root.append(createElement("p", "dd-stat__value", "-"));
        return root;
    }

    root.setAttribute("data-quick-mechanic-state", "resolved");
    root.setAttribute("data-mechanic-key", value.key);
    root.append(createElement("p", "dd-stat__value", formatMechanicalValue(value)));
    return root;
}

interface MechanicalScaffoldSlot {
    id: string;
    label: string;
    keys: readonly string[];
    labels?: readonly string[];
}

const ARMOR_CLASS_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "armor-class", label: "Armor Class", keys: ["defense.ac"], labels: ["Armor Class", "AC"] },
    { id: "touch-armor-class", label: "Touch Armor Class", keys: ["defense.ac.touch"], labels: ["Touch", "Touch AC"] },
    { id: "flat-footed-armor-class", label: "Flat-Footed Armor Class", keys: ["defense.ac.flat-footed"], labels: ["Flat-Footed", "Flat-Footed AC"] }
];

const SECONDARY_DEFENSE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "damage-reduction", label: "Damage Reduction", keys: ["defense.damage-reduction"] },
    { id: "spell-resistance", label: "Spell Resistance", keys: ["defense.spell-resistance"] }
];

const DEFENSE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    ...ARMOR_CLASS_SCAFFOLD,
    ...SECONDARY_DEFENSE_SCAFFOLD
];

const SAVE_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "fortitude", label: "Fortitude Save", keys: ["save.fortitude"], labels: ["Fortitude"] },
    { id: "reflex", label: "Reflex Save", keys: ["save.reflex"], labels: ["Reflex"] },
    { id: "will", label: "Will Save", keys: ["save.will"], labels: ["Will"] }
];

const COMBAT_SCAFFOLD: readonly MechanicalScaffoldSlot[] = [
    { id: "base-attack-bonus", label: "Base Attack Bonus", keys: ["combat.base-attack-bonus"] },
    { id: "grapple-modifier", label: "Grapple Modifier", keys: ["combat.grapple"], labels: ["Grapple"] }
];

export function renderCombatMechanicsSummary(mechanics: CharacterMechanicsView | null): HTMLElement {
    const section = createElement("section", "dd-combat-summary dd-combat-summary--mechanics");
    section.setAttribute("aria-labelledby", "dd-combat-heading");
    section.setAttribute("data-combat-mechanics-state", mechanics === null ? "unavailable" : "resolved");
    const heading = createElement("h2", "dd-visually-hidden", "Combat summary");
    heading.id = "dd-combat-heading";
    section.append(heading);

    appendCombatGroup(
        section,
        "Defense",
        "defense",
        renderMechanicalScaffold(
            orderedDefenses(mechanics),
            DEFENSE_SCAFFOLD));

    appendCombatGroup(
        section,
        "Saving Throws",
        "saves",
        renderSavingThrowScaffold(mechanics?.savingThrows ?? []));

    appendCombatGroup(
        section,
        "Health",
        "health",
        renderHealthScaffold(mechanics));

    const initiative = findInitiativeValue(mechanics?.combatFundamentals);
    const combatValues = (mechanics?.combatFundamentals ?? [])
        .filter(value => value !== initiative);
    appendCombatGroup(
        section,
        "Combat",
        "combat",
        renderMechanicalScaffold(combatValues, COMBAT_SCAFFOLD));

    return section;
}

export function renderArmorClassQuickCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const values = orderedDefenses(mechanics);
    const usedKeys = new Set<string>();
    const primary = findPrimaryArmorClassValue(mechanics, values, usedKeys);
    if (primary !== undefined) usedKeys.add(primary.key);

    const touch = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[1], usedKeys);
    if (touch !== undefined) usedKeys.add(touch.key);
    const flatFooted = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[2], usedKeys);

    const card = createElement("article", "dd-stat dd-stat--armor-class");
    card.setAttribute("data-armor-class-card", "true");

    const primaryRegion = createElement("div", "dd-armor-class__primary");
    primaryRegion.append(
        createElement("h3", "dd-stat__label", "Armor Class"),
        renderArmorClassValue(primary, ARMOR_CLASS_SCAFFOLD[0], "dd-armor-class__primary-value")
    );

    const variants = createElement("div", "dd-armor-class__variants");
    variants.append(
        renderArmorClassVariant("Touch AC", touch, ARMOR_CLASS_SCAFFOLD[1]),
        renderArmorClassVariant("Flat-Footed AC", flatFooted, ARMOR_CLASS_SCAFFOLD[2])
    );

    card.append(primaryRegion, variants);
    return card;
}

export function renderDefenseMechanicsCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const values = orderedDefenses(mechanics);
    const armorClassKeys = new Set(
        resolveArmorClassValues(mechanics, values)
            .filter((value): value is CalculatedMechanicalValueView => value !== undefined)
            .map(value => value.key));
    const secondaryValues = values.filter(value => !armorClassKeys.has(value.key));

    const card = createSectionCard("Defense", "dd-mechanic-group-card dd-defense-card");
    const grid = createElement("div", "dd-mechanic-group-card__grid dd-defense-card__grid");
    grid.append(...renderMechanicalScaffold(secondaryValues, SECONDARY_DEFENSE_SCAFFOLD));
    card.append(grid);
    return card;
}

export function renderCombatFundamentalsCard(mechanics: CharacterMechanicsView | null): HTMLElement {
    const card = createSectionCard("Combat", "dd-mechanic-group-card dd-combat-fundamentals-card");
    const initiative = findInitiativeValue(mechanics?.combatFundamentals);
    const values = (mechanics?.combatFundamentals ?? []).filter(value => value !== initiative);
    const grid = createElement("div", "dd-mechanic-group-card__grid dd-combat-fundamentals-card__grid");
    grid.append(...renderMechanicalScaffold(values, COMBAT_SCAFFOLD));
    card.append(grid);
    return card;
}

export type RestKind = "short" | "long";

export interface HealthControlOptions {
    currentHitPoints?: number | null;
    readOnly?: boolean;
    saving?: boolean;
    onSetCurrentHitPoints?: (currentHitPoints: number | null) => void;
}

export function adjustCurrentHitPoints(
    current: number | null,
    maximum: unknown,
    amount: number,
    direction: -1 | 1
): number | null {
    if (current === null || !Number.isFinite(amount) || amount < 0) return null;
    const delta = Math.trunc(amount) * direction;
    let next = current + delta;
    const numericMaximum = toFiniteInteger(maximum);
    if (direction > 0 && numericMaximum !== null) {
        next = Math.min(next, numericMaximum);
    }
    return next;
}

export function renderHealthMechanicsCard(
    mechanics: CharacterMechanicsView | null,
    control: HealthControlOptions = {}
): HTMLElement {
    const card = createSectionCard("Hit Points", "dd-support-card dd-health-card");
    const tracks = mechanics?.healthTracks ?? [];

    const hitPoints = tracks.find(track =>
        track.role === "hit-points" || normalizeMechanicalLabel(track.label) === "hitpoints");
    const temporaryHitPoints = tracks.find(track =>
        track.role === "temporary-hit-points"
        || normalizeMechanicalLabel(track.label) === "temporaryhitpoints"
        || normalizeMechanicalLabel(track.label) === "temphp");
    const nonlethal = tracks.find(track =>
        track.role === "nonlethal-damage"
        || track.key === "resource.nonlethal-damage"
        || normalizeMechanicalLabel(track.label) === "nonlethaldamage");

    const currentValue = control.currentHitPoints === undefined
        ? hitPoints?.current
        : control.currentHitPoints;

    const primary = createElement("div", "dd-health-card__primary");
    primary.append(
        renderHealthSummaryField("Current", currentValue, "dd-health-card__field--current", hitPoints),
        renderHealthSummaryField("Maximum", hitPoints?.maximum, "dd-health-card__field--maximum", hitPoints)
    );
    card.append(primary);

    if (control.readOnly !== true && control.onSetCurrentHitPoints !== undefined) {
        card.append(renderHitPointEditor(
            toFiniteInteger(currentValue),
            hitPoints?.maximum,
            control.saving === true,
            control.onSetCurrentHitPoints));
    }

    const secondary = createElement(
        "div",
        temporaryHitPoints === undefined
            ? "dd-health-card__secondary dd-health-card__secondary--single"
            : "dd-health-card__secondary");
    if (temporaryHitPoints !== undefined) {
        secondary.append(renderHealthSummaryField(
            "Temporary HP",
            temporaryHitPoints.formattedValue ?? temporaryHitPoints.current,
            "dd-health-card__field--temporary",
            temporaryHitPoints));
    }
    secondary.append(renderHealthSummaryField(
        "Nonlethal Damage",
        nonlethal?.formattedValue ?? nonlethal?.current,
        "dd-health-card__field--nonlethal",
        nonlethal,
        "nonlethal-damage"));
    card.append(secondary);

    const promoted = new Set(
        [hitPoints, temporaryHitPoints, nonlethal]
            .filter((track): track is NonNullable<typeof track> => track !== undefined)
            .map(track => track.key));
    const extraTracks = tracks.filter(track => !promoted.has(track.key));
    if (extraTracks.length > 0) {
        const extras = createElement("div", "dd-health-card__extras");
        extras.append(...extraTracks.map(renderHealthTrack));
        card.append(extras);
    }

    const sources = renderSourceAttributionDisclosure(
        collectHealthTrackSources([hitPoints, temporaryHitPoints, nonlethal, ...extraTracks]));
    if (sources !== null) card.append(sources);
    return card;
}

export function renderRestControls(
    readOnly: boolean,
    saving: boolean,
    onRest: ((kind: RestKind) => void) | undefined
): HTMLElement {
    const controls = createElement("div", "dd-rest-controls");
    controls.setAttribute("role", "group");
    controls.setAttribute("aria-label", "Rest actions");

    const available = onRest !== undefined;
    const disabled = readOnly || saving || !available;
    const unavailableTitle = readOnly
        ? "Rest actions are unavailable while this Character is read-only."
        : saving
            ? "Wait for the current Character state change to finish."
            : "Rest resolution is not available from the current rules projection.";

    const createRestButton = (kind: RestKind, label: string): HTMLButtonElement => {
        const button = createElement(
            "button",
            "dd-button dd-button--ghost dd-rest-button",
            label) as HTMLButtonElement;
        button.type = "button";
        button.disabled = disabled;
        button.setAttribute("data-rest-action", kind);
        if (disabled) button.title = unavailableTitle;
        if (!disabled) {
            button.onclick = () => onRest?.(kind);
        }
        return button;
    };

    controls.append(
        createRestButton("short", "Short Rest"),
        createRestButton("long", "Long Rest"));
    return controls;
}

function renderHitPointEditor(
    current: number | null,
    maximum: unknown,
    saving: boolean,
    onSetCurrentHitPoints: (currentHitPoints: number | null) => void
): HTMLElement {
    const details = createElement("details", "dd-health-editor");
    details.setAttribute("data-health-editor", "true");
    const summary = createElement(
        "summary",
        "dd-health-editor__summary",
        saving ? "Saving HP…" : "Adjust HP");
    details.append(summary);

    const body = createElement("div", "dd-health-editor__popover");
    body.setAttribute("role", "group");
    body.setAttribute("aria-label", "Adjust hit points");

    const direct = createElement("div", "dd-health-editor__direct");
    const currentField = createElement("label", "dd-health-editor__field");
    currentField.append(createElement("span", "dd-health-editor__label", "Current HP"));
    const currentInput = createElement("input", "dd-health-editor__input") as HTMLInputElement;
    currentInput.type = "number";
    currentInput.step = "1";
    currentInput.inputMode = "numeric";
    currentInput.value = current === null ? "" : String(current);
    currentInput.placeholder = "-";
    currentInput.disabled = saving;
    currentField.append(currentInput);

    const maximumField = createElement("div", "dd-health-editor__field");
    maximumField.append(
        createElement("span", "dd-health-editor__label", "Max HP"),
        createElement(
            "strong",
            "dd-health-editor__readonly",
            formatOptionalHealthNumber(maximum)));

    const set = createElement("button", "dd-button dd-button--secondary dd-health-editor__set", "Set") as HTMLButtonElement;
    set.type = "button";
    set.disabled = saving;
    set.setAttribute("data-health-action", "set");
    set.onclick = () => {
        const value = parseOptionalInteger(currentInput.value);
        if (value.valid) onSetCurrentHitPoints(value.value);
    };
    direct.append(currentField, maximumField, set);

    const adjust = createElement("div", "dd-health-editor__adjust");
    const amountField = createElement("label", "dd-health-editor__field");
    amountField.append(createElement("span", "dd-health-editor__label", "Modify by"));
    const amountInput = createElement("input", "dd-health-editor__input") as HTMLInputElement;
    amountInput.type = "number";
    amountInput.min = "0";
    amountInput.step = "1";
    amountInput.inputMode = "numeric";
    amountInput.placeholder = "0";
    amountInput.disabled = saving;
    amountField.append(amountInput);

    const subtract = createElement("button", "dd-button dd-button--ghost dd-health-editor__adjust-button", "−") as HTMLButtonElement;
    subtract.type = "button";
    subtract.disabled = saving || current === null;
    subtract.setAttribute("aria-label", "Subtract hit points");
    subtract.setAttribute("data-health-action", "subtract");

    const add = createElement("button", "dd-button dd-button--ghost dd-health-editor__adjust-button", "+") as HTMLButtonElement;
    add.type = "button";
    add.disabled = saving || current === null;
    add.setAttribute("aria-label", "Add hit points");
    add.setAttribute("data-health-action", "add");

    const apply = (direction: -1 | 1): void => {
        const amount = parseOptionalInteger(amountInput.value);
        if (!amount.valid || amount.value === null || amount.value < 0) return;
        const next = adjustCurrentHitPoints(current, maximum, amount.value, direction);
        if (next !== null) onSetCurrentHitPoints(next);
    };
    subtract.onclick = () => apply(-1);
    add.onclick = () => apply(1);

    adjust.append(amountField, subtract, add);
    body.append(direct, adjust);
    details.append(body);
    return details;
}

function parseOptionalInteger(value: string): { valid: boolean; value: number | null } {
    const trimmed = value.trim();
    if (trimmed.length === 0) return { valid: true, value: null };
    const parsed = Number(trimmed);
    return Number.isInteger(parsed)
        && parsed >= -2147483648
        && parsed <= 2147483647
        ? { valid: true, value: parsed }
        : { valid: false, value: null };
}

function toFiniteInteger(value: unknown): number | null {
    if (typeof value === "number") {
        return Number.isInteger(value) && Number.isFinite(value) ? value : null;
    }
    if (typeof value !== "string" || value.trim().length === 0) return null;
    const parsed = Number(value);
    return Number.isInteger(parsed) && Number.isFinite(parsed) ? parsed : null;
}

function formatOptionalHealthNumber(value: unknown): string {
    const parsed = toFiniteInteger(value);
    return parsed === null ? "-" : String(parsed);
}

function renderHealthSummaryField(
    label: string,
    value: unknown,
    className: string,
    track?: NonNullable<CharacterMechanicsView["healthTracks"]>[number],
    scaffoldKey?: string
): HTMLElement {
    const field = createElement("div", `dd-health-card__field ${className}`);
    if (track !== undefined) {
        field.setAttribute("data-health-track-key", track.key);
        field.setAttribute("data-health-track-role", track.role);
    } else if (scaffoldKey !== undefined) {
        field.setAttribute("data-sheet-scaffold-key", scaffoldKey);
    }
    field.append(
        createElement("span", "dd-health-card__label", label),
        createElement(
            "strong",
            "dd-health-card__value",
            value === undefined || value === null || String(value).trim().length === 0
                ? "-"
                : String(value))
    );
    return field;
}

function collectHealthTrackSources(
    tracks: readonly (NonNullable<CharacterMechanicsView["healthTracks"]>[number] | undefined)[]
): NonNullable<CharacterMechanicsView["sourceAttributions"]> {
    const byKey = new Map<string, NonNullable<CharacterMechanicsView["sourceAttributions"]>[number]>();
    for (const track of tracks) {
        for (const source of track?.sourceAttributions ?? []) {
            if (!byKey.has(source.key)) byKey.set(source.key, source);
        }
    }
    return [...byKey.values()];
}

function appendCombatGroup(
    target: HTMLElement,
    label: string,
    role: "defense" | "saves" | "health" | "combat",
    cells: readonly HTMLElement[]
): void {
    const group = createElement("section", `dd-combat-summary__group dd-combat-summary__group--${role}`);
    group.setAttribute("data-combat-group", role);
    group.append(createElement("h3", "dd-combat-summary__group-title", label));
    const grid = createElement("div", `dd-combat-summary__grid dd-combat-summary__grid--${role}`);
    grid.append(...cells);
    group.append(grid);
    target.append(group);
}

function resolveArmorClassValues(
    mechanics: CharacterMechanicsView | null,
    values: readonly CalculatedMechanicalValueView[]
): readonly (CalculatedMechanicalValueView | undefined)[] {
    const usedKeys = new Set<string>();
    const primary = findPrimaryArmorClassValue(mechanics, values, usedKeys);
    if (primary !== undefined) usedKeys.add(primary.key);
    const touch = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[1], usedKeys);
    if (touch !== undefined) usedKeys.add(touch.key);
    const flatFooted = findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[2], usedKeys);
    return [primary, touch, flatFooted];
}

function findPrimaryArmorClassValue(
    mechanics: CharacterMechanicsView | null,
    values: readonly CalculatedMechanicalValueView[],
    usedKeys: ReadonlySet<string>
): CalculatedMechanicalValueView | undefined {
    const primaryKey = mechanics?.defenses?.primaryKey;
    if (primaryKey !== undefined) {
        const primary = values.find(value =>
            !usedKeys.has(value.key)
            && value.key === primaryKey);
        if (primary !== undefined) return primary;
    }
    return findScaffoldValue(values, ARMOR_CLASS_SCAFFOLD[0], usedKeys);
}

function renderArmorClassValue(
    value: CalculatedMechanicalValueView | undefined,
    scaffold: MechanicalScaffoldSlot,
    className: string
): HTMLElement {
    const rendered = createElement("strong", className, value === undefined ? "-" : formatMechanicalValue(value));
    if (value === undefined) {
        rendered.setAttribute("data-sheet-scaffold-key", scaffold.id);
    } else {
        rendered.setAttribute("data-mechanic-key", value.key);
    }
    return rendered;
}

function renderArmorClassVariant(
    label: string,
    value: CalculatedMechanicalValueView | undefined,
    scaffold: MechanicalScaffoldSlot
): HTMLElement {
    const cell = createElement("div", "dd-armor-class__variant");
    if (value === undefined) {
        cell.setAttribute("data-sheet-scaffold-key", scaffold.id);
    } else {
        cell.setAttribute("data-mechanic-key", value.key);
    }
    cell.append(
        createElement("span", "dd-armor-class__variant-label", label),
        createElement(
            "strong",
            "dd-armor-class__variant-value",
            value === undefined ? "-" : formatMechanicalValue(value))
    );
    return cell;
}

function renderMechanicalScaffold(
    values: readonly CalculatedMechanicalValueView[],
    slots: readonly MechanicalScaffoldSlot[]
): HTMLElement[] {
    const usedKeys = new Set<string>();
    const cells = slots.map(slot => {
        const value = findScaffoldValue(values, slot, usedKeys);
        if (value !== undefined) {
            usedKeys.add(value.key);
            return renderMechanicalValue(value, true);
        }
        return renderScaffoldMechanicalValue(slot);
    });

    for (const value of values) {
        if (!usedKeys.has(value.key)) cells.push(renderMechanicalValue(value, true));
    }
    return cells;
}

function usesThreeXSaveScaffold(saves: readonly SavingThrowView[]): boolean {
    return SAVE_SCAFFOLD.some(slot =>
        findScaffoldValue(saves, slot, new Set<string>()) !== undefined);
}

function renderSavingThrowScaffold(saves: readonly SavingThrowView[]): HTMLElement[] {
    const usedKeys = new Set<string>();
    const cells = SAVE_SCAFFOLD.map(slot => {
        const save = findScaffoldValue(saves, slot, usedKeys) as SavingThrowView | undefined;
        if (save !== undefined) {
            usedKeys.add(save.key);
            return renderSavingThrowCell(save);
        }
        const item = createElement("div", "dd-saving-throw");
        item.append(renderScaffoldMechanicalValue(slot));
        return item;
    });

    for (const save of saves) {
        if (!usedKeys.has(save.key)) cells.push(renderSavingThrowCell(save));
    }
    return cells;
}

function renderHealthScaffold(mechanics: CharacterMechanicsView | null): HTMLElement[] {
    const tracks = mechanics?.healthTracks ?? [];
    const usedKeys = new Set<string>();

    const hitPoints = tracks.find(track =>
        track.role === "hit-points" || normalizeMechanicalLabel(track.label) === "hitpoints");
    if (hitPoints !== undefined) usedKeys.add(hitPoints.key);

    const nonlethal = tracks.find(track =>
        track.role === "nonlethal-damage"
        || track.key === "resource.nonlethal-damage"
        || normalizeMechanicalLabel(track.label) === "nonlethaldamage");
    if (nonlethal !== undefined) usedKeys.add(nonlethal.key);

    const cells = [
        hitPoints === undefined
            ? renderScaffoldHealthTrack("hit-points", "Hit Points")
            : renderHealthTrack(hitPoints),
        nonlethal === undefined
            ? renderScaffoldHealthTrack("nonlethal-damage", "Nonlethal Damage")
            : renderHealthTrack(nonlethal)
    ];

    for (const track of tracks) {
        if (!usedKeys.has(track.key)) cells.push(renderHealthTrack(track));
    }
    return cells;
}

function findScaffoldValue<T extends CalculatedMechanicalValueView>(
    values: readonly T[],
    slot: MechanicalScaffoldSlot,
    usedKeys: ReadonlySet<string>
): T | undefined {
    const keyMatch = values.find(value =>
        !usedKeys.has(value.key)
        && slot.keys.includes(value.key));
    if (keyMatch !== undefined) return keyMatch;

    const labels = new Set([slot.label, ...(slot.labels ?? [])].map(normalizeMechanicalLabel));
    return values.find(value =>
        !usedKeys.has(value.key)
        && labels.has(normalizeMechanicalLabel(value.label)));
}

function renderScaffoldMechanicalValue(slot: MechanicalScaffoldSlot): HTMLElement {
    const root = createElement(
        "div",
        "dd-mechanic-value dd-mechanic-value--compact dd-mechanic-value--scaffold");
    root.setAttribute("data-sheet-scaffold-key", slot.id);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", slot.label),
        createElement("strong", "dd-mechanic-value__value", "-"));
    root.append(head);
    return root;
}

function renderSavingThrowCell(save: SavingThrowView): HTMLElement {
    const item = createElement("div", "dd-saving-throw");
    item.append(renderMechanicalValue(save, true));
    const meta = renderFacts([["Ability", save.governingAbility], ["Training", save.training]]);
    if (meta !== null) item.append(meta);
    return item;
}

function renderHealthTrack(track: NonNullable<CharacterMechanicsView["healthTracks"]>[number]): HTMLElement {
    const cell = createElement("div", "dd-mechanic-value dd-mechanic-value--compact dd-health-track");
    cell.setAttribute("data-health-track-key", track.key);
    cell.setAttribute("data-health-track-role", track.role);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", track.label),
        createElement("strong", "dd-mechanic-value__value", formatHealthTrack(track)));
    cell.append(head);
    if (track.detail) cell.append(createElement("span", "dd-mechanic-value__meta", track.detail));
    appendSources(cell, track.sourceAttributions);
    return cell;
}

function renderScaffoldHealthTrack(role: "hit-points" | "nonlethal-damage", label: string): HTMLElement {
    const cell = createElement(
        "div",
        "dd-mechanic-value dd-mechanic-value--compact dd-health-track dd-mechanic-value--scaffold");
    cell.setAttribute("data-health-track-role", role);
    cell.setAttribute("data-sheet-scaffold-key", role);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", label),
        createElement("strong", "dd-mechanic-value__value", "-"));
    cell.append(head);
    return cell;
}

function isInitiativeValue(value: CalculatedMechanicalValueView): boolean {
    if (value.label.trim().toLowerCase() === "initiative") return true;
    return value.key
        .toLowerCase()
        .split(/[^a-z0-9]+/)
        .includes("initiative");
}

function normalizeMechanicalLabel(value: string): string {
    return value.toLowerCase().replace(/[^a-z0-9]+/g, "");
}

const MOVEMENT_PRESENTATION_MODES = [
    { id: "walk", label: "Walk", aliases: ["walk", "walking", "land", "land speed", "speed"] },
    { id: "swim", label: "Swim", aliases: ["swim", "swimming"] },
    { id: "climb", label: "Climb", aliases: ["climb", "climbing"] },
    { id: "fly", label: "Fly", aliases: ["fly", "flying"] }
] as const;

export function renderMovementValues(values: readonly CalculatedMechanicalValueView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-movement-values");
    root.setAttribute("data-movement-state", values === undefined ? "unavailable" : "resolved");

    const supplied = values ?? [];
    const claimed = new Set<CalculatedMechanicalValueView>();
    const walk = findMovementMode(supplied, MOVEMENT_PRESENTATION_MODES[0]);
    if (walk !== undefined) claimed.add(walk);

    const primary = createElement("div", "dd-movement-values__primary");
    primary.setAttribute("data-movement-primary", walk?.key ?? "movement.walk");
    primary.setAttribute("data-mechanic-key", walk?.key ?? "movement.walk");
    primary.setAttribute("data-movement-mode", "walk");
    primary.append(
        createElement("span", "dd-movement-values__primary-label", "Walk"),
        createElement(
            "strong",
            "dd-movement-values__primary-value",
            walk === undefined ? "-" : formatMechanicalValue(walk)));
    root.append(primary);

    const variants = createElement("div", "dd-movement-values__variants");
    variants.setAttribute("aria-label", "Additional movement speeds");

    for (const mode of MOVEMENT_PRESENTATION_MODES.slice(1)) {
        const value = findMovementMode(supplied, mode);
        if (value !== undefined) claimed.add(value);
        variants.append(renderMovementVariant(mode.id, mode.label, value));
    }

    for (const value of supplied.filter(value => !claimed.has(value))) {
        variants.append(renderMovementVariant(value.key, value.label, value));
    }

    root.append(variants);
    return root;
}

function renderMovementVariant(
    modeKey: string,
    label: string,
    value: CalculatedMechanicalValueView | undefined
): HTMLElement {
    const variant = createElement("div", "dd-movement-values__variant");
    variant.setAttribute("data-movement-mode", modeKey);
    variant.setAttribute("data-mechanic-key", value?.key ?? `movement.${modeKey}`);
    variant.append(
        createElement("span", "dd-movement-values__variant-label", label),
        createElement(
            "strong",
            "dd-movement-values__variant-value",
            value === undefined ? "-" : formatMechanicalValue(value)));
    return variant;
}

function findMovementMode(
    values: readonly CalculatedMechanicalValueView[],
    mode: { readonly id: string; readonly label: string; readonly aliases: readonly string[] }
): CalculatedMechanicalValueView | undefined {
    return values.find(value => {
        const normalizedKey = normalizeMechanicalLabel(value.key);
        const normalizedLabel = normalizeMechanicalLabel(value.label);
        const candidates = [mode.id, mode.label, ...mode.aliases].map(normalizeMechanicalLabel);
        return candidates.some(candidate =>
            normalizedLabel === candidate
            || normalizedKey === candidate
            || normalizedKey.endsWith(candidate));
    });
}

export function renderActionsPresentation(actions: readonly ActionAttackView[] | undefined): HTMLElement {
    const root = createElement("div", "dd-action-list");
    root.setAttribute("data-action-state", actions === undefined ? "unavailable" : "resolved");
    if (actions === undefined || actions.length === 0) {
        root.append(createInlineState("-", "neutral"));
        return root;
    }
    for (const action of actions) {
        const item = createElement("article", "dd-action-card");
        item.setAttribute("data-action-key", action.key);
        item.append(createElement("h3", "dd-action-card__name", action.name));
        if (action.actionType) item.append(createElement("span", "dd-action-card__type", action.actionType));
        if (action.attackOrCheck) item.append(renderMechanicalValue(action.attackOrCheck, true));
        const facts = renderFacts([
            ["Damage", action.damage === undefined ? undefined : action.damageType === undefined ? action.damage : `${action.damage} ${action.damageType}`],
            ["Critical", joinOptional(action.criticalRange, action.criticalMultiplier, " / ")],
            ["Range", action.range], ["Reach", action.reach], ["Ammunition", action.ammunition], ["Target", action.target]
        ]);
        if (facts !== null) item.append(facts);
        if (action.notes?.length) {
            const notes = createElement("ul", "dd-action-card__notes");
            for (const note of action.notes) notes.append(createElement("li", undefined, note));
            item.append(notes);
        }
        appendSources(item, action.sourceAttributions);
        root.append(item);
    }
    return root;
}

export function renderDisplayFields(fields: readonly DisplayFieldView[] | undefined, title?: string): HTMLElement | null {
    if (!fields?.length) return null;
    const root = createElement("div", "dd-display-fields");
    if (title) root.append(createElement("h5", "dd-display-fields__title", title));
    const list = createElement("dl", "dd-display-fields__list");
    for (const field of fields) appendDefinitionRow(list, field.label, field.value);
    root.append(list);
    return root;
}

export function renderFacts(entries: readonly (readonly [string, string | undefined] | undefined)[]): HTMLElement | null {
    const list = createElement("dl", "dd-display-fields__list");
    for (const entry of entries) if (entry?.[1]?.trim()) appendDefinitionRow(list, entry[0], entry[1]);
    return list.children.length === 0 ? null : list;
}

function renderUnavailableValue(): HTMLElement {
    const root = createElement("div", "dd-mechanic-value dd-mechanic-value--compact dd-mechanic-value--placeholder");
    root.append(createElement("strong", "dd-mechanic-value__value", "-"));
    return root;
}

function orderedDefenses(mechanics: CharacterMechanicsView | null): readonly CalculatedMechanicalValueView[] {
    const values = mechanics?.defenses?.values ?? [];
    const key = mechanics?.defenses?.primaryKey;
    const primary = key === undefined ? undefined : values.find(value => value.key === key);
    return primary === undefined ? values : [primary, ...values.filter(value => value.key !== key)];
}

function appendDefinitionRow(list: HTMLElement, label: string, value: string): void {
    const row = createElement("div", "dd-definition-row");
    row.append(createElement("dt", "dd-definition-row__term", label), createElement("dd", "dd-definition-row__value", value));
    list.append(row);
}

function appendSources(target: HTMLElement, sources: Parameters<typeof renderSourceAttributions>[0]): void {
    const rendered = renderSourceAttributions(sources, true);
    if (rendered !== null) target.append(rendered);
}

function joinOptional(left: string | undefined, right: string | undefined, separator: string): string | undefined {
    if (left === undefined) return right;
    if (right === undefined) return left;
    return `${left}${separator}${right}`;
}
