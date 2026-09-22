import {
    formatHealthTrack,
    type CharacterMechanicsView,
    type CharacterRecoveryProcedureView,
    type HealthTrackView
} from "../../ui/character-mechanics.js";
import { createElement, createSectionCard } from "../../ui/components.js";
import { renderSourceAttributionDisclosure } from "../../ui/source-attribution.js";
import { appendSources, normalizeMechanicalLabel } from "../../core/mechanics/mechanic-value.js";

export interface DeathSaveState {
    successes: number;
    failures: number;
}

export interface HealthControlOptions {
    currentHitPoints?: number | null;
    deathSaves?: DeathSaveState;
    readOnly?: boolean;
    saving?: boolean;
    onSetCurrentHitPoints?: (currentHitPoints: number | null) => void;
    onSetDeathSaves?: (successes: number, failures: number) => void;
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

export function renderHealthQuickCard(
    mechanics: CharacterMechanicsView | null,
    control: HealthControlOptions = {}
): HTMLElement {
    const card = createElement("article", "dd-stat dd-stat--health dd-health-quick");
    card.setAttribute("data-health-quick-card", "true");

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
    const hitDice = tracks.find(track =>
        track.role === "hit-dice"
        || normalizeMechanicalLabel(track.key) === "resourcehitdice"
        || normalizeMechanicalLabel(track.label) === "hitdice"
        || normalizeMechanicalLabel(track.label) === "hitdie");

    const currentValue = control.currentHitPoints === undefined
        ? hitPoints?.current
        : control.currentHitPoints;

    const header = createElement("div", "dd-health-quick__header");
    header.append(createElement("h3", "dd-stat__label", "Hit Points"));

    const promoted = new Set(
        [hitPoints, temporaryHitPoints, nonlethal, hitDice]
            .filter((track): track is NonNullable<typeof track> => track !== undefined)
            .map(track => track.key));
    const extraTracks = tracks.filter(track => !promoted.has(track.key));
    const sources = collectHealthTrackSources([
        hitPoints,
        temporaryHitPoints,
        nonlethal,
        hitDice,
        ...extraTracks
    ]);

    const editableHitPoints = control.readOnly !== true
        && control.onSetCurrentHitPoints !== undefined;
    const editableDeathSaves = control.readOnly !== true
        && control.deathSaves !== undefined
        && control.onSetDeathSaves !== undefined;
    if (editableHitPoints || editableDeathSaves || extraTracks.length > 0 || sources.length > 0) {
        const details = createElement("details", "dd-health-quick__details");
        details.append(createElement("summary", "dd-health-quick__details-toggle", "Details"));
        const popover = createElement("div", "dd-health-quick__details-popover");
        if (editableHitPoints) {
            popover.append(renderDirectHitPointSetter(
                toFiniteInteger(currentValue),
                hitPoints?.maximum,
                control.saving === true,
                control.onSetCurrentHitPoints!));
        }
        if (editableDeathSaves) {
            popover.append(renderDeathSaveEditor(
                control.deathSaves!,
                control.saving === true,
                control.onSetDeathSaves!));
        }
        if (extraTracks.length > 0) {
            const extras = createElement("div", "dd-health-card__extras");
            extras.append(...extraTracks.map(renderHealthTrack));
            popover.append(extras);
        }
        const sourceDisclosure = renderSourceAttributionDisclosure(sources);
        if (sourceDisclosure !== null) popover.append(sourceDisclosure);
        details.append(popover);
        header.append(details);
    }

    const values = createElement("div", "dd-health-quick__values");
    values.append(
        renderHealthQuickField("Current", currentValue, "current", hitPoints),
        renderHealthQuickField("Max", hitPoints?.maximum, "maximum", hitPoints),
        renderHealthQuickField(
            "Temporary HP",
            temporaryHitPoints?.formattedValue ?? temporaryHitPoints?.current,
            "temporary",
            temporaryHitPoints),
        renderHealthQuickField(
            "Nonlethal Damage",
            nonlethal?.formattedValue ?? nonlethal?.current,
            "nonlethal",
            nonlethal));

    const recovery = createElement("div", "dd-health-quick__recovery");
    recovery.append(
        renderHealthTextField(
            "Hit Dice",
            hitDice === undefined ? "-" : formatHealthTrack(hitDice),
            "hit-dice",
            hitDice),
        renderDeathSaveSummary(control.deathSaves));

    const quickAdjustment = editableHitPoints
        ? renderQuickHitPointAdjustment(
            toFiniteInteger(currentValue),
            hitPoints?.maximum,
            control.saving === true,
            control.onSetCurrentHitPoints!)
        : null;

    card.append(header, values, recovery);
    if (quickAdjustment !== null) card.append(quickAdjustment);
    return card;
}

function renderHealthQuickField(
    label: string,
    value: unknown,
    role: string,
    track?: HealthTrackView
): HTMLElement {
    const field = createElement("div", "dd-health-quick__field");
    field.setAttribute("data-health-quick-field", role);
    if (track !== undefined) field.setAttribute("data-health-track-key", track.key);
    field.append(
        createElement("span", "dd-health-quick__label", label),
        createElement("strong", "dd-health-quick__value", formatOptionalHealthNumber(value)));
    return field;
}

function renderHealthTextField(
    label: string,
    value: string,
    role: string,
    track?: HealthTrackView
): HTMLElement {
    const field = createElement("div", "dd-health-quick__field");
    field.setAttribute("data-health-quick-field", role);
    if (track !== undefined) field.setAttribute("data-health-track-key", track.key);
    field.append(
        createElement("span", "dd-health-quick__label", label),
        createElement("strong", "dd-health-quick__value", value));
    return field;
}

function renderDeathSaveSummary(state: DeathSaveState | undefined): HTMLElement {
    const successes = state?.successes;
    const failures = state?.failures;
    const value = successes === undefined || failures === undefined
        ? "-"
        : `S ${successes} • F ${failures}`;
    return renderHealthTextField("Death Saves", value, "death-saves");
}

function renderDeathSaveEditor(
    state: DeathSaveState,
    saving: boolean,
    onSetDeathSaves: (successes: number, failures: number) => void
): HTMLElement {
    const editor = createElement("div", "dd-death-saves-editor");
    editor.setAttribute("data-death-saves-editor", "true");
    editor.append(createElement("span", "dd-health-editor__label", "Death Saves"));

    const controls = createElement("div", "dd-death-saves-editor__controls");
    controls.append(
        renderDeathSaveCounter("Successes", state.successes, saving, value =>
            onSetDeathSaves(value, state.failures)),
        renderDeathSaveCounter("Failures", state.failures, saving, value =>
            onSetDeathSaves(state.successes, value)));

    const reset = createElement(
        "button",
        "dd-button dd-button--ghost dd-death-saves-editor__reset",
        "Reset") as HTMLButtonElement;
    reset.type = "button";
    reset.disabled = saving || (state.successes === 0 && state.failures === 0);
    reset.setAttribute("data-death-save-action", "reset");
    reset.onclick = () => onSetDeathSaves(0, 0);

    editor.append(controls, reset);
    return editor;
}

function renderDeathSaveCounter(
    label: string,
    value: number,
    saving: boolean,
    onChange: (value: number) => void
): HTMLElement {
    const counter = createElement("div", "dd-death-saves-editor__counter");
    counter.append(createElement("span", "dd-death-saves-editor__label", label));

    const decrement = createElement(
        "button",
        "dd-button dd-button--ghost dd-death-saves-editor__button",
        "−") as HTMLButtonElement;
    decrement.type = "button";
    decrement.disabled = saving || value <= 0;
    decrement.setAttribute(
        "data-death-save-action",
        label === "Successes" ? "success-decrement" : "failure-decrement");
    decrement.onclick = () => onChange(Math.max(0, value - 1));

    const count = createElement(
        "strong",
        "dd-death-saves-editor__value",
        String(value));

    const increment = createElement(
        "button",
        "dd-button dd-button--ghost dd-death-saves-editor__button",
        "+") as HTMLButtonElement;
    increment.type = "button";
    increment.disabled = saving || value >= 3;
    increment.setAttribute(
        "data-death-save-action",
        label === "Successes" ? "success-increment" : "failure-increment");
    increment.onclick = () => onChange(Math.min(3, value + 1));

    counter.append(decrement, count, increment);
    return counter;
}

export function renderHealthMechanicsCard(
    mechanics: CharacterMechanicsView | null,
    control: HealthControlOptions = {}
): HTMLElement {
    const card = createSectionCard("Hit Points", "dd-support-card dd-health-card");
    const tracks = mechanics?.healthTracks ?? [];

    const hitPoints = tracks.find(track =>
        track.role === "hit-points"
        || normalizeMechanicalLabel(track.label) === "hitpoints");
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
        renderHealthSummaryField(
            "Current",
            currentValue,
            "dd-health-card__field--current",
            hitPoints),
        renderHealthSummaryField(
            "Maximum",
            hitPoints?.maximum,
            "dd-health-card__field--maximum",
            hitPoints));
    card.append(primary);

    if (control.readOnly !== true
        && control.onSetCurrentHitPoints !== undefined) {
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
            .filter((track): track is NonNullable<typeof track> =>
                track !== undefined)
            .map(track => track.key));
    const extraTracks = tracks.filter(track => !promoted.has(track.key));
    if (extraTracks.length > 0) {
        const extras = createElement("div", "dd-health-card__extras");
        extras.append(...extraTracks.map(renderHealthTrack));
        card.append(extras);
    }

    const sources = renderSourceAttributionDisclosure(
        collectHealthTrackSources([
            hitPoints,
            temporaryHitPoints,
            nonlethal,
            ...extraTracks
        ]));
    if (sources !== null) card.append(sources);
    return card;
}

export function renderHealthScaffold(
    mechanics: CharacterMechanicsView | null
): HTMLElement[] {
    const tracks = mechanics?.healthTracks ?? [];
    const usedKeys = new Set<string>();

    const hitPoints = tracks.find(track =>
        track.role === "hit-points"
        || normalizeMechanicalLabel(track.label) === "hitpoints");
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
            ? renderScaffoldHealthTrack(
                "nonlethal-damage",
                "Nonlethal Damage")
            : renderHealthTrack(nonlethal)
    ];

    for (const track of tracks) {
        if (!usedKeys.has(track.key)) cells.push(renderHealthTrack(track));
    }
    return cells;
}

export function renderRecoveryControls(
    procedures: readonly CharacterRecoveryProcedureView[] | undefined,
    readOnly: boolean,
    saving: boolean,
    onRecovery: ((procedureKey: string) => void) | undefined
): HTMLElement | null {
    if (procedures === undefined || procedures.length === 0) return null;

    const controls = createElement("div", "dd-rest-controls");
    controls.setAttribute("role", "group");
    controls.setAttribute("aria-label", "Recovery actions");

    for (const procedure of procedures) {
        const applicable = procedure.applicabilityState === "applicable";
        const executable = onRecovery !== undefined;
        const disabled = readOnly || saving || !applicable || !executable;
        const button = createElement(
            "button",
            "dd-button dd-button--ghost dd-rest-button",
            procedure.displayName) as HTMLButtonElement;
        button.type = "button";
        button.disabled = disabled;
        button.setAttribute("data-recovery-procedure", procedure.procedureKey);
        if (procedure.presentationRole !== undefined) {
            button.setAttribute("data-recovery-role", procedure.presentationRole);
            if (procedure.presentationRole === "short-rest") {
                button.setAttribute("data-rest-action", "short");
            } else if (procedure.presentationRole === "long-rest") {
                button.setAttribute("data-rest-action", "long");
            }
        }
        if (disabled) {
            button.title = readOnly
                ? "Recovery actions are unavailable while this Character is read-only."
                : saving
                    ? "Wait for the current Character state change to finish."
                    : !applicable
                        ? "This recovery procedure is not currently applicable to the Character."
                        : "Recovery execution is not available yet.";
        } else {
            button.onclick = () => onRecovery(procedure.procedureKey);
        }
        controls.append(button);
    }

    return controls;
}

function renderQuickHitPointAdjustment(
    current: number | null,
    maximum: unknown,
    saving: boolean,
    onSetCurrentHitPoints: (currentHitPoints: number | null) => void
): HTMLElement {
    const controls = createElement("div", "dd-health-quick__adjust");
    controls.setAttribute("role", "group");
    controls.setAttribute("aria-label", "Quick hit point adjustment");

    const damage = createElement(
        "button",
        "dd-health-quick__adjust-button dd-health-quick__adjust-button--damage",
        "Damage") as HTMLButtonElement;
    damage.type = "button";
    damage.setAttribute("data-health-action", "damage");

    const amount = createElement(
        "input",
        "dd-health-quick__adjust-input") as HTMLInputElement;
    amount.type = "number";
    amount.min = "0";
    amount.step = "1";
    amount.inputMode = "numeric";
    amount.placeholder = "0";
    amount.setAttribute("aria-label", "Hit point adjustment amount");

    const heal = createElement(
        "button",
        "dd-health-quick__adjust-button dd-health-quick__adjust-button--heal",
        "Heal") as HTMLButtonElement;
    heal.type = "button";
    heal.setAttribute("data-health-action", "heal");

    const disabled = saving || current === null;
    damage.disabled = disabled;
    heal.disabled = disabled;
    amount.disabled = saving;

    const apply = (direction: -1 | 1): void => {
        const parsed = parseOptionalInteger(amount.value);
        if (!parsed.valid || parsed.value === null || parsed.value < 0) return;
        const next = adjustCurrentHitPoints(current, maximum, parsed.value, direction);
        if (next !== null) onSetCurrentHitPoints(next);
    };
    damage.onclick = () => apply(-1);
    heal.onclick = () => apply(1);

    controls.append(damage, amount, heal);
    return controls;
}

function renderDirectHitPointSetter(
    current: number | null,
    maximum: unknown,
    saving: boolean,
    onSetCurrentHitPoints: (currentHitPoints: number | null) => void
): HTMLElement {
    const direct = createElement("div", "dd-health-quick__direct");
    direct.setAttribute("data-health-editor", "true");
    direct.setAttribute("data-health-direct-setter", "true");

    const currentField = createElement("label", "dd-health-editor__field");
    currentField.append(createElement("span", "dd-health-editor__label", "Set Current HP"));
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
        createElement("strong", "dd-health-editor__readonly", formatOptionalHealthNumber(maximum)));

    const set = createElement(
        "button",
        "dd-button dd-button--secondary dd-health-editor__set",
        "Set") as HTMLButtonElement;
    set.type = "button";
    set.disabled = saving;
    set.setAttribute("data-health-action", "set-current");
    set.onclick = () => {
        const value = parseOptionalInteger(currentInput.value);
        if (value.valid) onSetCurrentHitPoints(value.value);
    };

    direct.append(currentField, maximumField, set);
    return direct;
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

    const set = createElement(
        "button",
        "dd-button dd-button--secondary dd-health-editor__set",
        "Set") as HTMLButtonElement;
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

    const subtract = createElement(
        "button",
        "dd-button dd-button--ghost dd-health-editor__adjust-button",
        "−") as HTMLButtonElement;
    subtract.type = "button";
    subtract.disabled = saving || current === null;
    subtract.setAttribute("aria-label", "Subtract hit points");
    subtract.setAttribute("data-health-action", "subtract");

    const add = createElement(
        "button",
        "dd-button dd-button--ghost dd-health-editor__adjust-button",
        "+") as HTMLButtonElement;
    add.type = "button";
    add.disabled = saving || current === null;
    add.setAttribute("aria-label", "Add hit points");
    add.setAttribute("data-health-action", "add");

    const apply = (direction: -1 | 1): void => {
        const amount = parseOptionalInteger(amountInput.value);
        if (!amount.valid || amount.value === null || amount.value < 0) return;
        const next = adjustCurrentHitPoints(
            current,
            maximum,
            amount.value,
            direction);
        if (next !== null) onSetCurrentHitPoints(next);
    };
    subtract.onclick = () => apply(-1);
    add.onclick = () => apply(1);

    adjust.append(amountField, subtract, add);
    body.append(direct, adjust);
    details.append(body);
    return details;
}

function parseOptionalInteger(
    value: string
): { valid: boolean; value: number | null } {
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
            value === undefined
                || value === null
                || String(value).trim().length === 0
                ? "-"
                : String(value)));
    return field;
}

function collectHealthTrackSources(
    tracks: readonly (
        NonNullable<CharacterMechanicsView["healthTracks"]>[number]
        | undefined
    )[]
): NonNullable<CharacterMechanicsView["sourceAttributions"]> {
    const byKey = new Map<
        string,
        NonNullable<CharacterMechanicsView["sourceAttributions"]>[number]
    >();
    for (const track of tracks) {
        for (const source of track?.sourceAttributions ?? []) {
            if (!byKey.has(source.key)) byKey.set(source.key, source);
        }
    }
    return [...byKey.values()];
}

function renderHealthTrack(
    track: NonNullable<CharacterMechanicsView["healthTracks"]>[number]
): HTMLElement {
    const cell = createElement(
        "div",
        "dd-mechanic-value dd-mechanic-value--compact dd-health-track");
    cell.setAttribute("data-health-track-key", track.key);
    cell.setAttribute("data-health-track-role", track.role);
    const head = createElement("div", "dd-mechanic-value__summary");
    head.append(
        createElement("span", "dd-mechanic-value__label", track.label),
        createElement(
            "strong",
            "dd-mechanic-value__value",
            formatHealthTrack(track)));
    cell.append(head);
    if (track.detail) {
        cell.append(createElement(
            "span",
            "dd-mechanic-value__meta",
            track.detail));
    }
    appendSources(cell, track.sourceAttributions);
    return cell;
}

function renderScaffoldHealthTrack(
    role: "hit-points" | "nonlethal-damage",
    label: string
): HTMLElement {
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
