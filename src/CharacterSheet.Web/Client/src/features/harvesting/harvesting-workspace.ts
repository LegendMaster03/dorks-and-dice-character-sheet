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
            "Resolve creature harvesting, helpers, cumulative Harvest DCs, and Loot Tavern crafting without expanding the Character Sheet Actions list."),
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
            "Harvesting rules could not be loaded from Rules Core.",
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
                row.append(createInlineState("Resolving helper mechanics…", "loading"));
            } else if (helper.resolutionStatus === "error") {
                row.append(createInlineState(
                    "Resolved helper data is incomplete. Manual values below remain editable.",
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
            "Proficient in the associated competency",
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
        "Order matters. Rules Core adds each Component DC to all previous Component DCs to produce the cumulative Harvest DC."));

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
        return createInlineState("Rules Core is calculating the Harvesting outcome…", "loading");
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
            ? "Rules Core resolves the effective Harvesting & Crafting mechanics."
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

    panel.append(renderManualRecipe(state, handlers, readOnly, routine));

    const procedure = createSectionCard("Crafting Procedure", "dd-crafting-procedure");
    const procedureChoices = createElement("div", "dd-harvesting-choice-row");
    procedureChoices.append(
        sourceButton(
            "Manufacturing",
            state.craftingProcedure === "manufacturing",
            () => handlers.setCraftingProcedure("manufacturing")),
        sourceButton(
            "Enchanting",
            state.craftingProcedure === "enchanting",
            () => handlers.setCraftingProcedure("enchanting")));
    procedure.append(
        procedureChoices,
        createElement(
            "p",
            "dd-harvesting-field__help",
            state.craftingProcedure === "manufacturing"
                ? "Manufacturing uses the effective universal competency resolved by Rules Core."
                : "Enchanting uses the effective creature-derived competency with the Character's resolved spellcasting ability."));
    panel.append(procedure);

    const competencyCard = createSectionCard("Competency", "dd-crafting-competency");
    const competencyMode = createElement("div", "dd-harvesting-choice-row");
    competencyMode.append(
        sourceButton(
            "Rules Core competency",
            state.craftingCompetencyMode === "resolved",
            () => handlers.setCraftingCompetencyMode("resolved")),
        sourceButton(
            "Manual entry",
            state.craftingCompetencyMode === "manual",
            () => handlers.setCraftingCompetencyMode("manual")));
    competencyCard.append(competencyMode);

    if (state.craftingCompetencyMode === "manual") {
        const name = createElement("label", "dd-harvesting-field");
        name.append(createElement("span", "dd-harvesting-field__label", "Competency name"));
        const input = createElement("input", "dd-harvesting-field__control");
        input.type = "text";
        input.value = state.craftingManualName;
        input.disabled = readOnly;
        input.addEventListener("change", () => handlers.setCraftingManualName(input.value));
        name.append(input);
        competencyCard.append(
            name,
            numberField(
                "Effective modifier",
                state.craftingManualContribution,
                handlers.setCraftingManualContribution,
                readOnly),
            booleanField(
                "Qualified for this competency",
                state.craftingManualQualified,
                handlers.setCraftingManualQualified,
                readOnly));
    } else {
        const label = createElement("label", "dd-harvesting-field");
        label.append(createElement("span", "dd-harvesting-field__label", "Universal competency"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = state.craftingProcedure === "enchanting"
            ? "Use creature-type competency"
            : "Choose competency";
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
        competencyCard.append(label);

        if (state.craftingProcedure === "enchanting"
            && state.craftingCompetencyKey.trim().length === 0) {
            const creature = createElement("label", "dd-harvesting-field");
            creature.append(createElement("span", "dd-harvesting-field__label", "Creature type"));
            const creatureSelect = createElement("select", "dd-harvesting-field__control");
            for (const creatureType of state.catalog?.creatureTypes ?? []) {
                const option = createElement("option");
                option.value = creatureType.key;
                option.textContent = creatureType.displayName;
                creatureSelect.append(option);
            }
            creatureSelect.value = state.craftingCreatureType;
            creatureSelect.disabled = readOnly;
            creatureSelect.addEventListener("change", () =>
                handlers.setCraftingCreatureType(creatureSelect.value));
            creature.append(creatureSelect);
            competencyCard.append(creature);
        }
    }
    panel.append(competencyCard);

    const check = createSectionCard("Check", "dd-crafting-check");
    if (state.craftingProcedure === "manufacturing") {
        const abilityMode = createElement("div", "dd-harvesting-choice-row");
        abilityMode.append(
            sourceButton(
                "Character Ability",
                state.craftingManufacturingAbilityMode === "character",
                () => handlers.setCraftingManufacturingAbilityMode("character")),
            sourceButton(
                "Manual Ability modifier",
                state.craftingManufacturingAbilityMode === "manual",
                () => handlers.setCraftingManufacturingAbilityMode("manual")));
        check.append(
            createElement(
                "p",
                "dd-harvesting-field__help",
                "Choose the Ability required by the recipe's tool/product. Rules Core resolves a selected Character Ability modifier; the browser does not calculate it."),
            abilityMode);

        if (state.craftingManufacturingAbilityMode === "character") {
            const ability = createElement("label", "dd-harvesting-field");
            ability.append(createElement("span", "dd-harvesting-field__label", "Manufacturing Ability"));
            const select = createElement("select", "dd-harvesting-field__control");
            const placeholder = createElement("option");
            placeholder.value = "";
            placeholder.textContent = "Choose Ability";
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
            check.append(ability);
        } else {
            check.append(numberField(
                "Manual Ability modifier",
                state.craftingManufacturingManualAbilityModifier,
                handlers.setCraftingManufacturingManualAbilityModifier,
                readOnly));
        }

        check.append(booleanField(
            "Qualified guidance is available",
            state.craftingHasQualifiedGuidance,
            handlers.setCraftingHasQualifiedGuidance,
            readOnly));
    } else {
        const spellcasting = mechanics?.spellcastingProfiles ?? [];
        if (spellcasting.length > 0) {
            const casting = createElement("label", "dd-harvesting-field");
            casting.append(createElement("span", "dd-harvesting-field__label", "Spellcasting profile"));
            const select = createElement("select", "dd-harvesting-field__control");
            const automatic = createElement("option");
            automatic.value = "";
            automatic.textContent = spellcasting.length === 1
                ? "Use resolved spellcasting"
                : "Choose spellcasting profile";
            select.append(automatic);
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
            check.append(casting);
        }
    }

    check.append(
        numberField(
            "Target DC",
            state.craftingTargetDc,
            handlers.setCraftingTargetDc,
            readOnly,
            1),
        numberField(
            "Other modifier",
            state.craftingOtherModifier,
            value => handlers.setCraftingOtherModifier(value ?? 0),
            readOnly),
        createButton(
            state.craftingStatus === "loading" ? "Resolving…" : "Resolve Check",
            "dd-button dd-button--secondary",
            () => void handlers.prepareCrafting(character.characterId),
            readOnly || state.craftingStatus === "loading"));
    panel.append(check);

    if (state.craftingStatus === "loading") {
        panel.append(createInlineState("Rules Core is resolving the effective crafting check…", "loading"));
        return panel;
    }

    if (state.craftingResolution !== null) {
        const resolution = state.craftingResolution;
        const resolved = createSectionCard("Resolved Check", "dd-crafting-resolution");
        const facts = createElement("dl", "dd-harvesting-facts");
        appendFact(facts, "Competency", resolution.competencyDisplayName);
        appendFact(facts, "Qualified", resolution.isQualified ? "Yes" : "No");
        appendFact(facts, "Ability contribution", signed(resolution.abilityContribution));
        appendFact(facts, "Competency contribution", signed(resolution.competencyContribution));
        appendFact(facts, "Other modifier", signed(resolution.otherModifier));
        appendFact(facts, "Roll mode", humanize(resolution.rollMode));
        if (resolution.targetDc !== null) {
            appendFact(facts, "Target DC", String(resolution.targetDc));
        }
        resolved.append(facts);

        if (resolution.total === null) {
            const rollControls = createElement("div", "dd-harvesting-choice-row");
            rollControls.append(
                numberField(
                    "Manual d20",
                    state.craftingSelectedRoll,
                    handlers.setCraftingSelectedRoll,
                    readOnly,
                    1),
                createButton(
                    "Use Manual Roll",
                    "dd-button dd-button--secondary",
                    () => void handlers.submitCraftingRoll(character.characterId),
                    readOnly || state.craftingSelectedRoll === null),
                createButton(
                    "Roll d20",
                    "dd-button dd-button--primary",
                    () => void handlers.rollCrafting(character.characterId),
                    readOnly));
            resolved.append(rollControls);
        } else {
            const result = createElement("dl", "dd-harvesting-facts");
            appendFact(result, "Selected d20", String(resolution.d20Roll));
            appendFact(result, "Total", String(resolution.total));
            appendFact(result, "Outcome", humanize(resolution.outcome));
            if (resolution.margin !== null) {
                appendFact(result, "Margin", signed(resolution.margin));
            }
            if (resolution.flawCount !== null && resolution.flawCount > 0) {
                appendFact(
                    result,
                    "Flaws",
                    String(resolution.flawCount));
            }
            appendFact(
                result,
                "Inputs",
                resolution.inputsConsumed ? "Consumed" : "Not consumed");
            appendFact(
                result,
                "Functional output",
                resolution.producesFunctionalOutput ? "Yes" : "No");
            resolved.append(result);
        }

        if (state.craftingRolls.length > 0) {
            resolved.append(createElement(
                "p",
                "dd-harvesting-field__help",
                `Rolled: ${state.craftingRolls.join(", ")}`));
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
            resolved.append(
                createInlineState(
                    "The roll-selection rule produced a tie. Choose the die result to use.",
                    "warning"),
                tie);
        }
        panel.append(resolved);
    }

    panel.append(renderCraftingCompletion(
        character,
        state,
        handlers,
        readOnly));

    return panel;
}

function renderManualRecipe(
    state: HarvestingCraftingUiState,
    handlers: HarvestingCraftingWorkflow,
    readOnly: boolean,
    routine: CharacterRoutineUiState | null
): HTMLElement {
    const card = createSectionCard("Recipe", "dd-crafting-recipe");
    card.append(createElement(
        "p",
        "dd-harvesting-field__help",
        "Manual recipe entry keeps this workflow usable without copying source recipe tables. Rules Core still resolves the required checks."));

    card.append(
        textField(
            "Recipe name",
            state.craftingRecipeName,
            handlers.setCraftingRecipeName,
            readOnly),
        textField(
            "Crafted output",
            state.craftingOutputName,
            handlers.setCraftingOutputName,
            readOnly),
        numberField(
            "Output quantity",
            state.craftingOutputQuantity,
            value => handlers.setCraftingOutputQuantity(value ?? 1),
            readOnly,
            1),
        booleanField(
            "Requires Manufacturing",
            state.craftingRequiresManufacturing,
            handlers.setCraftingRequiresManufacturing,
            readOnly),
        booleanField(
            "Requires Enchanting",
            state.craftingRequiresEnchanting,
            handlers.setCraftingRequiresEnchanting,
            readOnly));

    if (state.craftingRequiresManufacturing) {
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
    }

    if (state.craftingRequiresEnchanting) {
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

    const stages = createElement("dl", "dd-harvesting-facts");
    appendFact(stages, "Manufacturing", stageState(
        state.craftingRequiresManufacturing,
        state.craftingManufacturingSucceeded));
    appendFact(stages, "Enchanting", stageState(
        state.craftingRequiresEnchanting,
        state.craftingEnchantingSucceeded));
    card.append(stages);

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
                "Remove Material",
                "dd-button dd-button--ghost",
                () => handlers.removeCraftingMaterial(index),
                readOnly));
        materials.append(row);
    });

    const available = inventory.filter(item =>
        !state.craftingMaterials.some(material => material.occurrenceId === item.id));
    if (available.length > 0) {
        const addLabel = createElement("label", "dd-harvesting-field");
        addLabel.append(createElement("span", "dd-harvesting-field__label", "Add material from Inventory"));
        const select = createElement("select", "dd-harvesting-field__control");
        const placeholder = createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Choose inventory item";
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
            "No Character Inventory items are currently available. A recipe can still be resolved without material consumption."));
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
    const card = createSectionCard("Complete Crafting", "dd-crafting-completion");
    if (state.craftingCompleted) {
        card.append(createInlineState(
            "This recipe output has been added to Inventory and the selected materials have been consumed.",
            "neutral"));
    } else {
        card.append(createElement(
            "p",
            "dd-harvesting-field__help",
            "Finalization is explicit. It uses one atomic Inventory transaction: successful or flaw-bearing results record the output, while a nonfunctional result consumes the selected inputs without creating an output."));
    }

    card.append(createButton(
        state.craftingCompletionStatus === "loading"
            ? "Completing…"
            : state.craftingCompleted
                ? "Attempt Finalized"
                : "Finalize Recipe Attempt",
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
