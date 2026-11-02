import {
    addInventoryItemOccurrence,
    addCustomInventoryItemOccurrence,
    removeCharacterCurrencyBalance,
    removeInventoryItemOccurrence,
    setCharacterCurrencyBalance,
    updateInventoryItemOccurrence,
    type CharacterInventoryItemOccurrenceStateInput
} from "../../character-state-api.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import { loadNativeRuleset, searchNativeOptions } from "../../rules-core-native-api.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import { materializeStartingEquipmentInventory } from "./starting-equipment-api.js";

export interface InventoryWorkflow {
    openChooser(): void;
    closeChooser(): void;
    search(query: string): Promise<void>;
    add(characterId: string, conceptKey: string): Promise<void>;
    addCustom(characterId: string, name: string, quantity: number): Promise<void>;
    update(
        characterId: string,
        occurrenceId: string,
        input: CharacterInventoryItemOccurrenceStateInput
    ): Promise<void>;
    remove(characterId: string, occurrenceId: string): Promise<void>;
    setCurrency(characterId: string, currencyKey: string, amount: number): Promise<void>;
    removeCurrency(characterId: string, currencyKey: string): Promise<void>;
    materializeStartingEquipment(characterId: string): Promise<void>;
}

export function createInventoryWorkflow(
    application: CharacterSheetApplication,
    routine: RoutineStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): InventoryWorkflow {
    async function search(query: string): Promise<void> {
        const normalizedQuery = query.trim();
        application.dispatch({
            type: "inventory-chooser-load-started",
            query: normalizedQuery
        });
        try {
            const metadata = await loadNativeRuleset(environment);
            const catalog = await searchNativeOptions(environment, metadata, "item", normalizedQuery, 100);
            application.dispatch({
                type: "inventory-chooser-loaded",
                query: normalizedQuery,
                results: catalog.options
            });
        } catch (error) {
            application.dispatch({
                type: "inventory-chooser-load-failed",
                query: normalizedQuery,
                message: requestErrorMessage(error)
            });
        }
    }

    return {
        openChooser(): void {
            application.dispatch({ type: "inventory-chooser-opened" });
            void search("");
        },

        closeChooser(): void {
            application.dispatch({ type: "inventory-chooser-closed" });
        },

        search,

        async setCurrency(
            characterId: string,
            currencyKey: string,
            amount: number
        ): Promise<void> {
            await routine.mutate(
                "currency-update",
                () => setCharacterCurrencyBalance(
                    environment,
                    characterId,
                    currencyKey,
                    amount),
                currencyKey);
        },

        async removeCurrency(
            characterId: string,
            currencyKey: string
        ): Promise<void> {
            await routine.mutate(
                "currency-delete",
                () => removeCharacterCurrencyBalance(
                    environment,
                    characterId,
                    currencyKey),
                currencyKey);
        },

        async add(characterId: string, conceptKey: string): Promise<void> {
            const changed = await routine.mutate(
                "inventory-add",
                () => addInventoryItemOccurrence(
                    environment,
                    characterId,
                    conceptKey));
            if (changed) await presentation.load(characterId);
        },

        async addCustom(characterId: string, name: string, quantity: number): Promise<void> {
            const changed = await routine.mutate(
                "inventory-add",
                () => addCustomInventoryItemOccurrence(
                    environment, characterId, name, quantity));
            if (changed) await presentation.load(characterId);
        },

        async update(
            characterId: string,
            occurrenceId: string,
            input: CharacterInventoryItemOccurrenceStateInput
        ): Promise<void> {
            const changed = await routine.mutate(
                "inventory-update",
                () => updateInventoryItemOccurrence(
                    environment,
                    characterId,
                    occurrenceId,
                    input),
                occurrenceId);
            if (changed) await presentation.load(characterId);
        },

        async remove(characterId: string, occurrenceId: string): Promise<void> {
            const changed = await routine.mutate(
                "inventory-delete",
                () => removeInventoryItemOccurrence(
                    environment,
                    characterId,
                    occurrenceId),
                occurrenceId);
            if (changed) await presentation.load(characterId);
        },

        async materializeStartingEquipment(characterId: string): Promise<void> {
            const changed = await routine.mutate(
                "inventory-transaction",
                () => materializeStartingEquipmentInventory(
                    environment,
                    characterId),
                "starting-equipment");
            if (changed) await presentation.load(characterId);
        }
    };
}
