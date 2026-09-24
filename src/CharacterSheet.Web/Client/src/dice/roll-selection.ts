export type D20RollMode = "normal" | "advantage" | "disadvantage" | "emphasis";

export interface D20RollSelection {
    mode: D20RollMode;
    rolls: readonly number[];
    selectedIndex: number;
    selected: number;
    tied: boolean;
}

export const D20_ROLL_MODES: readonly D20RollMode[] = [
    "normal",
    "advantage",
    "disadvantage",
    "emphasis"
];

export function normalizeD20RollMode(value: string | null | undefined): D20RollMode {
    const normalized = value?.trim().toLowerCase() ?? "";
    return D20_ROLL_MODES.includes(normalized as D20RollMode)
        ? normalized as D20RollMode
        : "normal";
}

export function rollModeLabel(mode: D20RollMode): string {
    switch (mode) {
        case "normal": return "Normal";
        case "advantage": return "Advantage";
        case "disadvantage": return "Disadvantage";
        case "emphasis": return "Emphasis";
    }
}

export function selectD20Roll(
    mode: D20RollMode,
    first: number,
    second?: number
): D20RollSelection {
    validateD20(first);
    if (mode === "normal") {
        if (second !== undefined) throw new Error("Normal d20 rolls use exactly one die.");
        return { mode, rolls: [first], selectedIndex: 0, selected: first, tied: false };
    }

    if (second === undefined) throw new Error(`${mode} d20 rolls require two dice.`);
    validateD20(second);

    const firstScore = selectionScore(mode, first);
    const secondScore = selectionScore(mode, second);
    const selectedIndex = secondScore > firstScore ? 1 : 0;
    const rolls = [first, second] as const;
    return {
        mode,
        rolls,
        selectedIndex,
        selected: rolls[selectedIndex],
        tied: firstScore === secondScore
    };
}

export function rollD20(
    mode: D20RollMode,
    generate: () => number = secureD20
): D20RollSelection {
    const first = generate();
    return mode === "normal"
        ? selectD20Roll(mode, first)
        : selectD20Roll(mode, first, generate());
}

export function formatD20Selection(selection: D20RollSelection): string {
    if (selection.mode === "normal") return `d20 ${selection.selected}`;
    const tie = selection.tied ? " · selection tie" : "";
    return `${rollModeLabel(selection.mode)} [${selection.rolls.join(", ")}] → ${selection.selected}${tie}`;
}

export function isD20RollKind(value: string | null | undefined): boolean {
    return value?.trim().toLowerCase() === "d20";
}

function selectionScore(mode: Exclude<D20RollMode, "normal">, value: number): number {
    switch (mode) {
        case "advantage": return value;
        case "disadvantage": return -value;
        case "emphasis": return Math.abs(value - 10);
    }
}

function validateD20(value: number): void {
    if (!Number.isInteger(value) || value < 1 || value > 20) {
        throw new RangeError("d20 results must be whole numbers from 1 through 20.");
    }
}

function secureD20(): number {
    const buffer = new Uint32Array(1);
    crypto.getRandomValues(buffer);
    return (buffer[0] % 20) + 1;
}
