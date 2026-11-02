import {
    addCharacterCondition,
    removeCharacterCondition,
    updateCharacterCondition,
    type CharacterConditionStateInput
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface ConditionsWorkflow {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): Promise<void>;
    addRule(characterId: string, conceptKey: string, input: CharacterConditionStateInput): Promise<void>;
    addCustom(characterId: string, customName: string, input: CharacterConditionStateInput): Promise<void>;
    update(characterId: string, conditionId: string, input: CharacterConditionStateInput): Promise<void>;
    remove(characterId: string, conditionId: string): Promise<void>;
}

export function createConditionsWorkflow(
    application: CharacterSheetApplication,
    routine: RoutineStateWorkflow,
    environment: HostEnvironment
): ConditionsWorkflow {
    async function search(query: string): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({ type: "condition-chooser-load-started", query: normalizedQuery });
        try {
            const metadata = await loadNativeRuleset(environment);
            const [ordinary, leveled] = await Promise.all([
                searchNativeOptions(environment, metadata, "condition", normalizedQuery, 100),
                searchNativeOptions(environment, metadata, "leveled-condition", normalizedQuery, 100)
            ]);
            const results = [...ordinary.options, ...leveled.options]
                .sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
            application.dispatch({
                type: "condition-chooser-loaded",
                query: normalizedQuery,
                results
            });
        } catch (error) {
            application.dispatch({
                type: "condition-chooser-load-failed",
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        openChooser(): void {
            application.dispatch({ type: "condition-chooser-opened" });
            void search("");
        },

        closeChooser(): void {
            application.dispatch({ type: "condition-chooser-closed" });
        },

        search,

        async addRule(characterId, conceptKey, input): Promise<void> {
            await routine.mutate(
                "condition-add",
                () => addCharacterCondition(environment, characterId, {
                    ...input,
                    conceptKey,
                    customName: null
                }));
        },

        async addCustom(characterId, customName, input): Promise<void> {
            const normalizedName = customName.trim();
            if (normalizedName.length === 0) return;
            await routine.mutate(
                "condition-add",
                () => addCharacterCondition(environment, characterId, {
                    ...input,
                    conceptKey: null,
                    customName: normalizedName
                }));
        },

        async update(characterId, conditionId, input): Promise<void> {
            await routine.mutate(
                "condition-update",
                () => updateCharacterCondition(environment, characterId, conditionId, input),
                conditionId);
        },

        async remove(characterId, conditionId): Promise<void> {
            await routine.mutate(
                "condition-delete",
                () => removeCharacterCondition(environment, characterId, conditionId),
                conditionId);
        }
    };
}
