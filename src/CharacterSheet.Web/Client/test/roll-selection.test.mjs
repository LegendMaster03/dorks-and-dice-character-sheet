import assert from "node:assert/strict";
import test from "node:test";

import {
    formatD20Selection,
    isD20RollKind,
    normalizeD20RollMode,
    rollD20,
    selectD20Roll
} from "../.test-dist/dice/roll-selection.js";

test("advantage selects the higher d20", () => {
    assert.equal(selectD20Roll("advantage", 6, 17).selected, 17);
});

test("disadvantage selects the lower d20", () => {
    assert.equal(selectD20Roll("disadvantage", 6, 17).selected, 6);
});

test("emphasis selects the d20 furthest from ten", () => {
    assert.equal(selectD20Roll("emphasis", 4, 13).selected, 4);
    assert.equal(selectD20Roll("emphasis", 19, 2).selected, 19);
});

test("emphasis exposes equal-distance ties rather than inventing a new game rule", () => {
    const result = selectD20Roll("emphasis", 7, 13);
    assert.equal(result.selected, 7);
    assert.equal(result.tied, true);
    assert.match(formatD20Selection(result), /selection tie/);
});

test("normal rolls once while modified modes roll twice", () => {
    const normalDice = [12];
    assert.deepEqual(rollD20("normal", () => normalDice.shift()).rolls, [12]);

    const disadvantageDice = [15, 3];
    const result = rollD20("disadvantage", () => disadvantageDice.shift());
    assert.deepEqual(result.rolls, [15, 3]);
    assert.equal(result.selected, 3);
});

test("d20 mode input is normalized and only explicit d20 roll kinds enable automatic selection", () => {
    assert.equal(normalizeD20RollMode("ADVANTAGE"), "advantage");
    assert.equal(normalizeD20RollMode("unknown"), "normal");
    assert.equal(isD20RollKind("d20"), true);
    assert.equal(isD20RollKind("die"), false);
});
