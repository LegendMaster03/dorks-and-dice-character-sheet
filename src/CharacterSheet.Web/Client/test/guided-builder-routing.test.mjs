import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createInitialState } from "../.test-dist/app-state.js";
import {
    getGuidedBuilderChoiceSourceKeys,
    getGuidedBuilderOwnedChoiceSourceKeys,
    getGuidedBuilderSectionStates
} from "../.test-dist/ui/sheet-model.js";

const abilityKeys = ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];
const startingClassId = "11111111-1111-1111-1111-111111111111";

function readyBuilder() {
    return {
        status: "ready",
        build: {
            characterId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [
                {
                    id: "20000000-0000-0000-0000-000000000001",
                    category: "background",
                    ruleConceptKey: "background:acolyte",
                    createdAt: "now",
                    updatedAt: "now"
                },
                {
                    id: "20000000-0000-0000-0000-000000000002",
                    category: "species",
                    ruleConceptKey: "species:human",
                    createdAt: "now",
                    updatedAt: "now"
                },
                {
                    id: "20000000-0000-0000-0000-000000000003",
                    category: "subspecies",
                    ruleConceptKey: "subspecies:variant-human",
                    createdAt: "now",
                    updatedAt: "now"
                }
            ],
            progressionEntries: [
                {
                    id: startingClassId,
                    ordinal: 0,
                    kind: "class",
                    ruleConceptKey: "class:fighter",
                    parentAdvancementEntryId: null,
                    createdAt: "now",
                    updatedAt: "now",
                    level: 1
                },
                {
                    id: "30000000-0000-0000-0000-000000000002",
                    ordinal: null,
                    kind: "subclass",
                    ruleConceptKey: "subclass:champion",
                    parentAdvancementEntryId: startingClassId,
                    createdAt: "now",
                    updatedAt: "now",
                    level: null
                }
            ],
            baseAbilityScoreInputs: abilityKeys.map((abilityKey, index) => ({
                id: `40000000-0000-0000-0000-00000000000${index}`,
                abilityKey,
                score: 10 + index,
                createdAt: "now",
                updatedAt: "now"
            }))
        },
        references: {},
        chooser: { kind: "closed" },
        saving: null,
        savingAbility: null,
        savingAdvancementLevel: null,
        featReferences: {},
        featChooser: { kind: "closed" },
        savingFeat: null
    };
}

function choice(choiceKey, kind, sourceConceptKey) {
    return {
        choiceKey,
        groupKey: `group:${choiceKey}`,
        displayName: choiceKey,
        kind,
        state: "choice-required",
        options: [],
        sourceConceptKey
    };
}

test("guided builder routes rule choices to the selected entity that owns them", () => {
    const builder = readyBuilder();

    assert.deepEqual(
        new Set(getGuidedBuilderChoiceSourceKeys(builder, "class")),
        new Set(["class:fighter", "subclass:champion"]));
    assert.deepEqual(
        getGuidedBuilderChoiceSourceKeys(builder, "background"),
        ["background:acolyte"]);
    assert.deepEqual(
        new Set(getGuidedBuilderChoiceSourceKeys(builder, "species")),
        new Set(["species:human", "subspecies:variant-human"]));
    assert.deepEqual(
        new Set(getGuidedBuilderOwnedChoiceSourceKeys(builder)),
        new Set([
            "class:fighter",
            "subclass:champion",
            "background:acolyte",
            "species:human",
            "subspecies:variant-human"
        ]));

    const mechanics = {
        ruleChoices: [
            choice("Fighting Style", "fighting-style", "class:fighter"),
            choice("Background Ability", "ability-score", "background:acolyte"),
            choice("Variant Human Feat", "feat", "species:human"),
            choice("General Ability", "ability-score-set", undefined)
        ]
    };
    const states = getGuidedBuilderSectionStates(builder, mechanics);

    assert.equal(states.find(section => section.id === "class").status, "incomplete");
    assert.equal(states.find(section => section.id === "background").status, "incomplete");
    assert.equal(states.find(section => section.id === "species").status, "incomplete");
    assert.equal(states.find(section => section.id === "abilities").status, "incomplete");
    assert.match(states.find(section => section.id === "background").detail, /1 required Background choice remains/);
    assert.match(states.find(section => section.id === "species").detail, /1 required Species choice remains/);
    assert.match(states.find(section => section.id === "abilities").detail, /1 general Ability Score choice remains/);

    const sourceResolved = {
        ruleChoices: mechanics.ruleChoices.map(value =>
            value.sourceConceptKey === undefined ? value : { ...value, state: "resolved" })
    };
    const sourceResolvedStates = getGuidedBuilderSectionStates(builder, sourceResolved);
    assert.equal(sourceResolvedStates.find(section => section.id === "class").status, "resolved");
    assert.equal(sourceResolvedStates.find(section => section.id === "background").status, "resolved");
    assert.equal(sourceResolvedStates.find(section => section.id === "species").status, "resolved");
    assert.equal(sourceResolvedStates.find(section => section.id === "abilities").status, "incomplete");
});

test("guided builder opens on Class and renderer uses the same source routing model", async () => {
    const state = createInitialState({ kind: "new" });
    assert.equal(state.guidedBuilder.activeSection, "class");

    const source = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.match(source, /sourceConceptKeys:\s*getGuidedBuilderChoiceSourceKeys\(builder, "class"\)/);
    assert.match(source, /sourceConceptKeys:\s*getGuidedBuilderChoiceSourceKeys\(builder, "background"\)/);
    assert.match(source, /sourceConceptKeys:\s*getGuidedBuilderChoiceSourceKeys\(builder, "species"\)/);
    assert.match(source, /excludedSourceConceptKeys:\s*getGuidedBuilderOwnedChoiceSourceKeys\(builder\)/);
    assert.doesNotMatch(source, /choice\.kind,\s*choice\.sourceConceptKey/);
});
