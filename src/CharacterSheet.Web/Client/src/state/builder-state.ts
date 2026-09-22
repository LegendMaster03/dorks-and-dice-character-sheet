import { loadingRuleReference } from "../builder-rules.js";
import type {
    CharacterBuilderUiState,
    CharacterSheetAction,
    RuleChooserState
} from "../app-state.js";
import type {
    CharacterBuilderChoice,
    CharacterBuildResponse
} from "../builder-api.js";

export function reduceBuilderState(
    current: CharacterBuilderUiState,
    action: CharacterSheetAction
): CharacterBuilderUiState {
    let builder = current;
    switch (action.type) {
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
                saving: null,
                savingAbility: null,
                savingAdvancementLevel: null,
                savingFeat: null
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
        case "advancement-level-save-started":
            if (builder.status === "ready"
                && builder.build !== null
                && !builder.build.readOnly
                && builder.saving === null
                && builder.savingAbility === null
                && builder.savingAdvancementLevel === null
                && builder.savingFeat === null) {
                builder = {
                    ...builder,
                    savingAdvancementLevel: action.occurrenceId,
                    advancementLevelSaveError: undefined
                };
            }
            break;
        case "advancement-level-saved":
            builder = builderStateFromBuild(
                {
                    ...builder,
                    savingAdvancementLevel: null,
                    advancementLevelSaveError: undefined
                },
                action.build);
            break;
        case "advancement-level-save-failed":
            builder = {
                ...builder,
                savingAdvancementLevel: null,
                advancementLevelSaveError: {
                    occurrenceId: action.occurrenceId,
                    message: action.message
                }
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
                && builder.savingAdvancementLevel === null
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

    }
    return builder;
}

export function createInitialBuilderState(): CharacterBuilderUiState {
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
        savingAdvancementLevel: null,
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
        savingAdvancementLevel: null,
        advancementLevelSaveError: undefined,
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
