import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { createInitialState, reduceAppState } from "../.test-dist/app-state.js";
import {
    ABILITY_SCORE_DEFINITIONS,
    getGuidedBuilderSectionStates,
    createCharacterHeaderModel,
    getAbilityScoreActionPolicy,
    getBaseAbilityScoreDisplay,
    getChoiceActionPolicy,
    hasPendingBuildMutation,
    MECHANIC_PLACEHOLDERS,
    parseBaseAbilityScoreInput,
    SHEET_SECTIONS,
    toRuleReferenceDisplay
} from "../.test-dist/ui/sheet-model.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const character = {
    characterId,
    name: "Sai Cithreth",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: ["11111111-1111-1111-1111-111111111111"],
    hasRichSheet: true,
    sheet: {
        schemaVersion: 1,
        builderStatus: "BuildInProgress",
        createdAt: "now",
        updatedAt: "now"
    }
};
const build = {
    characterId,
    builderStatus: "BuildInProgress",
    readOnly: false,
    foundationalSelections: [],
    baseAbilityScoreInputs: [],
    progressionEntries: []
};
const resolved = (conceptKey, entityType, displayName) => ({
    status: "resolved",
    conceptKey,
    rule: {
        ruleConceptId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        conceptKey,
        entityType,
        displayName,
        sourceEntityName: displayName,
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    }
});

function builder(overrides = {}) {
    return {
        status: "ready",
        build,
        references: {
            raceSpecies: resolved("race:human", "race", "Human"),
            startingClass: resolved("class:wizard", "class", "Wizard"),
            subclass: resolved("subclass:wizard:evocation", "subclass", "School of Evocation")
        },
        chooser: { kind: "closed" },
        saving: null,
        savingAbility: null,
        featReferences: {},
        featChooser: { kind: "closed" },
        savingFeat: null,
        ...overrides
    };
}

test("header model presents real Character name and resolved Race, Class, and Subclass", () => {
    const model = createCharacterHeaderModel(character, builder(), false);
    assert.equal(model.name, "Sai Cithreth");
    assert.equal(model.raceSpecies.value, "Human");
    assert.equal(model.startingClass.value, "Wizard");
    assert.equal(model.subclass.value, "School of Evocation");
    assert.equal(model.campaignContext, "1 Campaign association");
});

test("header does not present an uninitialized builder as real empty selections", () => {
    const idleBuilder = {
        ...builder(),
        status: "idle",
        build: null,
        references: {
            raceSpecies: { status: "none" },
            startingClass: { status: "none" },
            subclass: { status: "none" }
        }
    };
    const model = createCharacterHeaderModel(
        { ...character, hasRichSheet: false, sheet: null },
        idleBuilder,
        false);
    assert.equal(model.raceSpecies.value, "Character Sheet not set up");
    assert.equal(model.startingClass.value, "Character Sheet not set up");
    assert.equal(model.subclass.value, "Character Sheet not set up");
});

test("unavailable persisted rule references remain visible instead of disappearing", () => {
    const reference = { status: "unavailable", conceptKey: "race:missing" };
    const display = toRuleReferenceDisplay(reference);
    assert.equal(display.value, "Unavailable saved selection");
    assert.equal(display.detail, "race:missing");
});

test("archived Character model is explicitly read-only", () => {
    const archived = { ...character, lifecycle: "Archived", archivedAt: "2026-09-16T02:00:00Z" };
    const model = createCharacterHeaderModel(archived, builder(), true);
    assert.equal(model.lifecycleLabel, "Archived");
    assert.equal(model.readOnly, true);
});

test("builder mutation policy removes editing affordances in read-only state", () => {
    const selected = resolved("class:wizard", "class", "Wizard");
    assert.deepEqual(getChoiceActionPolicy(selected, true, true, false), {
        canChoose: false,
        canClear: false,
        chooseLabel: "Replace"
    });
});

test("unfinished mechanics remain explicit placeholders without treating resolved generalized mechanics as placeholders", () => {
    assert.ok(MECHANIC_PLACEHOLDERS.length > 0);
    assert.equal(MECHANIC_PLACEHOLDERS.some(placeholder => placeholder.id === "proficiency"), false);
    assert.equal(MECHANIC_PLACEHOLDERS.some(placeholder => placeholder.id === "passive-values"), false);
    assert.equal(MECHANIC_PLACEHOLDERS.some(placeholder => placeholder.id === "training"), false);
    assert.equal(MECHANIC_PLACEHOLDERS.some(placeholder =>
        ABILITY_SCORE_DEFINITIONS.some(ability => ability.key === placeholder.id)), false);
    for (const placeholder of MECHANIC_PLACEHOLDERS) {
        assert.equal(Object.hasOwn(placeholder, "value"), false);
        assert.equal(/(^|\s)[+-]?\d+(?:\.\d+)?(?:\s|$)/.test(placeholder.message), false);
    }
});

test("primary sheet navigation establishes the requested scalable content regions", () => {
    assert.deepEqual(SHEET_SECTIONS.map(section => section.label), [
        "Actions",
        "Spells",
        "Inventory",
        "Features & Traits",
        "Notes"
    ]);
});

test("sheet section selection is explicit application state rather than DOM-only state", () => {
    let state = createInitialState({ kind: "character", characterId });
    assert.equal(state.activeSheetSection, "actions");
    state = reduceAppState(state, { type: "sheet-section-selected", section: "inventory" });
    assert.equal(state.activeSheetSection, "inventory");
});


test("all six stable Character ability keys are represented once", () => {
    assert.deepEqual(
        ABILITY_SCORE_DEFINITIONS.map(ability => ability.key),
        ["strength", "dexterity", "constitution", "intelligence", "wisdom", "charisma"]);
});

test("persisted base score is displayed as a base input", () => {
    const configuredBuilder = builder({
        build: {
            ...build,
            baseAbilityScoreInputs: [{
                id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                abilityKey: "strength",
                score: 15,
                createdAt: "now",
                updatedAt: "now"
            }]
        }
    });
    assert.deepEqual(getBaseAbilityScoreDisplay(configuredBuilder, "strength"), {
        status: "configured",
        value: "15",
        detail: "Base Score",
        score: 15
    });
});

test("missing base score remains unconfigured instead of becoming ten", () => {
    const display = getBaseAbilityScoreDisplay(builder(), "dexterity");
    assert.equal(display.status, "unconfigured");
    assert.equal(display.value, "-");
    assert.equal(display.score, null);
    assert.notEqual(display.value, "10");
});

test("build mutation lock spans builder choices and Ability Score mutations", () => {
    const selected = resolved("class:wizard", "class", "Wizard");
    const abilitySaving = builder({ savingAbility: "strength" });

    assert.equal(hasPendingBuildMutation(abilitySaving), true);
    assert.deepEqual(getChoiceActionPolicy(selected, false, true, hasPendingBuildMutation(abilitySaving)), {
        canChoose: false,
        canClear: false,
        chooseLabel: "Replace"
    });
    assert.equal(getAbilityScoreActionPolicy(abilitySaving, false, true).canSave, false);

    const choiceSaving = builder({ saving: "startingClass" });
    assert.equal(hasPendingBuildMutation(choiceSaving), true);
    assert.equal(getAbilityScoreActionPolicy(choiceSaving, false, true).canSave, false);
});

test("active Character ability policy supports set, replace, and clear while read-only does not mutate", () => {
    const activeBuilder = builder();
    assert.deepEqual(getAbilityScoreActionPolicy(activeBuilder, false, false), {
        canSave: true,
        canClear: false,
        saveLabel: "Set"
    });
    assert.deepEqual(getAbilityScoreActionPolicy(activeBuilder, false, true), {
        canSave: true,
        canClear: true,
        saveLabel: "Replace"
    });
    assert.deepEqual(getAbilityScoreActionPolicy(activeBuilder, true, true), {
        canSave: false,
        canClear: false,
        saveLabel: "Replace"
    });
});

test("base score parsing enforces only backend integer representation, not D&D score limits", () => {
    assert.deepEqual(parseBaseAbilityScoreInput("-2147483648"), { ok: true, score: -2147483648 });
    assert.deepEqual(parseBaseAbilityScoreInput("2147483647"), { ok: true, score: 2147483647 });
    assert.equal(parseBaseAbilityScoreInput("2147483648").ok, false);
    assert.equal(parseBaseAbilityScoreInput("3.5").ok, false);
    assert.equal(parseBaseAbilityScoreInput("").ok, false);
});

const mechanicsCssSource = await readFile(new URL("../src/styles/mechanics.css", import.meta.url), "utf8");
const css = (await Promise.all([
    "../src/styles/foundation.css",
    "../src/styles/builder.css",
    "../src/styles/advancement.css",
    "../src/styles/mechanics.css",
    "../src/styles/abilities.css",
    "../src/styles/supplemental.css"
].map(path => readFile(new URL(path, import.meta.url), "utf8")))).join("\n");
const sheetSource = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
const sheetModelSource = await readFile(new URL("../src/ui/sheet-model.ts", import.meta.url), "utf8");
const builderApiSource = await readFile(new URL("../src/builder-api.ts", import.meta.url), "utf8");
const appSource = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
const abilitySource = await readFile(new URL("../src/features/abilities/ability-stats.ts", import.meta.url), "utf8");
const coreStatsSource = await readFile(new URL("../src/ui/core-stats.ts", import.meta.url), "utf8");
const sheetContractsSource = await readFile(new URL("../src/ui/sheet-contracts.ts", import.meta.url), "utf8");
const primaryContentSource = await readFile(new URL("../src/ui/primary-content.ts", import.meta.url), "utf8");
const notesSource = await readFile(new URL("../src/features/notes/notes-section.ts", import.meta.url), "utf8");
const inventorySource = await readFile(new URL("../src/features/inventory/inventory-section.ts", import.meta.url), "utf8");
const inventoryWorkflowSource = await readFile(new URL("../src/features/inventory/inventory-workflow.ts", import.meta.url), "utf8");
const featuresSource = await readFile(new URL("../src/features/features/features-section.ts", import.meta.url), "utf8");
const featWorkflowSource = await readFile(new URL("../src/features/features/feat-workflow.ts", import.meta.url), "utf8");

test("UI shell responds to the embedded Character Sheet width instead of only the viewport", () => {
    assert.match(css, /\.dd-sheet\s*\{[^}]*container-name:\s*character-sheet;[^}]*container-type:\s*inline-size;/s);
    assert.match(css, /\.dd-sheet__stage\s*\{[^}]*container-name:\s*character-stage;[^}]*container-type:\s*inline-size;/s);
    assert.match(css, /@container character-sheet \(max-width: 78rem\)[\s\S]*?\.dd-sheet__dashboard/s);
    assert.match(css, /@container character-sheet \(max-width: 62rem\)[\s\S]*?\.dd-core-stats,[\s\S]*?repeat\(3,/s);
    assert.match(css, /@container character-sheet \(max-width: 45rem\)[\s\S]*?repeat\(2,/s);
});

test("desktop sheet uses the available viewport and content-aware top-stat columns", () => {
    assert.match(css, /\.dd-sheet-screen\s*\{[^}]*width:\s*100%;[^}]*max-width:\s*none;/s);
    assert.match(css, /\.dd-core-stats\s*\{[^}]*repeat\(6,\s*minmax\(5\.35rem,\s*1fr\)\)[^}]*minmax\(16rem,\s*2\.45fr\)/s);
    assert.match(css, /\.dd-core-stats__abilities,\s*\.dd-core-stats__quick\s*\{[^}]*display:\s*contents;/s);
});

test("Hit Points owns the wide final top-strip track and top cards are not fixed-height clipped", () => {
    assert.doesNotMatch(css, /\.dd-health-quick\s*\{[^}]*grid-column:\s*span 2;/s);
    assert.match(css, /\.dd-health-quick\s*\{[^}]*min-height:\s*5\.75rem;/s);
    assert.doesNotMatch(css, /\.dd-core-stats__quick > \.dd-stat\s*\{[^}]*block-size:/s);
});

test("Guided Builder overrides display-contents Ability composition with its own container-responsive grid", () => {
    assert.match(
        css,
        /\.dd-guided-builder__ability-grid\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*repeat\(6,/s);
    assert.match(
        css,
        /@container character-sheet \(max-width: 62rem\)[\s\S]*?\.dd-core-stats,\s*\.dd-guided-builder__ability-grid\s*\{[^}]*repeat\(3,/s);
    assert.match(
        css,
        /@container character-sheet \(max-width: 45rem\)[\s\S]*?\.dd-core-stats,\s*\.dd-guided-builder__ability-grid\s*\{[^}]*repeat\(2,/s);
});

test("Character Sheet CSS references only defined Character Sheet semantic tokens", () => {
    const defined = new Set([...css.matchAll(/(--dd-sheet-[a-z0-9-]+)\s*:/g)].map(match => match[1]));
    const used = new Set([...css.matchAll(/var\((--dd-sheet-[a-z0-9-]+)/g)].map(match => match[1]));
    for (const token of used) {
        assert.ok(defined.has(token), `Undefined Character Sheet token: ${token}`);
    }
});

test("retired workspace grid selectors are removed after the reference-layout composition change", () => {
    assert.doesNotMatch(css, /\.dd-sheet__workspace\b/);
});

test("wide layout uses a full-width top strip, a persistent left rail, and a broad primary workspace", () => {
    assert.match(css, /\.dd-sheet__dashboard\s*\{[^}]*minmax\(13\.5rem,\s*16rem\)[^}]*minmax\(20rem,\s*23rem\)[^}]*minmax\(0,\s*1fr\)/s);
    assert.match(css, /\.dd-sheet__top-row\s*\{[^}]*padding:/s);
    assert.match(sheetSource, /createElement\("aside", "dd-sheet__reference-rail"\)/);
    assert.match(sheetSource, /createElement\("aside", "dd-sheet__skills"\)/);
    assert.match(sheetSource, /dashboard\.append\(referenceRail, skillsColumn, stage\)/);
    assert.match(sheetSource, /stage\.append\(primary\)/);
    assert.match(coreStatsSource, /renderHealthQuickCard\(mechanics, healthControl\)/);
    assert.match(sheetSource, /renderCombatSummaryBand\([\s\S]*renderConditionsCard\(routine, readOnly, handlers\.routine\)/s);
});

test("responsive shell uses persistent presentation scaffolds without fabricating Character values", () => {
    assert.doesNotMatch(sheetSource, /renderSupportScaffoldCard/);
    assert.match(sheetSource, /renderSensesSummaryCard\(mechanics\)/);
    assert.match(sheetSource, /renderTrainingCard\(mechanics\)/);
    assert.doesNotMatch(sheetSource, /renderDefenseMechanicsCard\(mechanics\)/);
    assert.doesNotMatch(sheetSource, /renderCombatFundamentalsCard\(mechanics\)/);
    assert.match(coreStatsSource, /renderHealthQuickCard\(mechanics, healthControl\)/);
    assert.doesNotMatch(sheetSource, /renderSavingThrowsCard\(mechanics\?\.savingThrows\)/);
    assert.match(sheetSource, /renderSavingThrowsCard\(detachedSavingThrows, true\)/);
    assert.doesNotMatch(sheetSource, />?\s*(?:10|30|37)\s*(?:<|ft\.|HP|AC)/i);
});

test("deployed-density polish keeps primary tabs readable and separates 3.x saves from Ability saves", () => {
    assert.match(css, /\.dd-primary-nav__button\s*\{[^}]*white-space:\s*nowrap;/s);
    assert.match(css, /@container character-stage \(min-width: 54rem\)[\s\S]*?\.dd-primary-nav\s*\{[^}]*overflow-x:\s*visible;/s);
    assert.match(css, /\.dd-sheet-mode-bar\s*\{[^}]*padding:\s*0\.4rem/s);
    assert.doesNotMatch(sheetSource, /renderSavingThrowsCard\(mechanics\?\.savingThrows\)/);
    assert.match(sheetSource, /renderSavingThrowsCard\(detachedSavingThrows, true\)/);
    assert.match(abilitySource, /isThreeXSavingThrowKey\(key\)/);
});

test("desktop top stats stretch to a shared height and Movement uses a disclosure instead of a horizontal speed strip", () => {
    assert.match(css, /\.dd-core-stats\s*\{[^}]*align-items:\s*stretch;/s);
    assert.match(css, /\.dd-core-stats \.dd-stat\s*\{[^}]*height:\s*100%;/s);
    assert.match(css, /\.dd-movement-values__details\s*\{/s);
    assert.match(css, /\.dd-movement-values__variants\s*\{[^}]*position:\s*absolute;/s);
    assert.doesNotMatch(css, /\.dd-movement-values__variants\s*\{[^}]*overflow-x:\s*auto;/s);
});

test("skill rows use stable single-line columns for proficiency, stat, name, and modifier", () => {
    assert.match(css, /\.dd-skill-row\s*\{[^}]*display:\s*grid;[^}]*grid-template-columns:\s*1\.8rem\s+2\.15rem\s+minmax\(0,\s*1fr\)\s+minmax\(2\.1rem,\s*max-content\)/s);
    assert.doesNotMatch(mechanicsCssSource, /\.dd-skill-row\s*\{/);
    assert.match(css, /\.dd-skill-row__name\s*\{[^}]*text-overflow:\s*ellipsis;[^}]*white-space:\s*nowrap;/s);
    assert.match(css, /\.dd-skill-row__value\s*\{[^}]*text-align:\s*right;[^}]*white-space:\s*nowrap;/s);
    assert.doesNotMatch(css, /\.dd-skill-row__identity\s*\{/);
});

test("desktop composite Skills retain the compact split parent-and-children layout", () => {
    assert.match(css, /\.dd-skill-group--composite\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*0\.95fr\)\s+minmax\(0,\s*1\.05fr\)/s);
    assert.match(css, /\.dd-skill-group__parent\s*\{[^}]*grid-row:\s*1 \/ span var\(--dd-skill-component-count\);[^}]*border-right:/s);
    assert.match(css, /\.dd-skill-group__component\s*\{[^}]*grid-column:\s*2;/s);
});

test("embedded module loads its stylesheet from the same Tool Module asset subtree", () => {
    assert.match(appSource, /new URL\("\.\/app\.css", import\.meta\.url\)/);
    assert.match(appSource, /data-character-sheet-stylesheet/);
});


test("normal Character workspace omits redundant technical Character ID disclosure", () => {
    assert.doesNotMatch(appSource, /Technical details/);
    assert.doesNotMatch(appSource, /renderDevelopmentDetails/);
    assert.doesNotMatch(appSource, /Character ID:/);
});

test("persistent combat mechanics are owned by the combat band rather than trailing the Skills list", () => {
    assert.doesNotMatch(sheetSource, /dd-sheet__mechanics/);
    assert.doesNotMatch(sheetSource, /dd-mechanics-summary-grid/);
    assert.doesNotMatch(sheetSource, /skillsColumn\.append\(mechanicsColumn\)/);
});

test("Character workspace does not create a nested document-level main landmark", () => {
    assert.doesNotMatch(sheetSource, /createElement\("main"/);
    assert.match(
        sheetSource,
        /createElement\("section", "dd-sheet__main"\)[\s\S]*aria-label", "Character details and controls"/);
});

test("ability UI does not invent effective-score or modifier calculations", () => {
    assert.doesNotMatch(builderApiSource, /effectiveScore/);
    assert.doesNotMatch(sheetModelSource, /effectiveScore/);
    assert.doesNotMatch(abilitySource, /effectiveScore/);
    assert.doesNotMatch(sheetModelSource, /\(\s*score\s*-\s*10\s*\)\s*\/\s*2/);
    assert.doesNotMatch(abilitySource, /\(\s*score\s*-\s*10\s*\)\s*\/\s*2/);
    assert.match(abilitySource, /data-ability-modifier/);
    assert.match(abilitySource, /data-ability-save/);
    assert.match(abilitySource, /"Modifier"/);
    assert.match(abilitySource, /"Save"/);
    assert.match(abilitySource, /modifier === undefined \? "-" : formatMechanicalValue\(modifier\)/);
    assert.match(abilitySource, /savingThrow === undefined \? "-" : formatMechanicalValue\(savingThrow\)/);
});

test("ability editor uses integer input without edition-specific min or max attributes", () => {
    assert.match(abilitySource, /input\.type = "number"/);
    assert.match(abilitySource, /input\.step = "1"/);
    assert.doesNotMatch(abilitySource, /input\.(?:min|max)\s*=/);
    assert.doesNotMatch(abilitySource, /setAttribute\("(?:min|max)"/);
});


function cssRule(selectorPattern) {
    const match = css.match(new RegExp(selectorPattern + "\\s*\\{([^}]*)\\}", "s"));
    assert.ok(match, `Expected CSS rule matching ${selectorPattern}`);
    return match[1];
}

test("Character Sheet semantic colors inherit the Site Bootstrap theme contract", () => {
    const themeBlock = cssRule("\\.dd-sheet,\\s*\\.dd-sheet-screen");
    const hostMappings = [
        ["--dd-sheet-background", "--bs-body-bg"],
        ["--dd-sheet-surface", "--bs-tertiary-bg"],
        ["--dd-sheet-surface-muted", "--bs-secondary-bg"],
        ["--dd-sheet-ink", "--bs-body-color"],
        ["--dd-sheet-muted", "--bs-secondary-color"],
        ["--dd-sheet-border", "--bs-border-color"],
        ["--dd-sheet-accent", "--bs-primary"],
        ["--dd-sheet-danger", "--bs-danger-text-emphasis"],
        ["--dd-sheet-warning", "--bs-warning-text-emphasis"]
    ];

    for (const [sheetToken, hostToken] of hostMappings) {
        assert.match(themeBlock, new RegExp(sheetToken + ":\\s*var\\(" + hostToken));
    }
});

test("Site light and dark appearance drive Character Sheet native color schemes", () => {
    assert.match(
        css,
        /:root\[data-bs-theme="light"\][\s\S]*?\.dd-sheet-screen\s*\{[^}]*color-scheme:\s*light;/s);
    assert.match(
        css,
        /:root\[data-bs-theme="dark"\][\s\S]*?\.dd-sheet-screen\s*\{[^}]*color-scheme:\s*dark;/s);
});

test("focus indication uses the semantic host-driven focus token", () => {
    const focusMatch = css.match(/\.dd-sheet button:focus-visible,[\s\S]*?\.dd-sheet summary:focus-visible\s*\{([^}]*)\}/);
    assert.ok(focusMatch);
    assert.match(focusMatch[1], /outline:\s*3px solid var\(--dd-sheet-focus\)/);
    assert.match(cssRule("\\.dd-sheet,\\s*\\.dd-sheet-screen"), /--dd-sheet-focus:\s*var\(--bs-primary,/);
});

test("major Character Sheet surfaces consume semantic tokens instead of local palette literals", () => {
    const majorSurfaceRules = [
        cssRule("\\.dd-sheet"),
        cssRule("\\.dd-sheet-header"),
        cssRule("\\.dd-stat"),
        cssRule("\\.dd-readonly-banner"),
        cssRule("\\.dd-rule-chooser"),
        cssRule("\\.dd-primary-nav__button--active"),
        cssRule("\\.dd-inline-state--error")
    ].join("\n");

    assert.match(majorSurfaceRules, /var\(--dd-sheet-(?:background|surface|header|warning-surface|danger-surface)/);

    const semanticTokenBlock = css.match(/^\.dd-sheet,\n\.dd-sheet-screen \{[\s\S]*?\n\}/)?.[0] ?? "";
    const componentRules = css.replace(semanticTokenBlock, "");
    assert.doesNotMatch(componentRules, /#[0-9a-f]{3,8}\b|rgba?\s*\(|hsla?\s*\(/i);
    assert.doesNotMatch(
        componentRules,
        /(?:color|background(?:-color)?|border-color|outline):\s*(?:white|black)\b/i);
});

test("Character Sheet theme adds no external visual assets or custom font branding", () => {
    assert.doesNotMatch(css, /@font-face/i);
    assert.doesNotMatch(css, /url\s*\(/i);
    const portraitRule = cssRule("\\.dd-sheet-header__portrait");
    assert.match(portraitRule, /background:\s*var\(--dd-sheet-accent\)/);
    assert.doesNotMatch(portraitRule, /gradient\s*\(/i);
});

test("guided setup reports only backend-known unresolved structural configuration", () => {
    const current = builder({
        foundationalSelections: [],
        progressionEntries: [],
        baseAbilityScoreInputs: []
    });
    const states = getGuidedBuilderSectionStates(current);
    assert.deepEqual(
        states.map(section => [section.id, section.status]),
        [
            ["species", "incomplete"],
            ["advancement", "incomplete"],
            ["abilities", "incomplete"],
            ["review", "available"]
        ]
    );
    assert.match(states.find(section => section.id === "advancement").detail, /No Starting Class/);
    assert.doesNotMatch(states.find(section => section.id === "advancement").detail, /Subclass.*required/i);
});

test("normal View rendering keeps structural editors out of the sheet until Edit Mode is active", () => {
    assert.match(sheetSource, /const structuralEditing = editable && sheetMode === "edit"/);
    assert.match(sheetSource, /if \(structuralEditing\) \{[\s\S]*renderCharacterBuilder/);
    assert.match(
        abilitySource,
        /if \(structuralEditing && !readOnly && \(display\.status === "configured" \|\| display\.status === "unconfigured"\)\)/
    );
});

test("Guided Builder remains optional and supports direct section navigation", () => {
    assert.match(sheetSource, /data-guided-builder-section/);
    assert.match(sheetSource, /aria-current", "page"/);
    assert.match(sheetSource, /selectGuidedBuilderSection/);
    assert.match(sheetSource, /The Character Sheet remains available even when setup is incomplete/);
});

test("structural mutation handlers are namespaced separately from ordinary sheet interaction", () => {
    assert.match(sheetContractsSource, /interface CharacterSheetHandlers \{[\s\S]*structural: StructuralCharacterHandlers;/);
    assert.match(sheetContractsSource, /selectSection\(section: SheetSection\): void;/);
    assert.doesNotMatch(sheetSource, /if \(sheetMode === "edit"\)[\s\S]*renderPrimaryContent/);
});

test("new Edit and Guided Builder styling uses only Character Sheet semantic theme variables", () => {
    const marker = "/* Structural edit and guided builder presentation */";
    const start = css.indexOf(marker);
    assert.notEqual(start, -1);
    const end = css.indexOf("@media (max-width: 1099px)", start);
    const modeCss = css.slice(start, end);
    assert.doesNotMatch(modeCss, /#[0-9a-f]{3,8}\b|rgb\(/i);
    assert.match(modeCss, /var\(--dd-sheet-/);
});

test("Character Sheet implementation does not copy D&D Beyond source identifiers or assets", () => {
    for (const source of [sheetSource, appSource]) {
        assert.doesNotMatch(source, /dndbeyond|ddbc-|builder-sections-|Character Builder - D&D Beyond/i);
    }
});


test("Notes are rendered from routine Character state and remain routine View-mode interactions", () => {
    assert.match(primaryContentSource, /renderNotesSection\(routine, readOnly, handlers\.routine\)/);
    assert.match(notesSource, /handlers\.addNote\(input\.value\)/);
    assert.match(notesSource, /handlers\.updateNote\(note\.id, editor\.value\)/);
    assert.match(notesSource, /handlers\.deleteNote\(note\.id\)/);
    assert.doesNotMatch(sheetModelSource, /future Campaign-scoped modules/);
});


test("Inventory keeps distinct occurrence identity without exposing raw UUIDs to players", () => {
    assert.match(inventorySource, /data-inventory-occurrence-id/);
    assert.doesNotMatch(inventorySource, /\`Occurrence \${occurrence\.id}\`/);
    assert.match(inventorySource, /handlers\.addInventoryItem\(rule\.conceptKey\)/);
    assert.match(inventorySource, /handlers\.removeInventoryItem\(occurrence\.id\)/);
    assert.match(inventoryWorkflowSource, /searchResolvedRules\([\s\S]*"item"[\s\S]*normalizedQuery/);
    assert.doesNotMatch(sheetModelSource, /Equipment, carried items, currency/);
});


test("Features and Traits keeps Feat occurrence identity internal while mutations remain structural", () => {
    assert.match(primaryContentSource, /renderFeaturesSection\([\s\S]*builder,[\s\S]*structuralEditing,[\s\S]*readOnly,[\s\S]*handlers\.feats/);
    assert.match(featuresSource, /data-feat-occurrence-id/);
    assert.doesNotMatch(featuresSource, /\`Occurrence \${occurrence\.id}\`/);
    assert.match(featuresSource, /if \(editable\) \{[\s\S]*"Add Feat"/);
    assert.match(featuresSource, /handlers\.remove\(occurrence\.id\)/);
    assert.match(featWorkflowSource, /searchResolvedRules\([\s\S]*"feat"[\s\S]*normalizedQuery/);
    assert.match(featuresSource, /Other Features & Traits/);
    assert.doesNotMatch(sheetModelSource, /Features and traits are not available yet/);
});

test("Feat mutations participate in the shared structural build mutation lock", () => {
    assert.equal(hasPendingBuildMutation(builder({ savingFeat: "add" })), true);
    assert.equal(hasPendingBuildMutation(builder({ savingFeat: "99999999-9999-9999-9999-999999999999" })), true);
});
