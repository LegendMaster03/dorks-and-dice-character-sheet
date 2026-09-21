import type {
    CharacterBuilderUiState,
    CharacterRoutineUiState
} from "../app-state.js";
import type { CharacterAbilityKey } from "../builder-api.js";
import type { RestKind } from "../features/health/health.js";
import type { CharacterBuilderHandlers } from "./builder.js";
import type { GuidedBuilderSection, SheetSection } from "./sheet-model.js";

export interface StructuralCharacterHandlers extends CharacterBuilderHandlers {
    setBaseAbilityScore(abilityKey: CharacterAbilityKey, score: number): void;
    clearBaseAbilityScore(abilityKey: CharacterAbilityKey): void;
}

export interface RoutineCharacterHandlers {
    setCurrentHitPoints(currentHitPoints: number | null): void;
    addNote(content: string): void;
    updateNote(noteId: string, content: string): void;
    deleteNote(noteId: string): void;
    openInventoryChooser(): void;
    closeInventoryChooser(): void;
    searchInventory(query: string): void;
    addInventoryItem(conceptKey: string): void;
    removeInventoryItem(occurrenceId: string): void;
    rest?(kind: RestKind): void;
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
