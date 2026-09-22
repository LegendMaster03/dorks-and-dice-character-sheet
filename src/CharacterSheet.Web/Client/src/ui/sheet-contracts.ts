import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState
} from "../app-state.js";
import type { CharacterAbilityKey } from "../builder-api.js";
import type {
    CharacterConditionStateInput,
    CharacterInventoryItemOccurrenceStateInput
} from "../character-state-api.js";
import type { RestKind } from "../features/health/health.js";
import type { CharacterBuilderHandlers } from "./builder.js";
import type { GuidedBuilderSection, SheetSection } from "./sheet-model.js";

export interface StructuralCharacterHandlers extends CharacterBuilderHandlers {
    setAdvancementLevel(occurrenceId: string, level: number): void;
    setBaseAbilityScore(abilityKey: CharacterAbilityKey, score: number): void;
    clearBaseAbilityScore(abilityKey: CharacterAbilityKey): void;
}

export interface RoutineCharacterHandlers {
    setCurrentHitPoints(currentHitPoints: number | null): void;
    setDeathSaves(successes: number, failures: number): void;
    addNote(content: string): void;
    updateNote(noteId: string, content: string): void;
    deleteNote(noteId: string): void;
    openInventoryChooser(): void;
    closeInventoryChooser(): void;
    searchInventory(query: string): void;
    addInventoryItem(conceptKey: string): void;
    updateInventoryItem(
        occurrenceId: string,
        input: CharacterInventoryItemOccurrenceStateInput
    ): void;
    removeInventoryItem(occurrenceId: string): void;
    openConditionChooser(): void;
    closeConditionChooser(): void;
    searchConditions(query: string): void;
    addRuleCondition(conceptKey: string, input: CharacterConditionStateInput): void;
    addCustomCondition(customName: string, input: CharacterConditionStateInput): void;
    updateCondition(conditionId: string, input: CharacterConditionStateInput): void;
    removeCondition(conditionId: string): void;
    rest?(kind: RestKind): void;
}

export interface RulesInputCharacterHandlers {
    setChoice(choiceKey: string, value: string): void;
    clearChoice(choiceKey: string): void;
    setResource(resourceKey: string, currentValue: number): void;
}

export interface FeatCharacterHandlers {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): void;
    add(conceptKey: string): void;
    remove(occurrenceId: string): void;
}

export interface CharacterSheetHandlers {
    structural: StructuralCharacterHandlers;
    feats: FeatCharacterHandlers;
    rules: RulesInputCharacterHandlers;
    routine: RoutineCharacterHandlers;
    selectSection(section: SheetSection): void;
    enterEditMode(): void;
    leaveEditMode(): void;
    openGuidedBuilder(): void;
    closeGuidedBuilder(): void;
    selectGuidedBuilderSection(section: GuidedBuilderSection): void;
}

export type CharacterSheetBuilderState = CharacterBuilderUiState;
export type CharacterSheetRoutineState = CharacterRoutineUiState;
