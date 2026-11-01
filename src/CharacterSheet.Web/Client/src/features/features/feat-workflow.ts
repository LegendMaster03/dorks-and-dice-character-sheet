import {
    addCharacterFeatOccurrence,
    removeCharacterFeatOccurrence
} from "../../builder-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";
import type { BuildStateWorkflow } from "../../core/application/build-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface FeatWorkflow {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): Promise<void>;
    add(characterId: string, conceptKey: string): Promise<void>;
    remove(characterId: string, occurrenceId: string): Promise<void>;
}

export function createFeatWorkflow(
    application: CharacterSheetApplication,
    buildState: BuildStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): FeatWorkflow {
    function canMutate(): boolean {
        const builder = buildState.current();
        return builder.status === "ready"
            && builder.build !== null
            && !builder.build.readOnly
            && builder.saving === null
            && builder.savingAbility === null
            && builder.savingFeat === null;
    }

    async function search(query: string): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({
            type: "feat-chooser-load-started",
            query: normalizedQuery
        });
        try {
            const metadata = await loadNativeRuleset(environment);
            const catalog = await searchNativeOptions(environment, metadata, "feat", normalizedQuery, 100);
            application.dispatch({
                type: "feat-chooser-loaded",
                query: normalizedQuery,
                results: catalog.options
            });
        } catch (error) {
            application.dispatch({
                type: "feat-chooser-load-failed",
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    async function mutate(
        characterId: string,
        occurrenceId: string | undefined,
        operation: () => ReturnType<typeof addCharacterFeatOccurrence>
    ): Promise<void> {
        if (!canMutate()) return;

        application.dispatch({
            type: "feat-save-started",
            occurrenceId
        });
        try {
            const build = await operation();
            application.dispatch({ type: "feat-saved", build });
            await buildState.resolveReferences(build);
            await presentation.load(characterId);
        } catch (error) {
            application.dispatch({
                type: "feat-save-failed",
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        openChooser(): void {
            application.dispatch({ type: "feat-chooser-opened" });
            void search("");
        },

        closeChooser(): void {
            application.dispatch({ type: "feat-chooser-closed" });
        },

        search,

        add(characterId, conceptKey): Promise<void> {
            return mutate(
                characterId,
                undefined,
                () => addCharacterFeatOccurrence(
                    environment,
                    characterId,
                    conceptKey));
        },

        remove(characterId, occurrenceId): Promise<void> {
            return mutate(
                characterId,
                occurrenceId,
                () => removeCharacterFeatOccurrence(
                    environment,
                    characterId,
                    occurrenceId));
        }
    };
}
