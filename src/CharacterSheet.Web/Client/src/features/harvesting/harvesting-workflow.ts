import type {
    HarvestingCraftingMode,
    HarvestingCraftingUiState,
    HarvestingHelperUiState
} from "../../app-state.js";
import type { HostEnvironment } from "../../host-environment.js";
import type { CharacterSheetApplication } from "../../render-lifecycle.js";
import {
    loadHarvestingRules,
    resolveHarvestingOutcome,
    resolveHarvestingTable,
    searchResolvedRules,
    type HarvestingComponentEditInput,
    type HarvestingResolvedTableResponse,
    type HarvestingRulesCatalogResponse,
    type HarvestingTableResolutionInput
} from "../../rules-core-api.js";
import { requestErrorMessage } from "../../core/application/request-error.js";
import {
    resolveCharacterEnchanting,
    resolveCharacterManufacturing,
    type CraftingCompetencyInput
} from "../../crafting-api.js";
import { applyInventoryTransaction } from "../../character-state-api.js";
import { loadCharacterPresentation } from "../../character-presentation-api.js";
import { loadHostedCampaignContext } from "../../campaign-context-api.js";
import type { RoutineStateWorkflow } from "../../core/application/routine-state-workflow.js";
import type { PresentationWorkflow } from "../../core/application/presentation-workflow.js";
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
    setHarvestManualComponentName(value: string): void;
    setHarvestManualComponentDc(value: number | null): void;
    setHarvestManualComponentQuantity(value: number | null): void;
    editHarvestComponent(key: string, componentDc: number, quantity: number | null): Promise<void>;
    removeHarvestComponent(key: string): Promise<void>;
    addHarvestComponent(): Promise<void>;
    resetHarvestEdits(): Promise<void>;
    setAssessmentResult(value: number | null): void;
    setCarvingResult(value: number | null): void;
    setSameActor(value: boolean): void;
    setCreatureSize(value: string): void;
    addHelper(): void;
    updateHelper(index: number, helper: HarvestingHelperUiState): void;
    setHelperCharacter(index: number, characterId: string | null): Promise<void>;
    removeHelper(index: number): void;
    moveComponent(key: string, direction: -1 | 1): void;
    evaluate(): Promise<void>;
    awardHarvest(characterId: string): Promise<void>;
    setCraftingProcedure(value: "manufacturing" | "enchanting"): void;
    setCraftingCompetencyMode(value: "resolved" | "manual"): void;
    setCraftingCompetencyKey(value: string): void;
    setCraftingManualName(value: string): void;
    setCraftingManualContribution(value: number | null): void;
    setCraftingManualQualified(value: boolean): void;
    setCraftingHasQualifiedGuidance(value: boolean): void;
    setCraftingManufacturingAbilityMode(value: "character" | "manual"): void;
    setCraftingManufacturingAbilityKey(value: string): void;
    setCraftingManufacturingManualAbilityModifier(value: number | null): void;
    setCraftingCreatureType(value: string): void;
    setCraftingSpellcastingKey(value: string): void;
    setCraftingTargetDc(value: number | null): void;
    setCraftingOtherModifier(value: number): void;
    prepareCrafting(characterId: string): Promise<void>;
    setCraftingSelectedRoll(value: number | null): void;
    submitCraftingRoll(characterId: string): Promise<void>;
    rollCrafting(characterId: string): Promise<void>;
    chooseCraftingRoll(characterId: string, value: number): Promise<void>;
    setCraftingRecipeName(value: string): void;
    setCraftingOutputName(value: string): void;
    setCraftingOutputQuantity(value: number): void;
    setCraftingRequiresManufacturing(value: boolean): void;
    setCraftingRequiresEnchanting(value: boolean): void;
    setCraftingManufacturingRequiredHours(value: number | null): void;
    setCraftingManufacturingCompletedHours(value: number): void;
    setCraftingEnchantingRequiredHours(value: number | null): void;
    setCraftingEnchantingCompletedHours(value: number): void;
    addCraftingMaterial(occurrenceId: string): void;
    updateCraftingMaterial(index: number, quantity: number): void;
    removeCraftingMaterial(index: number): void;
    completeCrafting(characterId: string): Promise<void>;
}

export function createHarvestingCraftingWorkflow(
    application: CharacterSheetApplication,
    routine: RoutineStateWorkflow,
    presentation: PresentationWorkflow,
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
            harvestInventoryStatus: "idle",
            harvestInventoryAwarded: false,
            message: undefined
        };
    }

    async function awardHarvest(characterId: string): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (state.outcome === null || state.harvestInventoryAwarded) return;

        const awarded = state.outcome.components.filter(component => component.awarded);
        if (awarded.length === 0) {
            update(current => ({
                ...current,
                harvestInventoryStatus: "error",
                message: "No harvested components are available to add to Inventory."
            }));
            return;
        }
        if (awarded.some(component => component.quantity === null)) {
            update(current => ({
                ...current,
                harvestInventoryStatus: "error",
                message: "Set a quantity for every harvested component before adding the harvest to Inventory."
            }));
            return;
        }

        update(current => ({
            ...current,
            harvestInventoryStatus: "loading",
            message: undefined
        }));
        const changed = await routine.mutate(
            "inventory-add",
            () => applyInventoryTransaction(
                environment,
                characterId,
                {
                    additions: awarded.map(component => ({
                        customName: component.displayName,
                        quantity: component.quantity!
                    }))
                }),
            undefined,
            { render: false });
        if (!changed) {
            update(current => ({
                ...current,
                harvestInventoryStatus: "error",
                message: routine.current().mutationError
                    ?? "Harvested components could not be added to Inventory."
            }));
            return;
        }

        await presentation.load(characterId);
        update(current => ({
            ...current,
            harvestInventoryStatus: "ready",
            harvestInventoryAwarded: true,
            message: `Added ${awarded.length} harvested component type${awarded.length === 1 ? "" : "s"} to Inventory.`
        }));
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
        if (state.craftingProcedure === "manufacturing"
            && state.craftingManufacturingAbilityMode === "character"
            && state.craftingManufacturingAbilityKey.trim().length === 0) {
            update(current => ({
                ...current,
                craftingStatus: "error",
                message: "Choose the Ability required by the Manufacturing tool/product, or use a manual Ability modifier."
            }));
            return;
        }
        if (state.craftingProcedure === "manufacturing"
            && state.craftingManufacturingAbilityMode === "manual"
            && state.craftingManufacturingManualAbilityModifier === null) {
            update(current => ({
                ...current,
                craftingStatus: "error",
                message: "Enter the manual Ability modifier required for this Manufacturing check."
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
                        hasQualifiedGuidance: state.craftingHasQualifiedGuidance,
                        abilityKey: state.craftingManufacturingAbilityMode === "character"
                            ? state.craftingManufacturingAbilityKey.trim()
                            : null,
                        manualAbilityModifier: state.craftingManufacturingAbilityMode === "manual"
                            ? state.craftingManufacturingManualAbilityModifier
                            : null
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
                craftingManufacturingSucceeded:
                    state.craftingProcedure === "manufacturing"
                        && d20Roll !== null
                        && resolution.outcome !== "pending"
                        ? resolution.producesFunctionalOutput
                        : current.craftingManufacturingSucceeded,
                craftingEnchantingSucceeded:
                    state.craftingProcedure === "enchanting"
                        && d20Roll !== null
                        && resolution.outcome !== "pending"
                        ? resolution.producesFunctionalOutput
                        : current.craftingEnchantingSucceeded,
                craftingCompletionStatus: d20Roll !== null
                    ? "idle"
                    : current.craftingCompletionStatus,
                craftingCompleted: d20Roll !== null
                    ? false
                    : current.craftingCompleted,
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

    function inferredCampaignScope(): string | null {
        const screen = application.getState().screen;
        if (screen.kind !== "rich-character" && screen.kind !== "archived") {
            return null;
        }

        const campaignIds = screen.character.campaignIds;
        if (campaignIds.length === 0) return null;

        const current = application.getState().harvestingCrafting.scopeCampaignId;
        return current !== null && campaignIds.includes(current)
            ? current
            : campaignIds[0] ?? null;
    }

    function typeTable(
        catalog: HarvestingRulesCatalogResponse,
        creatureType: string
    ): HarvestingResolvedTableResponse | null {
        const definition = catalog.creatureTypes.find(value => value.key === creatureType);
        if (definition === undefined) return null;
        return {
            source: catalog.source,
            creatureType: definition.key,
            creatureTypeDisplayName: definition.displayName,
            competencyKey: definition.competencyKey,
            competencyDisplayName: definition.competencyDisplayName,
            components: definition.defaultComponents.map(component => ({ ...component })),
            creatureConceptKey: null,
            creatureDisplayName: null,
            creatureSize: null,
            creatureOverridesApplied: false,
            manualEditsApplied: false
        };
    }

    function selectCreatureType(
        state: HarvestingCraftingUiState,
        creatureType: string
    ): HarvestingCraftingUiState {
        const table = state.catalog === null
            ? null
            : typeTable(state.catalog, creatureType);
        return {
            ...clearResolution(state),
            creatureType,
            tableStatus: table === null ? "idle" : "ready",
            tableRequest: table === null ? null : { creatureType },
            table,
            harvestOrder: table?.components.map(component => component.key) ?? []
        };
    }

    async function ensureCatalog(): Promise<void> {
        const current = application.getState().harvestingCrafting;
        if (current.catalogStatus === "loading" || current.catalogStatus === "ready") return;

        update(state => ({ ...state, catalogStatus: "loading", message: undefined }));
        try {
            const catalog = await loadHarvestingRules(environment);
            update(state => {
                const creatureType = state.creatureType.length > 0
                    ? state.creatureType
                    : (catalog.creatureTypes[0]?.key ?? "");
                const table = state.sourceKind === "creature-type"
                    ? typeTable(catalog, creatureType)
                    : null;
                return {
                    ...state,
                    catalogStatus: "ready",
                    catalog,
                    creatureType,
                    craftingCreatureType: state.craftingCreatureType.length > 0
                        ? state.craftingCreatureType
                        : (catalog.creatureTypes[0]?.key ?? ""),
                    tableStatus: table === null ? state.tableStatus : "ready",
                    tableRequest: table === null ? state.tableRequest : { creatureType },
                    table: table ?? state.table,
                    harvestOrder: table?.components.map(component => component.key)
                        ?? state.harvestOrder,
                    message: undefined
                };
            });
        } catch (error) {
            update(state => ({
                ...state,
                catalogStatus: "error",
                message: requestErrorMessage(error)
            }));
        }
    }

    async function ensureCampaignContext(campaignId: string | null): Promise<void> {
        if (campaignId === null) {
            update(state => ({
                ...state,
                campaignContextStatus: "idle",
                campaignCharacters: []
            }));
            return;
        }

        update(state => ({
            ...state,
            campaignContextStatus: "loading",
            campaignCharacters: []
        }));
        try {
            const context = await loadHostedCampaignContext(environment, campaignId);
            update(state => ({
                ...state,
                campaignContextStatus: "ready",
                campaignCharacters: context.characters.map(character => ({
                    characterId: character.characterId,
                    name: character.name
                })),
                message: undefined
            }));
        } catch (error) {
            update(state => ({
                ...state,
                campaignContextStatus: "error",
                campaignCharacters: [],
                message: `Campaign helper roster could not be loaded. Manual helper entry remains available. ${requestErrorMessage(error)}`
            }));
        }
    }

    function numericMechanicalValue(value: unknown): number | null {
        if (typeof value === "number" && Number.isFinite(value)) return value;
        if (typeof value !== "string") return null;
        const normalized = value.trim().replace(/^\+/, "");
        if (!/^-?\d+$/.test(normalized)) return null;
        return Number.parseInt(normalized, 10);
    }

    async function setHelperCharacter(
        index: number,
        characterId: string | null
    ): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (index < 0 || index >= state.helpers.length) return;

        if (characterId === null || characterId.trim().length === 0) {
            update(current => {
                const helpers = [...current.helpers];
                helpers[index] = {
                    ...helpers[index]!,
                    characterId: null,
                    displayName: null,
                    source: "manual",
                    resolutionStatus: "idle"
                };
                return {
                    ...current,
                    helpers,
                    outcomeStatus: "idle",
                    outcome: null,
                    message: undefined
                };
            });
            return;
        }

        const normalized = characterId.trim();
        const selected = state.campaignCharacters.find(
            character => character.characterId === normalized);
        update(current => {
            const helpers = [...current.helpers];
            helpers[index] = {
                ...helpers[index]!,
                characterId: normalized,
                displayName: selected?.name ?? "Campaign Character",
                source: "campaign-character",
                resolutionStatus: "loading"
            };
            return { ...current, helpers, outcomeStatus: "idle", outcome: null };
        });

        try {
            const presentation = await loadCharacterPresentation(environment, normalized);
            const mechanics = presentation.mechanics;
            const proficiency = mechanics?.combatFundamentals?.find(value =>
                value.key === "proficiency.standard"
                || value.key === "proficiency-bonus"
                || value.label.trim().toLowerCase() === "proficiency bonus");
            const proficiencyBonus = numericMechanicalValue(proficiency?.effectiveValue);
            const competencyKey = application.getState().harvestingCrafting.table?.competencyKey;
            const competency = competencyKey === undefined
                ? undefined
                : mechanics?.competencies?.entries.find(value =>
                    value.key === competencyKey
                    || value.identityKey === competencyKey.replace(/^competency\./, ""));
            const training = competency?.training?.trim().toLowerCase();
            const isProficient = training !== undefined
                && (training.includes("proficient") || training.includes("trained"));

            if (proficiencyBonus === null || competency === undefined) {
                update(current => {
                    const helpers = [...current.helpers];
                    helpers[index] = {
                        ...helpers[index]!,
                        resolutionStatus: "error"
                    };
                    return {
                        ...current,
                        helpers,
                        message: "The selected campaign Character does not expose enough resolved helper mechanics. Enter the helper values manually."
                    };
                });
                return;
            }

            update(current => {
                const helpers = [...current.helpers];
                helpers[index] = {
                    ...helpers[index]!,
                    proficiencyBonus,
                    isProficient,
                    resolutionStatus: "ready"
                };
                return {
                    ...current,
                    helpers,
                    outcomeStatus: "idle",
                    outcome: null,
                    message: undefined
                };
            });
        } catch (error) {
            update(current => {
                const helpers = [...current.helpers];
                helpers[index] = {
                    ...helpers[index]!,
                    resolutionStatus: "error"
                };
                return {
                    ...current,
                    helpers,
                    message: `The selected campaign Character could not be resolved. Manual helper entry remains available. ${requestErrorMessage(error)}`
                };
            });
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
            harvestInventoryStatus: "idle",
            harvestInventoryAwarded: false,
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

    async function applyHarvestTableRequest(
        request: HarvestingTableResolutionInput,
        clearDraft = false
    ): Promise<void> {
        const state = application.getState().harvestingCrafting;
        const previousOrder = state.harvestOrder;
        update(current => ({
            ...current,
            tableStatus: "loading",
            outcomeStatus: "idle",
            outcome: null,
            harvestInventoryStatus: "idle",
            harvestInventoryAwarded: false,
            message: undefined
        }));
        try {
            const table = await resolveHarvestingTable(
                environment,
                request,
                state.scopeCampaignId);
            const availableKeys = new Set(table.components.map(component => component.key));
            const retainedOrder = previousOrder.filter(key => availableKeys.has(key));
            const retainedKeys = new Set(retainedOrder);
            const harvestOrder = [
                ...retainedOrder,
                ...table.components
                    .map(component => component.key)
                    .filter(key => !retainedKeys.has(key))
            ];
            update(current => ({
                ...current,
                tableStatus: "ready",
                tableRequest: request,
                table,
                creatureSize: table.creatureSize ?? current.creatureSize,
                harvestOrder,
                harvestManualComponentName: clearDraft ? "" : current.harvestManualComponentName,
                harvestManualComponentDc: clearDraft ? null : current.harvestManualComponentDc,
                harvestManualComponentQuantity: clearDraft ? null : current.harvestManualComponentQuantity,
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

    function nextHarvestEdits(
        state: HarvestingCraftingUiState,
        key: string,
        edit: HarvestingComponentEditInput | null,
        remove: boolean
    ): HarvestingTableResolutionInput["manualEdits"] {
        const previous = state.tableRequest?.manualEdits;
        const removals = new Set(previous?.removeComponentKeys ?? []);
        const upserts = (previous?.upsertComponents ?? [])
            .filter(value => value.key !== key);

        if (remove) {
            removals.add(key);
        } else {
            removals.delete(key);
            if (edit !== null) upserts.push(edit);
        }

        return {
            removeComponentKeys: [...removals],
            upsertComponents: upserts
        };
    }

    async function editHarvestComponent(
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
            update(current => ({
                ...current,
                message: "Component DC and quantity must be positive whole numbers."
            }));
            return;
        }

        await applyHarvestTableRequest({
            ...request,
            manualEdits: nextHarvestEdits(
                state,
                key,
                {
                    key,
                    displayName: component.displayName,
                    componentDc,
                    quantity
                },
                false)
        });
    }

    async function removeHarvestComponent(key: string): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (state.tableRequest === null) return;
        await applyHarvestTableRequest({
            ...state.tableRequest,
            manualEdits: nextHarvestEdits(state, key, null, true)
        });
    }

    async function addHarvestComponent(): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (state.tableRequest === null) return;
        const displayName = state.harvestManualComponentName.trim();
        const componentDc = state.harvestManualComponentDc;
        const quantity = state.harvestManualComponentQuantity;
        if (displayName.length === 0 || componentDc === null
            || !Number.isInteger(componentDc) || componentDc <= 0
            || (quantity !== null && (!Number.isInteger(quantity) || quantity <= 0))) {
            update(current => ({
                ...current,
                message: "Enter a component name, a positive whole-number Component DC, and an optional positive whole-number quantity."
            }));
            return;
        }

        const slug = displayName.toLowerCase()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
        if (slug.length === 0) {
            update(current => ({
                ...current,
                message: "The component name can not be converted to a usable key."
            }));
            return;
        }
        const key = `manual-${slug}`;
        if (state.table?.components.some(component => component.key === key)) {
            update(current => ({
                ...current,
                message: "A manual component with that name already exists. Edit the existing row instead."
            }));
            return;
        }

        await applyHarvestTableRequest({
            ...state.tableRequest,
            manualEdits: nextHarvestEdits(
                state,
                key,
                { key, displayName, componentDc, quantity },
                false)
        }, true);
    }

    async function resetHarvestEdits(): Promise<void> {
        const state = application.getState().harvestingCrafting;
        if (state.tableRequest === null) return;
        const { manualEdits: _manualEdits, ...baseRequest } = state.tableRequest;
        await applyHarvestTableRequest(baseRequest, true);
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
                    helpers: state.helpers.map(helper => ({
                        proficiencyBonus: helper.proficiencyBonus,
                        isProficient: helper.isProficient,
                        participatedForEntireDuration: helper.participatedForEntireDuration,
                        isAssessmentParticipant: helper.isAssessmentParticipant,
                        isCarvingParticipant: helper.isCarvingParticipant
                    }))
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

    async function completeCrafting(characterId: string): Promise<void> {
        const state = application.getState().harvestingCrafting;
        const recipeName = state.craftingRecipeName.trim();
        const outputName = state.craftingOutputName.trim();

        if (recipeName.length === 0 || outputName.length === 0) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: "Enter a recipe name and crafted output before completing Crafting."
            }));
            return;
        }
        if (!Number.isInteger(state.craftingOutputQuantity) || state.craftingOutputQuantity <= 0) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: "Crafted output quantity must be a positive whole number."
            }));
            return;
        }
        if (!state.craftingRequiresManufacturing && !state.craftingRequiresEnchanting) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: "The recipe must require Manufacturing, Enchanting, or both."
            }));
            return;
        }
        if (state.craftingRequiresManufacturing
            && state.craftingManufacturingSucceeded === null) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: "Resolve the required Manufacturing check before finalizing the recipe attempt."
            }));
            return;
        }

        if (state.craftingRequiresManufacturing) {
            if (state.craftingManufacturingRequiredHours === null
                || state.craftingManufacturingRequiredHours <= 0) {
                update(current => ({
                    ...current,
                    craftingCompletionStatus: "error",
                    message: "Enter the required Manufacturing time for this recipe."
                }));
                return;
            }
            if (state.craftingManufacturingCompletedHours
                < state.craftingManufacturingRequiredHours) {
                update(current => ({
                    ...current,
                    craftingCompletionStatus: "error",
                    message: "Complete the required Manufacturing time before finalizing the recipe attempt."
                }));
                return;
            }
        }

        if (state.craftingRequiresEnchanting) {
            if (state.craftingEnchantingRequiredHours === null
                || state.craftingEnchantingRequiredHours <= 0) {
                update(current => ({
                    ...current,
                    craftingCompletionStatus: "error",
                    message: "Enter the required Enchanting time for this recipe."
                }));
                return;
            }
            if (state.craftingEnchantingCompletedHours
                < state.craftingEnchantingRequiredHours) {
                update(current => ({
                    ...current,
                    craftingCompletionStatus: "error",
                    message: "Complete the required Enchanting time before finalizing the recipe attempt."
                }));
                return;
            }
        }

        const manufacturingFailed =
            state.craftingRequiresManufacturing
            && state.craftingManufacturingSucceeded === false;
        if (!manufacturingFailed
            && state.craftingRequiresEnchanting
            && state.craftingEnchantingSucceeded === null) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: "Resolve the required Enchanting check before finalizing the recipe attempt."
            }));
            return;
        }

        const producesOutput =
            (!state.craftingRequiresManufacturing
                || state.craftingManufacturingSucceeded === true)
            && (!state.craftingRequiresEnchanting
                || state.craftingEnchantingSucceeded === true);

        const inventory = routine.current().state?.inventoryItemOccurrences ?? [];
        for (const material of state.craftingMaterials) {
            const occurrence = inventory.find(item => item.id === material.occurrenceId);
            if (occurrence === undefined || material.quantity <= 0 || occurrence.quantity < material.quantity) {
                update(current => ({
                    ...current,
                    craftingCompletionStatus: "error",
                    message: "One or more recipe materials are unavailable in the required quantity."
                }));
                return;
            }
        }

        update(current => ({
            ...current,
            craftingCompletionStatus: "loading",
            message: undefined
        }));

        const changed = await routine.mutate(
            "inventory-transaction",
            () => applyInventoryTransaction(
                environment,
                characterId,
                {
                    consumptions: state.craftingMaterials.map(material => ({
                        occurrenceId: material.occurrenceId,
                        quantity: material.quantity
                    })),
                    additions: producesOutput
                        ? [{
                            customName: outputName,
                            quantity: state.craftingOutputQuantity
                        }]
                        : []
                }),
            undefined,
            { render: false });

        if (!changed) {
            update(current => ({
                ...current,
                craftingCompletionStatus: "error",
                message: routine.current().mutationError
                    ?? "The crafting inventory transaction could not be completed."
            }));
            return;
        }

        await presentation.load(characterId);
        update(current => ({
            ...current,
            craftingCompletionStatus: "ready",
            craftingCompleted: true,
            message: producesOutput
                ? `${recipeName} completed. Inventory materials were consumed and ${state.craftingOutputQuantity} × ${outputName} was added.`
                : `${recipeName} did not produce a functional output. The selected inputs were consumed according to the resolved crafting outcome.`
        }));
    }

    return {
        open(): void {
            const campaignId = inferredCampaignScope();
            update(state => ({
                ...state,
                open: true,
                scopeCampaignId: campaignId,
                message: undefined
            }));
            void ensureCatalog();
            void ensureCampaignContext(campaignId);
        },

        close(): void {
            update(state => ({ ...state, open: false, message: undefined }));
        },

        setMode(mode): void {
            update(state => ({ ...state, mode, message: undefined }));
        },

        setScope(campaignId): void {
            update(state => {
                const scoped = {
                    ...state,
                    scopeCampaignId: campaignId,
                    campaignContextStatus: campaignId === null
                        ? "idle" as const
                        : state.campaignContextStatus,
                    campaignCharacters: campaignId === null ? [] : state.campaignCharacters
                };
                const harvesting = state.sourceKind === "creature-type"
                    ? selectCreatureType(scoped, state.creatureType)
                    : clearResolution(scoped);
                return clearCraftingResult(harvesting);
            });
            void ensureCampaignContext(campaignId);
            const current = application.getState().harvestingCrafting;
            if (current.sourceKind === "monster"
                && current.creatureConceptKey.trim().length > 0) {
                void resolveTable();
            }
        },

        setSourceKind(kind): void {
            update(state => {
                if (kind === "creature-type") {
                    return selectCreatureType(
                        {
                            ...state,
                            sourceKind: kind,
                            creatureConceptKey: ""
                        },
                        state.creatureType);
                }
                return clearResolution({
                    ...state,
                    sourceKind: kind
                });
            });
            if (kind === "monster"
                && application.getState().harvestingCrafting.monsterStatus === "idle") {
                void searchMonsters("");
            }
        },

        setCreatureType(creatureType): void {
            update(state => selectCreatureType(state, creatureType));
        },

        searchMonsters,

        selectMonster(conceptKey): void {
            const normalized = conceptKey.trim();
            update(state => clearResolution({
                ...state,
                creatureConceptKey: normalized
            }));
            if (normalized.length > 0) {
                void resolveTable();
            }
        },

        resolveTable,

        setHarvestManualComponentName(value): void {
            update(state => ({ ...state, harvestManualComponentName: value, message: undefined }));
        },

        setHarvestManualComponentDc(value): void {
            update(state => ({ ...state, harvestManualComponentDc: value, message: undefined }));
        },

        setHarvestManualComponentQuantity(value): void {
            update(state => ({ ...state, harvestManualComponentQuantity: value, message: undefined }));
        },

        editHarvestComponent,
        removeHarvestComponent,
        addHarvestComponent,
        resetHarvestEdits,

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
                        isCarvingParticipant: false,
                        characterId: null,
                        displayName: null,
                        source: "manual",
                        resolutionStatus: "idle"
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

        setHelperCharacter,

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
        awardHarvest,

        setCraftingProcedure(value): void {
            update(state => {
                if (state.craftingProcedure === value) return state;
                return clearCraftingResult({
                    ...state,
                    craftingProcedure: value,
                    craftingCompetencyMode: "resolved",
                    craftingCompetencyKey: "",
                    craftingManualName: "",
                    craftingManualContribution: null,
                    craftingManualQualified: false,
                    craftingTargetDc: null,
                    craftingOtherModifier: 0
                });
            });
        },

        setCraftingCompetencyMode(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingCompetencyMode: value
            })));
        },

        setCraftingCompetencyKey(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingCompetencyKey: value
            })));
        },

        setCraftingManualName(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManualName: value
            })));
        },

        setCraftingManualContribution(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManualContribution: value
            })));
        },

        setCraftingManualQualified(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManualQualified: value
            })));
        },

        setCraftingHasQualifiedGuidance(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingHasQualifiedGuidance: value
            })));
        },

        setCraftingManufacturingAbilityMode(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManufacturingAbilityMode: value
            })));
        },

        setCraftingManufacturingAbilityKey(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManufacturingAbilityKey: value
            })));
        },

        setCraftingManufacturingManualAbilityModifier(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingManufacturingManualAbilityModifier: value
            })));
        },

        setCraftingCreatureType(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingCreatureType: value
            })));
        },

        setCraftingSpellcastingKey(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingSpellcastingKey: value
            })));
        },

        setCraftingTargetDc(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingTargetDc: value
            })));
        },

        setCraftingOtherModifier(value): void {
            update(state => clearCraftingResult(invalidateCurrentCraftingStage({
                ...state,
                craftingOtherModifier: value
            })));
        },

        async prepareCrafting(characterId): Promise<void> {
            await resolveCrafting(characterId, null);
        },

        setCraftingSelectedRoll(value): void {
            update(state => ({
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

        async submitCraftingRoll(characterId): Promise<void> {
            const value = application.getState().harvestingCrafting.craftingSelectedRoll;
            if (value === null || !Number.isInteger(value) || value < 1 || value > 20) {
                update(state => ({
                    ...state,
                    craftingStatus: "error",
                    message: "Enter a whole-number d20 result from 1 through 20."
                }));
                return;
            }
            await resolveCrafting(characterId, value);
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
        },

        setCraftingRecipeName(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingRecipeName: value
            }));
        },

        setCraftingOutputName(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingOutputName: value
            }));
        },

        setCraftingOutputQuantity(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingOutputQuantity: value
            }));
        },

        setCraftingRequiresManufacturing(value): void {
            update(state => {
                const procedure = !value
                    && state.craftingProcedure === "manufacturing"
                    && state.craftingRequiresEnchanting
                    ? "enchanting"
                    : value
                        && !state.craftingRequiresEnchanting
                        ? "manufacturing"
                        : state.craftingProcedure;
                const changedStage = procedure !== state.craftingProcedure;
                const next = resetCraftingCompletion({
                    ...state,
                    craftingRequiresManufacturing: value,
                    craftingProcedure: procedure
                });
                return changedStage
                    ? clearCraftingResult({
                        ...next,
                        craftingCompetencyMode: "resolved",
                        craftingCompetencyKey: "",
                        craftingManualName: "",
                        craftingManualContribution: null,
                        craftingManualQualified: false,
                        craftingTargetDc: null,
                        craftingOtherModifier: 0
                    })
                    : next;
            });
        },

        setCraftingRequiresEnchanting(value): void {
            update(state => {
                const procedure = !value
                    && state.craftingProcedure === "enchanting"
                    && state.craftingRequiresManufacturing
                    ? "manufacturing"
                    : value
                        && !state.craftingRequiresManufacturing
                        ? "enchanting"
                        : state.craftingProcedure;
                const changedStage = procedure !== state.craftingProcedure;
                const next = resetCraftingCompletion({
                    ...state,
                    craftingRequiresEnchanting: value,
                    craftingProcedure: procedure
                });
                return changedStage
                    ? clearCraftingResult({
                        ...next,
                        craftingCompetencyMode: "resolved",
                        craftingCompetencyKey: "",
                        craftingManualName: "",
                        craftingManualContribution: null,
                        craftingManualQualified: false,
                        craftingTargetDc: null,
                        craftingOtherModifier: 0
                    })
                    : next;
            });
        },

        setCraftingManufacturingRequiredHours(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingManufacturingRequiredHours: value
            }));
        },

        setCraftingManufacturingCompletedHours(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingManufacturingCompletedHours: Math.max(0, value)
            }));
        },

        setCraftingEnchantingRequiredHours(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingEnchantingRequiredHours: value
            }));
        },

        setCraftingEnchantingCompletedHours(value): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingEnchantingCompletedHours: Math.max(0, value)
            }));
        },

        addCraftingMaterial(occurrenceId): void {
            const normalized = occurrenceId.trim();
            if (normalized.length === 0) return;
            update(state => {
                if (state.craftingMaterials.some(material => material.occurrenceId === normalized)) {
                    return state;
                }
                return resetCraftingCompletion({
                    ...state,
                    craftingMaterials: [
                        ...state.craftingMaterials,
                        { occurrenceId: normalized, quantity: 1 }
                    ]
                });
            });
        },

        updateCraftingMaterial(index, quantity): void {
            update(state => {
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
        },

        removeCraftingMaterial(index): void {
            update(state => resetCraftingCompletion({
                ...state,
                craftingMaterials: state.craftingMaterials.filter((_, current) => current !== index)
            }));
        },

        completeCrafting
    };
}
