import {
    addCharacterCondition,
    removeCharacterCondition,
    updateCharacterCondition,
    type CharacterConditionStateInput
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { searchResolvedRules } from "../../rules-core-api.js";
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
            const catalog = await searchResolvedRules(environment, "condition", normalizedQuery);
            application.dispatch({
                type: "condition-chooser-loaded",
                query: normalizedQuery,
                results: catalog.rules.filter(rule => rule.entityType === "condition")
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
