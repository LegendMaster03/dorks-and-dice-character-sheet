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
import {
    resolveCharacterEnchanting,
    resolveCharacterManufacturing,
    type CraftingCompetencyInput
} from "../../crafting-api.js";
import {
    normalizeD20RollMode,
    rollD20
} from "../../dice/roll-selection.js";

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
    setCraftingProcedure(value: "manufacturing" | "enchanting"): void;
    setCraftingCompetencyMode(value: "resolved" | "manual"): void;
    setCraftingCompetencyKey(value: string): void;
    setCraftingManualName(value: string): void;
    setCraftingManualContribution(value: number | null): void;
    setCraftingManualQualified(value: boolean): void;
    setCraftingHasQualifiedGuidance(value: boolean): void;
    setCraftingCreatureType(value: string): void;
    setCraftingSpellcastingKey(value: string): void;
    setCraftingTargetDc(value: number | null): void;
    setCraftingOtherModifier(value: number): void;
    prepareCrafting(characterId: string): Promise<void>;
    rollCrafting(characterId: string): Promise<void>;
    chooseCraftingRoll(characterId: string, value: number): Promise<void>;
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

    function craftingCompetency(
        state: HarvestingCraftingUiState
    ): CraftingCompetencyInput | null {
        if (state.craftingCompetencyMode === "manual") {
            const displayName = state.craftingManualName.trim();
            if (displayName.length === 0 || state.craftingManualContribution === null) {
                return null;
            }
            return {
                manual: {
                    displayName,
                    contribution: state.craftingManualContribution,
                    isQualified: state.craftingManualQualified
                }
            };
        }

        const key = state.craftingCompetencyKey.trim();
        return key.length === 0 ? null : { competencyKey: key };
    }

    async function resolveCrafting(
        characterId: string,
        d20Roll: number | null
    ): Promise<void> {
        const state = application.getState().harvestingCrafting;
        const competency = craftingCompetency(state);
        if (state.craftingProcedure === "manufacturing" && competency === null) {
            update(current => ({
                ...current,
                craftingStatus: "error",
                message: state.craftingCompetencyMode === "manual"
                    ? "Enter a manual competency name and modifier."
                    : "Choose a universal competency or use manual entry."
            }));
            return;
        }
        if (state.craftingProcedure === "enchanting"
            && competency === null
            && state.craftingCreatureType.trim().length === 0) {
            update(current => ({
                ...current,
                craftingStatus: "error",
                message: "Choose a creature type, a universal competency, or manual entry for Enchanting."
            }));
            return;
        }

        update(current => ({
            ...current,
            craftingStatus: "loading",
            craftingResolution: d20Roll === null ? null : current.craftingResolution,
            message: undefined
        }));
        try {
            const common = {
                campaignId: state.scopeCampaignId,
                d20Roll,
                otherModifier: state.craftingOtherModifier,
                targetDc: state.craftingTargetDc
            };
            const resolution = state.craftingProcedure === "manufacturing"
                ? await resolveCharacterManufacturing(
                    environment,
                    characterId,
                    {
                        ...common,
                        competency: competency!,
                        hasQualifiedGuidance: state.craftingHasQualifiedGuidance
                    })
                : await resolveCharacterEnchanting(
                    environment,
                    characterId,
                    {
                        ...common,
                        creatureType: competency === null
                            ? state.craftingCreatureType.trim()
                            : null,
                        competency,
                        spellcastingKey: state.craftingSpellcastingKey.trim().length > 0
                            ? state.craftingSpellcastingKey.trim()
                            : null
                    });

            update(current => ({
                ...current,
                craftingStatus: "ready",
                craftingResolution: resolution,
                craftingSelectedRoll: d20Roll,
                message: undefined
            }));
        } catch (error) {
            update(current => ({
                ...current,
                craftingStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    async function rollCrafting(characterId: string): Promise<void> {
        let state = application.getState().harvestingCrafting;
        if (state.craftingResolution === null || state.craftingResolution.d20Roll !== null) {
            await resolveCrafting(characterId, null);
            state = application.getState().harvestingCrafting;
        }
        if (state.craftingResolution === null) return;

        const selection = rollD20(normalizeD20RollMode(state.craftingResolution.rollMode));
        if (selection.tied) {
            update(current => ({
                ...current,
                craftingRolls: [...selection.rolls],
                craftingSelectedRoll: null,
                craftingRollTie: true,
                message: "The roll selection is tied. Choose which die result to use."
            }));
            return;
        }

        update(current => ({
            ...current,
            craftingRolls: [...selection.rolls],
            craftingSelectedRoll: selection.selected,
            craftingRollTie: false,
            message: undefined
        }));
        await resolveCrafting(characterId, selection.selected);
    }

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
            update(state => clearCraftingResult(clearResolution({
                ...state,
                scopeCampaignId: campaignId
            })));
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

        evaluate,

        setCraftingProcedure(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingProcedure: value
            }));
        },

        setCraftingCompetencyMode(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingCompetencyMode: value
            }));
        },

        setCraftingCompetencyKey(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingCompetencyKey: value
            }));
        },

        setCraftingManualName(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingManualName: value
            }));
        },

        setCraftingManualContribution(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingManualContribution: value
            }));
        },

        setCraftingManualQualified(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingManualQualified: value
            }));
        },

        setCraftingHasQualifiedGuidance(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingHasQualifiedGuidance: value
            }));
        },

        setCraftingCreatureType(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingCreatureType: value
            }));
        },

        setCraftingSpellcastingKey(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingSpellcastingKey: value
            }));
        },

        setCraftingTargetDc(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingTargetDc: value
            }));
        },

        setCraftingOtherModifier(value): void {
            update(state => clearCraftingResult({
                ...state,
                craftingOtherModifier: value
            }));
        },

        async prepareCrafting(characterId): Promise<void> {
            await resolveCrafting(characterId, null);
        },

        rollCrafting,

        async chooseCraftingRoll(characterId, value): Promise<void> {
            const state = application.getState().harvestingCrafting;
            if (!state.craftingRollTie || !state.craftingRolls.includes(value)) return;
            update(current => ({
                ...current,
                craftingSelectedRoll: value,
                craftingRollTie: false,
                message: undefined
            }));
            await resolveCrafting(characterId, value);
        }
    };
}
