import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createInitialState } from "../.test-dist/app-state.js";
import { createApplication } from "../.test-dist/render-lifecycle.js";
import { createHarvestingCraftingWorkflow } from "../.test-dist/features/harvesting/harvesting-workflow.js";
import { getGuidedBuilderSectionStates } from "../.test-dist/ui/sheet-model.js";

const sourceAttribution = {
    workKey: "fixture",
    workDisplayName: "Fixture Harvesting",
    provider: "Fixture",
    gameEdition: "5e",
    releaseKind: "test",
    publicationDate: "2026-09-26",
    referenceUri: "https://example.test/harvesting"
};

function createWorkflowHarness() {
    let renders = 0;
    const application = createApplication(
        createInitialState({ kind: "new" }),
        () => { renders += 1; });
    const routine = {
        current: () => application.getState().routine,
        mutate: async () => false
    };
    const presentation = { load: async () => {} };
    const environment = {
        embedded: false,
        toolBasePath: "",
        toolRoute: "/",
        contextUrl: null,
        standaloneDevelopment: true,
        rulesCoreDevelopmentBaseUrl: "https://rules.example.test"
    };
    const workflow = createHarvestingCraftingWorkflow(
        application,
        routine,
        presentation,
        environment);
    return {
        application,
        workflow,
        renderCount: () => renders,
        resetRenderCount: () => { renders = 0; }
    };
}

test("Harvesting draft edits do not replace the action target before Calculate Harvest", async () => {
    const harness = createWorkflowHarness();
    const { application, workflow } = harness;
    const table = {
        source: sourceAttribution,
        creatureType: "beast",
        creatureTypeDisplayName: "Beast",
        competencyKey: "skill.survival",
        competencyDisplayName: "Survival",
        components: [{
            key: "hide",
            displayName: "Hide",
            componentDc: 10,
            quantity: null,
            origin: null
        }],
        creatureConceptKey: null,
        creatureDisplayName: null,
        creatureSize: "Medium",
        creatureOverridesApplied: false,
        manualEditsApplied: false
    };
    application.dispatch({
        type: "harvesting-crafting-updated",
        state: {
            ...application.getState().harvestingCrafting,
            open: true,
            catalogStatus: "ready",
            tableStatus: "ready",
            tableRequest: { creatureType: "beast" },
            table,
            creatureType: "beast",
            creatureSize: "Medium",
            harvestOrder: ["hide"]
        }
    }, { render: false });
    harness.resetRenderCount();

    workflow.setAssessmentResult(20);
    workflow.setCarvingResult(20);
    await workflow.editHarvestComponent("hide", 10, 1);

    assert.equal(harness.renderCount(), 0);
    const draft = application.getState().harvestingCrafting;
    assert.equal(draft.assessmentResult, 20);
    assert.equal(draft.carvingResult, 20);
    assert.equal(draft.table.components[0].quantity, 1);
    assert.equal(draft.tableRequest.manualEdits.upsertComponents[0].quantity, 1);

    const requests = [];
    globalThis.window = {
        fetch: async (_url, init) => {
            const request = JSON.parse(init.body);
            requests.push(request);
            const edit = request.table.manualEdits.upsertComponents[0];
            return {
                ok: true,
                json: async () => ({
                    table: {
                        ...table,
                        components: [{
                            ...table.components[0],
                            componentDc: edit.componentDc,
                            quantity: edit.quantity
                        }],
                        manualEditsApplied: true
                    },
                    assessmentRollMode: "normal",
                    carvingRollMode: "normal",
                    creatureSize: request.creatureSize,
                    assessmentResult: request.assessmentResult,
                    carvingResult: request.carvingResult,
                    helperCount: 0,
                    helperContribution: 0,
                    harvestingResult: request.carvingResult,
                    components: [{
                        ...table.components[0],
                        componentDc: edit.componentDc,
                        harvestDc: edit.componentDc,
                        quantity: edit.quantity,
                        awarded: true
                    }]
                })
            };
        }
    };

    await workflow.evaluate();

    assert.equal(requests.length, 1);
    assert.equal(requests[0].assessmentResult, 20);
    assert.equal(requests[0].carvingResult, 20);
    assert.equal(requests[0].table.manualEdits.upsertComponents[0].quantity, 1);
    assert.ok(application.getState().harvestingCrafting.outcome);
    assert.ok(harness.renderCount() >= 2);
});

test("invalid Harvesting component drafts still surface validation immediately", async () => {
    const harness = createWorkflowHarness();
    const { application, workflow } = harness;
    application.dispatch({
        type: "harvesting-crafting-updated",
        state: {
            ...application.getState().harvestingCrafting,
            tableStatus: "ready",
            tableRequest: { creatureType: "beast" },
            table: {
                source: sourceAttribution,
                creatureType: "beast",
                creatureTypeDisplayName: "Beast",
                competencyKey: "skill.survival",
                competencyDisplayName: "Survival",
                components: [{
                    key: "hide",
                    displayName: "Hide",
                    componentDc: 10,
                    quantity: null,
                    origin: null
                }],
                creatureConceptKey: null,
                creatureDisplayName: null,
                creatureSize: "Medium",
                creatureOverridesApplied: false,
                manualEditsApplied: false
            },
            harvestOrder: ["hide"]
        }
    }, { render: false });
    harness.resetRenderCount();

    await workflow.editHarvestComponent("hide", 0, 1);

    assert.match(
        application.getState().harvestingCrafting.message,
        /positive whole numbers/i);
    assert.equal(harness.renderCount(), 1);
});

test("Crafting scalar drafts persist without rerendering the modal", () => {
    const harness = createWorkflowHarness();
    const { application, workflow } = harness;

    workflow.setCraftingRecipeName("Moonsteel Blade");
    workflow.setCraftingOutputName("Moonsteel Longsword");
    workflow.setCraftingOutputQuantity(1);
    workflow.setCraftingManualName("Blacksmithing");
    workflow.setCraftingManualContribution(7);
    workflow.setCraftingManufacturingManualAbilityModifier(4);
    workflow.setCraftingTargetDc(18);
    workflow.setCraftingOtherModifier(2);
    workflow.setCraftingManufacturingRequiredHours(8);
    workflow.setCraftingManufacturingCompletedHours(8);

    assert.equal(harness.renderCount(), 0);
    const state = application.getState().harvestingCrafting;
    assert.equal(state.craftingRecipeName, "Moonsteel Blade");
    assert.equal(state.craftingOutputName, "Moonsteel Longsword");
    assert.equal(state.craftingManualContribution, 7);
    assert.equal(state.craftingManufacturingManualAbilityModifier, 4);
    assert.equal(state.craftingTargetDc, 18);
    assert.equal(state.craftingOtherModifier, 2);
    assert.equal(state.craftingManufacturingCompletedHours, 8);
});

test("Skill disclosures use native details-summary keyboard semantics with visible focus", async () => {
    const skills = await readFile(new URL("../src/ui/skills.ts", import.meta.url), "utf8");
    const foundation = await readFile(new URL("../src/styles/foundation.css", import.meta.url), "utf8");

    assert.match(skills, /createElement\("details", "dd-skill-disclosure/);
    assert.match(skills, /createElement\("summary", "dd-skill-disclosure__summary"\)/);
    assert.doesNotMatch(skills, /summary\.setAttribute\("role",\s*"button"\)/);
    assert.doesNotMatch(skills, /summary\.setAttribute\("tabindex"/i);
    assert.doesNotMatch(skills, /summary\.tabIndex\s*=/);
    assert.doesNotMatch(skills, /summary\.addEventListener\("keydown"/);
    assert.match(
        foundation,
        /\.dd-sheet summary:focus-visible\s*\{[^}]*outline:\s*3px solid var\(--dd-sheet-focus\)/s);
});

test("core Proficiency Bonus consumes Rules Core proficiency mechanics from Training", async () => {
    const coreStats = await readFile(new URL("../src/ui/core-stats.ts", import.meta.url), "utf8");

    assert.match(coreStats, /\.\.\.\(mechanics\?\.training \?\? \[\]\)/);
    assert.match(coreStats, /\.\.\.\(mechanics\?\.combatFundamentals \?\? \[\]\)/);
    assert.match(coreStats, /renderProficiencyQuickCard\(proficiencyValues\)/);
});

test("Guided Abilities remains incomplete while Rules Core requires Ability Score choices", async () => {
    const abilityKeys = ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"];
    const builder = {
        status: "ready",
        build: {
            characterId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            progressionEntries: [],
            baseAbilityScoreInputs: abilityKeys.map((abilityKey, index) => ({
                id: `00000000-0000-0000-0000-00000000000${index}`,
                abilityKey,
                score: 10 + index,
                createdAt: "now",
                updatedAt: "now"
            }))
        }
    };
    const choice = {
        choiceKey: "choice.background.acolyte.ability.0.0",
        groupKey: "choice-group.background.acolyte.ability.0",
        displayName: "Acolyte Ability +2",
        kind: "ability-score",
        state: "choice-required",
        options: [
            { value: "intelligence", displayName: "Intelligence" },
            { value: "wisdom", displayName: "Wisdom" },
            { value: "charisma", displayName: "Charisma" }
        ]
    };

    const pending = getGuidedBuilderSectionStates(builder, { ruleChoices: [choice] });
    const pendingAbilities = pending.find(section => section.id === "abilities");
    assert.equal(pendingAbilities.status, "incomplete");
    assert.match(pendingAbilities.detail, /1 required Ability Score choice remains/);

    const resolved = getGuidedBuilderSectionStates(builder, {
        ruleChoices: [{ ...choice, state: "resolved", selectedValue: "wisdom" }]
    });
    const resolvedAbilities = resolved.find(section => section.id === "abilities");
    assert.equal(resolvedAbilities.status, "resolved");

    const sheet = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.match(sheet, /getGuidedBuilderSectionStates\(builder, mechanics\)/);
    assert.match(sheet, /heading:\s*"Required Ability Choices"/);
    assert.match(sheet, /choiceKinds:\s*\["ability-score", "ability-score-set"\]/);
});

test("ordinary advancement uses a dedicated floating workspace instead of raw level editing", async () => {
    const advancement = await readFile(new URL("../src/ui/advancement.ts", import.meta.url), "utf8");
    const panel = await readFile(new URL("../src/features/advancement/advancement-panel.ts", import.meta.url), "utf8");
    const css = await readFile(new URL("../src/styles/advancement.css", import.meta.url), "utf8");

    assert.match(advancement, /"Advance Character"/);
    assert.match(advancement, /renderCharacterAdvancementPanel/);
    assert.doesNotMatch(advancement, /Save Level/);
    assert.doesNotMatch(advancement, /data-advancement-level-editor/);

    assert.match(panel, /dd-character-advancement-overlay/);
    assert.match(panel, /aria-modal", "true"/);
    assert.match(panel, /Take a level in another Class/);
    assert.match(panel, /Qualify for a Prestige Class/);
    assert.match(panel, /What will change/);
    assert.match(panel, /Apply Advancement/);
    assert.doesNotMatch(panel, /Save Level|data-advancement-level-editor/);

    assert.match(css, /\.dd-character-advancement-overlay\s*\{[\s\S]*position:\s*fixed/);
    assert.match(css, /\.dd-character-advancement-surface\s*\{[\s\S]*width:\s*min\(78rem, 100%\)/);
});

test("Prestige Class advancement remains inspectable when eligibility is unresolved", async () => {
    const panel = await readFile(new URL("../src/features/advancement/advancement-panel.ts", import.meta.url), "utf8");
    const workflow = await readFile(new URL("../src/features/advancement/advancement-workflow.ts", import.meta.url), "utf8");

    assert.match(workflow, /"class" \| "prestigeClass" \| "subclass"/);
    assert.match(workflow, /prestigeClassConceptKey/);
    assert.match(workflow, /searchResolvedRules\(environment, entityType, normalizedQuery\)/);
    assert.doesNotMatch(workflow, /eligible[^\n]*filter/i);

    assert.match(panel, /Eligibility unresolved/);
    assert.match(panel, /Known requirements are shown below/);
    assert.match(panel, /Check Requirements/);
    assert.match(panel, /Rules Core does not yet have enough normalized prerequisite data/);
});
