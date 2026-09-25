import test from "node:test";
import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const srcRoot = fileURLToPath(new URL("../src/", import.meta.url));

async function source(relativePath) {
    return readFile(path.join(srcRoot, relativePath), "utf8");
}

async function tsFiles(relativeDir) {
    const root = path.join(srcRoot, relativeDir);
    const files = [];

    async function visit(dir) {
        for (const entry of await readdir(dir, { withFileTypes: true })) {
            const absolute = path.join(dir, entry.name);
            if (entry.isDirectory()) {
                await visit(absolute);
            } else if (entry.isFile() && entry.name.endsWith(".ts")) {
                files.push(path.relative(srcRoot, absolute).replaceAll(path.sep, "/"));
            }
        }
    }

    await visit(root);
    return files.sort();
}

test("mechanics compatibility surface re-exports feature owners instead of implementing them", async () => {
    const compatibility = await source("ui/mechanics-components.ts");
    assert.match(compatibility, /Compatibility exports/);
    assert.match(compatibility, /features\/health\/health\.js/);
    assert.match(compatibility, /features\/movement\/movement\.js/);
    assert.doesNotMatch(compatibility, /\bfunction\s+\w+\s*\(/);
});

test("sheet shell delegates feature sections and top-stat composition", async () => {
    const sheet = await source("ui/sheet.ts");
    assert.match(sheet, /from "\.\/core-stats\.js"/);
    assert.match(sheet, /from "\.\/primary-content\.js"/);
    for (const implementation of [
        "renderCoreStats",
        "renderAbilityScoreCard",
        "renderNotesSection",
        "renderInventorySection",
        "renderFeaturesSection"
    ]) {
        assert.doesNotMatch(
            sheet,
            new RegExp(`function\\s+${implementation}\\s*\\(`),
            `${implementation} should remain owned outside the sheet shell`);
    }
});

test("core modules do not depend on feature modules", async () => {
    for (const file of await tsFiles("core")) {
        const text = await source(file);
        assert.doesNotMatch(
            text,
            /from\s+["'][^"']*features\//,
            `${file} must not import a feature module`);
    }
});

test("leaf features do not import unrelated sibling features", async () => {
    const allowedComposition = new Map([
        ["combat", new Set(["initiative", "defense", "health", "proficiency", "saving-throws"])]
    ]);

    for (const file of await tsFiles("features")) {
        const [, owner] = file.split("/");
        const text = await source(file);
        const siblingImports = [...text.matchAll(/from\s+["']\.\.\/(?!\.\.\/)([^/"']+)\/[^"']+["']/g)]
            .map(match => match[1])
            .filter(target => target !== owner);

        for (const target of siblingImports) {
            assert.ok(
                allowedComposition.get(owner)?.has(target),
                `${file} imports unrelated feature ${target}`);
        }
    }
});

test("application bootstrap composes feature workflows instead of owning their mutations", async () => {
    const app = await source("app.ts");
    for (const factory of [
        "createPresentationWorkflow",
        "createRoutineStateWorkflow",
        "createBuildStateWorkflow",
        "createAdvancementWorkflow",
        "createAbilityWorkflow",
        "createFeatWorkflow",
        "createHealthWorkflow",
        "createInventoryWorkflow",
        "createNotesWorkflow"
    ]) {
        assert.match(app, new RegExp(`\\b${factory}\\b`));
    }

    for (const implementation of [
        "addInventoryItem",
        "removeInventoryItem",
        "addFeat",
        "removeFeat",
        "saveBaseAbilityScore",
        "clearBaseAbilityScore"
    ]) {
        assert.doesNotMatch(app, new RegExp(`async\\s+function\\s+${implementation}\\s*\\(`));
    }
});

test("root state reducer delegates slice-local transitions", async () => {
    const state = await source("app-state.ts");
    assert.match(state, /reduceBuilderState\(state\.builder, action\)/);
    assert.match(state, /reduceRoutineState\(state\.routine, action\)/);
    assert.match(state, /reducePresentationState\(state\.presentation, action\)/);
    assert.doesNotMatch(state, /case "routine-load-started":/);
    assert.doesNotMatch(state, /case "presentation-load-started":/);
    assert.match(state, /case "sheet-edit-entered":/);
    assert.match(state, /case "guided-builder-opened":/);
});

test("stylesheet entrypoint is composition-only", async () => {
    const entry = await source("styles.css");
    const lines = entry.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
    assert.deepEqual(lines, [
        '@import "./styles/foundation.css";',
        '@import "./styles/builder.css";',
        '@import "./styles/advancement.css";',
        '@import "./styles/mechanics.css";',
        '@import "./styles/abilities.css";',
        '@import "./styles/supplemental.css";'
    ]);
});


test("Harvesting and Crafting keeps rule resolution, inventory mutation, and source attribution behind their owners", async () => {
    const workflow = await source("features/harvesting/harvesting-workflow.ts");
    const workspace = await source("features/harvesting/harvesting-workspace.ts");
    const craftingApi = await source("crafting-api.ts");

    assert.match(workflow, /applyInventoryTransaction/);
    assert.match(workflow, /resolution\.producesFunctionalOutput/);
    assert.match(workflow, /craftingManufacturingRequiredHours/);
    assert.match(workflow, /craftingEnchantingRequiredHours/);
    assert.match(workflow, /abilityKey:/);
    assert.match(craftingApi, /abilityKey\?: string \| null/);
    assert.match(craftingApi, /manualAbilityModifier\?: number \| null/);

    assert.match(workspace, /state\.catalog\?\.source/);
    assert.match(workspace, /renderRulesSource/);
    assert.doesNotMatch(
        workspace,
        /patreon\.com\/LootTavern\/posts\/helianas-and-to-107406117/,
        "the frontend must consume Rules Core attribution instead of hard-coding the Loot Tavern URL");
});

test("campaign helper selection is optional and preserves manual fallback", async () => {
    const workflow = await source("features/harvesting/harvesting-workflow.ts");
    const campaignApi = await source("campaign-context-api.ts");

    assert.match(workflow, /loadHostedCampaignContext/);
    assert.match(workflow, /setHelperCharacter/);
    assert.match(workflow, /Manual helper entry remains available/);
    assert.match(campaignApi, /\/api\/campaigns\/\$\{encodeURIComponent\(campaignId\)\}\/context/);
});


test("Harvesting UI does not expose Rules Core resolution controls", async () => {
    const workspace = await source("features/harvesting/harvesting-workspace.ts");
    const workflow = await source("features/harvesting/harvesting-workflow.ts");
    const rulesApi = await source("rules-core-api.ts");

    for (const internalLabel of [
        "Effective rules",
        "Resolve Harvesting Table",
        "Resolved Harvesting Rule",
        "Resolve Check",
        "Rules Core competency"
    ]) {
        assert.doesNotMatch(
            workspace,
            new RegExp(internalLabel),
            `${internalLabel} is an implementation detail, not a player workflow control`);
    }

    assert.match(workflow, /function typeTable\(/);
    assert.match(workflow, /void resolveTable\(\)/);
    assert.match(rulesApi, /input\.creatureConceptKey/);
});

test("Crafting UI is organized around project and crafting steps", async () => {
    const workspace = await source("features/harvesting/harvesting-workspace.ts");

    assert.match(workspace, /Crafting Project/);
    assert.match(workspace, /Required steps/);
    assert.match(workspace, /Roll Manufacturing/);
    assert.match(workspace, /Roll Enchanting/);
    assert.match(workspace, /Finish & Update Inventory/);
    assert.doesNotMatch(workspace, /Crafting Procedure/);
    assert.doesNotMatch(workspace, /Universal competency/);
});


test("Harvesting and Crafting opens as a modal over the Character Sheet", async () => {
    const sheet = await source("ui/sheet.ts");
    const workspace = await source("features/harvesting/harvesting-workspace.ts");
    const css = await source("styles/supplemental.css");

    assert.match(sheet, /renderHarvestingCraftingOverlay/);
    assert.doesNotMatch(
        sheet,
        /if \(harvestingCrafting\.open\)[\s\S]{0,600}return shell;/,
        "opening Harvesting & Crafting must not replace the Character Sheet");
    assert.match(workspace, /aria-modal", "true"/);
    assert.match(workspace, /event\.key !== "Escape"/);
    assert.match(workspace, /event\.target === overlay/);
    assert.match(css, /\.dd-harvesting-overlay\s*\{[\s\S]*position:\s*fixed/);
    assert.match(css, /\.dd-harvesting-dialog\s*\{[\s\S]*width:\s*min\(94vw, 88rem\)/);
});


test("Loot Tavern modal uses worksheet and workshop layouts", async () => {
    const workspace = await source("features/harvesting/harvesting-workspace.ts");
    const css = await source("styles/supplemental.css");

    assert.match(workspace, /dd-harvesting-worksheet/);
    assert.match(workspace, /dd-crafting-workshop/);
    assert.match(workspace, /Current Project/);
    assert.match(workspace, /required · \$\{availableQuantity\} available/);
    assert.match(workspace, /document\.createElement\("progress"\)/);

    assert.match(css, /\.dd-harvesting-worksheet\s*\{[\s\S]*grid-template-columns/);
    assert.match(css, /\.dd-crafting-workshop\s*\{[\s\S]*grid-template-columns/);
    assert.match(css, /\.dd-crafting-material--short/);
});
