import type {
    CharacterRoutineUiState,
    HarvestingCraftingUiState
} from "../../app-state.js";
import type { CharacterSheetBootstrapResponse } from "../../character-api.js";
import type { CharacterMechanicsView } from "../../ui/character-mechanics.js";
import type { HarvestingCraftingWorkflow } from "./harvesting-workflow.js";
import type {
    HarvestingComponentResponse
} from "../../rules-core-api.js";
import {
    createButton,
    createElement,
    createInlineState,
    createSectionCard
} from "../../ui/components.js";
import { toRuleReferenceDisplay } from "../../ui/sheet-model.js";

const SIZE_OPTIONS = ["Tiny", "Small", "Medium", "Large", "Huge", "Gargantuan"];

export function renderHarvestingCraftingWorkspace(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null = null,
    routine: CharacterRoutineUiState | null = null
): HTMLElement {
    const workspace = createElement("section", "dd-harvesting-workspace");
    workspace.setAttribute("data-harvesting-crafting-workspace", "true");

    const header = createElement("header", "dd-harvesting-workspace__header");
    const heading = createElement("div", "dd-harvesting-workspace__heading");
    heading.append(
        createElement("h2", "dd-harvesting-workspace__title", "Harvesting & Crafting"),
        renderRulesSource(state));
    header.append(
        heading,
        createButton(
            "Back to Character Sheet",
            "dd-button dd-button--ghost",
            handlers.close));
    workspace.append(header);

    const modeNav = createElement("div", "dd-harvesting-workspace__mode-nav");
    modeNav.setAttribute("role", "tablist");
    for (const mode of ["harvesting", "crafting"] as const) {
        const active = state.mode === mode;
        const button = createButton(
            mode === "harvesting" ? "Harvesting" : "Crafting",
            active
                ? "dd-button dd-button--secondary dd-harvesting-workspace__mode--active"
                : "dd-button dd-button--ghost",
            () => handlers.setMode(mode));
        button.setAttribute("role", "tab");
        button.setAttribute("aria-selected", active ? "true" : "false");
        button.setAttribute("data-harvesting-mode", mode);
        modeNav.append(button);
    }
    workspace.append(modeNav);

    const campaignContext = renderCampaignContext(character, state, handlers);
    if (campaignContext !== null) workspace.append(campaignContext);

    if (state.message !== undefined) {
        workspace.append(createInlineState(
            state.message,
            state.tableStatus === "error" || state.outcomeStatus === "error"
                || state.catalogStatus === "error" || state.monsterStatus === "error"
                || state.craftingStatus === "error"
                ? "error"
                : "warning"));
    }

    if (state.mode === "crafting") {
        workspace.append(renderCraftingPanel(
            character,
            state,
            handlers,
            readOnly,
            mechanics,
            routine));
        return workspace;
    }

    workspace.append(renderHarvestingPanel(character, state, handlers, readOnly));
    return workspace;
}

export function renderHarvestingLauncher(
    handlers: Pick<HarvestingCraftingWorkflow, "open">,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard(
        "Harvesting & Crafting",
        "dd-harvesting-launcher");
    card.setAttribute("data-harvesting-crafting-launcher", "true");
    card.append(
        createElement(
            "p",
            "dd-harvesting-launcher__copy",
            "Harvest components from creatures and craft items using the Loot Tavern Harvesting & Crafting rules."),
        createButton(
            "Open Harvesting & Crafting",
            "dd-button dd-button--secondary",
            handlers.open,
            readOnly));
    return card;
}

function renderCampaignContext(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow
): HTMLElement | null {
    const campaigns = character.campaigns ?? [];
    if (campaigns.length <= 1) return null;

    const card = createSectionCard("Campaign", "dd-harvesting-scope");
    const label = createElement("label", "dd-harvesting-field");
    label.append(createElement(
        "span",
        "dd-harvesting-field__label",
        "This attempt belongs to"));

    const select = createElement("select", "dd-harvesting-field__control");
    for (const campaign of campaigns) {
        const option = createElement("option");
        option.value = campaign.campaignId;
        option.textContent = campaign.name;
        select.append(option);
    }
    select.value = state.scopeCampaignId ?? campaigns[0]?.campaignId ?? "";
    select.addEventListener("change", () => handlers.setScope(select.value));
    label.append(select);
    card.append(
        label,
        createElement(
            "p",
            "dd-harvesting-field__help",
            "This Character belongs to more than one campaign. Choose the campaign for this Harvesting or Crafting attempt."));
    return card;
}

function renderHarvestingPanel(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const panel = createElement("div", "dd-harvesting-workspace__panel");

    if (state.catalogStatus === "loading" || state.catalogStatus === "idle") {
        panel.append(createInlineState("Loading Harvesting rules…", "loading"));
        return panel;
    }
    if (state.catalog === null) {
        panel.append(createInlineState(
            "Harvesting rules could not be loaded.",
            "error"));
        return panel;
    }

    panel.append(renderCreatureSelection(state, handlers));

    if (state.tableStatus === "loading") {
        panel.append(createInlineState("Loading harvestable components…", "loading"));
        return panel;
    }
    if (state.table === null) {
        return panel;
    }

    panel.append(
        renderAvailableHarvestComponents(state, handlers, readOnly),
        renderHarvestingInputs(state, handlers, readOnly),
        renderHelperInputs(state, handlers, readOnly),
        renderHarvestOrder(state, handlers, readOnly),
        renderOutcome(character, state, handlers, readOnly));

    return panel;
}

function renderCreatureSelection(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow
): HTMLElement {
    const card = createSectionCard("Creature", "dd-harvesting-creature");

    const source = createElement("div", "dd-harvesting-choice-row");
    source.append(
        sourceButton(
            "Creature type",
            state.sourceKind === "creature-type",
            () => handlers.setSourceKind("creature-type")),
        sourceButton(
            "Specific creature",
            state.sourceKind === "monster",
            () => handlers.setSourceKind("monster")));
    card.append(source);

    if (state.sourceKind === "creature-type") {
        const label = createElement("label", "dd-harvesting-field");
        label.append(createElement("span", "dd-harvesting-field__label", "Creature type"));
        const select = createElement("select", "dd-harvesting-field__control");
        for (const creatureType of state.catalog?.creatureTypes ?? []) {
            const option = createElement("option");
            option.value = creatureType.key;
            option.textContent = creatureType.displayName;
            select.append(option);
        }
        select.value = state.creatureType;
        select.addEventListener("change", () => handlers.setCreatureType(select.value));
        label.append(select);
        card.append(label);
    } else {
        const searchRow = createElement("div", "dd-harvesting-search");
        const input = createElement("input", "dd-harvesting-field__control");
        input.type = "search";
        input.placeholder = "Search creatures";
        input.value = state.monsterQuery;
        input.setAttribute("aria-label", "Search creatures");
        const search = createButton(
            state.monsterStatus === "loading" ? "Searching…" : "Search",
            "dd-button dd-button--secondary",
            () => void handlers.searchMonsters(input.value),
            state.monsterStatus === "loading");
        searchRow.append(input, search);
        card.append(searchRow);

        if (state.monsterResults.length > 0) {
            const label = createElement("label", "dd-harvesting-field");
            label.append(createElement("span", "dd-harvesting-field__label", "Creature"));
            const select = createElement("select", "dd-harvesting-field__control");
            const placeholder = createElement("option");
            placeholder.value = "";
            placeholder.textContent = "Choose a creature";
            select.append(placeholder);
            for (const monster of state.monsterResults) {
                const option = createElement("option");
                option.value = monster.conceptKey;
                option.textContent = monster.displayName;
                select.append(option);
            }
            select.value = state.creatureConceptKey;
            select.addEventListener("change", () => handlers.selectMonster(select.value));
            label.append(select);
            card.append(label);
        } else if (state.monsterStatus === "ready") {
            card.append(createInlineState("No matching creatures found.", "neutral"));
        }
    }

    if (state.table !== null) {
        const facts = createElement("dl", "dd-harvesting-facts");
        appendFact(
            facts,
            "Harvesting skill",
            state.table.competencyDisplayName);
        if (state.table.creatureSize !== null) {
            appendFact(facts, "Size", state.table.creatureSize);
        }
        card.append(facts);
    }

    return card;
}

function renderAvailableHarvestComponents(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard("Available Components", "dd-harvesting-components");
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        "Adjust this specific corpse before building the harvest list. Removing or editing a component applies only to this Harvesting attempt."));

    for (const component of state.table?.components ?? []) {
        const row = createElement("div", "dd-harvesting-helper");
        row.append(createElement("strong", "dd-harvesting-helper__title", component.displayName));

        const dc = createElement("input", "dd-harvesting-field__control");
        dc.type = "number";
        dc.min = "1";
        dc.step = "1";
        dc.value = String(component.componentDc);
        dc.disabled = readOnly;

        const quantity = createElement("input", "dd-harvesting-field__control");
        quantity.type = "number";
        quantity.min = "1";
        quantity.step = "1";
        quantity.value = component.quantity === null ? "" : String(component.quantity);
        quantity.placeholder = "GM decides";
        quantity.disabled = readOnly;

        const apply = async (): Promise<void> => {
            const parsedDc = Number.parseInt(dc.value, 10);
            const normalizedQuantity = quantity.value.trim();
            const parsedQuantity = normalizedQuantity.length === 0
                ? null
                : Number.parseInt(normalizedQuantity, 10);
            await handlers.editHarvestComponent(
                component.key,
                parsedDc,
                parsedQuantity);
        };
        dc.addEventListener("change", () => void apply());
        quantity.addEventListener("change", () => void apply());

        const dcLabel = createElement("label", "dd-harvesting-field");
        dcLabel.append(
            createElement("span", "dd-harvesting-field__label", "Component DC"),
            dc);
        const quantityLabel = createElement("label", "dd-harvesting-field");
        quantityLabel.append(
            createElement("span", "dd-harvesting-field__label", "Quantity"),
            quantity);

        row.append(
            dcLabel,
            quantityLabel,
            createButton(
                "Unavailable",
                "dd-button dd-button--ghost",
                () => void handlers.removeHarvestComponent(component.key),
                readOnly));
        card.append(row);
    }

    const add = createElement("div", "dd-harvesting-helper");
    add.append(createElement("strong", "dd-harvesting-helper__title", "Add encounter-specific component"));
    const name = createElement("input", "dd-harvesting-field__control");
    name.type = "text";
    name.placeholder = "Component name";
    name.value = state.harvestManualComponentName;
    name.disabled = readOnly;
    name.addEventListener("change", () =>
        handlers.setHarvestManualComponentName(name.value));

    const dc = createElement("input", "dd-harvesting-field__control");
    dc.type = "number";
    dc.min = "1";
    dc.step = "1";
    dc.placeholder = "Component DC";
    dc.value = state.harvestManualComponentDc === null
        ? ""
        : String(state.harvestManualComponentDc);
    dc.disabled = readOnly;
    dc.addEventListener("change", () =>
        handlers.setHarvestManualComponentDc(
            dc.value.trim().length === 0 ? null : Number.parseInt(dc.value, 10)));

    const quantity = createElement("input", "dd-harvesting-field__control");
    quantity.type = "number";
    quantity.min = "1";
    quantity.step = "1";
    quantity.placeholder = "Quantity (optional)";
    quantity.value = state.harvestManualComponentQuantity === null
        ? ""
        : String(state.harvestManualComponentQuantity);
    quantity.disabled = readOnly;
    quantity.addEventListener("change", () =>
        handlers.setHarvestManualComponentQuantity(
            quantity.value.trim().length === 0
                ? null
                : Number.parseInt(quantity.value, 10)));

    add.append(
        name,
        dc,
        quantity,
        createButton(
            "Add Component",
            "dd-button dd-button--secondary",
            () => void handlers.addHarvestComponent(),
            readOnly));
    card.append(add);

    if (state.table?.manualEditsApplied) {
        card.append(createButton(
            "Reset GM Component Edits",
            "dd-button dd-button--ghost",
            () => void handlers.resetHarvestEdits(),
            readOnly));
    }
    return card;
}

function renderHarvestingInputs(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard("Assessment & Carving", "dd-harvesting-checks");
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        "Enter the final Assessment and Carving totals. These can come from physical dice, another roller, or the Character's resolved modifiers."));

    const grid = createElement("div", "dd-harvesting-input-grid");
    grid.append(
        numberField(
            "Assessment result",
            state.assessmentResult,
            value => handlers.setAssessmentResult(value),
            readOnly),
        numberField(
            "Carving result",
            state.carvingResult,
            value => handlers.setCarvingResult(value),
            readOnly));

    const sizeLabel = createElement("label", "dd-harvesting-field");
    sizeLabel.append(createElement("span", "dd-harvesting-field__label", "Creature size"));
    const size = createElement("select", "dd-harvesting-field__control");
    const unknown = createElement("option");
    unknown.value = "";
    unknown.textContent = "Not specified";
    size.append(unknown);
    for (const optionName of SIZE_OPTIONS) {
        const option = createElement("option");
        option.value = optionName;
        option.textContent = optionName;
        size.append(option);
    }
    size.value = state.creatureSize;
    size.disabled = readOnly || state.table?.creatureSize !== null;
    size.addEventListener("change", () => handlers.setCreatureSize(size.value));
    sizeLabel.append(size);
    grid.append(sizeLabel);
    card.append(grid);

    const sameActor = createElement("label", "dd-harvesting-toggle");
    const checkbox = createElement("input");
    checkbox.type = "checkbox";
    checkbox.checked = state.sameActor;
    checkbox.disabled = readOnly;
    checkbox.addEventListener("change", () => handlers.setSameActor(checkbox.checked));
    sameActor.append(
        checkbox,
        createElement(
            "span",
            "",
            "The same character performs both Assessment and Carving"));
    card.append(sameActor);

    const competency = state.table?.competencyDisplayName ?? "the associated skill";
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        state.sameActor
            ? `The same character is doing both jobs, so both checks use disadvantage. Assessment uses Intelligence + ${competency}; Carving uses Dexterity + ${competency}.`
            : `Assessment uses Intelligence + ${competency}; Carving uses Dexterity + ${competency}.`));
    return card;
}

function renderHelperInputs(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard("Helpers", "dd-harvesting-helpers");
    const size = state.table?.creatureSize ?? state.creatureSize;
    const limit = size.length > 0
        ? state.catalog?.procedure.helpers.maximumByCreatureSize[size]
        : undefined;
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        limit === undefined
            ? "Choose a creature size before using helpers."
            : `${size} creatures allow up to ${limit} helper${limit === 1 ? "" : "s"}. Standard Help does not apply.`));

    state.helpers.forEach((helper, index) => {
        const row = createElement("div", "dd-harvesting-helper");
        row.append(createElement(
            "strong",
            "dd-harvesting-helper__title",
            helper.displayName?.trim().length
                ? helper.displayName
                : `Helper ${index + 1}`));

        if (state.scopeCampaignId !== null) {
            const source = createElement("label", "dd-harvesting-field");
            source.append(createElement("span", "dd-harvesting-field__label", "Campaign Character"));
            const select = createElement("select", "dd-harvesting-field__control");
            const manual = createElement("option");
            manual.value = "";
            manual.textContent = "Manual helper";
            select.append(manual);
            for (const character of state.campaignCharacters) {
                const option = createElement("option");
                option.value = character.characterId;
                option.textContent = character.name;
                select.append(option);
            }
            select.value = helper.characterId ?? "";
            select.disabled = readOnly || state.campaignContextStatus === "loading";
            select.addEventListener("change", () =>
                void handlers.setHelperCharacter(
                    index,
                    select.value.length === 0 ? null : select.value));
            source.append(select);
            row.append(source);

            if (helper.resolutionStatus === "loading") {
                row.append(createInlineState("Loading helper Character…", "loading"));
            } else if (helper.resolutionStatus === "error") {
                row.append(createInlineState(
                    "Helper Character data is incomplete. Enter the values below manually.",
                    "warning"));
            }
        }

        row.append(numberField(
                "Proficiency bonus",
                helper.proficiencyBonus,
                value => handlers.updateHelper(
                    index,
                    { ...helper, proficiencyBonus: value ?? 0 }),
                readOnly,
                0));
        row.append(booleanField(
            "Proficient with the required skill",
            helper.isProficient,
            value => handlers.updateHelper(index, { ...helper, isProficient: value }),
            readOnly));
        row.append(booleanField(
            "Participated for the entire duration",
            helper.participatedForEntireDuration !== false,
            value => handlers.updateHelper(
                index,
                { ...helper, participatedForEntireDuration: value }),
            readOnly));
        row.append(createButton(
            "Remove Helper",
            "dd-button dd-button--ghost",
            () => handlers.removeHelper(index),
            readOnly));
        card.append(row);
    });

    if (state.scopeCampaignId !== null && state.campaignContextStatus === "loading") {
        card.append(createInlineState("Loading campaign helper roster…", "loading"));
    } else if (state.scopeCampaignId !== null
        && state.campaignContextStatus === "ready"
        && state.campaignCharacters.length === 0) {
        card.append(createElement(
            "p",
            "dd-harvesting-field__help",
            "No active campaign-linked Characters are available. Manual helper entry remains available."));
    }

    card.append(createButton(
        "Add Helper",
        "dd-button dd-button--secondary",
        handlers.addHelper,
        readOnly || (limit !== undefined && state.helpers.length >= limit)));
    return card;
}

function renderHarvestOrder(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard("Harvest List", "dd-harvesting-order");
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        "Order matters. Each Component DC is added to the previous Component DCs to produce the cumulative Harvest DC."));

    const byKey = new Map(
        state.table?.components.map(component => [component.key, component]) ?? []);
    state.harvestOrder.forEach((key, index) => {
        const component = byKey.get(key);
        if (component === undefined) return;
        const row = createElement("div", "dd-harvesting-component");
        row.append(componentIdentity(component));
        const actions = createElement("div", "dd-harvesting-component__actions");
        actions.append(
            createButton(
                "Up",
                "dd-button dd-button--ghost",
                () => handlers.moveComponent(key, -1),
                readOnly || index === 0),
            createButton(
                "Down",
                "dd-button dd-button--ghost",
                () => handlers.moveComponent(key, 1),
                readOnly || index === state.harvestOrder.length - 1));
        row.append(actions);
        card.append(row);
    });

    card.append(createButton(
        state.outcomeStatus === "loading" ? "Calculating…" : "Calculate Harvest",
        "dd-button dd-button--primary",
        () => void handlers.evaluate(),
        readOnly || state.outcomeStatus === "loading"));
    return card;
}

function renderOutcome(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    if (state.outcomeStatus === "loading") {
        return createInlineState("Calculating the Harvesting result…", "loading");
    }
    if (state.outcome === null) {
        return createElement("div");
    }

    const outcome = state.outcome;
    const card = createSectionCard("Harvesting Result", "dd-harvesting-outcome");
    const summary = createElement("dl", "dd-harvesting-facts");
    appendFact(summary, "Assessment", String(outcome.assessmentResult));
    appendFact(summary, "Carving", String(outcome.carvingResult));
    appendFact(summary, "Helper contribution", signed(outcome.helperContribution));
    appendFact(summary, "Harvesting result", String(outcome.harvestingResult));
    card.append(summary);

    const list = createElement("div", "dd-harvesting-outcome__list");
    for (const component of outcome.components) {
        const item = createElement(
            "article",
            component.awarded
                ? "dd-harvesting-outcome__component dd-harvesting-outcome__component--awarded"
                : "dd-harvesting-outcome__component");
        item.setAttribute("data-harvest-awarded", component.awarded ? "true" : "false");
        item.append(
            createElement("strong", "", component.displayName),
            createElement(
                "span",
                "",
                `Component DC ${component.componentDc} · Harvest DC ${component.harvestDc}`),
            createElement(
                "span",
                "",
                component.awarded ? "Harvested" : "Not harvested"));
        list.append(item);
    }
    card.append(list);
    const awarded = outcome.components.filter(component => component.awarded);
    if (awarded.length > 0) {
        card.append(createButton(
            state.harvestInventoryStatus === "loading"
                ? "Adding to Inventory…"
                : state.harvestInventoryAwarded
                    ? "Added to Inventory"
                    : "Add Harvested Components to Inventory",
            "dd-button dd-button--secondary",
            () => void handlers.awardHarvest(character.characterId),
            readOnly
                || state.harvestInventoryStatus === "loading"
                || state.harvestInventoryAwarded));
    }
    return card;
}

function renderRulesSource(state: HarvestingCraftingUiState): HTMLElement {
    const source = state.catalog?.source;
    const line = createElement(
        "p",
        "dd-harvesting-workspace__source",
        source === undefined
            ? "Loading Harvesting & Crafting rules."
            : `Rules: ${source.provider} — ${source.workDisplayName}. `);
    if (source !== undefined && isSafeHttpsUrl(source.referenceUri)) {
        const link = document.createElement("a");
        link.href = source.referenceUri;
        link.target = "_blank";
        link.rel = "noopener noreferrer";
        link.textContent = "Official rules";
        line.append(link);
    }
    return line;
}

function isSafeHttpsUrl(value: string): boolean {
    try {
        return new URL(value).protocol === "https:";
    } catch {
        return false;
    }
}

function renderCraftingPanel(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null,
    routine: CharacterRoutineUiState | null
): HTMLElement {
    const panel = createElement("div", "dd-harvesting-workspace__panel");

    panel.append(renderCraftingProject(state, handlers, readOnly, routine));

    if (!state.craftingRequiresManufacturing && !state.craftingRequiresEnchanting) {
        panel.append(createInlineState(
            "Choose at least one crafting step: Manufacturing, Enchanting, or both.",
            "warning"));
        return panel;
    }

    if (state.craftingRequiresManufacturing && state.craftingRequiresEnchanting) {
        const steps = createSectionCard("Crafting Steps", "dd-crafting-procedure");
        const choices = createElement("div", "dd-harvesting-choice-row");
        choices.append(
            sourceButton(
                `1. Manufacturing · ${stageState(true, state.craftingManufacturingSucceeded)}`,
                state.craftingProcedure === "manufacturing",
                () => handlers.setCraftingProcedure("manufacturing")),
            sourceButton(
                `2. Enchanting · ${stageState(true, state.craftingEnchantingSucceeded)}`,
                state.craftingProcedure === "enchanting",
                () => handlers.setCraftingProcedure("enchanting")));
        steps.append(choices);
        panel.append(steps);
    }

    panel.append(renderCraftingStage(
        character,
        state,
        handlers,
        readOnly,
        mechanics));

    panel.append(renderCraftingCompletion(
        character,
        state,
        handlers,
        readOnly));

    return panel;
}

function renderCraftingStage(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const manufacturing = state.craftingProcedure === "manufacturing";
    const card = createSectionCard(
        manufacturing ? "Manufacturing" : "Enchanting",
        manufacturing ? "dd-crafting-manufacturing" : "dd-crafting-enchanting");

    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        manufacturing
            ? "Make the physical item. Choose the tool or competency, Ability, and DC required by the recipe."
            : "Enchant the finished item. Choose the component's creature type and the spellcasting source used for the check."));

    if (manufacturing) {
        card.append(renderManufacturingInputs(state, handlers, readOnly, mechanics));
        card.append(
            decimalField(
                "Manufacturing time required (hours)",
                state.craftingManufacturingRequiredHours,
                handlers.setCraftingManufacturingRequiredHours,
                readOnly,
                0.01),
            decimalField(
                "Manufacturing time completed (hours)",
                state.craftingManufacturingCompletedHours,
                value => handlers.setCraftingManufacturingCompletedHours(value ?? 0),
                readOnly,
                0));
    } else {
        card.append(renderEnchantingInputs(state, handlers, readOnly, mechanics));
        card.append(
            decimalField(
                "Enchanting time required (hours)",
                state.craftingEnchantingRequiredHours,
                handlers.setCraftingEnchantingRequiredHours,
                readOnly,
                0.01),
            decimalField(
                "Enchanting time completed (hours)",
                state.craftingEnchantingCompletedHours,
                value => handlers.setCraftingEnchantingCompletedHours(value ?? 0),
                readOnly,
                0));
    }

    card.append(
        numberField(
            "Crafting DC",
            state.craftingTargetDc,
            handlers.setCraftingTargetDc,
            readOnly,
            1),
        numberField(
            "Additional modifier",
            state.craftingOtherModifier,
            value => handlers.setCraftingOtherModifier(value ?? 0),
            readOnly));

    const actions = createElement("div", "dd-harvesting-choice-row");
    actions.append(
        numberField(
            "d20 result",
            state.craftingSelectedRoll,
            handlers.setCraftingSelectedRoll,
            readOnly,
            1),
        createButton(
            "Use Entered Roll",
            "dd-button dd-button--secondary",
            () => void handlers.submitCraftingRoll(character.characterId),
            readOnly
                || state.craftingSelectedRoll === null
                || state.craftingStatus === "loading"),
        createButton(
            state.craftingStatus === "loading"
                ? "Calculating…"
                : manufacturing
                    ? "Roll Manufacturing"
                    : "Roll Enchanting",
            "dd-button dd-button--primary",
            () => void handlers.rollCrafting(character.characterId),
            readOnly || state.craftingStatus === "loading"));
    card.append(actions);

    if (state.craftingStatus === "loading") {
        card.append(createInlineState("Calculating the crafting check…", "loading"));
        return card;
    }

    if (state.craftingResolution !== null) {
        const resolution = state.craftingResolution;
        const result = createElement("dl", "dd-harvesting-facts");
        const checkModifier =
            resolution.abilityContribution
            + resolution.competencyContribution
            + resolution.otherModifier;
        appendFact(result, "Tool / skill", resolution.competencyDisplayName);
        appendFact(result, "Check modifier", signed(checkModifier));
        if (resolution.rollMode !== "normal") {
            appendFact(result, "Roll", humanize(resolution.rollMode));
        }
        if (resolution.total !== null) {
            appendFact(result, "d20", String(resolution.d20Roll));
            appendFact(result, "Total", String(resolution.total));
            if (resolution.targetDc !== null) {
                appendFact(result, "DC", String(resolution.targetDc));
            }
            appendFact(result, "Outcome", humanize(resolution.outcome));
            if (resolution.flawCount !== null && resolution.flawCount > 0) {
                appendFact(result, "Flaws", String(resolution.flawCount));
            }
        }
        card.append(result);

        if (!resolution.isQualified && resolution.rollMode === "disadvantage") {
            card.append(createInlineState(
                "This Character is not qualified for the selected competency and has no qualified guidance, so the check uses disadvantage.",
                "warning"));
        }
        if (resolution.total !== null && resolution.inputsConsumed) {
            card.append(createElement(
                "p",
                "dd-harvesting-field__help",
                "The selected materials will be consumed when this crafting attempt is finalized."));
        }
    }

    if (state.craftingRolls.length > 0) {
        card.append(createElement(
            "p",
            "dd-harvesting-field__help",
            `Dice: ${state.craftingRolls.join(", ")}`));
    }
    if (state.craftingRollTie) {
        const tie = createElement("div", "dd-harvesting-choice-row");
        for (const value of state.craftingRolls) {
            tie.append(createButton(
                `Use ${value}`,
                "dd-button dd-button--secondary",
                () => void handlers.chooseCraftingRoll(character.characterId, value),
                readOnly));
        }
        card.append(
            createInlineState(
                "The roll-selection rule is tied. Choose which d20 result to use.",
                "warning"),
            tie);
    }

    return card;
}

function renderManufacturingInputs(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const group = createElement("div", "dd-crafting-stage-inputs");

    if (state.craftingCompetencyMode === "manual") {
        group.append(
            textField(
                "Tool or competency",
                state.craftingManualName,
                handlers.setCraftingManualName,
                readOnly),
            numberField(
                "Tool / competency modifier",
                state.craftingManualContribution,
                handlers.setCraftingManualContribution,
                readOnly),
            booleanField(
                "Qualified with this tool or competency",
                state.craftingManualQualified,
                handlers.setCraftingManualQualified,
                readOnly),
            createButton(
                "Choose From Character",
                "dd-button dd-button--ghost",
                () => handlers.setCraftingCompetencyMode("resolved"),
                readOnly));
    } else {
        const label = createElement("label", "dd-harvesting-field");
        label.append(createElement("span", "dd-harvesting-field__label", "Tool or competency"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose from this Character";
        select.append(placeholder);
        for (const competency of mechanics?.competencies?.entries ?? []) {
            if (competency.isFamily === true) continue;
            const option = createElement("option");
            option.value = competency.key;
            option.textContent = competency.label;
            select.append(option);
        }
        select.value = state.craftingCompetencyKey;
        select.disabled = readOnly;
        select.addEventListener("change", () =>
            handlers.setCraftingCompetencyKey(select.value));
        label.append(select);
        group.append(
            label,
            createButton(
                "Enter Tool / Competency Manually",
                "dd-button dd-button--ghost",
                () => handlers.setCraftingCompetencyMode("manual"),
                readOnly));
    }

    if (state.craftingManufacturingAbilityMode === "manual") {
        group.append(
            numberField(
                "Ability modifier",
                state.craftingManufacturingManualAbilityModifier,
                handlers.setCraftingManufacturingManualAbilityModifier,
                readOnly),
            createButton(
                "Choose Character Ability",
                "dd-button dd-button--ghost",
                () => handlers.setCraftingManufacturingAbilityMode("character"),
                readOnly));
    } else {
        const ability = createElement("label", "dd-harvesting-field");
        ability.append(createElement("span", "dd-harvesting-field__label", "Ability"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose the Ability required by the recipe";
        select.append(placeholder);
        for (const value of mechanics?.abilityValues ?? []) {
            const option = createElement("option");
            option.value = value.key;
            option.textContent = value.label;
            select.append(option);
        }
        select.value = state.craftingManufacturingAbilityKey;
        select.disabled = readOnly;
        select.addEventListener("change", () =>
            handlers.setCraftingManufacturingAbilityKey(select.value));
        ability.append(select);
        group.append(
            ability,
            createButton(
                "Enter Ability Modifier Manually",
                "dd-button dd-button--ghost",
                () => handlers.setCraftingManufacturingAbilityMode("manual"),
                readOnly));
    }

    group.append(booleanField(
        "A qualified crafter is guiding this attempt",
        state.craftingHasQualifiedGuidance,
        handlers.setCraftingHasQualifiedGuidance,
        readOnly));

    return group;
}

function renderEnchantingInputs(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    mechanics: CharacterMechanicsView | null
): HTMLElement {
    const group = createElement("div", "dd-crafting-stage-inputs");

    if (state.craftingCompetencyMode === "manual") {
        group.append(
            textField(
                "Enchanting skill",
                state.craftingManualName,
                handlers.setCraftingManualName,
                readOnly),
            numberField(
                "Skill modifier",
                state.craftingManualContribution,
                handlers.setCraftingManualContribution,
                readOnly),
            booleanField(
                "Qualified with this skill",
                state.craftingManualQualified,
                handlers.setCraftingManualQualified,
                readOnly),
            createButton(
                "Use Creature Type",
                "dd-button dd-button--ghost",
                () => {
                    handlers.setCraftingCompetencyKey("");
                    handlers.setCraftingCompetencyMode("resolved");
                },
                readOnly));
    } else {
        const creature = createElement("label", "dd-harvesting-field");
        creature.append(createElement(
            "span",
            "dd-harvesting-field__label",
            "Component creature type"));
        const creatureSelect = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose creature type";
        creatureSelect.append(placeholder);
        for (const creatureType of state.catalog?.creatureTypes ?? []) {
            const option = createElement("option");
            option.value = creatureType.key;
            option.textContent = creatureType.displayName;
            creatureSelect.append(option);
        }
        creatureSelect.value = state.craftingCreatureType;
        creatureSelect.disabled = readOnly;
        creatureSelect.addEventListener("change", () => {
            handlers.setCraftingCompetencyKey("");
            handlers.setCraftingCreatureType(creatureSelect.value);
        });
        creature.append(creatureSelect);
        group.append(
            creature,
            createButton(
                "Enter Skill Manually",
                "dd-button dd-button--ghost",
                () => handlers.setCraftingCompetencyMode("manual"),
                readOnly));
    }

    const spellcasting = mechanics?.spellcastingProfiles ?? [];
    if (spellcasting.length === 0) {
        group.append(createInlineState(
            "This Character does not currently have a resolved spellcasting Ability for Enchanting.",
            "warning"));
    } else if (spellcasting.length === 1) {
        group.append(createElement(
            "p",
            "dd-harvesting-field__help",
            `Spellcasting: ${spellcasting[0]!.label}`));
    } else {
        const casting = createElement("label", "dd-harvesting-field");
        casting.append(createElement("span", "dd-harvesting-field__label", "Spellcasting"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose spellcasting source";
        select.append(placeholder);
        for (const profile of spellcasting) {
            const option = createElement("option");
            option.value = profile.key;
            option.textContent = profile.label;
            select.append(option);
        }
        select.value = state.craftingSpellcastingKey;
        select.disabled = readOnly;
        select.addEventListener("change", () =>
            handlers.setCraftingSpellcastingKey(select.value));
        casting.append(select);
        group.append(casting);
    }

    return group;
}

function renderCraftingProject(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    routine: CharacterRoutineUiState | null
): HTMLElement {
    const card = createSectionCard("Crafting Project", "dd-crafting-recipe");
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        "Enter the recipe details from your source, then work through the required crafting steps."));

    card.append(
        textField(
            "Recipe / project",
            state.craftingRecipeName,
            handlers.setCraftingRecipeName,
            readOnly),
        textField(
            "Output item",
            state.craftingOutputName,
            handlers.setCraftingOutputName,
            readOnly),
        numberField(
            "Output quantity",
            state.craftingOutputQuantity,
            value => handlers.setCraftingOutputQuantity(value ?? 1),
            readOnly,
            1));

    const steps = createElement("div", "dd-crafting-materials");
    steps.append(createElement("strong", "", "Required steps"));
    steps.append(
        booleanField(
            "Manufacturing",
            state.craftingRequiresManufacturing,
            handlers.setCraftingRequiresManufacturing,
            readOnly),
        booleanField(
            "Enchanting",
            state.craftingRequiresEnchanting,
            handlers.setCraftingRequiresEnchanting,
            readOnly));
    card.append(steps);

    const inventory = routine?.state?.inventoryItemOccurrences ?? [];
    const materials = createElement("div", "dd-crafting-materials");
    materials.append(createElement("strong", "", "Materials"));

    state.craftingMaterials.forEach((material, index) => {
        const row = createElement("div", "dd-harvesting-helper");
        row.append(
            createElement(
                "span",
                "dd-harvesting-helper__title",
                inventoryOccurrenceLabel(routine, material.occurrenceId)),
            numberField(
                "Quantity",
                material.quantity,
                value => handlers.updateCraftingMaterial(index, value ?? 1),
                readOnly,
                1),
            createButton(
                "Remove",
                "dd-button dd-button--ghost",
                () => handlers.removeCraftingMaterial(index),
                readOnly));
        materials.append(row);
    });

    const available = inventory.filter(item =>
        !state.craftingMaterials.some(material => material.occurrenceId === item.id));
    if (available.length > 0) {
        const addLabel = createElement("label", "dd-harvesting-field");
        addLabel.append(createElement("span", "dd-harvesting-field__label", "Add material"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose from Inventory";
        select.append(placeholder);
        for (const occurrence of available) {
            const option = createElement("option");
            option.value = occurrence.id;
            option.textContent = `${inventoryOccurrenceLabel(routine, occurrence.id)} × ${occurrence.quantity}`;
            select.append(option);
        }
        select.disabled = readOnly;
        select.addEventListener("change", () => {
            if (select.value.length === 0) return;
            handlers.addCraftingMaterial(select.value);
            select.value = "";
        });
        addLabel.append(select);
        materials.append(addLabel);
    } else if (inventory.length === 0) {
        materials.append(createElement(
            "p",
            "dd-harvesting-field__help",
            "This Character has no Inventory items available to select as materials."));
    }

    card.append(materials);
    return card;
}

function renderCraftingCompletion(
    character: CharacterSheetBootstrapResponse,
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean
): HTMLElement {
    const card = createSectionCard("Finish Crafting", "dd-crafting-completion");

    if (state.craftingCompleted) {
        card.append(createInlineState(
            "This crafting attempt has been finalized and Inventory has been updated.",
            "neutral"));
    } else {
        const status = createElement("dl", "dd-harvesting-facts");
        if (state.craftingRequiresManufacturing) {
            appendFact(status, "Manufacturing", stageState(
                true,
                state.craftingManufacturingSucceeded));
        }
        if (state.craftingRequiresEnchanting) {
            appendFact(status, "Enchanting", stageState(
                true,
                state.craftingEnchantingSucceeded));
        }
        card.append(
            status,
            createElement(
                "p",
                "dd-harvesting-field__help",
                "When you finish, the selected materials are consumed and the crafted output is added to Inventory if the resolved result produced a functional item."));
    }

    card.append(createButton(
        state.craftingCompletionStatus === "loading"
            ? "Updating Inventory…"
            : state.craftingCompleted
                ? "Crafting Finalized"
                : "Finish & Update Inventory",
        "dd-button dd-button--primary",
        () => void handlers.completeCrafting(character.characterId),
        readOnly
            || state.craftingCompletionStatus === "loading"
            || state.craftingCompleted));
    return card;
}

function inventoryOccurrenceLabel(
    routine: CharacterRoutineUiState | null,
    occurrenceId: string
): string {
    const occurrence = routine?.state?.inventoryItemOccurrences
        .find(item => item.id === occurrenceId);
    if (occurrence === undefined) return "Unavailable inventory item";
    if (occurrence.customName !== null && occurrence.customName.trim().length > 0) {
        return occurrence.customName;
    }
    const reference = routine?.references[occurrence.id];
    if (reference !== undefined) {
        return toRuleReferenceDisplay(reference).value;
    }
    return occurrence.ruleConceptKey ?? "Inventory item";
}

function stageState(required: boolean, succeeded: boolean | null): string {
    if (!required) return "Not required";
    if (succeeded === true) return "Passed";
    if (succeeded === false) return "Failed";
    return "Pending";
}

function textField(
    labelText: string,
    value: string,
    onChange: (value: string) => void,
    readOnly: boolean
): HTMLElement {
    const label = createElement("label", "dd-harvesting-field");
    label.append(createElement("span", "dd-harvesting-field__label", labelText));
    const input = createElement("input", "dd-harvesting-field__control");
    input.type = "text";
    input.value = value;
    input.disabled = readOnly;
    input.addEventListener("change", () => onChange(input.value));
    label.append(input);
    return label;
}

function decimalField(
    labelText: string,
    value: number | null,
    onChange: (value: number | null) => void,
    readOnly: boolean,
    minimum?: number
): HTMLElement {
    const label = createElement("label", "dd-harvesting-field");
    label.append(createElement("span", "dd-harvesting-field__label", labelText));
    const input = createElement("input", "dd-harvesting-field__control");
    input.type = "number";
    input.step = "any";
    if (minimum !== undefined) input.min = String(minimum);
    input.value = value === null ? "" : String(value);
    input.disabled = readOnly;
    input.addEventListener("change", () => {
        const normalized = input.value.trim();
        if (normalized.length === 0) {
            onChange(null);
            return;
        }
        const parsed = Number.parseFloat(normalized);
        onChange(Number.isFinite(parsed) ? parsed : null);
    });
    label.append(input);
    return label;
}

function numberField(
    labelText: string,
    value: number | null,
    onChange: (value: number | null) => void,
    readOnly: boolean,
    minimum?: number
): HTMLElement {
    const label = createElement("label", "dd-harvesting-field");
    label.append(createElement("span", "dd-harvesting-field__label", labelText));
    const input = createElement("input", "dd-harvesting-field__control");
    input.type = "number";
    if (minimum !== undefined) input.min = String(minimum);
    input.value = value === null ? "" : String(value);
    input.disabled = readOnly;
    input.addEventListener("change", () => {
        const normalized = input.value.trim();
        if (normalized.length === 0) {
            onChange(null);
            return;
        }
        const parsed = Number.parseInt(normalized, 10);
        onChange(Number.isFinite(parsed) ? parsed : null);
    });
    label.append(input);
    return label;
}

function booleanField(
    labelText: string,
    value: boolean,
    onChange: (value: boolean) => void,
    readOnly: boolean
): HTMLElement {
    const label = createElement("label", "dd-harvesting-toggle");
    const input = createElement("input");
    input.type = "checkbox";
    input.checked = value;
    input.disabled = readOnly;
    input.addEventListener("change", () => onChange(input.checked));
    label.append(input, createElement("span", "", labelText));
    return label;
}

function sourceButton(
    label: string,
    active: boolean,
    onClick: () => void
): HTMLButtonElement {
    const button = createButton(
        label,
        active ? "dd-button dd-button--secondary" : "dd-button dd-button--ghost",
        onClick);
    button.setAttribute("aria-pressed", active ? "true" : "false");
    return button;
}

function componentIdentity(component: HarvestingComponentResponse): HTMLElement {
    const identity = createElement("div", "dd-harvesting-component__identity");
    identity.append(
        createElement("strong", "", component.displayName),
        createElement(
            "span",
            "",
            `Component DC ${component.componentDc}${component.quantity === null ? "" : ` · Qty ${component.quantity}`}`));
    return identity;
}

function appendFact(list: HTMLElement, label: string, value: string): void {
    const item = createElement("div", "dd-harvesting-fact");
    item.append(
        createElement("dt", "", label),
        createElement("dd", "", value));
    list.append(item);
}

function signed(value: number): string {
    return value > 0 ? `+${value}` : String(value);
}

function humanize(value: string): string {
    if (value.length === 0) return value;
    return value[0]!.toUpperCase() + value.slice(1).replaceAll("-", " ");
}
