import type {
    CharacterAbilityKey,
    CharacterBuildResponse,
    CharacterBuilderChoice
} from "./builder-api.js";
import type { CharacterSheetBootstrapResponse } from "./character-api.js";
import type { CharacterStateResponse } from "./character-state-api.js";
import {
    loadingRuleReference,
    type RuleReferenceState
} from "./builder-rules.js";
import type { ResolvedRuleCatalogItem } from "./rules-core-api.js";
import type { CharacterSheetRoute } from "./routes.js";
import type { GuidedBuilderSection, SheetSection } from "./ui/sheet-model.js";

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
    | "note-add"
    | "note-update"
    | "note-delete"
    | "inventory-add"
    | "inventory-delete";

export type InventoryChooserState =
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
    mutation: { kind: RoutineMutationKind; entryId?: string } | null;
    mutationError?: string;
}

export interface CharacterSheetAppState {
    route: CharacterSheetRoute;
    screen: CharacterSheetScreen;
    builder: CharacterBuilderUiState;
    routine: CharacterRoutineUiState;
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
    | { type: "routine-mutation-started"; kind: RoutineMutationKind; entryId?: string }
    | { type: "routine-mutation-succeeded"; state: CharacterStateResponse }
    | { type: "routine-mutation-failed"; message: string }
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
    let builder = state.builder;
    let routine = state.routine;
    let activeSheetSection = state.activeSheetSection;
    let sheetMode = state.sheetMode;
    let guidedBuilder = state.guidedBuilder;

    switch (action.type) {
        case "character-loaded":
            builder = createInitialBuilderState();
            routine = createInitialRoutineState();
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
        case "builder-load-started":
            builder = {
                ...builder,
                status: "loading",
                build: null,
                message: undefined,
                saveError: undefined
            };
            break;
        case "builder-loaded":
            builder = builderStateFromBuild(builder, action.build);
            if (action.build.readOnly) {
                sheetMode = "view";
                guidedBuilder = createInitialGuidedBuilderState();
            }
            break;
        case "builder-load-failed":
            builder = {
                ...builder,
                status: "error",
                build: null,
                message: action.message,
                chooser: { kind: "closed" },
                saving: null
            };
            break;
        case "rule-reference-resolved": {
            const currentReference = builder.references[action.target];
            if ("conceptKey" in currentReference && currentReference.conceptKey === action.conceptKey) {
                builder = {
                    ...builder,
                    references: { ...builder.references, [action.target]: action.reference }
                };
            }
            break;
        }
        case "chooser-opened":
            builder = {
                ...builder,
                chooser: {
                    kind: "open",
                    target: action.target,
                    query: "",
                    status: "idle",
                    results: []
                },
                saveError: undefined
            };
            break;
        case "chooser-query-changed":
            if (builder.chooser.kind === "open") {
                builder = {
                    ...builder,
                    chooser: { ...builder.chooser, query: action.query }
                };
            }
            break;
        case "chooser-load-started":
            if (builder.chooser.kind === "open" && builder.chooser.target === action.target) {
                builder = {
                    ...builder,
                    chooser: {
                        ...builder.chooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "chooser-loaded":
            if (chooserRequestMatches(builder.chooser, action.target, action.query)) {
                builder = {
                    ...builder,
                    chooser: {
                        ...builder.chooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "chooser-load-failed":
            if (chooserRequestMatches(builder.chooser, action.target, action.query)) {
                builder = {
                    ...builder,
                    chooser: {
                        ...builder.chooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "chooser-closed":
            builder = {
                    ...builder,
                    chooser: { kind: "closed" },
                    featChooser: { kind: "closed" }
                };
            break;
        case "selection-save-started":
            builder = { ...builder, saving: action.target, saveError: undefined };
            break;
        case "selection-saved":
            builder = builderStateFromBuild(
                { ...builder, chooser: { kind: "closed" }, saving: null, saveError: undefined },
                action.build);
            break;
        case "selection-save-failed":
            builder = { ...builder, saving: null, saveError: action.message };
            break;
        case "ability-save-started":
            builder = {
                ...builder,
                savingAbility: action.abilityKey,
                abilitySaveError: undefined
            };
            break;
        case "ability-saved":
            builder = builderStateFromBuild(
                { ...builder, savingAbility: null, abilitySaveError: undefined },
                action.build);
            break;
        case "ability-save-failed":
            builder = {
                ...builder,
                savingAbility: null,
                abilitySaveError: { abilityKey: action.abilityKey, message: action.message }
            };
            break;
        case "feat-reference-resolved": {
            const occurrence = builder.build?.progressionEntries.find(value =>
                value.id === action.occurrenceId && value.kind === "feat");
            const currentReference = builder.featReferences[action.occurrenceId];
            if (occurrence?.ruleConceptKey === action.conceptKey
                && currentReference !== undefined
                && "conceptKey" in currentReference
                && currentReference.conceptKey === action.conceptKey) {
                builder = {
                    ...builder,
                    featReferences: {
                        ...builder.featReferences,
                        [action.occurrenceId]: action.reference
                    }
                };
            }
            break;
        }
        case "feat-chooser-opened":
            if (builder.status === "ready" && builder.build !== null && !builder.build.readOnly) {
                builder = {
                    ...builder,
                    featChooser: { kind: "open", query: "", status: "idle", results: [] },
                    featSaveError: undefined
                };
            }
            break;
        case "feat-chooser-query-changed":
            if (builder.featChooser.kind === "open") {
                builder = { ...builder, featChooser: { ...builder.featChooser, query: action.query } };
            }
            break;
        case "feat-chooser-load-started":
            if (builder.featChooser.kind === "open") {
                builder = {
                    ...builder,
                    featChooser: {
                        ...builder.featChooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "feat-chooser-loaded":
            if (builder.featChooser.kind === "open" && builder.featChooser.query === action.query) {
                builder = {
                    ...builder,
                    featChooser: {
                        ...builder.featChooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "feat-chooser-load-failed":
            if (builder.featChooser.kind === "open" && builder.featChooser.query === action.query) {
                builder = {
                    ...builder,
                    featChooser: {
                        ...builder.featChooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "feat-chooser-closed":
            builder = { ...builder, featChooser: { kind: "closed" } };
            break;
        case "feat-save-started":
            if (builder.status === "ready"
                && builder.build !== null
                && !builder.build.readOnly
                && builder.saving === null
                && builder.savingAbility === null
                && builder.savingFeat === null) {
                builder = {
                    ...builder,
                    savingFeat: action.occurrenceId ?? "add",
                    featSaveError: undefined
                };
            }
            break;
        case "feat-saved":
            builder = builderStateFromBuild(
                {
                    ...builder,
                    featChooser: { kind: "closed" },
                    savingFeat: null,
                    featSaveError: undefined
                },
                action.build);
            break;
        case "feat-save-failed":
            builder = { ...builder, savingFeat: null, featSaveError: action.message };
            break;
        case "routine-load-started":
            routine = {
                ...createInitialRoutineState(),
                status: "loading"
            };
            break;
        case "routine-loaded":
            routine = routineStateFromResponse(action.state);
            break;
        case "routine-load-failed":
            routine = {
                ...createInitialRoutineState(),
                status: "error",
                message: action.message
            };
            break;
        case "routine-reference-resolved": {
            const occurrence = routine.state?.inventoryItemOccurrences.find(value => value.id === action.occurrenceId);
            const currentReference = routine.references[action.occurrenceId];
            if (occurrence?.ruleConceptKey === action.conceptKey
                && currentReference !== undefined
                && "conceptKey" in currentReference
                && currentReference.conceptKey === action.conceptKey) {
                routine = {
                    ...routine,
                    references: {
                        ...routine.references,
                        [action.occurrenceId]: action.reference
                    }
                };
            }
            break;
        }
        case "inventory-chooser-opened":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        kind: "open",
                        query: "",
                        status: "idle",
                        results: []
                    },
                    mutationError: undefined
                };
            }
            break;
        case "inventory-chooser-query-changed":
            if (routine.inventoryChooser.kind === "open") {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        query: action.query
                    }
                };
            }
            break;
        case "inventory-chooser-load-started":
            if (routine.inventoryChooser.kind === "open") {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        query: action.query,
                        status: "loading",
                        results: [],
                        message: undefined
                    }
                };
            }
            break;
        case "inventory-chooser-loaded":
            if (routine.inventoryChooser.kind === "open"
                && routine.inventoryChooser.query === action.query) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        status: "ready",
                        results: action.results,
                        message: undefined
                    }
                };
            }
            break;
        case "inventory-chooser-load-failed":
            if (routine.inventoryChooser.kind === "open"
                && routine.inventoryChooser.query === action.query) {
                routine = {
                    ...routine,
                    inventoryChooser: {
                        ...routine.inventoryChooser,
                        status: "error",
                        results: [],
                        message: action.message
                    }
                };
            }
            break;
        case "inventory-chooser-closed":
            routine = { ...routine, inventoryChooser: { kind: "closed" } };
            break;
        case "routine-mutation-started":
            if (routine.status === "ready" && routine.state !== null && !routine.state.readOnly && routine.mutation === null) {
                routine = {
                    ...routine,
                    mutation: { kind: action.kind, entryId: action.entryId },
                    mutationError: undefined
                };
            }
            break;
        case "routine-mutation-succeeded":
            routine = routineStateFromResponse(action.state);
            break;
        case "routine-mutation-failed":
            routine = {
                ...routine,
                mutation: null,
                mutationError: action.message
            };
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
        activeSheetSection,
        sheetMode,
        guidedBuilder,
        renderRevision: state.renderRevision + 1
    };
}

function createInitialRoutineState(): CharacterRoutineUiState {
    return {
        status: "idle",
        state: null,
        references: {},
        inventoryChooser: { kind: "closed" },
        mutation: null
    };
}

function routineStateFromResponse(state: CharacterStateResponse): CharacterRoutineUiState {
    const references: Record<string, RuleReferenceState> = {};
    for (const occurrence of state.inventoryItemOccurrences) {
        references[occurrence.id] = {
            status: "loading",
            conceptKey: occurrence.ruleConceptKey
        };
    }
    return {
        status: "ready",
        state,
        references,
        inventoryChooser: { kind: "closed" },
        mutation: null
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

function createInitialBuilderState(): CharacterBuilderUiState {
    return {
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
}

function builderStateFromBuild(
    current: CharacterBuilderUiState,
    build: CharacterBuildResponse
): CharacterBuilderUiState {
    return {
        ...current,
        status: "ready",
        build,
        message: undefined,
        references: {
            raceSpecies: loadingRuleReference(build, "raceSpecies"),
            startingClass: loadingRuleReference(build, "startingClass"),
            subclass: loadingRuleReference(build, "subclass")
        },
        saving: null,
        saveError: undefined,
        savingAbility: null,
        abilitySaveError: undefined,
        featReferences: Object.fromEntries(
            build.progressionEntries
                .filter(value => value.kind === "feat")
                .map(value => [value.id, {
                    status: "loading" as const,
                    conceptKey: value.ruleConceptKey
                }])),
        savingFeat: null,
        featSaveError: undefined
    };
}

function chooserRequestMatches(
    chooser: RuleChooserState,
    target: CharacterBuilderChoice,
    query: string
): chooser is Extract<RuleChooserState, { kind: "open" }> {
    return chooser.kind === "open" && chooser.target === target && chooser.query === query;
}
