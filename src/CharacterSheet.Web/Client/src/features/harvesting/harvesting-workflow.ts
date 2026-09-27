import type {
    HarvestingCraftingUiState,
    HarvestingHelperUiState
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import type {
    HarvestingComponentEditInput,
    HarvestingTableResolutionInput
} from "../../rules-core-api.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
import {
    createHarvestingCraftingWorkflow as createCoreHarvestingCraftingWorkflow,
    type HarvestingCraftingWorkflow
} from "./harvesting-workflow-core.js";

export type { HarvestingCraftingWorkflow } from "./harvesting-workflow-core.js";

export function createHarvestingCraftingWorkflow(
    application: CharacterSheetApplication,
    routine: RoutineStateWorkflow,
    presentation: PresentationWorkflow,
    environment: HostEnvironment
): HarvestingCraftingWorkflow {
    const core = createCoreHarvestingCraftingWorkflow(
        application,
        routine,
        presentation,
        environment);

    function updateDraft(
        transform: (state: HarvestingCraftingUiState) => HarvestingCraftingUiState
    ): void {
        const current = application.getState().harvestingCrafting;
        application.dispatch({
            type: "harvesting-crafting-updated",
            state: transform(current)
        }, { render: false });
    }

    function clearCraftingResult(
        state: HarvestingCraftingUiState
    ): HarvestingCraftingUiState {
        return {
            ...state,
            craftingStatus: "idle",
            craftingResolution: null,
            craftingRolls: [],
            craftingSelectedRoll: null,
            craftingRollTie: false,
            message: undefined
        };
    }

    function invalidateCurrentCraftingStage(
        state: HarvestingCraftingUiState
    ): HarvestingCraftingUiState {
        return {
            ...state,
            craftingManufacturingSucceeded: state.craftingProcedure === "manufacturing"
                ? null
                : state.craftingManufacturingSucceeded,
            craftingEnchantingSucceeded: state.craftingProcedure === "enchanting"
                ? null
                : state.craftingEnchantingSucceeded,
            craftingCompletionStatus: "idle",
            craftingCompleted: false
        };
    }

    function resetCraftingCompletion(
        state: HarvestingCraftingUiState
    ): HarvestingCraftingUiState {
        return {
            ...state,
            craftingCompletionStatus: "idle",
            craftingCompleted: false
        };
    }

    function nextHarvestEdits(
        state: HarvestingCraftingUiState,
        key: string,
        edit: HarvestingComponentEditInput
    ): HarvestingTableResolutionInput["manualEdits"] {
        const previous = state.tableRequest?.manualEdits;
        const removals = new Set(previous?.removeComponentKeys ?? []);
        const upserts = (previous?.upsertComponents ?? [])
            .filter(value => value.key !== key);
        removals.delete(key);
        upserts.push(edit);
        return {
            removeComponentKeys: [...removals],
            upsertComponents: upserts
        };
    }

    async function editHarvestComponentDraft(
        key: string,
        componentDc: number,
        quantity: number | null
    ): Promise<void> {
        const state = application.getState().harvestingCrafting;
        const request = state.tableRequest;
        const component = state.table?.components.find(value => value.key === key);
        if (request === null || component === undefined) return;

        if (!Number.isInteger(componentDc) || componentDc <= 0
            || (quantity !== null && (!Number.isInteger(quantity) || quantity <= 0))) {
            await core.editHarvestComponent(key, componentDc, quantity);
            return;
        }

        const nextRequest: HarvestingTableResolutionInput = {
            ...request,
            manualEdits: nextHarvestEdits(
                state,
                key,
                {
                    key,
                    displayName: component.displayName,
                    componentDc,
                    quantity
                })
        };

        updateDraft(current => ({
            ...current,
            tableRequest: nextRequest,
            table: current.table === null
                ? null
                : {
                    ...current.table,
                    components: current.table.components.map(value =>
                        value.key === key
                            ? { ...value, componentDc, quantity }
                            : value),
                    manualEditsApplied: true
                },
            outcomeStatus: "idle",
            outcome: null,
            harvestInventoryStatus: "idle",
            harvestInventoryAwarded: false,
            message: undefined
        }));
    }

    return {
        ...core,

        setHarvestManualComponentName(value): void {
            updateDraft(state => ({
                ...state,
                harvestManualComponentName: value,
                message: undefined
            }));
        },

        setHarvestManualComponentDc(value): void {
            updateDraft(state => ({
                ...state,
                harvestManualComponentDc: value,
                message: undefined
            }));
        },

        setHarvestManualComponentQuantity(value): void {
            updateDraft(state => ({
                ...state,
                harvestManualComponentQuantity: value,
                message: undefined
            }));
        },

        editHarvestComponent: editHarvestComponentDraft,

        setAssessmentResult(value): void {
            updateDraft(state => ({
                ...state,
                assessmentResult: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        setCarvingResult(value): void {
            updateDraft(state => ({
                ...state,
                carvingResult: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        updateHelper(index, helper: HarvestingHelperUiState): void {
            updateDraft(state => {
                if (index < 0 || index >= state.helpers.length) return state;
                const helpers = [...state.helpers];
                helpers[index] = helper;
                return {
                    ...state,
                    helpers,
                    outcomeStatus: "idle",
                    outcome: null,
                    message: undefined
                };
            });
        },

        setCraftingManualName(value): void {
            updateDraft(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManualName: value
            })));
        },

        setCraftingManualContribution(value): void {
            updateDraft(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManualContribution: value
            })));
        },

        setCraftingManufacturingManualAbilityModifier(value): void {
            updateDraft(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManufacturingManualAbilityModifier: value
            })));
        },

        setCraftingTargetDc(value): void {
            updateDraft(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingTargetDc: value
            })));
        },

        setCraftingOtherModifier(value): void {
            updateDraft(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingOtherModifier: value
            })));
        },

        setCraftingSelectedRoll(value): void {
            updateDraft(state => ({
                ...state,
                craftingSelectedRoll: value,
                craftingResolution: state.craftingResolution === null
                    ? null
                    : {
                        ...state.craftingResolution,
                        d20Roll: null,
                        total: null,
                        meetsTarget: null
                    },
                message: undefined
            }));
        },

        setCraftingRecipeName(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingRecipeName: value
            }));
        },

        setCraftingOutputName(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingOutputName: value
            }));
        },

        setCraftingOutputQuantity(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingOutputQuantity: value
            }));
        },

        setCraftingManufacturingRequiredHours(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingManufacturingRequiredHours: value
            }));
        },

        setCraftingManufacturingCompletedHours(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingManufacturingCompletedHours: Math.max(0, value)
            }));
        },

        setCraftingEnchantingRequiredHours(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingEnchantingRequiredHours: value
            }));
        },

        setCraftingEnchantingCompletedHours(value): void {
            updateDraft(state => resetCraftingCompletion({
                ...state,
                craftingEnchantingCompletedHours: Math.max(0, value)
            }));
        },

        updateCraftingMaterial(index, quantity): void {
            updateDraft(state => {
                if (index < 0 || index >= state.craftingMaterials.length) return state;
                const materials = [...state.craftingMaterials];
                materials[index] = {
                    ...materials[index]!,
                    quantity
                };
                return resetCraftingCompletion({
                    ...state,
                    craftingMaterials: materials
                });
            });
        }
    };
}
