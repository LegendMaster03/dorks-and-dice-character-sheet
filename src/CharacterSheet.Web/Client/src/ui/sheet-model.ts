import {
    CHARACTER_ABILITY_KEYS,
    type BaseAbilityScoreInputResponse,
    type CharacterAbilityKey,
    type CharacterBuilderChoice
} from "../builder-api.js";
import type { CharacterBuilderUiState } from "../app-state.js";
import { getStartingClassEntry, getStoredChoiceConceptKey } from "../builder-rules.js";
import type { RuleReferenceState } from "../builder-rules.js";
import type { CharacterSheetBootstrapResponse } from "../character-api.js";
import type { CharacterMechanicsView } from "./character-mechanics.js";

export type SheetSection = "actions" | "spells" | "inventory" | "features" | "details" | "notes";

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
        emptyTitle: "No items yet",
        emptyMessage: "Items added to this Character appear here as separate inventory entries."
    },
    {
        id: "features",
        label: "Features & Traits",
        emptyTitle: "No feats yet",
        emptyMessage: "Added feats appear here. Other granted features are not available in the sheet yet."
    },
    {
        id: "details",
        label: "Background",
        emptyTitle: "No Background details yet",
        emptyMessage: "Background, identity, and Character-authored profile details appear here."
    },
    {
        id: "notes",
        label: "Notes",
        emptyTitle: "No notes yet",
        emptyMessage: "Notes you add here are saved with this Character."
    }
];

export type GuidedBuilderSection = "class" | "background" | "species" | "abilities" | "review";
export type GuidedBuilderChoiceOwnerSection = "class" | "background" | "species";

export interface GuidedBuilderSectionDefinition {
    id: GuidedBuilderSection;
    label: string;
}

export const GUIDED_BUILDER_SECTIONS: readonly GuidedBuilderSectionDefinition[] = [
    { id: "class", label: "Class" },
    { id: "background", label: "Background" },
    { id: "species", label: "Species" },
    { id: "abilities", label: "Abilities" },
    { id: "review", label: "Review" }
];

export type GuidedBuilderSectionStatus = "resolved" | "incomplete" | "available" | "unavailable";

export interface GuidedBuilderSectionState extends GuidedBuilderSectionDefinition {
    status: GuidedBuilderSectionStatus;
    detail: string;
}

export function getGuidedBuilderChoiceSourceKeys(
    builder: CharacterBuilderUiState,
    section: GuidedBuilderChoiceOwnerSection
): readonly string[] {
    if (builder.status !== "ready" || builder.build === null) {
        return [];
    }

    const keys = new Set<string>();
    if (section === "class") {
        const startingClass = getStartingClassEntry(builder.build);
        if (startingClass === null) {
            return [];
        }
        keys.add(startingClass.ruleConceptKey);
        for (const entry of builder.build.progressionEntries) {
            if (entry.kind === "subclass" && entry.parentAdvancementEntryId === startingClass.id) {
                keys.add(entry.ruleConceptKey);
            }
        }
        return [...keys];
    }

    const targets: readonly CharacterBuilderChoice[] = section === "background"
        ? ["background"]
        : ["species", "subspecies"];
    for (const target of targets) {
        const conceptKey = getStoredChoiceConceptKey(builder.build, target);
        if (conceptKey !== null) {
            keys.add(conceptKey);
        }
    }
    return [...keys];
}

export function getGuidedBuilderOwnedChoiceSourceKeys(
    builder: CharacterBuilderUiState
): readonly string[] {
    return [...new Set([
        ...getGuidedBuilderChoiceSourceKeys(builder, "class"),
        ...getGuidedBuilderChoiceSourceKeys(builder, "background"),
        ...getGuidedBuilderChoiceSourceKeys(builder, "species")
    ])];
}

export function getGuidedBuilderSectionStates(
    builder: CharacterBuilderUiState,
    mechanics: CharacterMechanicsView | null = null
): readonly GuidedBuilderSectionState[] {
    if (builder.status !== "ready" || builder.build === null) {
        return GUIDED_BUILDER_SECTIONS.map(section => ({
            ...section,
            status: "unavailable",
            detail: "Character setup is not available."
        }));
    }

    const speciesSelected = getStoredChoiceConceptKey(builder.build, "species") !== null;
    const backgroundSelected = getStoredChoiceConceptKey(builder.build, "background") !== null;
    const startingClassSelected = getStartingClassEntry(builder.build) !== null;
    const classSourceKeys = new Set(getGuidedBuilderChoiceSourceKeys(builder, "class"));
    const backgroundSourceKeys = new Set(getGuidedBuilderChoiceSourceKeys(builder, "background"));
    const speciesSourceKeys = new Set(getGuidedBuilderChoiceSourceKeys(builder, "species"));
    const ownedSourceKeys = new Set(getGuidedBuilderOwnedChoiceSourceKeys(builder));
    const ruleChoices = mechanics?.ruleChoices ?? [];

    const pendingClassChoices = ruleChoices.filter(choice =>
        choice.state !== "resolved"
        && choice.sourceConceptKey !== undefined
        && classSourceKeys.has(choice.sourceConceptKey));
    const pendingBackgroundChoices = ruleChoices.filter(choice =>
        choice.state !== "resolved"
        && choice.sourceConceptKey !== undefined
        && backgroundSourceKeys.has(choice.sourceConceptKey));
    const pendingSpeciesChoices = ruleChoices.filter(choice =>
        choice.state !== "resolved"
        && choice.sourceConceptKey !== undefined
        && speciesSourceKeys.has(choice.sourceConceptKey));

    const configuredAbilities = new Set(
        builder.build.baseAbilityScoreInputs.map(input => input.abilityKey)
    ).size;
    const allBaseAbilitiesConfigured = configuredAbilities === CHARACTER_ABILITY_KEYS.length;
    const pendingAbilityChoices = ruleChoices.filter(choice =>
        (choice.kind === "ability-score" || choice.kind === "ability-score-set")
        && choice.state !== "resolved"
        && (choice.sourceConceptKey === undefined || !ownedSourceKeys.has(choice.sourceConceptKey)));

    const classResolved = startingClassSelected && pendingClassChoices.length === 0;
    const backgroundResolved = backgroundSelected && pendingBackgroundChoices.length === 0;
    const speciesResolved = speciesSelected && pendingSpeciesChoices.length === 0;
    const abilitiesResolved = allBaseAbilitiesConfigured && pendingAbilityChoices.length === 0;

    const classDetail = guidedSelectionDetail(
        "Starting Class",
        startingClassSelected,
        pendingClassChoices.length,
        "Class");
    const backgroundDetail = guidedSelectionDetail(
        "Background",
        backgroundSelected,
        pendingBackgroundChoices.length,
        "Background");
    const speciesDetail = guidedSelectionDetail(
        "Species",
        speciesSelected,
        pendingSpeciesChoices.length,
        "Species");

    let abilityDetail: string;
    if (!allBaseAbilitiesConfigured) {
        abilityDetail = `${configuredAbilities} of ${CHARACTER_ABILITY_KEYS.length} base Ability Score inputs are configured.`;
    } else if (pendingAbilityChoices.length > 0) {
        abilityDetail = pendingAbilityChoices.length === 1
            ? "1 general Ability Score choice remains."
            : `${pendingAbilityChoices.length} general Ability Score choices remain.`;
    } else {
        abilityDetail = "All base Ability Scores and general Ability Score choices are configured.";
    }

    return [
        {
            id: "class",
            label: "Class",
            status: classResolved ? "resolved" : "incomplete",
            detail: classDetail
        },
        {
            id: "background",
            label: "Background",
            status: backgroundResolved ? "resolved" : "incomplete",
            detail: backgroundDetail
        },
        {
            id: "species",
            label: "Species",
            status: speciesResolved ? "resolved" : "incomplete",
            detail: speciesDetail
        },
        {
            id: "abilities",
            label: "Abilities",
            status: abilitiesResolved ? "resolved" : "incomplete",
            detail: abilityDetail
        },
        {
            id: "review",
            label: "Review",
            status: "available",
            detail: "Review the Character setup currently available in the sheet."
        }
    ];
}

function guidedSelectionDetail(
    selectionLabel: string,
    selected: boolean,
    pendingChoiceCount: number,
    choiceLabel: string
): string {
    if (!selected) {
        return `No ${selectionLabel} is selected.`;
    }
    if (pendingChoiceCount === 1) {
        return `${selectionLabel} selected. 1 required ${choiceLabel} choice remains.`;
    }
    if (pendingChoiceCount > 1) {
        return `${selectionLabel} selected. ${pendingChoiceCount} required ${choiceLabel} choices remain.`;
    }
    return `${selectionLabel} and current ${choiceLabel} choices are configured.`;
}

export interface MechanicPlaceholderDefinition {
    id: string;
    label: string;
    message: string;
    group: "quick" | "support" | "combat";
}

export const MECHANIC_PLACEHOLDERS: readonly MechanicPlaceholderDefinition[] = [
    { id: "movement", label: "Movement", message: "Speed is not available yet.", group: "quick" },
    { id: "skills", label: "Skills", message: "Combined skill values are not available yet.", group: "support" },
    { id: "armor-class", label: "Armor Class", message: "Armor Class is not available yet.", group: "combat" },
    { id: "initiative", label: "Initiative", message: "Initiative is not available yet.", group: "combat" },
    { id: "hit-points", label: "Hit Points", message: "Hit points are not available yet.", group: "combat" },
    { id: "hit-dice", label: "Hit Dice", message: "Hit dice are not available yet.", group: "combat" }
];

export interface AbilityScoreDefinition {
    key: CharacterAbilityKey;
    label: string;
}

const ABILITY_LABELS: Record<CharacterAbilityKey, string> = {
    strength: "Strength",
    dexterity: "Dexterity",
    constitution: "Constitution",
    intelligence: "Intelligence",
    wisdom: "Wisdom",
    charisma: "Charisma"
};

export const ABILITY_SCORE_DEFINITIONS: readonly AbilityScoreDefinition[] =
    CHARACTER_ABILITY_KEYS.map(key => ({ key, label: ABILITY_LABELS[key] }));

export type BaseAbilityScoreDisplayStatus = "loading" | "unavailable" | "unconfigured" | "configured";

export interface BaseAbilityScoreDisplay {
    status: BaseAbilityScoreDisplayStatus;
    value: string;
    detail: string;
    score: number | null;
}

export interface AbilityScoreActionPolicy {
    canSave: boolean;
    canClear: boolean;
    saveLabel: "Set" | "Replace";
}

export function hasPendingBuildMutation(builder: CharacterBuilderUiState): boolean {
    return builder.saving !== null
        || builder.savingAbility !== null
        || builder.savingAdvancementLevel != null
        || builder.savingFeat !== null;
}

export function getBaseAbilityScoreInput(
    builder: CharacterBuilderUiState,
    abilityKey: CharacterAbilityKey
): BaseAbilityScoreInputResponse | null {
    return builder.build?.baseAbilityScoreInputs.find(value => value.abilityKey === abilityKey) ?? null;
}

export function getBaseAbilityScoreDisplay(
    builder: CharacterBuilderUiState,
    abilityKey: CharacterAbilityKey
): BaseAbilityScoreDisplay {
    if (builder.status === "idle" || builder.status === "loading") {
        return { status: "loading", value: "Loading…", detail: "Base Score", score: null };
    }
    if (builder.status === "error" || builder.build === null) {
        return { status: "unavailable", value: "Unavailable", detail: "Base Score unavailable", score: null };
    }
    const input = getBaseAbilityScoreInput(builder, abilityKey);
    if (input === null) {
        return { status: "unconfigured", value: "-", detail: "Base Score", score: null };
    }
    return { status: "configured", value: String(input.score), detail: "Base Score", score: input.score };
}

export function getAbilityScoreActionPolicy(
    builder: CharacterBuilderUiState,
    readOnly: boolean,
    configured: boolean
): AbilityScoreActionPolicy {
    const canMutate =
        builder.status === "ready"
        && builder.build !== null
        && !builder.build.readOnly
        && !readOnly
        && !hasPendingBuildMutation(builder);
    return {
        canSave: canMutate,
        canClear: canMutate && configured,
        saveLabel: configured ? "Replace" : "Set"
    };
}

export type ParsedBaseAbilityScoreInput =
    | { ok: true; score: number }
    | { ok: false; message: string };

const BACKEND_INT32_MIN = -2147483648;
const BACKEND_INT32_MAX = 2147483647;

export function parseBaseAbilityScoreInput(value: string): ParsedBaseAbilityScoreInput {
    const normalized = value.trim();
    if (!/^-?\d+$/.test(normalized)) {
        return { ok: false, message: "Enter a whole-number base score." };
    }
    const score = Number(normalized);
    if (!Number.isSafeInteger(score) || score < BACKEND_INT32_MIN || score > BACKEND_INT32_MAX) {
        return { ok: false, message: "Enter an integer that can be represented by the Character Sheet API." };
    }
    return { ok: true, score };
}

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
                value: "Saved selection could not be loaded",
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
    species: RuleReferenceDisplay;
    subspecies: RuleReferenceDisplay;
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
        campaignContext: formatCampaignContext(character),
        species: builderReferenceDisplay(builder, "species"),
        subspecies: builderReferenceDisplay(builder, "subspecies"),
        startingClass: builderReferenceDisplay(builder, "startingClass"),
        subclass: builderReferenceDisplay(builder, "subclass")
    };
}

function formatCampaignContext(character: CharacterSheetBootstrapResponse): string | null {
    const namedCampaigns = (character.campaigns ?? [])
        .filter(value => value.name.trim().length > 0)
        .map(value => value.name.trim());
    const names = [...new Set(namedCampaigns)];
    if (names.length === 1) return names[0]!;
    if (names.length > 1) return names.join(" • ");
    if (character.campaignIds.length === 0) return null;
    return character.campaignIds.length === 1
        ? "1 Campaign association"
        : `${character.campaignIds.length} Campaign associations`;
}

export function builderReferenceDisplay(
    builder: CharacterBuilderUiState,
    target: CharacterBuilderChoice
): RuleReferenceDisplay {
    if (builder.status === "idle" && builder.build === null) {
        return { value: "Character Sheet not set up", tone: "empty" };
    }
    if (builder.status === "loading") {
        return { value: "Loading Character setup", tone: "loading" };
    }
    if (builder.status === "error") {
        return {
            value: "Character setup unavailable",
            detail: builder.message,
            tone: "error"
        };
    }
    return toRuleReferenceDisplay(builder.references[target] ?? { status: "none" });
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
    mutationPending: boolean
): ChoiceActionPolicy {
    return {
        canChoose: available && !readOnly && !mutationPending,
        canClear: available && !readOnly && !mutationPending && reference.status !== "none",
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
