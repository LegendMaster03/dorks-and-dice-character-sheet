import test from "node:test";
import assert from "node:assert/strict";
import {
    buildCompetencyPresentation,
    findAbilityValue,
    findItemOccurrenceMechanics,
    formatHealthTrack,
    formatMechanicalValue,
    hasMechanicalDetails
} from "../.test-dist/ui/character-mechanics.js";

const value = (key, label, effectiveValue, extra = {}) => ({ key, label, effectiveValue, ...extra });

test("mechanical values prefer backend formatting and otherwise append only supplied units", () => {
    assert.equal(formatMechanicalValue(value("fort", "Fortitude", 9, { formattedValue: "+9" })), "+9");
    assert.equal(formatMechanicalValue(value("speed", "Speed", 30, { unit: "ft." })), "30 ft.");
    assert.equal(formatMechanicalValue(value("bab", "Base Attack Bonus", "+6/+1")), "+6/+1");
});

test("health tracks keep temporary hit points and nonlethal damage as independent tracks", () => {
    const hp = { key: "hp", label: "Hit Points", role: "hit-points", current: 18, maximum: 24 };
    const temp = { key: "temp", label: "Temporary HP", role: "temporary-hit-points", current: 4 };
    const nonlethal = { key: "nonlethal", label: "Nonlethal Damage", role: "nonlethal-damage", current: 7 };
    assert.equal(formatHealthTrack(hp), "18 / 24");
    assert.equal(formatHealthTrack(temp), "4");
    assert.equal(formatHealthTrack(nonlethal), "7");
    assert.notEqual(temp.role, nonlethal.role);
});

test("competency relationships are driven only by keys and support arbitrary component counts", () => {
    const entries = [
        value("parent", "Fieldcraft", "+5"),
        value("a", "Tracking", "+6"),
        value("b", "Foraging", "+4"),
        value("c", "Weather Sense", "+3"),
        value("standalone", "Cartography", "+7")
    ];
    const items = buildCompetencyPresentation({
        entries,
        relationships: [{ parentKey: "parent", componentKeys: ["a", "b", "c"] }]
    });
    assert.equal(items.length, 2);
    assert.equal(items[0].kind, "composite");
    assert.equal(items[0].components.length, 3);
    assert.equal(items[1].kind, "standalone");
    assert.equal(items[1].competency.key, "standalone");
});

test("specialty skills and tool proficiencies remain distinct competencies", () => {
    const craft = value("craft:weaponsmithing", "Craft (Weaponsmithing)", "+11", {
        kind: "skill",
        specialty: "Weaponsmithing",
        ranks: 8
    });
    const tools = value("tool:smiths", "Smith's Tools", "Proficient", {
        kind: "tool",
        training: "Proficient"
    });
    const items = buildCompetencyPresentation({ entries: [craft, tools] });
    assert.deepEqual(items.map(item => item.competency.key), [
        "craft:weaponsmithing",
        "tool:smiths"
    ]);
});

test("competency metadata can represent class-skill, trained-only, armor-check-penalty, and governing ability independently", () => {
    const ranked = value("skill", "Cliff Work", "+8", {
        ranks: 5,
        governingAbility: "Dexterity",
        classSkill: true,
        trainedOnly: true,
        armorCheckPenalty: { applies: true, formattedEffect: "-2 applied" }
    });
    const item = buildCompetencyPresentation({ entries: [ranked] })[0];
    assert.equal(item.kind, "standalone");
    assert.equal(item.competency.ranks, 5);
    assert.equal(item.competency.classSkill, true);
    assert.equal(item.competency.trainedOnly, true);
    assert.equal(item.competency.armorCheckPenalty.formattedEffect, "-2 applied");
});

test("breakdown details are optional and accept arbitrary contribution labels", () => {
    const save = value("fortitude", "Fortitude", 9, {
        formattedValue: "+9",
        breakdown: [
            value("progression", "Veteran progression", 5, { formattedValue: "+5" }),
            value("constitution", "Constitution", 3, { formattedValue: "+3" }),
            value("cloak", "Cloak of Resistance", 1, { formattedValue: "+1" })
        ]
    });
    assert.equal(hasMechanicalDetails(save), true);
    assert.deepEqual(save.breakdown.map(entry => entry.label), [
        "Veteran progression",
        "Constitution",
        "Cloak of Resistance"
    ]);
});

test("inventory mechanics are keyed by Character-owned occurrence identity", () => {
    const mechanics = {
        itemOccurrences: [
            { occurrenceId: "one", facts: [{ key: "weight", label: "Weight", value: "2 lb" }] },
            { occurrenceId: "two", facts: [{ key: "weight", label: "Weight", value: "3 lb" }] }
        ]
    };
    assert.equal(findItemOccurrenceMechanics(mechanics, "one").facts[0].value, "2 lb");
    assert.equal(findItemOccurrenceMechanics(mechanics, "two").facts[0].value, "3 lb");
});


test("effective Ability values join standard Ability cards only by the supplied stable key", () => {
    const values = [
        value("strength", "Strength", 18, { formattedValue: "18" }),
        value("honor", "Honor", 14, { formattedValue: "14" })
    ];
    assert.equal(findAbilityValue(values, "strength").formattedValue, "18");
    assert.equal(findAbilityValue(values, "dexterity"), undefined);
    assert.equal(findAbilityValue(values, "honor").label, "Honor");
});
