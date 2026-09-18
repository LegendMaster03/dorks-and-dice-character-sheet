import type {
    CharacterBuildResponse,
    CharacterBuilderChoice
} from "./builder-api.js";
import type { CharacterSheetBootstrapResponse } from "./character-api.js";
import {
    loadingRuleReference,
    type RuleReferenceState
} from "./builder-rules.js";
import type { ResolvedRuleCatalogItem } from "./rules-core-api.js";
import type { CharacterSheetRoute } from "./routes.js";
import type { SheetSection } from "./ui/sheet-model.js";

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

export interface CharacterBuilderUiState {
    status: "idle" | "loading" | "ready" | "error";
    build: CharacterBuildResponse | null;
    message?: string;
    references: Record<CharacterBuilderChoice, RuleReferenceState>;
    chooser: RuleChooserState;
    saving: CharacterBuilderChoice | null;
    saveError?: string;
}

export interface CharacterSheetAppState {
    route: CharacterSheetRoute;
    screen: CharacterSheetScreen;
    builder: CharacterBuilderUiState;
    activeSheetSection: SheetSection;
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
        activeSheetSection: "actions",
        renderRevision: 0
    };
}

export function reduceAppState(
    state: CharacterSheetAppState,
    action: CharacterSheetAction
): CharacterSheetAppState {
    let screen = state.screen;
    let builder = state.builder;
    let activeSheetSection = state.activeSheetSection;

    switch (action.type) {
        case "character-loaded":
            builder = createInitialBuilderState();
            activeSheetSection = "actions";
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
            builder = { ...builder, chooser: { kind: "closed" } };
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
        activeSheetSection,
        renderRevision: state.renderRevision + 1
    };
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
        saving: null
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
        saveError: undefined
    };
}

function chooserRequestMatches(
    chooser: RuleChooserState,
    target: CharacterBuilderChoice,
    query: string
): chooser is Extract<RuleChooserState, { kind: "open" }> {
    return chooser.kind === "open" && chooser.target === target && chooser.query === query;
}
