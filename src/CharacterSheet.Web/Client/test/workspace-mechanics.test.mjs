import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { renderCharacterWorkspace } from "../.test-dist/ui/sheet.js";

class FakeStyle {
    values = new Map();
    setProperty(name, value) { this.values.set(name, String(value)); }
    getPropertyValue(name) { return this.values.get(name) ?? ""; }
}
class FakeElement {
    constructor(tagName) {
        this.tagName = String(tagName).toUpperCase();
        this.className = "";
        this.textContent = "";
        this.children = [];
        this.attributes = new Map();
        this.style = new FakeStyle();
        this.id = "";
        this.type = "";
        this.disabled = false;
        this.value = "";
        this.placeholder = "";
        this.inputMode = "";
        this.autocomplete = "";
        this.rows = 0;
        this.maxLength = 0;
        this.href = "";
        this.target = "";
        this.rel = "";
    }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name) ?? null; }
    append(...children) { this.children.push(...children); }
    addEventListener() {}
    setCustomValidity() {}
    reportValidity() { return true; }
}
globalThis.document = { createElement: tagName => new FakeElement(tagName) };
globalThis.window = { confirm: () => true };

function walk(root) {
    const nodes = [root];
    for (const child of root.children) nodes.push(...walk(child));
    return nodes;
}
function byAttribute(root, name, value) { return walk(root).filter(node => node.getAttribute(name) === value); }
function byClass(root, className) { return walk(root).filter(node => node.className.split(/\s+/).includes(className)); }
function byTag(root, tagName) { return walk(root).filter(node => node.tagName === tagName.toUpperCase()); }
function visibleText(root) { return walk(root).map(node => node.textContent).filter(Boolean).join(" "); }

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const character = {
    characterId,
    name: "Projection Test",
    lifecycle: "Active",
    archivedAt: null,
    campaignIds: [],
    hasRichSheet: true,
    sheet: { schemaVersion: 1, builderStatus: "BuildInProgress", createdAt: "now", updatedAt: "now" }
};
const builder = {
    status: "idle",
    build: null,
    references: {
        raceSpecies: { status: "none" },
        startingClass: { status: "none" },
        subclass: { status: "none" }
    },
    chooser: { kind: "closed" },
    saving: null,
    savingAbility: null,
    featReferences: {},
    featChooser: { kind: "closed" },
    savingFeat: null
};
const guidedBuilder = { open: false, activeSection: "species", returnSheetMode: "view" };
const handlers = {
    structural: {
        openChooser() {}, clearChoice() {}, submitChooserSearch() {}, closeChooser() {}, saveChoice() {},
        setBaseAbilityScore() {}, clearBaseAbilityScore() {}
    },
    feats: { openChooser() {}, closeChooser() {}, search() {}, add() {}, remove() {} },
    routine: {
        addNote() {}, updateNote() {}, deleteNote() {}, openInventoryChooser() {}, closeInventoryChooser() {},
        searchInventory() {}, addInventoryItem() {}, removeInventoryItem() {}
    },
    selectSection() {}, enterEditMode() {}, leaveEditMode() {}, openGuidedBuilder() {},
    closeGuidedBuilder() {}, selectGuidedBuilderSection() {}
};
const routine = (occurrences = [], references = {}, readOnly = true) => ({
    status: "ready",
    state: { characterId, readOnly, inventoryItemOccurrences: occurrences, notes: [] },
    references,
    inventoryChooser: { kind: "closed" },
    mutation: null
});
const mechanical = (key, label, formattedValue, extra = {}) => ({
    key, label, effectiveValue: formattedValue, formattedValue, ...extra
});
const resolvedItem = (conceptKey, displayName) => ({
    status: "resolved",
    conceptKey,
    rule: {
        ruleConceptId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
        conceptKey,
        entityType: "item",
        displayName,
        sourceEntityName: displayName,
        sourceCode: "SRD",
        packageKey: "fixture",
        packageDisplayName: "Fixture",
        editionKey: "5e",
        editionDisplayName: "5e"
    }
});
function render(section, mechanics, currentRoutine = routine()) {
    return renderCharacterWorkspace(
        character, builder, currentRoutine, section, true, "view", guidedBuilder,
        null, mechanics, handlers
    );
}

test("workspace consumes supplied saving throws, competencies, combat, actions, movement, checks, and procedures", () => {
    const mechanics = {
        savingThrows: [mechanical("fort", "Fortitude", "+8")],
        competencies: { entries: [mechanical("listen", "Listen", "+7", { kind: "skill" })] },
        defenses: { primaryKey: "ac", values: [mechanical("ac", "Armor Class", "18")] },
        healthTracks: [{ key: "hp", label: "Hit Points", role: "hit-points", current: 20, maximum: 30 }],
        combatFundamentals: [mechanical("bab", "Base Attack Bonus", "+6/+1")],
        movement: [mechanical("walk", "Walk", "30 ft.")],
        actions: [{ key: "sword", name: "Longsword", attackOrCheck: mechanical("attack", "Attack", "+8") }],
        checks: [{ key: "assessment", name: "Assessment", effectiveModifierOrResult: mechanical("check", "Modifier", "+5") }],
        procedures: [{ key: "field", name: "Field Procedure", components: [] }]
    };
    const rendered = render("actions", mechanics);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "fort").length, 1);
    assert.equal(byAttribute(rendered, "data-skill-id", "listen").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "ac").length, 1);
    assert.equal(byAttribute(rendered, "data-health-track-key", "hp").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "bab").length, 1);
    assert.equal(byAttribute(rendered, "data-mechanic-key", "walk").length, 1);
    assert.equal(byAttribute(rendered, "data-action-key", "sword").length, 1);
    assert.equal(byAttribute(rendered, "data-check-key", "assessment").length, 1);
    assert.equal(byAttribute(rendered, "data-procedure-key", "field").length, 1);
});

test("null mechanics projection keeps normal surfaces and uses neutral dashes for missing values", () => {
    const rendered = render("actions", null);
    const text = visibleText(rendered);
    assert.ok(byClass(rendered, "dd-mechanic-value--placeholder").length >= 2);
    assert.equal(byClass(rendered, "dd-skill-row--placeholder").length, 1);
    assert.doesNotMatch(text, /Saving throw mechanics are not available|Resolved competencies are not available|Combat mechanics are not available|Movement mechanics are not available|Resolved checks and procedures are not available/);
    assert.match(text, /Resolved actions and attacks are not available/);
    assert.doesNotMatch(text, /Fortitude|Armor Class 18|Base Attack Bonus \+6\/\+1/);
});

test("Inventory joins mechanics by stable occurrence identity and keeps duplicate concepts independent", () => {
    const occurrences = [
        { id: "item-one", ruleConceptKey: "item:rope", createdAt: "now" },
        { id: "item-two", ruleConceptKey: "item:rope", createdAt: "now" }
    ];
    const references = {
        "item-one": resolvedItem("item:rope", "Rope"),
        "item-two": resolvedItem("item:rope", "Rope")
    };
    const mechanics = {
        inventory: {
            itemOccurrences: [
                { occurrenceId: "item-one", facts: [{ key: "marker", label: "State", value: "First occurrence mechanics" }] },
                { occurrenceId: "item-two", facts: [{ key: "marker", label: "State", value: "Second occurrence mechanics" }] }
            ]
        }
    };
    const rendered = render("inventory", mechanics, routine(occurrences, references));
    const first = byAttribute(rendered, "data-inventory-occurrence-id", "item-one")[0];
    const second = byAttribute(rendered, "data-inventory-occurrence-id", "item-two")[0];
    assert.match(visibleText(first), /First occurrence mechanics/);
    assert.doesNotMatch(visibleText(first), /Second occurrence mechanics/);
    assert.match(visibleText(second), /Second occurrence mechanics/);
    assert.doesNotMatch(visibleText(second), /First occurrence mechanics/);
});

test("Inventory carrying, components, procedures, and crafting render only when supplied", () => {
    const populated = render("inventory", {
        inventory: {
            carrying: {
                carried: { key: "carried", label: "Carried", value: "42 lb." },
                thresholds: [{ key: "heavy", label: "Heavy", value: "67-100 lb." }]
            },
            components: [{ key: "component-a", label: "Recovered Component", quantity: "2" }],
            procedures: [{ key: "procedure-a", name: "General Procedure", components: [] }],
            crafting: [{ key: "craft-a", name: "Craft Item", progress: { key: "progress", label: "Progress", value: "1 / 3" } }]
        }
    });
    assert.equal(byClass(populated, "dd-inventory-mechanics").length, 1);
    assert.equal(byClass(populated, "dd-component-card").length, 1);
    assert.equal(byAttribute(populated, "data-procedure-key", "procedure-a").length, 1);
    assert.equal(byAttribute(populated, "data-crafting-key", "craft-a").length, 1);
    assert.match(visibleText(populated), /42 lb/);

    const sparse = render("inventory", { inventory: { itemOccurrences: [] } });
    assert.equal(byClass(sparse, "dd-component-card").length, 0);
    assert.equal(byClass(sparse, "dd-crafting-card").length, 0);
    assert.doesNotMatch(visibleText(sparse), /Carrying & Load/);
});

test("spellcasting profiles render only when supplied", () => {
    const supplied = render("spells", {
        spellcastingProfiles: [{
            key: "wizard",
            label: "Wizard Spellcasting",
            castingAbility: "Intelligence",
            saveDc: mechanical("dc", "Save DC", "16")
        }]
    });
    assert.equal(byAttribute(supplied, "data-spellcasting-profile-key", "wizard").length, 1);
    assert.match(visibleText(supplied), /Wizard Spellcasting/);

    const unavailable = render("spells", null);
    assert.match(visibleText(unavailable), /Resolved spellcasting profiles are not available/);
    assert.equal(byClass(unavailable, "dd-spellcasting-profile").length, 0);
});

test("production consumes backend advancement and mechanics projections instead of hard-coded nulls", async () => {
    const source = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
    assert.match(source, /state\.presentation\.status === "ready" \? state\.presentation\.advancement : null/);
    assert.match(source, /state\.presentation\.status === "ready" \? state\.presentation\.mechanics : null/);
    assert.doesNotMatch(source, /state\.guidedBuilder,\s*null,\s*null,\s*\{/s);
});

test("production refreshes presentation after structural, Ability, Feat, and Inventory mutations", async () => {
    const source = await readFile(new URL("../src/app.ts", import.meta.url), "utf8");
    assert.match(source, /async function bootstrapPresentation/);
    assert.match(source, /addInventoryItem[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /removeInventoryItem[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /addFeat[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /removeFeat[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /saveChoice[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /clearChoice[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /saveBaseAbilityScore[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.match(source, /clearBaseAbilityScore[\s\S]*bootstrapPresentation\(characterId\)/);
    assert.doesNotMatch(source, /calculateAbilityModifier|score\s*-\s*10/i);
});

test("generalized presentation contains no source-specific Loot Tavern formula or prose", async () => {
    const sources = await Promise.all([
        "character-mechanics.ts", "mechanics-components.ts", "procedure-components.ts",
        "source-attribution.ts", "sheet.ts"
    ].map(name => readFile(new URL(`../src/ui/${name}`, import.meta.url), "utf8")));
    assert.doesNotMatch(sources.join("\n"), /Loot Tavern|Harvest Assessment|Carving/i);
});

test("generalized mechanics styles use host semantic tokens and include mobile layout rules", async () => {
    const css = await readFile(new URL("../src/styles.css", import.meta.url), "utf8");
    assert.match(css, /\.dd-mechanic-value/);
    assert.match(css, /\.dd-source-attribution/);
    assert.match(css, /\.dd-procedure-card/);
    assert.match(css, /\.dd-crafting-card/);
    assert.match(css, /\.dd-spellcasting-profile/);
    assert.match(css, /@media \(max-width: 720px\)[\s\S]*\.dd-action-list/s);
    const marker = "/* Generalized Character mechanics presentation */";
    const start = css.indexOf(marker);
    assert.notEqual(start, -1);
    const mechanicsCss = css.slice(start);
    assert.match(mechanicsCss, /var\(--dd-sheet-/);
    assert.doesNotMatch(mechanicsCss, /#[0-9a-f]{3,8}\b|rgba?\s*\(|hsla?\s*\(/i);
});


test("effective Ability projection is separate from Character-owned base Ability input", () => {
    const configuredBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [{ abilityKey: "strength", score: 12 }],
            progressionEntries: []
        }
    };
    const mechanics = {
        abilityValues: [{
            key: "strength",
            label: "Strength",
            effectiveValue: 18,
            formattedValue: "18",
            relatedValues: [{ key: "modifier", label: "Modifier", effectiveValue: 4, formattedValue: "+4" }],
            breakdown: [{ key: "enhancement", label: "Enhancement", effectiveValue: 6, formattedValue: "+6" }],
            sourceAttributions: [{ key: "module", label: "Rules module" }]
        }]
    };
    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        routine([], {}, false),
        "actions",
        false,
        "edit",
        guidedBuilder,
        null,
        mechanics,
        handlers
    );
    const strength = byAttribute(rendered, "data-ability-key", "strength")[0];
    assert.equal(strength.getAttribute("data-effective-ability-state"), "resolved");
    assert.match(visibleText(strength), /18/);
    assert.match(visibleText(strength), /Base input: 12/);
    assert.match(visibleText(strength), /Modifier \+4/);
    assert.match(visibleText(strength), /Enhancement \+6/);
    assert.match(visibleText(strength), /Rules module/);

    const editor = byTag(strength, "input")[0];
    assert.ok(editor);
    assert.equal(editor.value, "12");
    assert.notEqual(editor.value, "18");
});

test("absent effective Ability mechanics preserve base input and honest modifier unavailability", () => {
    const configuredBuilder = {
        ...builder,
        status: "ready",
        build: {
            characterId,
            builderStatus: "BuildInProgress",
            readOnly: false,
            foundationalSelections: [],
            baseAbilityScoreInputs: [{ abilityKey: "strength", score: 12 }],
            progressionEntries: []
        }
    };
    const rendered = renderCharacterWorkspace(
        character,
        configuredBuilder,
        routine([], {}, false),
        "actions",
        false,
        "view",
        guidedBuilder,
        null,
        { abilityValues: [] },
        handlers
    );
    const strength = byAttribute(rendered, "data-ability-key", "strength")[0];
    assert.equal(strength.getAttribute("data-effective-ability-state"), "unavailable");
    assert.match(visibleText(strength), /12/);
    assert.match(visibleText(strength), /Base Score/);
    assert.match(visibleText(strength), /Modifier -/);
});

test("backend-supplied Ability values outside the six structural keys use the generic fallback presentation", () => {
    const rendered = render("actions", {
        abilityValues: [mechanical("honor", "Honor", "14", {
            relatedValues: [{ key: "modifier", label: "Modifier", effectiveValue: 2, formattedValue: "+2" }]
        })]
    });
    const extra = byAttribute(rendered, "data-additional-ability-key", "honor")[0];
    assert.ok(extra);
    assert.match(visibleText(extra), /Honor/);
    assert.match(visibleText(extra), /14/);
    assert.match(visibleText(extra), /Modifier/);
});

test("top-level Character mechanics attribution renders once as a projection-wide rules-module surface", () => {
    const rendered = render("actions", {
        sourceAttributions: [{
            key: "module",
            label: "External rules module",
            detail: "Applies across several Character mechanics",
            officialUrl: "https://example.test/module"
        }]
    });
    const surfaces = byClass(rendered, "dd-character-mechanics-sources");
    assert.equal(surfaces.length, 1);
    assert.match(visibleText(surfaces[0]), /Rules modules/);
    assert.match(visibleText(surfaces[0]), /External rules module/);
    assert.equal(byTag(surfaces[0], "a").length, 1);
});

test("production Ability presentation contains no D&D Ability modifier formula", async () => {
    const sources = await Promise.all([
        "sheet.ts",
        "character-mechanics.ts",
        "mechanics-components.ts"
    ].map(name => readFile(new URL(`../src/ui/${name}`, import.meta.url), "utf8")));
    const production = sources.join("\n");
    assert.doesNotMatch(production, /score\s*-\s*10|Math\.floor\s*\([^\n]*-\s*10|calculateAbilityModifier/i);
});

test("legacy combat placeholder renderer is removed in favor of the generalized combat path", async () => {
    const source = await readFile(new URL("../src/ui/sheet.ts", import.meta.url), "utf8");
    assert.doesNotMatch(source, /function renderCombatSummary\s*\(/);
    assert.match(source, /renderCombatMechanicsSummary\(mechanics\)/);
});


test("backend-supplied Proficiency Bonus renders only through generalized combat fundamentals", () => {
    const rendered = render("actions", {
        combatFundamentals: [mechanical("proficiency-bonus", "Proficiency Bonus", "+3")]
    });
    const resolved = byAttribute(rendered, "data-mechanic-key", "proficiency-bonus");
    assert.equal(resolved.length, 1);
    assert.match(visibleText(resolved[0]), /Proficiency Bonus/);
    assert.match(visibleText(resolved[0]), /\+3/);
    assert.equal(byAttribute(rendered, "data-unimplemented-mechanic", "proficiency").length, 0);
    assert.doesNotMatch(visibleText(rendered), /Proficiency Bonus\s+Not (?:configured|yet configured)/i);
});

test("frontend does not infer Proficiency Bonus when the backend does not supply it", () => {
    const rendered = render("actions", { combatFundamentals: [] });
    assert.doesNotMatch(visibleText(rendered), /Proficiency Bonus/);
    assert.equal(byAttribute(rendered, "data-unimplemented-mechanic", "proficiency").length, 0);
});


test("workspace renders specialized, composite, independent, and unconfigured competencies from backend data", () => {
    const mechanics = {
        competencies: {
            entries: [
                mechanical("skill.stealth", "Stealth", "-", { kind: "skill" }),
                mechanical("skill.hide", "Hide", "-", { kind: "skill", governingAbility: "dexterity" }),
                mechanical("skill.move-silently", "Move Silently", "-", { kind: "skill" }),
                mechanical("skill.knowledge-planes", "Knowledge (the planes)", "-", {
                    kind: "skill",
                    family: "Knowledge",
                    specialty: "the planes",
                    supportsRanks: true,
                    supportsClassSkillState: true,
                    supportsTrainingState: true,
                    trainedOnly: true,
                    armorCheckPenalty: { applies: false }
                }),
                mechanical("skill.perception", "Perception", "-", { kind: "skill" }),
                mechanical("skill.listen", "Listen", "-", { kind: "skill" }),
                mechanical("skill.spot", "Spot", "-", { kind: "skill" })
            ],
            relationships: [{
                parentKey: "skill.stealth",
                componentKeys: ["skill.hide", "skill.move-silently"]
            }]
        }
    };

    const rendered = render("actions", mechanics);
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.stealth")[0].getAttribute("data-skill-role"), "parent");
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.hide")[0].getAttribute("data-skill-role"), "component");
    assert.equal(byAttribute(rendered, "data-skill-id", "skill.move-silently")[0].getAttribute("data-skill-role"), "component");

    const specialty = byAttribute(rendered, "data-skill-id", "skill.knowledge-planes")[0];
    assert.match(visibleText(specialty), /Knowledge \(the planes\)/);
    assert.match(visibleText(specialty), /Family\s+Knowledge/);
    assert.match(visibleText(specialty), /Specialty\s+the planes/);
    assert.match(visibleText(specialty), /Ranks\s+-/);
    assert.match(visibleText(specialty), /Training\s+-/);
    assert.match(visibleText(specialty), /Class skill\s+-/);
    assert.doesNotMatch(visibleText(specialty), /\b0\b/);

    for (const key of ["skill.perception", "skill.listen", "skill.spot"]) {
        assert.equal(byAttribute(rendered, "data-skill-id", key)[0].getAttribute("data-skill-role"), "standalone");
    }
});

test("workspace renders backend-supplied 3.x saving throws, defenses, combat, and nonlethal state without formulas", () => {
    const mechanics = {
        savingThrows: [
            mechanical("save.fortitude", "Fortitude Save", "+7"),
            mechanical("save.reflex", "Reflex Save", "+5"),
            mechanical("save.will", "Will Save", "+6")
        ],
        defenses: {
            primaryKey: "defense.ac",
            values: [
                mechanical("defense.ac.touch", "Touch Armor Class", "13"),
                mechanical("defense.ac.flat-footed", "Flat-Footed Armor Class", "17"),
                mechanical("defense.spell-resistance", "Spell Resistance", "18"),
                mechanical("defense.damage-reduction", "Damage Reduction", "5/magic")
            ]
        },
        combatFundamentals: [
            mechanical("combat.initiative", "Initiative", "+4"),
            mechanical("combat.base-attack-bonus", "Base Attack Bonus", "+6/+1"),
            mechanical("combat.grapple", "Grapple", "+9")
        ],
        healthTracks: [{
            key: "resource.nonlethal-damage",
            label: "Nonlethal Damage",
            role: "nonlethal-damage",
            current: 4
        }]
    };

    const rendered = render("actions", mechanics);
    for (const key of [
        "save.fortitude",
        "save.reflex",
        "save.will",
        "defense.ac.touch",
        "defense.ac.flat-footed",
        "defense.spell-resistance",
        "defense.damage-reduction",
        "combat.initiative",
        "combat.base-attack-bonus",
        "combat.grapple"
    ]) {
        assert.equal(byAttribute(rendered, "data-mechanic-key", key).length, 1, key);
    }
    assert.equal(byAttribute(rendered, "data-health-track-key", "resource.nonlethal-damage").length, 1);
    const initiative = byAttribute(rendered, "data-mechanic-key", "combat.initiative")[0];
    assert.ok(initiative);
    assert.ok(byClass(rendered, "dd-stat--initiative").some(node => walk(node).includes(initiative)));
    assert.match(visibleText(rendered), /Touch Armor Class/);
    assert.match(visibleText(rendered), /Base Attack Bonus/);
    assert.match(visibleText(rendered), /Nonlethal Damage/);
});
