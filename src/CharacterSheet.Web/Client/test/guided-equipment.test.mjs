import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { attachRulesCoreContext } from "../.test-dist/rules-core-context.js";
import { projectGuidedStartingEquipment } from "../.test-dist/ui/guided-equipment.js";

const source = await readFile(new URL("../src/ui/guided-equipment.ts", import.meta.url), "utf8");
const css = await readFile(new URL("../src/styles/guided-equipment.css", import.meta.url), "utf8");

test("guided starting equipment models fixed grants and bundle choices without inventing inventory state", () => {
    assert.match(source, /interface GuidedStartingEquipmentView/);
    assert.match(source, /grants:\s*readonly GuidedEquipmentItemView\[\]/);
    assert.match(source, /choices:\s*readonly GuidedEquipmentChoiceView\[\]/);
    assert.match(source, /conflicts:\s*readonly GuidedEquipmentConflictView\[\]/);
    assert.match(source, /interface GuidedEquipmentOptionView/);
    assert.match(source, /items:\s*readonly GuidedEquipmentItemView\[\]/);
    assert.match(source, /fields\?:\s*readonly DisplayFieldView\[\]/);
    assert.doesNotMatch(source, /isCarried|isEquipped|isAttuned|containerOccurrenceId/);
});

test("Rules Core starting-equipment grants, choices, and conflicts project into the guided view", () => {
    const sourceAttribution = { key: "srd", label: "SRD" };
    const mechanics = {
        ruleChoices: [{
            choiceKey: "choice.class-fighter.starting-equipment.0.0",
            groupKey: "group.class-fighter.starting-equipment.0",
            displayName: "Starting Equipment 1",
            kind: "starting-equipment",
            state: "choice-required",
            options: [
                { value: "longsword", displayName: "Longsword", conceptKey: "item:longsword" },
                { value: "pack", displayName: "Explorer's Pack" }
            ],
            sourceConceptKey: "class:fighter",
            sourceAttributions: [sourceAttribution]
        }],
        projectionConflicts: [{
            conflictKey: "conflict.class:fighter.starting-equipment.item.0",
            kind: "source-unavailable",
            message: "Fighter: one starting item is unavailable.",
            relatedMechanicKeys: [],
            relatedConceptKeys: ["class:fighter"]
        }]
    };
    attachRulesCoreContext(mechanics, {
        grants: [
            {
                grantKey: "grant.one",
                kind: "starting-equipment-item",
                targetKey: "item:dagger",
                displayName: "Dagger",
                sourceConceptKey: "class:fighter"
            },
            {
                grantKey: "grant.two",
                kind: "starting-equipment-item",
                targetKey: "item:dagger",
                displayName: "Dagger",
                sourceConceptKey: "class:fighter"
            },
            {
                grantKey: "grant.gp.one",
                kind: "starting-equipment-currency",
                targetKey: "gp",
                displayName: "GP",
                sourceConceptKey: "background:soldier"
            },
            {
                grantKey: "grant.gp.two",
                kind: "starting-equipment-currency",
                targetKey: "gp",
                displayName: "GP",
                sourceConceptKey: "background:soldier"
            },
            {
                grantKey: "unrelated",
                kind: "weapon-proficiency",
                targetKey: "weapon:simple",
                displayName: "Simple Weapons",
                sourceConceptKey: "class:fighter"
            }
        ]
    });

    const projected = projectGuidedStartingEquipment(mechanics);
    assert.notEqual(projected, null);
    assert.deepEqual(projected.grants, [
        {
            conceptKey: "item:dagger",
            displayName: "Dagger",
            quantity: 2,
            detail: undefined
        },
        {
            conceptKey: undefined,
            displayName: "GP",
            quantity: 2,
            detail: "Starting currency"
        }
    ]);
    assert.equal(projected.choices.length, 1);
    assert.deepEqual(projected.choices[0].options[0].items, [{
        conceptKey: "item:longsword",
        displayName: "Longsword",
        quantity: 1
    }]);
    assert.deepEqual(projected.choices[0].options[1].items, []);
    assert.deepEqual(projected.conflicts, [{
        conflictKey: "conflict.class:fighter.starting-equipment.item.0",
        message: "Fighter: one starting item is unavailable."
    }]);
    assert.deepEqual(projected.sourceAttributions, [sourceAttribution]);
});

test("starting-equipment projection stays unavailable until raw Rules Core projection context is attached", () => {
    const mechanics = {
        ruleChoices: [{
            choiceKey: "choice.fixture",
            groupKey: "group.fixture",
            displayName: "Starting Equipment",
            kind: "starting-equipment",
            state: "choice-required",
            options: [],
            sourceConceptKey: "class:fighter"
        }]
    };

    assert.equal(projectGuidedStartingEquipment(null), null);
    assert.equal(projectGuidedStartingEquipment(mechanics), null);

    attachRulesCoreContext(mechanics, { grants: [] });
    assert.notEqual(projectGuidedStartingEquipment(mechanics), null);
});

test("guided starting equipment renders one explicit selection per choice and delegates persistence", () => {
    assert.match(source, /input\.type = "radio"/);
    assert.match(source, /input\.name = `guided-equipment:\$\{choice\.choiceKey\}`/);
    assert.match(source, /input\.checked = option\.value === choice\.selectedValue/);
    assert.match(source, /handlers\.selectChoice\(choice\.choiceKey, option\.value\)/);
    assert.match(source, /choice\.required && choice\.selectedValue === undefined/);
    assert.match(source, /Choose one option/);
});

test("guided starting equipment keeps quantities, option contents, conflicts, and source attribution visible", () => {
    assert.match(source, /item\.quantity === 1/);
    assert.match(source, /renderEquipmentOptionContents/);
    assert.match(source, /option\.conceptKey === undefined/);
    assert.match(source, /Starting Equipment Conflicts/);
    assert.match(source, /data-rule-conflict-key/);
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
