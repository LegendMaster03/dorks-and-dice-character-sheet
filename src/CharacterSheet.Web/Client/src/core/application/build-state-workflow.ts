import type { CharacterBuilderUiState } from "../../app-state.js";
import type {
    CharacterBuildResponse,
    CharacterBuilderChoice
} from "../../builder-api.js";
import { loadCharacterBuild } from "../../builder-api.js";
import {
    getStartingClassEntry,
    getStoredChoiceConceptKey,
    resolveStoredChoice
} from "../../builder-rules.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";

import { requestErrorMessage } from "./request-error.js";

export interface BuildStateWorkflow {
    load(characterId: string): Promise<void>;
    resolveReferences(build: CharacterBuildResponse): Promise<void>;
    current(): Readonly<CharacterBuilderUiState>;
    currentStartingClassId(): string;
}

export function createBuildStateWorkflow(
    application: CharacterSheetApplication,
    environment: HostEnvironment
): BuildStateWorkflow {
    async function resolveReferences(build: CharacterBuildResponse): Promise<void> {
        const structural = (
            ["species", "subspecies", "background", "deity", "startingClass", "subclass"] as const
        ).map(async target => {
            const conceptKey = getStoredChoiceConceptKey(build, target);
            if (conceptKey === null) return;
            const reference = await resolveStoredChoice(
                environment,
                build,
                target);
            application.dispatch({
                type: "rule-reference-resolved",
                target,
                conceptKey,
                reference
            });
        });

        const feats = build.progressionEntries.filter(value => value.kind === "feat");
        const nativeFeats = feats.filter(value =>
            value.ruleConceptKey.startsWith("feat."));
        for (const entry of feats.filter(value =>
            !value.ruleConceptKey.startsWith("feat."))) {
            application.dispatch({
                type: "feat-reference-resolved",
                occurrenceId: entry.id,
                conceptKey: entry.ruleConceptKey,
                reference: { status: "unavailable", conceptKey: entry.ruleConceptKey }
            });
        }
        if (nativeFeats.length > 0) {
            try {
                const metadata = await loadNativeRuleset(environment);
                await Promise.all(nativeFeats.map(async entry => {
                    try {
                        const page = await searchNativeOptions(
                            environment, metadata, "feat", entry.ruleConceptKey, 100);
                        const rule = page.options.find(value =>
                            value.id === entry.ruleConceptKey);
                        application.dispatch({
                            type: "feat-reference-resolved",
                            occurrenceId: entry.id,
                            conceptKey: entry.ruleConceptKey,
                            reference: rule === undefined
                                ? { status: "unavailable", conceptKey: entry.ruleConceptKey }
                                : { status: "resolved", conceptKey: entry.ruleConceptKey, rule }
                        });
                    } catch (error) {
                        application.dispatch({
                            type: "feat-reference-resolved",
                            occurrenceId: entry.id,
                            conceptKey: entry.ruleConceptKey,
                            reference: {
                                status: "error", conceptKey: entry.ruleConceptKey,
                                message: requestErrorMessage(error)
                            }
                        });
                    }
                }));
            } catch (error) {
                for (const entry of nativeFeats) {
                    application.dispatch({
                        type: "feat-reference-resolved",
                        occurrenceId: entry.id,
                        conceptKey: entry.ruleConceptKey,
                        reference: {
                            status: "error", conceptKey: entry.ruleConceptKey,
                            message: requestErrorMessage(error)
                        }
                    });
                }
            }
        }

        await Promise.all(structural);
    }

    return {
        async load(characterId: string): Promise<void> {
            application.dispatch({ type: "builder-load-started" });
            try {
                const build = await loadCharacterBuild(environment, characterId);
                application.dispatch({ type: "builder-loaded", build });
                await resolveReferences(build);
            } catch (error) {
                application.dispatch({
                    type: "builder-load-failed",
                    message: requestErrorMessage(error)
                });
            }
        },

        resolveReferences,

        current(): Readonly<CharacterBuilderUiState> {
            return application.getState().builder;
        },

        currentStartingClassId(): string {
            const build = application.getState().builder.build;
            const startingClass = build === null
                ? null
                : getStartingClassEntry(build);
            if (startingClass === null) {
                throw new Error(
                    "Choose a Class before selecting a Subclass.");
            }
            return startingClass.id;
        }
    };
}
