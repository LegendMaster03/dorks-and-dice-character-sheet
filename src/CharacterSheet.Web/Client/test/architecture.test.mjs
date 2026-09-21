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
        ["combat", new Set(["initiative", "defense", "health", "saving-throws"])]
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
