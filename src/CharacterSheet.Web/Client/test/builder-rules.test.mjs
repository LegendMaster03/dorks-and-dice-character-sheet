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

const fingerprint = "a".repeat(64);
const nativeClassBuild = {
    ...build,
    progressionEntries: build.progressionEntries.map(entry =>
        entry.ordinal === 0
            ? { ...entry, ruleConceptKey: "class.warlock" }
            : entry)
};
function nativeFetch(available = true, category = "class") {
    const requests = [];
    const fetcher = async (url) => {
        requests.push(String(url));
        if (url.includes("/api/native-rules/ruleset"))
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                schemaVersion: 1, fingerprint,
                capabilities: { nativeOptionSearch: true }
            });
        if (url.includes("/api/native-rules/options")) {
            return Response.json({
                contractVersion: 1, rulesetId: "first-party-001",
                fingerprint, category, total: available ? 1 : 0,
                options: available ? [{
                    id: "class.warlock", name: "Warlock", kind: category
                }] : []
            });
        }
        throw new Error("Retired API was called: " + url);
    };
    return { fetcher, requests };
}

test("native class reference resolves by exact ID without legacy concept detail requests", async () => {
    const { fetcher, requests } = nativeFetch();
    const resolved = await resolveStoredChoice(
        environment, nativeClassBuild, "startingClass", fetcher);
    assert.equal(resolved.status, "resolved");
    assert.equal(resolved.rule.id, "class.warlock");
    assert.equal(resolved.rule.name, "Warlock");
    assert.deepEqual(requests.map(url => new URL(url, "https://example.test").pathname), [
        "/tool-host/character-sheet/api/upstream/api/native-rules/ruleset",
        "/tool-host/character-sheet/api/upstream/api/native-rules/options"
    ]);
});

test("unknown class is unavailable without inventing a replacement", async () => {
    const { fetcher, requests } = nativeFetch(false);
    const result = await resolveStoredChoice(
        environment, nativeClassBuild, "startingClass", fetcher);
    assert.deepEqual(result, { status: "unavailable", conceptKey: "class.warlock" });
    assert.equal(requests.length, 2);
    assert.equal(nativeClassBuild.progressionEntries[0].ruleConceptKey, "class.warlock");
});

test("retired and unsupported identities do not trigger network calls", async () => {
    const { fetcher, requests } = nativeFetch();
    assert.deepEqual(await resolveStoredChoice(
        environment, build, "startingClass", fetcher),
        { status: "unavailable", conceptKey: "class:wizard" });
    assert.deepEqual(await resolveStoredChoice(
        environment, nativeClassBuild, "species", fetcher),
        { status: "unavailable", conceptKey: "species:elf" });
    assert.equal(requests.length, 0);
});

test("Rules Core technical failure remains explicit rather than pretending an empty catalog", async () => {
    const result = await resolveStoredChoice(
        environment, nativeClassBuild, "startingClass",
        async () => Response.json({ error: "offline" }, { status: 503 }));
    assert.equal(result.status, "error");
    assert.equal(result.conceptKey, "class.warlock");
    assert.match(result.message, /HTTP 503/);
});
