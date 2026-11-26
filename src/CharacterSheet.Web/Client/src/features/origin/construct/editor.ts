import { createElement, createButton, createSectionCard } from "../../../ui/components.js";
import { isCurrentEvaluation, type EditorContext, type MethodEditor, type NativePreviewEnvelope } from "../../guided/contracts.js";
import { renderAllocation, renderDecisionNavigation, renderDraftStatus, renderFacts, renderValidation,
    renderWorkspace } from "../../guided/components.js";
import { addComponent, constructionAllocation, constructReference, editComponent, removeComponent,
    setManifestation, toValidation, hasPhysicalDeclaration, hasProposedContact, hasPowerConnection,
    setPhysicalConnection, setExplicitWiring, setPowerConnection, type ComponentInstance, type Configuration,
    type ConstructDraft, type ConstructPreview, type NamedOption } from "./model.js";
import type { ConstructCatalog } from "./native.js";

/** The composition point supplies authorized draft state and callbacks. These
 * controls do not open a second persistence channel or establish characters. */
export interface ConstructEditorContext extends EditorContext<ConstructDraft, ConstructPreview> {
    catalog: ConstructCatalog | null;
    configurations: ReadonlyMap<string, NativePreviewEnvelope<Configuration>>;
    query: string;
    selectedInstanceId: string | null;
    setQuery: (query: string) => void;
    inspectInstance: (id: string) => void;
    requestConfiguration: (definitionId: string) => void;
    reportError: (message: string) => void;
}
const decisions = [{ id: "assembly", label: "Assembly" }, { id: "configuration", label: "Configuration" },
    { id: "review", label: "Review" }] as const;
const buttonClass = "dd-button dd-button--secondary";
const readable = (id: string) => id.split(".").at(-1)!.replaceAll("-", " ").replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, s => s.toUpperCase());
function labelFor(id: string, options: readonly NamedOption[]): string {
    return options.find(x => x.id === id)?.name ?? readable(id);
}
function selectField(title: string, options: readonly NamedOption[], selected: string | null | undefined,
    change: (value: string | null) => void, disabled: boolean): HTMLElement {
    const label = createElement("label", undefined, title + " ");
    const select = createElement("select"); select.disabled = disabled;
    const empty = createElement("option", undefined, "Not selected"); empty.value = ""; select.append(empty);
    // A stale choice stays visible; the native evaluator, not this dropdown,
    // decides whether it may be retained after a catalog/configuration change.
    if (selected && !options.some(x => x.id === selected)) {
        const missing = createElement("option", undefined, "Previous selection — review: " + readable(selected));
        missing.value = selected; select.append(missing);
    }
    for (const option of options) {
        const node = createElement("option", undefined, option.name); node.value = option.id; select.append(node);
    }
    select.value = selected ?? ""; select.addEventListener("change", () => change(select.value || null));
    label.append(select); return label;
}
function checkField(title: string, checked: boolean, disabled: boolean, change: (checked: boolean) => void): HTMLElement {
    const label = createElement("label"); const input = createElement("input");
    input.type = "checkbox"; input.checked = checked; input.disabled = disabled;
    input.addEventListener("change", () => change(input.checked));
    label.append(input, document.createTextNode(" " + title)); return label;
}
function textField(title: string, value: string | null | undefined, disabled: boolean,
    change: (value: string | null) => void): HTMLElement {
    const label = createElement("label", undefined, title + " "); const input = createElement("input");
    input.value = value ?? ""; input.disabled = disabled; input.maxLength = 96; input.pattern = "[A-Za-z0-9_.-]{1,96}";
    input.addEventListener("change", () => {
        if (input.value.trim() && !/^[A-Za-z0-9_.-]{1,96}$/.test(input.value.trim())) {
            input.setCustomValidity("Use up to 96 letters, digits, dots, hyphens or underscores.");
            input.reportValidity(); return;
        }
        input.setCustomValidity(""); change(input.value.trim() || null);
    });
    input.addEventListener("input", () => input.setCustomValidity(""));
    label.append(input); return label;
}
function renderEditor(context: ConstructEditorContext): HTMLElement {
    const root = createElement("section");
    root.append(renderDecisionNavigation(decisions, context.decisionId, context.navigate));
    if (!context.draft) {
        root.append(createElement("p", undefined, "An authorized initial Construct draft is not available."));
        return root;
    }
    const draftView = context.draft, draft = draftView.proposal;
    const preview = context.preview && isCurrentEvaluation(draftView, context.preview) &&
        draftView.saveStatus === "saved" ? context.preview.result : null;
    const catalog = context.catalog && context.catalog.fingerprint === draftView.fingerprint &&
        context.catalog.rulesetId === draftView.rulesetId ? context.catalog : null;
    const blocked = context.readOnly || draftView.readOnly || !catalog ||
        !["saved", "dirty"].includes(draftView.saveStatus);
    const names = catalog?.components ?? [], materials = catalog?.materials ?? [], traits = catalog?.traits ?? [];
    const name = (id: string) => labelFor(id, names);
    const partName = (id: string) => {
        const part = draft.assembly.components.find(x => x.instanceId === id);
        if (!part) return "Removed component";
        const peers = draft.assembly.components.filter(x => x.definitionId === part.definitionId);
        return name(part.definitionId) + (peers.length > 1 ? " (" + (peers.indexOf(part) + 1) + ")" : "");
    };
    const save = (next: ConstructDraft) => {
        if (!blocked) void context.save(next).catch(error =>
            context.reportError(error instanceof Error ? error.message : "Construct draft save failed."));
    };
    const configFor = (id: string): Configuration | null => {
        const envelope = context.configurations.get(id);
        return envelope?.previewOnly === true && envelope.rulesetId === draftView.rulesetId &&
            envelope.fingerprint === draftView.fingerprint && envelope.result.definitionId === id ? envelope.result : null;
    };
    const work = createSectionCard(context.decisionId === "review" ? "Construction review" : "Installed components");
    work.append(renderDraftStatus(draftView.saveStatus));
    if (draftView.saveStatus === "failed") work.append(createButton("Retry save", buttonClass, () => {
        void context.retry().catch(error => context.reportError(error instanceof Error ? error.message : "Retry failed."));
    }, context.readOnly || draftView.readOnly));
    if (!catalog) work.append(createElement("p", undefined, "Current native component catalog is unavailable."));
    else if (catalog.initialAllowance === null) work.append(createElement("p", undefined,
        "Native starting allowance is unavailable; construction can be drafted but not completed."));
    else if (catalog.initialAllowance !== draft.assembly.initialAllowance) {
        work.append(createElement("p", undefined, "This draft must use the current native starting allowance."),
            createButton("Use native allowance (" + catalog.initialAllowance + ")", buttonClass,
                () => save({ ...draft, assembly: { ...draft.assembly, initialAllowance: catalog.initialAllowance } }), blocked));
    }
    if (context.decisionId !== "review") {
        const wiring = createElement("details");
        wiring.append(createElement("summary", undefined, draft.assembly.powerConnections == null ?
            "Power wiring: physical contacts conduct" : "Power wiring: explicit connections"),
            createElement("p", undefined, draft.assembly.powerConnections == null ?
                "Switching to explicit wiring starts with no power links. The physical assembly is retained." :
                "Switching to physical-contact wiring removes explicit power links. Physical contacts will conduct according to the native rule."),
            createButton(draft.assembly.powerConnections == null ? "Use explicit wiring" : "Use physical-contact wiring",
                buttonClass, () => save(setExplicitWiring(draft, draft.assembly.powerConnections == null)), blocked));
        work.append(wiring);
    }
    if (!draft.assembly.components.length) work.append(createElement("p", undefined, "No components installed in this proposal."));
    for (const part of draft.assembly.components) {
        const card = createElement("article", "dd-guided-detail");
        const assessed = preview?.assembly.components.find(x => x.instanceId === part.instanceId &&
            x.definitionId === part.definitionId);
        card.append(createElement("h3", undefined, partName(part.instanceId)),
            createElement("p", undefined, assessed ? (assessed.operational ? "Operating in native preview" :
                !assessed.requestedEnabled ? "Disabled in proposal" : "Not operating in native preview") :
                "Operating state not evaluated"));
        if (assessed) card.append(renderFacts([{ label: "Installed in preview", value: assessed.installed ? "Yes" : "No" },
            { label: "Active capabilities in preview", value: assessed.activeCapabilities.map(readable).join(", ") || "None reported" },
            ...(assessed.sourceSupplyingPower == null ? [] : [{ label: "Source supplies in preview",
                value: assessed.sourceSupplyingPower ? "Yes (not live fuel)" : "No" }])]));
        const inspect = createButton("Configure", buttonClass, () => {
            context.inspectInstance(part.instanceId); context.requestConfiguration(part.definitionId); context.navigate("configuration");
        });
        card.append(inspect);
        if (!blocked && context.decisionId !== "review") {
            const removal = createElement("details"); removal.append(createElement("summary", undefined, "Remove component"));
            const affected = draft.assembly.components.filter(x => x.parentInstanceId === part.instanceId ||
                x.connectedTo?.includes(part.instanceId) ||
                draft.assembly.powerConnections?.some(link =>
                    link.fromInstanceId === part.instanceId && link.toInstanceId === x.instanceId ||
                    link.toInstanceId === part.instanceId && link.fromInstanceId === x.instanceId));
            removal.append(createElement("p", undefined, affected.length ? "Affected connections or mounts: " +
                affected.map(x => partName(x.instanceId)).join(", ") + ". Other configurations remain." :
                "Remove this exact instance. Other configurations remain."),
                createButton("Confirm removal", buttonClass, () => save(removeComponent(draft, part.instanceId))));
            card.append(removal);
        }
        if (context.decisionId === "configuration" && context.selectedInstanceId === part.instanceId) {
            const config = configFor(part.definitionId);
            if (!config) card.append(createElement("p", undefined, "Native configuration not loaded."),
                createButton("Load configuration", buttonClass, () => context.requestConfiguration(part.definitionId)));
            else card.append(renderConfiguration(part, config, draft, materials, partName, configFor,
                blocked, patch => save(editComponent(draft, part.instanceId, patch)), save, context.navigate));
        }
        work.append(card);
    }

    if (context.decisionId === "review") {
        work.append(createElement("h3", undefined, "Proposed Origin characteristics"));
        if (!preview) work.append(createElement("p", undefined, "A current evaluation is required to review characteristics."));
        for (const offer of preview?.origin.candidates ?? []) {
            const selected = draft.proposal.manifestations.find(x => x.candidateId === offer.id);
            const card = createElement("article", "dd-guided-detail");
            card.append(createElement("h4", undefined, labelFor(offer.labelId, traits)));
            const sourceInstance = offer.occurrencePath.split("/instance/").at(-1);
            const installedSource = draft.assembly.components.some(x => x.instanceId === sourceInstance);
            card.append(createElement("p", undefined, "Source: " + name(offer.sourceDefinitionId) +
                (installedSource && sourceInstance ? " — " + partName(sourceInstance) : "")));
            if (offer.eligibility !== "Eligible") card.append(createElement("p", undefined,
                "Native eligibility: " + offer.eligibility + (offer.eligibilityExplanation ? " — " + offer.eligibilityExplanation : "")));
            if (offer.offeredValue !== undefined && offer.offeredValue !== null &&
                ["string", "number", "boolean"].includes(typeof offer.offeredValue))
                card.append(createElement("p", undefined, "Native offer: " + String(offer.offeredValue)));
            const choose = () => setManifestation(draft, offer.id, { id: selected?.id ?? "manifest-" + crypto.randomUUID(),
                candidateId: offer.id, ...(offer.kind === "Intrinsic" && offer.offeredValue != null ? { selectedValue: offer.offeredValue } : {}),
                choices: selected?.choices, selectedParameters: selected?.selectedParameters });
            card.append(checkField("Select proposed characteristic", !!selected, blocked ||
                (offer.eligibility !== "Eligible" || !!offer.unresolvedEvidence.length) && !selected,
                checked => save(checked ? choose() : setManifestation(draft, offer.id, null))));
            if (selected && offer.kind === "Intrinsic" && offer.offeredValue != null &&
                JSON.stringify(selected.selectedValue) !== JSON.stringify(offer.offeredValue))
                card.append(createButton("Review and accept changed native value", buttonClass, () => save(choose()), blocked));
            if (offer.unresolvedEvidence.length) card.append(createElement("p", undefined, "Native offer has unresolved evidence."));
            if (selected) for (const req of offer.requiredChoices) {
                const chosen = selected.choices?.find(x => x.requirementId === req.id)?.selectedOptionIds ?? [];
                card.append(createElement("p", undefined, readable(req.id) + ": " + req.minimum + "–" + req.maximum + " selections"));
                const setChoice = (ids: readonly string[]) => save(setManifestation(draft, offer.id,
                    { ...selected, choices: [...(selected.choices ?? []).filter(x => x.requirementId !== req.id),
                        { requirementId: req.id, selectedOptionIds: ids }] }));
                if (req.finiteOptions) {
                    for (const option of new Set([...req.finiteOptions, ...chosen]))
                        card.append(checkField(readable(option), chosen.includes(option),
                            blocked || !chosen.includes(option) && (!req.finiteOptions.includes(option) || chosen.length >= req.maximum),
                            checked => setChoice(checked ? [...chosen, option] : chosen.filter(x => x !== option))));
                } else card.append(createElement("p", undefined, "This choice needs the shared native catalog/configuration editor."));
            }
            work.append(card);
        }
        if (preview) for (const selection of draft.proposal.manifestations.filter(x =>
            !preview.origin.candidates.some(c => c.id === x.candidateId))) {
            work.append(createElement("p", undefined, "A previous characteristic selection is no longer offered; review before removal."),
                createButton("Remove unavailable selection", buttonClass,
                    () => save(setManifestation(draft, selection.candidateId, null)), blocked));
        }
        work.append(createElement("p", undefined, "Review is a proposal only. Character finalization requires the authorized creation lifecycle."));
    }

    const browser = createSectionCard("Add components");
    const search = createElement("input"); search.type = "search"; search.value = context.query;
    const searchLabel = createElement("label", undefined, "Search components "); searchLabel.append(search);
    search.addEventListener("input", () => context.setQuery(search.value)); browser.append(searchLabel);
    const term = context.query.trim().toLocaleLowerCase();
    const categoryOf = (option: typeof names[number]) =>
        option.componentCategory ?? configFor(option.id)?.componentCategory ?? null;
    const visible = names.filter(x => x.name.toLocaleLowerCase().includes(term) ||
        (categoryOf(x) && readable(categoryOf(x)!).toLocaleLowerCase().includes(term)) ||
        configFor(x.id)?.baseCapabilities.some(c => readable(c).toLocaleLowerCase().includes(term)));
    const categories = new Map<string | null, typeof visible>();
    for (const option of visible) {
        const category = categoryOf(option);
        const group = categories.get(category) ?? [];
        group.push(option); categories.set(category, group);
    }
    for (const [category, options] of categories) {
        const group = createElement("details"); group.open = true;
        group.append(createElement("summary", undefined, category ? readable(category) : "Components — category not supplied"));
        for (const option of options) {
            const config = configFor(option.id);
            const item = createElement("article", "dd-guided-detail");
            item.append(createElement("h3", undefined, option.name));
            if (config) item.append(createElement("p", undefined, config.baseCapabilities.map(readable).join(", ") || "No base capability metadata"));
            else item.append(createButton("View native options", buttonClass, () => context.requestConfiguration(option.id)));
            item.append(createButton("Add component", buttonClass, () => {
                save(addComponent(draft, "part-" + crypto.randomUUID(), option.id));
                context.requestConfiguration(option.id);
            }, blocked));
            group.append(item);
        }
        browser.append(group);
    }
    if (!visible.length) browser.append(createElement("p", undefined, "No matching native components."));
    browser.append(createElement("p", undefined, "Starting configurations are unavailable until published through native discovery."));

    const side = createSectionCard("Mechanical preview");
    side.append(renderAllocation(constructionAllocation(catalog?.initialAllowance ?? null, preview?.assembly ?? null)));
    side.append(createElement("p", undefined, "Rated power and projected operation do not verify live fuel, stored energy, or operating authority."));
    if (preview) {
        const assembly = preview.assembly;
        side.append(renderFacts([{ label: "Assembly validation", value: assembly.status },
            { label: "Origin validation", value: preview.origin.status },
            { label: "Derived size", value: assembly.derivedCreatureSize ?? "Unresolved" },
            { label: "Retained material mass", value: assembly.knownRetainedMaterialMassPounds === null ? "Unresolved"
                : assembly.knownRetainedMaterialMassPounds + " lb" + (assembly.isBodyMassComplete ? "" : " (known lower bound)") }]));
        if (assembly.unresolvedRetainedMassMaterials?.length || assembly.productsWithUncertifiedMass?.length)
            side.append(createElement("p", undefined, "Unresolved mass evidence: " +
                [...(assembly.unresolvedRetainedMassMaterials ?? []), ...(assembly.productsWithUncertifiedMass ?? [])].map(readable).join(", ")));
        const quotation = createSectionCard("Native ingredient quotation");
        const quote = assembly.manufacturingQuote;
        if (quote && quote.status !== "unavailable") {
            quotation.append(createElement("p", undefined, quote.status === "resolved" ?
                "Native external ingredient quotation" : "Partial quotation — unresolved inputs or prices remain"));
            for (const ingredient of quote.ingredients) quotation.append(renderFacts([
                { label: labelFor(ingredient.definitionId, materials), value: String(ingredient.quantity) },
                { label: "Starting unit value", value: ingredient.unitCost === null ? "Unresolved" : String(ingredient.unitCost) },
                { label: "Known line cost", value: ingredient.knownCost === null ? "Unresolved" : String(ingredient.knownCost) }]));
            const stages = createSectionCard("Manufacturing stages");
            stages.append(createElement("p", undefined, "Native recipe requirements only; crafting completion, performer readiness and inventory are not verified."));
            for (const recipe of quote.recipes) {
                const recipeView = createElement("details");
                recipeView.append(createElement("summary", undefined, (recipe.instanceId ? partName(recipe.instanceId) + " — " : "") +
                    readable(recipe.sourceDefinitionId)), createElement("p", undefined, "Recipe: " + readable(recipe.recipeId)));
                for (const stage of recipe.stages) {
                    const step = createSectionCard(readable(stage.stepId));
                    step.append(renderFacts([{ label: "Competency", value: readable(stage.competencyId) },
                        { label: "Minimum ranks", value: String(stage.minimumRanks) },
                        { label: "Prerequisite stages", value: stage.dependencies.map(readable).join(", ") || "None" }]));
                    for (const input of stage.inputs) step.append(createElement("p", undefined,
                        "Input: " + input.amount + " × " + labelFor(input.materialId, materials) +
                        (input.retainedInProduct ? " (retained)" : "")));
                    for (const output of stage.outputs) step.append(createElement("p", undefined,
                        "Output: " + output.amount + " × " + labelFor(output.materialId, materials)));
                    recipeView.append(step);
                }
                stages.append(recipeView);
            }
            if (!quote.recipes.length) stages.append(createElement("p", undefined, "No resolved stages available."));
            quotation.append(stages);
        } else {
            if (assembly.requiredConstructionResources === null) quotation.append(createElement("p", undefined, "Ingredient bill unavailable."));
            else quotation.append(renderFacts(Object.entries(assembly.requiredConstructionResources).map(([id, amount]) =>
                ({ label: labelFor(id, materials), value: String(amount) }))));
            quotation.append(createElement("p", undefined, quote?.status === "unavailable" ?
                "Detailed native quotation exceeds the supported presentation bound." :
                "The current Core build has not provided stage-level quotation."));
        }
        side.append(quotation);
        const power = createSectionCard("Power networks");
        if (!assembly.powerNetworks?.length) power.append(createElement("p", undefined, "No evaluated power network."));
        for (const [index, network] of (assembly.powerNetworks ?? []).entries()) {
            power.append(createElement("h4", undefined, "Network " + (index + 1)),
                createElement("p", undefined, network.componentInstanceIds.map(partName).join(", ")),
                renderFacts([{ label: "Requested demand", value: network.hasUnresolvedDemand ? "Unresolved" : String(network.requestedDemand) },
                    { label: "Evaluated supply capacity", value: network.hasUnresolvedOutput ? "Unresolved" : String(network.availableOutput) },
                    { label: "Operational demand (preview)", value: String(network.operationalDemand) },
                    { label: "Operational output (preview)", value: String(network.operationalOutput) },
                    { label: "Network validation", value: network.isOverallocated === true ? "Insufficient power" :
                        network.isOverallocated === false && !network.hasUnresolvedDemand && !network.hasUnresolvedOutput ?
                            "Not overallocated in preview" : "Unresolved" }]));
        }
        for (const state of assembly.components) {
            if (state.activeOperationalOriginTraitIds?.length)
                power.append(createElement("p", undefined, partName(state.instanceId) + " operating characteristic candidates: " +
                    state.activeOperationalOriginTraitIds.map(id => labelFor(id, traits)).join(", ")));
        }
        for (const consequence of assembly.shutdownConsequences)
            power.append(createElement("p", undefined, partName(consequence.instanceId) + ": " + readable(consequence.consequenceId)));
        side.append(power);
        if (preview.movement) side.append(renderFacts([{ label: "Movement preview", value: preview.movement.status },
            ...preview.movement.modes.map(m => ({ label: readable(m.mode), value: m.speedFeet + " ft" }))]));
        side.append(renderValidation(toValidation([...assembly.issues, ...preview.origin.issues]), context.navigate));
    } else side.append(createElement("p", undefined, "Awaiting a current native evaluation. Previous results are not current mechanics."));
    root.append(renderWorkspace(work, context.decisionId === "review" ? undefined : browser, side));
    return root;
}
function renderConfiguration(part: ComponentInstance, config: Configuration, draft: ConstructDraft,
    materials: readonly NamedOption[], partName: (id: string) => string, configFor: (id: string) => Configuration | null,
    disabled: boolean, update: (patch: import("./model.js").ComponentPatch) => void,
    save: (draft: ConstructDraft) => void, navigate: (id: string) => void): HTMLElement {
    const panel = createSectionCard("Component configuration");
    const simple = (ids: readonly string[]): NamedOption[] => ids.map(id => ({ id, name: readable(id) }));
    panel.append(renderFacts([{ label: "Native capabilities", value: config.baseCapabilities.map(readable).join(", ") || "None reported" },
        { label: "Operating requirements", value: config.requiresAnyOf.map(group => group.map(readable).join(" or ")).join("; ") || "None reported" },
        { label: "Continuous power required", value: config.requiresContinuousPower ? "Yes" : "No" }]));
    if (config.defaultRecipeId) panel.append(createElement("p", undefined, "Default manufacturing recipe: " + readable(config.defaultRecipeId)));
    if (config.constructionVariants.length || part.constructionVariantId)
        panel.append(selectField("Construction variant", config.constructionVariants.map(v => ({ id: v.id,
            name: readable(v.id) + (v.bodySize ? " (" + readable(v.bodySize) + ")" : "") })),
        part.constructionVariantId, id => update({ constructionVariantId: id }), disabled));
    for (const slot of new Set([...config.materialSlots, ...Object.keys(part.materials ?? {})])) {
        const available = config.materialSlots.includes(slot) ? materials : [];
        panel.append(selectField("Material — " + readable(slot), available, part.materials?.[slot], id => {
            const next = { ...part.materials }; if (id) next[slot] = id; else delete next[slot]; update({ materials: next });
        }, disabled));
    }
    if (config.materialSlots.length) panel.append(createElement("p", undefined, "Materials are native catalog candidates; recipe compatibility is evaluated by Rules Core."));
    if (config.powerSourceRequired || part.powerSourceId) {
        panel.append(selectField("Power source", config.powerSources, part.powerSourceId, id => update({ powerSourceId: id }), disabled));
        const source = config.powerSourceDetails.find(x => x.id === part.powerSourceId);
        if (source) {
            const details = createSectionCard(source.name);
            if (source.description) details.append(createElement("p", undefined, source.description));
            details.append(renderFacts([{ label: "Operating principle", value: source.operatingPrinciple ? readable(source.operatingPrinciple) : "Unresolved" },
                { label: "Rated output", value: source.ratedOutput === null ? "Unresolved" : String(source.ratedOutput) },
                { label: "Rated Overdrive output", value: source.ratedOverdriveOutput === null ? "Not authored" : String(source.ratedOverdriveOutput) }]));
            if (source.rulesStatus) details.append(createElement("p", undefined, "Native rule status: " + source.rulesStatus));
            details.append(createElement("p", undefined, "Current fuel, energy and source-specific operating authority are unverified."));
            panel.append(details);
        } else if (part.powerSourceId) panel.append(createElement("p", undefined, "Native source-specific operating context is unavailable."));
    }
    if (config.designs.length || part.designId)
        panel.append(selectField("Design", config.designs, part.designId, id => update({ designId: id }), disabled));
    for (const id of new Set([...config.enhancements.map(x => x.id), ...(part.enhancementIds ?? [])])) {
        const selected = part.enhancementIds?.includes(id) ?? false;
        panel.append(checkField("Enhancement — " + labelFor(id, config.enhancements), selected,
            disabled || !selected && !config.enhancements.some(x => x.id === id),
            checked => update({ enhancementIds: checked ? [...(part.enhancementIds ?? []), id] :
                (part.enhancementIds ?? []).filter(x => x !== id) })));
    }
    if (config.modeIds.length || part.modeId)
        panel.append(selectField("Operating mode (proposal)", simple(config.modeIds), part.modeId, id => update({ modeId: id }), disabled));
    panel.append(checkField("Enabled in proposal", part.enabled, disabled, enabled => update({ enabled })));
    const others = draft.assembly.components.filter(x => x.instanceId !== part.instanceId);
    if (config.mountInterfaces.length || part.parentInstanceId) {
        const compatibleParents = others.filter(other => {
            const parent = configFor(other.definitionId);
            return !parent || config.mountInterfaces.some(id => parent.acceptsMountInterfaces.includes(id));
        });
        panel.append(selectField("Mount parent", compatibleParents.map(x => ({ id: x.instanceId, name: partName(x.instanceId) })),
            part.parentInstanceId, id => update({ parentInstanceId: id, ...(id ? {} : { mountInterfaceId: null }) }), disabled));
        const parent = others.find(x => x.instanceId === part.parentInstanceId);
        const parentConfig = parent ? configFor(parent.definitionId) : null;
        const interfaces = config.mountInterfaces.filter(id => !parentConfig || parentConfig.acceptsMountInterfaces.includes(id));
        panel.append(selectField("Mount interface", simple(interfaces), part.mountInterfaceId,
            id => update({ mountInterfaceId: id }), disabled || !part.parentInstanceId));
        if (parent && !parentConfig) panel.append(createElement("p", undefined, "Parent compatibility requires native validation."));
    }
    const connections = createSectionCard("Physical connections");
    connections.append(createElement("p", undefined, "Physical contacts and mounting are separate from explicit power wiring."));
    for (const other of others) {
        const mounted = part.parentInstanceId === other.instanceId || other.parentInstanceId === part.instanceId;
        connections.append(checkField("Physical connection to " + partName(other.instanceId),
            hasPhysicalDeclaration(draft, part.instanceId, other.instanceId), disabled,
            checked => save(setPhysicalConnection(draft, part.instanceId, other.instanceId, checked))));
        if (mounted) connections.append(createElement("p", undefined, "Proposed mounting contact: " + partName(other.instanceId)));
    }
    panel.append(connections);
    if (draft.assembly.powerConnections != null) {
        const power = createSectionCard("Explicit power connections");
        power.append(createElement("p", undefined, "Wiring must follow native-validated direct physical contacts. Removing contact can invalidate a retained wire."));
        for (const other of others) {
            const selected = hasPowerConnection(draft, part.instanceId, other.instanceId);
            const contact = hasProposedContact(draft, part.instanceId, other.instanceId);
            if (selected || contact) power.append(checkField("Wire to " + partName(other.instanceId) +
                (!contact ? " (contact missing — review)" : ""), selected, disabled,
                checked => save(setPowerConnection(draft, part.instanceId, other.instanceId, checked))));
        }
        panel.append(power);
    }
    if (config.baseCapabilities.includes("protective-surface") || part.protectedRegionId) {
        panel.append(textField("Protected region (proposal)", part.protectedRegionId, disabled,
            id => update({ protectedRegionId: id })),
            createElement("p", undefined, "A region label is physical evidence only; it does not grant armor or damage reduction."));
    }
    if (config.issues.length) panel.append(renderValidation(toValidation(config.issues), navigate));
    return panel;
}
export const constructMethodEditor: MethodEditor<ConstructEditorContext> = {
    reference: constructReference, decisions, render: renderEditor
};
