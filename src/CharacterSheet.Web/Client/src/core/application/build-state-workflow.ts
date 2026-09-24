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
import { resolveRuleConcept } from "../../rules-core-api.js";
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
            ["raceSpecies", "background", "deity", "startingClass", "subclass"] as const
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

        const feats = build.progressionEntries
            .filter(value => value.kind === "feat")
            .map(async occurrence => {
                try {
                    const rule = await resolveRuleConcept(
                        environment,
                        occurrence.ruleConceptKey);
                    const reference = rule !== null
                        && rule.entityType === "feat"
                        && rule.conceptKey === occurrence.ruleConceptKey
                        ? {
                            status: "resolved" as const,
                            conceptKey: occurrence.ruleConceptKey,
                            rule
                        }
                        : {
                            status: "unavailable" as const,
                            conceptKey: occurrence.ruleConceptKey
                        };
                    application.dispatch({
                        type: "feat-reference-resolved",
                        occurrenceId: occurrence.id,
                        conceptKey: occurrence.ruleConceptKey,
                        reference
                    });
                } catch (error) {
                    application.dispatch({
                        type: "feat-reference-resolved",
                        occurrenceId: occurrence.id,
                        conceptKey: occurrence.ruleConceptKey,
                        reference: {
                            status: "error",
                            conceptKey: occurrence.ruleConceptKey,
                            message: requestErrorMessage(error)
                        }
                    });
                }
            });

        await Promise.all([...structural, ...feats]);
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
