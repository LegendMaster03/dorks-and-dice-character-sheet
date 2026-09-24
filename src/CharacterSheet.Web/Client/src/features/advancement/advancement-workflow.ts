import {
    clearCharacterBuildChoice,
    setCharacterAdvancementLevel,
    setCharacterBuildChoice,
    type CharacterBuilderChoice
} from "../../builder-api.js";
import {
    filterSubclassesForClass,
    getStartingClassEntry
} from "../../builder-rules.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { searchResolvedRules } from "../../rules-core-api.js";
import type { BuildStateWorkflow } from "../../core/application/build-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface AdvancementWorkflow {
    openChooser(target: CharacterBuilderChoice): void;
    closeChooser(): void;
    search(target: CharacterBuilderChoice, query: string): Promise<void>;
    save(
        characterId: string,
        target: CharacterBuilderChoice,
        conceptKey: string
    ): Promise<void>;
    clear(characterId: string, target: CharacterBuilderChoice): Promise<void>;
    setLevel(characterId: string, occurrenceId: string, level: number): Promise<void>;
}

export function createAdvancementWorkflow(
    application: CharacterSheetApplication,
    buildState: BuildStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): AdvancementWorkflow {
    async function search(
        target: CharacterBuilderChoice,
        query: string
    ): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({
            type: "chooser-load-started",
            target,
            query: normalizedQuery
        });
        try {
            const entityType = target === "raceSpecies"
                ? "race"
                : target === "background"
                    ? "background"
                    : target === "deity"
                        ? "deity"
                        : target === "startingClass"
                            ? "class"
                            : "subclass";
            const catalog = await searchResolvedRules(
                environment,
                entityType,
                normalizedQuery);
            let results = catalog.rules.filter(
                rule => rule.entityType === entityType);
            if (target === "subclass") {
                const build = buildState.current().build;
                const startingClass = build === null
                    ? null
                    : getStartingClassEntry(build);
                if (startingClass === null) {
                    throw new Error(
                        "Choose a Class before selecting a Subclass.");
                }
                results = filterSubclassesForClass(
                    results,
                    startingClass.ruleConceptKey);
            }
            application.dispatch({
                type: "chooser-loaded",
                target,
                query: normalizedQuery,
                results
            });
        } catch (error) {
            application.dispatch({
                type: "chooser-load-failed",
                target,
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    async function mutate(
        characterId: string,
        target: CharacterBuilderChoice,
        operation: () => ReturnType<typeof setCharacterBuildChoice>
    ): Promise<void> {
        application.dispatch({ type: "selection-save-started", target });
        try {
            const build = await operation();
            application.dispatch({ type: "selection-saved", build });
            await buildState.resolveReferences(build);
            await presentation.load(characterId);
        } catch (error) {
            application.dispatch({
                type: "selection-save-failed",
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        openChooser(target: CharacterBuilderChoice): void {
            application.dispatch({ type: "chooser-opened", target });
            void search(target, "");
        },

        closeChooser(): void {
            application.dispatch({ type: "chooser-closed" });
        },

        search,

        async save(characterId, target, conceptKey): Promise<void> {
            const classId = target === "subclass"
                ? buildState.currentStartingClassId()
                : undefined;
            await mutate(
                characterId,
                target,
                () => setCharacterBuildChoice(
                    environment,
                    characterId,
                    target,
                    conceptKey,
                    classId));
        },

        async clear(characterId, target): Promise<void> {
            const classId = target === "subclass"
                ? buildState.currentStartingClassId()
                : undefined;
            await mutate(
                characterId,
                target,
                () => clearCharacterBuildChoice(
                    environment,
                    characterId,
                    target,
                    classId));
        },

        async setLevel(characterId, occurrenceId, level): Promise<void> {
            application.dispatch({
                type: "advancement-level-save-started",
                occurrenceId
            });
            try {
                const build = await setCharacterAdvancementLevel(
                    environment,
                    characterId,
                    occurrenceId,
                    level);
                application.dispatch({ type: "advancement-level-saved", build });
                await buildState.resolveReferences(build);
                await presentation.load(characterId);
            } catch (error) {
                application.dispatch({
                    type: "advancement-level-save-failed",
                    occurrenceId,
                    message: requestErrorMessage(error)
                });
            }
        }
    };
}
