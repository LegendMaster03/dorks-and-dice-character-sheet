import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../src/ui/guided-equipment.ts", import.meta.url), "utf8");
const css = await readFile(new URL("../src/styles/guided-equipment.css", import.meta.url), "utf8");

test("guided starting equipment models fixed grants and bundle choices without inventing inventory state", () => {
    assert.match(source, /interface GuidedStartingEquipmentView/);
    assert.match(source, /grants:\s*readonly GuidedEquipmentItemView\[\]/);
    assert.match(source, /choices:\s*readonly GuidedEquipmentChoiceView\[\]/);
    assert.match(source, /interface GuidedEquipmentOptionView/);
    assert.match(source, /items:\s*readonly GuidedEquipmentItemView\[\]/);
    assert.match(source, /fields\?:\s*readonly DisplayFieldView\[\]/);
    assert.doesNotMatch(source, /isCarried|isEquipped|isAttuned|containerOccurrenceId/);
});

test("guided starting equipment renders one explicit selection per choice and delegates persistence", () => {
    assert.match(source, /input\.type = "radio"/);
    assert.match(source, /input\.name = `guided-equipment:\$\{choice\.choiceKey\}`/);
    assert.match(source, /input\.checked = option\.value === choice\.selectedValue/);
    assert.match(source, /handlers\.selectChoice\(choice\.choiceKey, option\.value\)/);
    assert.match(source, /choice\.required && choice\.selectedValue === undefined/);
    assert.match(source, /Choose one option/);
});

test("guided starting equipment keeps quantities, bundle fields, and source attribution visible", () => {
    assert.match(source, /item\.quantity === 1/);
    assert.match(source, /renderEquipmentOptionContents/);
    assert.match(source, /field\.label/);
    assert.match(source, /field\.value/);
    assert.match(source, /renderSourceAttributions\(equipment\.sourceAttributions, true\)/);
    assert.match(source, /renderSourceAttributions\(choice\.sourceAttributions, true\)/);
});

test("guided Equipment styling uses selectable bundle cards and a visibly disabled builder stage", () => {
    assert.match(css, /\.dd-guided-equipment__options\s*\{[\s\S]*grid-template-columns:\s*repeat\(auto-fit,\s*minmax\(14rem,\s*1fr\)\)/);
    assert.match(css, /\.dd-guided-equipment__option:has\(input:checked\)/);
    assert.match(css, /\.dd-guided-builder__nav-button:disabled/);
    assert.match(css, /cursor:\s*not-allowed/);
    assert.doesNotMatch(css, /#[0-9a-f]{3,8}\b|rgba?\s*\(|hsla?\s*\(/i);
});
