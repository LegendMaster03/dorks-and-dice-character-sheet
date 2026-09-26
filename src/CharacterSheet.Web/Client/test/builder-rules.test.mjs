import assert from "node:assert/strict";
import test from "node:test";
import {
    filterSubclassesForClass,
    filterSubspeciesForSpecies,
    getStartingClassEntry,
    getStoredChoiceConceptKey,
    getStoredSubclassEntry,
    loadingRuleReference,
    resolveStoredChoice
} from "../.test-dist/builder-rules.js";

const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: "/characters/example",
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};
const classEntryId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const build = {
    characterId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    builderStatus: "BuildInProgress",
    readOnly: false,
    foundationalSelections: [{
        id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
        category: "species",
        ruleConceptKey: "species:elf",
        createdAt: "now",
        updatedAt: "now"
    }, {
        id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
        category: "subspecies",
        ruleConceptKey: "subspecies:high-elf",
        createdAt: "now",
        updatedAt: "now"
    }],
    progressionEntries: [{
        id: classEntryId,
        ordinal: 0,
        kind: "class",
        ruleConceptKey: "class:wizard",
        parentAdvancementEntryId: null,
        createdAt: "now",
        updatedAt: "now"
    }, {
        id: "dddddddd-dddd-dddd-dddd-dddddddddddd",
        ordinal: null,
        kind: "subclass",
        ruleConceptKey: "subclass.wizard.evocation",
        parentAdvancementEntryId: classEntryId,
        createdAt: "now",
        updatedAt: "now"
    }]
};

test("stored builder references are identified from canonical Character Sheet-owned categories and progression", () => {
    assert.equal(getStoredChoiceConceptKey(build, "species"), "species:elf");
    assert.equal(getStoredChoiceConceptKey(build, "subspecies"), "subspecies:high-elf");
    assert.equal(getStoredChoiceConceptKey(build, "startingClass"), "class:wizard");
    assert.equal(getStoredChoiceConceptKey(build, "subclass"), "subclass.wizard.evocation");
    assert.equal(getStartingClassEntry(build)?.id, classEntryId);
    assert.equal(getStoredSubclassEntry(build)?.parentAdvancementEntryId, classEntryId);
    assert.deepEqual(loadingRuleReference(build, "species"), {
        status: "loading",
        conceptKey: "species:elf"
    });
    assert.deepEqual(loadingRuleReference(build, "subspecies"), {
        status: "loading",
        conceptKey: "subspecies:high-elf"
    });
});

test("subspecies chooser retains only explicit Subspecies related to the selected Species", () => {
    const relationship = relatedConceptKey => [{
        kind: "parent-species",
        relatedRuleConceptId: "11111111-1111-1111-1111-111111111111",
        relatedConceptKey,
        relatedEntityType: "species",
        relatedDisplayName: relatedConceptKey
    }];
    const rule = (conceptKey, entityType, relationships) => ({
        ruleConceptId: conceptKey,
        conceptKey,
        entityType,
        displayName: conceptKey,
        decisionKind: "select-source",
        hasCampaignOverride: false,
        sourceEntityId: conceptKey,
        sourceEntityRevisionId: conceptKey,
        sourceRevisionNumber: 1,
        sourceEntityName: conceptKey,
        sourceCode: "TST",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e",
        relationships
    });

    const elfSubspecies = rule(
        "subspecies:high-elf",
        "subspecies",
        relationship("species:elf"));
    const dwarfSubspecies = rule(
        "subspecies:hill-dwarf",
        "subspecies",
        relationship("species:dwarf"));
    const unlinkedSubspecies = rule("subspecies:unknown", "subspecies", []);
    const species = rule(
        "species:human",
        "species",
        relationship("species:elf"));

    assert.deepEqual(
        filterSubspeciesForSpecies(
            [elfSubspecies, dwarfSubspecies, unlinkedSubspecies, species],
            "species:elf"),
        [elfSubspecies]);
    assert.deepEqual(filterSubspeciesForSpecies([elfSubspecies], "   "), []);
});

test("subclass chooser retains only explicit Subclasses related to the selected Class", () => {
    const relationship = relatedConceptKey => [{
        kind: "parent-class",
        relatedRuleConceptId: "11111111-1111-1111-1111-111111111111",
        relatedConceptKey,
        relatedEntityType: "class",
        relatedDisplayName: relatedConceptKey
    }];
    const rule = (conceptKey, entityType, relationships) => ({
        ruleConceptId: conceptKey,
        conceptKey,
        entityType,
        displayName: conceptKey,
        decisionKind: "select-source",
        hasCampaignOverride: false,
        sourceEntityId: conceptKey,
        sourceEntityRevisionId: conceptKey,
        sourceRevisionNumber: 1,
        sourceEntityName: conceptKey,
        sourceCode: "TST",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e",
        relationships
    });

    const wizardSubclass = rule(
        "subclass.wizard.evocation",
        "subclass",
        relationship("class:wizard"));
    const fighterSubclass = rule(
        "subclass.fighter.champion",
        "subclass",
        relationship("class:fighter"));
    const unlinkedSubclass = rule("subclass.unknown", "subclass", []);
    const prestigeClass = rule(
        "prestigeclass.arcane-archer",
        "prestigeClass",
        relationship("class:wizard"));

    assert.deepEqual(
        filterSubclassesForClass(
            [wizardSubclass, fighterSubclass, unlinkedSubclass, prestigeClass],
            "class:wizard"),
        [wizardSubclass]);
    assert.deepEqual(filterSubclassesForClass([wizardSubclass], "   "), []);
});

test("canonical persisted Species reference resolves through Rules Core rather than being falsely unavailable", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "11111111-1111-1111-1111-111111111111",
        conceptKey: "species:elf",
        entityType: "species",
        displayName: "Elf",
        sourceEntityName: "Elf",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    });

    const resolved = await resolveStoredChoice(environment, build, "species", fetcher);

    assert.equal(resolved.status, "resolved");
    assert.equal(resolved.conceptKey, "species:elf");
    assert.equal(resolved.rule.displayName, "Elf");
});

test("canonical persisted Subspecies reference resolves only as an explicit Subspecies concept", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "33333333-3333-3333-3333-333333333333",
        conceptKey: "subspecies:high-elf",
        entityType: "subspecies",
        displayName: "High Elf",
        sourceEntityName: "High Elf",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    });

    const resolved = await resolveStoredChoice(environment, build, "subspecies", fetcher);
    assert.equal(resolved.status, "resolved");
    assert.equal(resolved.rule.entityType, "subspecies");
});

test("subclass reference resolves only as an explicit subclass concept", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "22222222-2222-2222-2222-222222222222",
        conceptKey: "subclass.wizard.evocation",
        entityType: "subclass",
        displayName: "School of Evocation",
        sourceEntityName: "School of Evocation",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    });

    const resolved = await resolveStoredChoice(environment, build, "subclass", fetcher);
    assert.equal(resolved.status, "resolved");
    assert.equal(resolved.rule.entityType, "subclass");
});

test("unavailable stored reference remains retained and is not substituted", async () => {
    const fetcher = async () => new Response(null, { status: 404 });

    const result = await resolveStoredChoice(environment, build, "startingClass", fetcher);

    assert.deepEqual(result, { status: "unavailable", conceptKey: "class:wizard" });
    assert.equal(build.progressionEntries[0].ruleConceptKey, "class:wizard");
});

test("a concept that resolves as the wrong entity type is unavailable rather than accepted by display name", async () => {
    const fetcher = async () => Response.json({
        ruleConceptId: "11111111-1111-1111-1111-111111111111",
        conceptKey: "class:wizard",
        entityType: "prestigeClass",
        displayName: "Wizard",
        sourceEntityName: "Wizard",
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "3.5e",
        editionDisplayName: "3.5e"
    });

    assert.deepEqual(
        await resolveStoredChoice(environment, build, "startingClass", fetcher),
        { status: "unavailable", conceptKey: "class:wizard" });
});

test("Rules Core technical failures produce an explicit Species reference error without clearing the key", async () => {
    const fetcher = async () => Response.json({ detail: "offline" }, { status: 503 });

    const result = await resolveStoredChoice(environment, build, "species", fetcher);

    assert.equal(result.status, "error");
    assert.equal(result.conceptKey, "species:elf");
    assert.match(result.message, /offline/);
});
