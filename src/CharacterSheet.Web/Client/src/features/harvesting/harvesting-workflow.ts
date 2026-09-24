import type {
    HarvestingCraftingMode,
    HarvestingCraftingUiState
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import {
    loadHarvestingRules,
    resolveHarvestingOutcome,
    resolveHarvestingTable,
    searchResolvedRules,
    type HarvestingHelperInput
} from "../../rules-core-api.js";
import { requestErrorMessage } from "../../core/application/request-error.js";

export interface HarvestingCraftingWorkflow {
    open(): void;
    close(): void;
    setMode(mode: HarvestingCraftingMode): void;
    setScope(campaignId: string | null): void;
    setSourceKind(kind: "creature-type" | "monster"): void;
    setCreatureType(creatureType: string): void;
    searchMonsters(query: string): Promise<void>;
    selectMonster(conceptKey: string): void;
    resolveTable(): Promise<void>;
    setAssessmentResult(value: number | null): void;
    setCarvingResult(value: number | null): void;
    setSameActor(value: boolean): void;
    setCreatureSize(value: string): void;
    addHelper(): void;
    updateHelper(index: number, helper: HarvestingHelperInput): void;
    removeHelper(index: number): void;
    moveComponent(key: string, direction: -1 | 1): void;
    evaluate(): Promise<void>;
}

export function createHarvestingCraftingWorkflow(
    application: CharacterSheetApplication,
    environment: HostEnvironment
): HarvestingCraftingWorkflow {
    function update(
        transform: (state: HarvestingCraftingUiState) => HarvestingCraftingUiState
    ): void {
        const current = application.getState().harvestingCrafting;
        application.dispatch({
            type: "harvesting-crafting-updated",
            state: transform(current)
        });
    }

    function clearResolution(
        state: HarvestingCraftingUiState
    ): HarvestingCraftingUiState {
        return {
            ...state,
            tableStatus: "idle",
            tableRequest: null,
            table: null,
            harvestOrder: [],
            outcomeStatus: "idle",
            outcome: null,
            message: undefined
        };
    }

    async function ensureCatalog(): Promise<void> {
        const current = application.getState().harvestingCrafting;
        if (current.catalogStatus === "loading" || current.catalogStatus === "ready") return;

        update(state => ({ ...state, catalogStatus: "loading", message: undefined }));
        try {
            const catalog = await loadHarvestingRules(environment);
            update(state => ({
                ...state,
                catalogStatus: "ready",
                catalog,
                creatureType: state.creatureType.length > 0
                    ? state.creatureType
                    : (catalog.creatureTypes[0]?.key ?? ""),
                message: undefined
            }));
        } catch (error) {
            update(state => ({
                ...state,
                catalogStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    async function searchMonsters(query: string): Promise<void> {
        const normalized = query.trim();
        update(state => ({
            ...state,
            monsterQuery: normalized,
            monsterStatus: "loading",
            message: undefined
        }));
        try {
            const catalog = await searchResolvedRules(
                environment,
                "monster",
                normalized);
            update(state => ({
                ...state,
                monsterStatus: "ready",
                monsterResults: catalog.rules.filter(rule => rule.entityType === "monster"),
                message: undefined
            }));
        } catch (error) {
            update(state => ({
                ...state,
                monsterStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    async function resolveTable(): Promise<void> {
        const state = application.getState().harvestingCrafting;
        const request = state.sourceKind === "monster"
            ? {
                creatureConceptKey: state.creatureConceptKey.trim()
            }
            : {
                creatureType: state.creatureType.trim()
            };
        const selected = state.sourceKind === "monster"
            ? request.creatureConceptKey
            : request.creatureType;
        if (selected === undefined || selected.length === 0) {
            update(current => ({
                ...current,
                tableStatus: "error",
                message: state.sourceKind === "monster"
                    ? "Choose a creature before resolving its Harvesting table."
                    : "Choose a creature type before resolving its Harvesting table."
            }));
            return;
        }

        update(current => ({
            ...current,
            tableStatus: "loading",
            table: null,
            tableRequest: null,
            harvestOrder: [],
            outcomeStatus: "idle",
            outcome: null,
            message: undefined
        }));
        try {
            const table = await resolveHarvestingTable(
                environment,
                request,
                state.scopeCampaignId);
            update(current => ({
                ...current,
                tableStatus: "ready",
                tableRequest: request,
                table,
                creatureSize: table.creatureSize ?? current.creatureSize,
                harvestOrder: table.components.map(component => component.key),
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        } catch (error) {
            update(current => ({
                ...current,
                tableStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    async function evaluate(): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (state.table === null || state.tableRequest === null) {
            update(current => ({
                ...current,
                outcomeStatus: "error",
                message: "Resolve a Harvesting table before calculating the outcome."
            }));
            return;
        }
        if (state.assessmentResult === null || state.carvingResult === null) {
            update(current => ({
                ...current,
                outcomeStatus: "error",
                message: "Enter both Assessment and Carving results."
            }));
            return;
        }
        if (state.harvestOrder.length === 0) {
            update(current => ({
                ...current,
                outcomeStatus: "error",
                message: "The harvest list must contain at least one component."
            }));
            return;
        }

        update(current => ({
            ...current,
            outcomeStatus: "loading",
            outcome: null,
            message: undefined
        }));
        try {
            const outcome = await resolveHarvestingOutcome(
                environment,
                {
                    table: state.tableRequest,
                    assessmentResult: state.assessmentResult,
                    carvingResult: state.carvingResult,
                    sameActor: state.sameActor,
                    harvestOrderComponentKeys: state.harvestOrder,
                    creatureSize: state.creatureSize.trim().length > 0
                        ? state.creatureSize.trim()
                        : null,
                    helpers: state.helpers
                },
                state.scopeCampaignId);
            update(current => ({
                ...current,
                outcomeStatus: "ready",
                outcome,
                message: undefined
            }));
        } catch (error) {
            update(current => ({
                ...current,
                outcomeStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    return {
        open(): void {
            update(state => ({ ...state, open: true, message: undefined }));
            void ensureCatalog();
        },

        close(): void {
            update(state => ({ ...state, open: false, message: undefined }));
        },

        setMode(mode): void {
            update(state => ({ ...state, mode, message: undefined }));
        },

        setScope(campaignId): void {
            update(state => clearResolution({
                ...state,
                scopeCampaignId: campaignId
            }));
        },

        setSourceKind(kind): void {
            update(state => clearResolution({
                ...state,
                sourceKind: kind,
                creatureConceptKey: kind === "monster" ? state.creatureConceptKey : ""
            }));
            if (kind === "monster"
                && application.getState().harvestingCrafting.monsterStatus === "idle") {
                void searchMonsters("");
            }
        },

        setCreatureType(creatureType): void {
            update(state => clearResolution({ ...state, creatureType }));
        },

        searchMonsters,

        selectMonster(conceptKey): void {
            update(state => clearResolution({
                ...state,
                creatureConceptKey: conceptKey.trim()
            }));
        },

        resolveTable,

        setAssessmentResult(value): void {
            update(state => ({
                ...state,
                assessmentResult: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        setCarvingResult(value): void {
            update(state => ({
                ...state,
                carvingResult: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        setSameActor(value): void {
            update(state => ({
                ...state,
                sameActor: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        setCreatureSize(value): void {
            update(state => ({
                ...state,
                creatureSize: value,
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        addHelper(): void {
            update(state => ({
                ...state,
                helpers:
                [
                    ...state.helpers,
                    {
                        proficiencyBonus: 0,
                        isProficient: false,
                        participatedForEntireDuration: true,
                        isAssessmentParticipant: false,
                        isCarvingParticipant: false
                    }
                ],
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        updateHelper(index, helper): void {
            update(state => {
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

        removeHelper(index): void {
            update(state => ({
                ...state,
                helpers: state.helpers.filter((_, current) => current !== index),
                outcomeStatus: "idle",
                outcome: null,
                message: undefined
            }));
        },

        moveComponent(key, direction): void {
            update(state => {
                const index = state.harvestOrder.indexOf(key);
                const next = index + direction;
                if (index < 0 || next < 0 || next >= state.harvestOrder.length) return state;
                const order = [...state.harvestOrder];
                [order[index], order[next]] = [order[next]!, order[index]!];
                return {
                    ...state,
                    harvestOrder: order,
                    outcomeStatus: "idle",
                    outcome: null,
                    message: undefined
                };
            });
        },

        evaluate
    };
}
