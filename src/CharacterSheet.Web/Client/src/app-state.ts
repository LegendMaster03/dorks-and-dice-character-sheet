import type {
    CharacterAbilityKey,
    CharacterBuildResponse,
    CharacterBuilderChoice
} from "./builder-api.js";
import type { CharacterSheetBootstrapResponse } from "./character-api.js";
import type { CharacterPresentationResponse } from "./character-presentation-api.js";
import type { CharacterStateResponse } from "./character-state-api.js";
import type { RuleReferenceState } from "./builder-rules.js";
import type { ResolvedRuleCatalogItem } from "./rules-core-api.js";
import type { CharacterSheetRoute } from "./routes.js";
import type { GuidedBuilderSection, SheetSection } from "./ui/sheet-model.js";
import {
    createInitialBuilderState,
    reduceBuilderState
} from "./state/builder-state.js";
import {
    createInitialRoutineState,
    reduceRoutineState
} from "./state/routine-state.js";
import {
    createInitialPresentationState,
    reducePresentationState
} from "./state/presentation-state.js";

export type CharacterSheetScreen =
    | { kind: "new-character"; status: "ready" | "error"; message?: string; recoveryCharacterId?: string }
    | { kind: "loading" }
    | { kind: "submitting"; operation: "new-character" | "initialize-sheet"; character?: CharacterSheetBootstrapResponse }
    | { kind: "basic-character"; character: CharacterSheetBootstrapResponse }
    | { kind: "rich-character"; character: CharacterSheetBootstrapResponse }
    | { kind: "archived"; character: CharacterSheetBootstrapResponse }
    | { kind: "not-found" }
    | { kind: "error"; message: string }
    | { kind: "invalid"; route: string };

export type RuleChooserState =
    | { kind: "closed" }
    | {
        kind: "open";
        target: CharacterBuilderChoice;
        query: string;
        status: "idle" | "loading" | "ready" | "error";
        results: ResolvedRuleCatalogItem[];
        message?: string;
    };

export type FeatChooserState =
    | { kind: "closed" }
    | {
        kind: "open";
        query: string;
        status: "idle" | "loading" | "ready" | "error";
        results: ResolvedRuleCatalogItem[];
        message?: string;
    };

export interface CharacterBuilderUiState {
    status: "idle" | "loading" | "ready" | "error";
    build: CharacterBuildResponse | null;
    message?: string;
    references: Record<CharacterBuilderChoice, RuleReferenceState>;
    chooser: RuleChooserState;
    saving: CharacterBuilderChoice | null;
    saveError?: string;
    savingAbility: CharacterAbilityKey | null;
    savingAdvancementLevel: string | null;
    advancementLevelSaveError?: {
        occurrenceId: string;
        message: string;
    };
    abilitySaveError?: {
        abilityKey: CharacterAbilityKey;
        message: string;
    };
    featReferences: Record<string, RuleReferenceState>;
    featChooser: FeatChooserState;
    savingFeat: "add" | string | null;
    featSaveError?: string;
}

export type SheetMode = "view" | "edit";

export interface GuidedBuilderUiState {
    open: boolean;
    activeSection: GuidedBuilderSection;
    returnSheetMode: SheetMode;
}

export type RoutineMutationKind =
    | "health-update"
    | "death-saves-update"
    | "note-add"
    | "note-update"
    | "note-delete"
    | "inventory-add"
    | "inventory-update"
    | "inventory-delete"
    | "rules-input-update"
    | "rules-input-delete"
    | "hit-point-gain-update"
    | "hit-point-gain-delete"
    | "condition-add"
    | "condition-update"
    | "condition-delete";

export type InventoryChooserState =
    | { kind: "closed" }
    | {
        kind: "open";
        query: string;
        status: "idle" | "loading" | "ready" | "error";
        results: ResolvedRuleCatalogItem[];
        message?: string;
    };

export type ConditionChooserState =
    | { kind: "closed" }
    | {
        kind: "open";
        query: string;
        status: "idle" | "loading" | "ready" | "error";
        results: ResolvedRuleCatalogItem[];
        message?: string;
    };

export interface CharacterRoutineUiState {
    status: "idle" | "loading" | "ready" | "error";
    state: CharacterStateResponse | null;
    message?: string;
    references: Record<string, RuleReferenceState>;
    inventoryChooser: InventoryChooserState;
    conditionChooser: ConditionChooserState;
    mutation: { kind: RoutineMutationKind; entryId?: string } | null;
    mutationError?: string;
}

export interface CharacterPresentationUiState {
    status: "idle" | "loading" | "ready" | "error";
    requestId: number;
    advancement: CharacterPresentationResponse["advancement"] | null;
    mechanics: CharacterPresentationResponse["mechanics"];
    message?: string;
}

export interface CharacterSheetAppState {
    route: CharacterSheetRoute;
    screen: CharacterSheetScreen;
    builder: CharacterBuilderUiState;
    routine: CharacterRoutineUiState;
    presentation: CharacterPresentationUiState;
    activeSheetSection: SheetSection;
    sheetMode: SheetMode;
    guidedBuilder: GuidedBuilderUiState;
    renderRevision: number;
}

export type CharacterSheetAction =
    | { type: "character-loaded"; character: CharacterSheetBootstrapResponse | null }
    | { type: "load-failed"; message: string }
    | { type: "new-submit-started" }
    | { type: "new-submit-failed"; message: string; recoveryCharacterId?: string }
    | { type: "sheet-submit-started"; character: CharacterSheetBootstrapResponse }
    | { type: "builder-load-started" }
    | { type: "builder-loaded"; build: CharacterBuildResponse }
    | { type: "builder-load-failed"; message: string }
    | { type: "rule-reference-resolved"; target: CharacterBuilderChoice; conceptKey: string; reference: RuleReferenceState }
    | { type: "chooser-opened"; target: CharacterBuilderChoice }
    | { type: "chooser-query-changed"; query: string }
    | { type: "chooser-load-started"; target: CharacterBuilderChoice; query: string }
    | { type: "chooser-loaded"; target: CharacterBuilderChoice; query: string; results: ResolvedRuleCatalogItem[] }
    | { type: "chooser-load-failed"; target: CharacterBuilderChoice; query: string; message: string }
    | { type: "chooser-closed" }
    | { type: "selection-save-started"; target: CharacterBuilderChoice }
    | { type: "selection-saved"; build: CharacterBuildResponse }
    | { type: "selection-save-failed"; message: string }
    | { type: "ability-save-started"; abilityKey: CharacterAbilityKey }
    | { type: "ability-saved"; build: CharacterBuildResponse }
    | { type: "ability-save-failed"; abilityKey: CharacterAbilityKey; message: string }
    | { type: "advancement-level-save-started"; occurrenceId: string }
    | { type: "advancement-level-saved"; build: CharacterBuildResponse }
    | { type: "advancement-level-save-failed"; occurrenceId: string; message: string }
    | { type: "feat-reference-resolved"; occurrenceId: string; conceptKey: string; reference: RuleReferenceState }
    | { type: "feat-chooser-opened" }
    | { type: "feat-chooser-query-changed"; query: string }
    | { type: "feat-chooser-load-started"; query: string }
    | { type: "feat-chooser-loaded"; query: string; results: ResolvedRuleCatalogItem[] }
    | { type: "feat-chooser-load-failed"; query: string; message: string }
    | { type: "feat-chooser-closed" }
    | { type: "feat-save-started"; occurrenceId?: string }
    | { type: "feat-saved"; build: CharacterBuildResponse }
    | { type: "feat-save-failed"; message: string }
    | { type: "routine-load-started" }
    | { type: "routine-loaded"; state: CharacterStateResponse }
    | { type: "routine-load-failed"; message: string }
    | { type: "routine-reference-resolved"; occurrenceId: string; conceptKey: string; reference: RuleReferenceState }
    | { type: "inventory-chooser-opened" }
    | { type: "inventory-chooser-query-changed"; query: string }
    | { type: "inventory-chooser-load-started"; query: string }
    | { type: "inventory-chooser-loaded"; query: string; results: ResolvedRuleCatalogItem[] }
    | { type: "inventory-chooser-load-failed"; query: string; message: string }
    | { type: "inventory-chooser-closed" }
    | { type: "condition-chooser-opened" }
    | { type: "condition-chooser-query-changed"; query: string }
    | { type: "condition-chooser-load-started"; query: string }
    | { type: "condition-chooser-loaded"; query: string; results: ResolvedRuleCatalogItem[] }
    | { type: "condition-chooser-load-failed"; query: string; message: string }
    | { type: "condition-chooser-closed" }
    | { type: "routine-mutation-started"; kind: RoutineMutationKind; entryId?: string }
    | { type: "routine-mutation-succeeded"; state: CharacterStateResponse }
    | { type: "routine-mutation-failed"; message: string }
    | { type: "presentation-load-started"; requestId: number }
    | { type: "presentation-loaded"; requestId: number; presentation: CharacterPresentationResponse }
    | { type: "presentation-load-failed"; requestId: number; message: string }
    | { type: "sheet-edit-entered" }
    | { type: "sheet-edit-exited" }
    | { type: "guided-builder-opened" }
    | { type: "guided-builder-closed" }
    | { type: "guided-builder-section-selected"; section: GuidedBuilderSection }
    | { type: "sheet-section-selected"; section: SheetSection }
    | { type: "rerender" };

export function createInitialState(route: CharacterSheetRoute): CharacterSheetAppState {
    let screen: CharacterSheetScreen;
    switch (route.kind) {
        case "new":
            screen = { kind: "new-character", status: "ready" };
            break;
        case "character":
            screen = { kind: "loading" };
            break;
        case "invalid":
            screen = { kind: "invalid", route: route.route };
            break;
    }

    return {
        route,
        screen,
        builder: createInitialBuilderState(),
        routine: createInitialRoutineState(),
        presentation: createInitialPresentationState(),
        activeSheetSection: "actions",
        sheetMode: "view",
        guidedBuilder: createInitialGuidedBuilderState(),
        renderRevision: 0
    };
}

export function reduceAppState(
    state: CharacterSheetAppState,
    action: CharacterSheetAction
): CharacterSheetAppState {
    let screen = state.screen;
    let builder = reduceBuilderState(state.builder, action);
    let routine = reduceRoutineState(state.routine, action);
    let presentation = reducePresentationState(state.presentation, action);
    let activeSheetSection = state.activeSheetSection;
    let sheetMode = state.sheetMode;
    let guidedBuilder = state.guidedBuilder;

    switch (action.type) {
        case "character-loaded":
            builder = createInitialBuilderState();
            routine = createInitialRoutineState();
            presentation = createInitialPresentationState();
            activeSheetSection = "actions";
            sheetMode = "view";
            guidedBuilder = createInitialGuidedBuilderState();
            if (action.character === null) {
                screen = { kind: "not-found" };
            } else if (action.character.lifecycle === "Archived") {
                screen = { kind: "archived", character: action.character };
            } else if (action.character.hasRichSheet) {
                screen = { kind: "rich-character", character: action.character };
            } else {
                screen = { kind: "basic-character", character: action.character };
            }
            break;
        case "load-failed":
            screen = { kind: "error", message: action.message };
            builder = createInitialBuilderState();
            routine = createInitialRoutineState();
            presentation = createInitialPresentationState();
            sheetMode = "view";
            guidedBuilder = createInitialGuidedBuilderState();
            break;
        case "new-submit-started":
            screen = { kind: "submitting", operation: "new-character" };
            break;
        case "new-submit-failed":
            screen = {
                kind: "new-character",
                status: "error",
                message: action.message,
                recoveryCharacterId: action.recoveryCharacterId
            };
            break;
        case "sheet-submit-started":
            screen = { kind: "submitting", operation: "initialize-sheet", character: action.character };
            break;

        case "builder-loaded":
            if (action.build.readOnly) {
                sheetMode = "view";
                guidedBuilder = createInitialGuidedBuilderState();
            }
            break;
        case "sheet-edit-entered":
            if (canEditStructuralConfiguration(screen, builder)) {
                sheetMode = "edit";
                guidedBuilder = { ...guidedBuilder, open: false, returnSheetMode: "view" };
                builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            }
            break;
        case "sheet-edit-exited":
            sheetMode = "view";
            builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            break;
        case "guided-builder-opened":
            if (canEditStructuralConfiguration(screen, builder)) {
                guidedBuilder = {
                    ...guidedBuilder,
                    open: true,
                    returnSheetMode: sheetMode
                };
                sheetMode = "view";
                builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            }
            break;
        case "guided-builder-closed":
            if (guidedBuilder.open) {
                const returnSheetMode = guidedBuilder.returnSheetMode;
                guidedBuilder = {
                    ...guidedBuilder,
                    open: false,
                    returnSheetMode: "view"
                };
                sheetMode = canEditStructuralConfiguration(screen, builder)
                    ? returnSheetMode
                    : "view";
                builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            }
            break;
        case "guided-builder-section-selected":
            if (guidedBuilder.open) {
                guidedBuilder = { ...guidedBuilder, activeSection: action.section };
                builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            }
            break;
        case "sheet-section-selected":
            activeSheetSection = action.section;
            break;
        case "rerender":
            break;

    }

    return {
        ...state,
        screen,
        builder,
        routine,
        presentation,
        activeSheetSection,
        sheetMode,
        guidedBuilder,
        renderRevision: state.renderRevision + 1
    };
}

function createInitialGuidedBuilderState(): GuidedBuilderUiState {
    return {
        open: false,
        activeSection: "species",
        returnSheetMode: "view"
    };
}

function canEditStructuralConfiguration(
    screen: CharacterSheetScreen,
    builder: CharacterBuilderUiState
): boolean {
    return screen.kind === "rich-character"
        && screen.character.lifecycle !== "Archived"
        && builder.status === "ready"
        && builder.build !== null
        && !builder.build.readOnly;
}
