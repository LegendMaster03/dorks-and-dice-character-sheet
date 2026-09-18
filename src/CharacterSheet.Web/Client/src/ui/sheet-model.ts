import type { CharacterBuilderChoice } from "../builder-api.js";
import type { CharacterBuilderUiState } from "../app-state.js";
import type { RuleReferenceState } from "../builder-rules.js";
import type { CharacterSheetBootstrapResponse } from "../character-api.js";

export type SheetSection = "actions" | "spells" | "inventory" | "features" | "notes";

export interface SheetSectionDefinition {
    id: SheetSection;
    label: string;
    emptyTitle: string;
    emptyMessage: string;
}

export const SHEET_SECTIONS: readonly SheetSectionDefinition[] = [
    {
        id: "actions",
        label: "Actions",
        emptyTitle: "Actions are not available yet",
        emptyMessage: "Attack, action, bonus action, reaction, and other action mechanics will appear here when Character calculations are implemented."
    },
    {
        id: "spells",
        label: "Spells",
        emptyTitle: "Spellcasting is not available yet",
        emptyMessage: "Known, prepared, slotted, or point-based spellcasting will be presented here when Character spell state is implemented."
    },
    {
        id: "inventory",
        label: "Inventory",
        emptyTitle: "Inventory is not available yet",
        emptyMessage: "Equipment, carried items, currency, and related Character-owned state will appear here when implemented."
    },
    {
        id: "features",
        label: "Features & Traits",
        emptyTitle: "Features and traits are not available yet",
        emptyMessage: "Class, Subclass, Prestige Class, Feat, species, and other resolved features will appear here as those mechanics become available."
    },
    {
        id: "notes",
        label: "Notes",
        emptyTitle: "Notes are not available yet",
        emptyMessage: "Character notes and future Campaign-scoped modules will appear here when Character-owned note state is implemented."
    }
];

export interface MechanicPlaceholderDefinition {
    id: string;
    label: string;
    message: string;
    group: "ability" | "quick" | "support" | "combat";
}

export const MECHANIC_PLACEHOLDERS: readonly MechanicPlaceholderDefinition[] = [
    { id: "strength", label: "Strength", message: "Ability scores are not available yet.", group: "ability" },
    { id: "dexterity", label: "Dexterity", message: "Ability scores are not available yet.", group: "ability" },
    { id: "constitution", label: "Constitution", message: "Ability scores are not available yet.", group: "ability" },
    { id: "intelligence", label: "Intelligence", message: "Ability scores are not available yet.", group: "ability" },
    { id: "wisdom", label: "Wisdom", message: "Ability scores are not available yet.", group: "ability" },
    { id: "charisma", label: "Charisma", message: "Ability scores are not available yet.", group: "ability" },
    { id: "proficiency", label: "Proficiency Bonus", message: "Not yet configured.", group: "quick" },
    { id: "movement", label: "Movement", message: "Speed is not available yet.", group: "quick" },
    { id: "saving-throws", label: "Saving Throws", message: "Saving throw modifiers are not available yet.", group: "support" },
    { id: "skills", label: "Skills", message: "Combined skill values are not available yet.", group: "support" },
    { id: "passive-values", label: "Passive Values", message: "Passive values are not available yet.", group: "support" },
    { id: "training", label: "Proficiencies & Training", message: "Armor, weapon, tool, language, and other training data is not available yet.", group: "support" },
    { id: "armor-class", label: "Armor Class", message: "Armor Class is not available yet.", group: "combat" },
    { id: "initiative", label: "Initiative", message: "Initiative is not available yet.", group: "combat" },
    { id: "hit-points", label: "Hit Points", message: "Hit points are not available yet.", group: "combat" },
    { id: "hit-dice", label: "Hit Dice", message: "Hit dice are not available yet.", group: "combat" }
];

export type ReferenceTone = "empty" | "loading" | "resolved" | "unavailable" | "error";

export interface RuleReferenceDisplay {
    value: string;
    detail?: string;
    tone: ReferenceTone;
}

export function toRuleReferenceDisplay(reference: RuleReferenceState): RuleReferenceDisplay {
    switch (reference.status) {
        case "none":
            return { value: "Not selected", tone: "empty" };
        case "loading":
            return { value: "Resolving selection", detail: reference.conceptKey, tone: "loading" };
        case "resolved": {
            const metadata = [
                reference.rule.editionDisplayName,
                reference.rule.sourceCode,
                reference.rule.packageDisplayName
            ].map(value => value.trim()).filter(value => value.length > 0);
            return {
                value: reference.rule.displayName,
                detail: metadata.length > 0 ? metadata.join(" • ") : undefined,
                tone: "resolved"
            };
        }
        case "unavailable":
            return {
                value: "Unavailable saved selection",
                detail: reference.conceptKey,
                tone: "unavailable"
            };
        case "error":
            return {
                value: "Rules Core resolution failed",
                detail: `${reference.conceptKey} • ${reference.message}`,
                tone: "error"
            };
    }
}

export interface CharacterHeaderModel {
    name: string;
    lifecycleLabel: string;
    readOnly: boolean;
    builderStatus: string | null;
    campaignContext: string | null;
    raceSpecies: RuleReferenceDisplay;
    startingClass: RuleReferenceDisplay;
    subclass: RuleReferenceDisplay;
}

export function createCharacterHeaderModel(
    character: CharacterSheetBootstrapResponse,
    builder: CharacterBuilderUiState,
    forceReadOnly: boolean
): CharacterHeaderModel {
    const readOnly = forceReadOnly || builder.build?.readOnly === true || character.lifecycle === "Archived";
    return {
        name: character.name,
        lifecycleLabel: character.lifecycle === "Archived" ? "Archived" : "Active Character",
        readOnly,
        builderStatus: builder.build?.builderStatus ?? character.sheet?.builderStatus ?? null,
        campaignContext: character.campaignIds.length > 0
            ? `${character.campaignIds.length} Campaign ${character.campaignIds.length === 1 ? "association" : "associations"}`
            : null,
        raceSpecies: headerReferenceDisplay(builder, "raceSpecies"),
        startingClass: headerReferenceDisplay(builder, "startingClass"),
        subclass: headerReferenceDisplay(builder, "subclass")
    };
}

function headerReferenceDisplay(
    builder: CharacterBuilderUiState,
    target: CharacterBuilderChoice
): RuleReferenceDisplay {
    if (builder.status === "idle" && builder.build === null) {
        return { value: "Digital sheet not initialized", tone: "empty" };
    }
    if (builder.status === "loading") {
        return { value: "Loading build state", tone: "loading" };
    }
    if (builder.status === "error") {
        return {
            value: "Build state unavailable",
            detail: builder.message,
            tone: "error"
        };
    }
    return toRuleReferenceDisplay(builder.references[target]);
}

export interface ChoiceActionPolicy {
    canChoose: boolean;
    canClear: boolean;
    chooseLabel: "Choose" | "Replace";
}

export function getChoiceActionPolicy(
    reference: RuleReferenceState,
    readOnly: boolean,
    available: boolean,
    saving: CharacterBuilderChoice | null
): ChoiceActionPolicy {
    return {
        canChoose: available && !readOnly && saving === null,
        canClear: available && !readOnly && saving === null && reference.status !== "none",
        chooseLabel: reference.status === "none" ? "Choose" : "Replace"
    };
}

export function humanizeBuilderStatus(value: string | null): string | null {
    if (value === null || value.trim().length === 0) return null;
    return value
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/[_-]+/g, " ")
        .replace(/^./, first => first.toUpperCase());
}
