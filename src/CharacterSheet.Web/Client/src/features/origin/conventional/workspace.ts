import type { HostEnvironment } from "../../../host-environment.js";
import { discoverNativeOriginPresets, discoverNativeOriginMethods, discoverNativeOriginChoiceCatalog,
    type NativeRulesetMetadata } from "../../../rules-core-native-api.js";
import { createButton, createElement, createSectionCard, createInlineState } from "../../../ui/components.js";
import { renderRuleBrowser, renderRuleDetail, renderChoice, renderValidation, renderWorkspace,
    renderDependencyReview, renderDraftStatus } from "../../guided/components.js";
import { resolveMethodEditor, type NativeMethod, type MethodEditor, type RuleDetail, type DisplayFact,
    type ChoiceRequirement, type ValidationIssue, type DependencyChange, type DraftSaveStatus } from "../../guided/contracts.js";
import { previewOriginProposal, type NativeOriginDraftProposal, type NativeOriginPreview } from "../origin-api.js";
import { createInitialOriginProposal, originPreviewCandidates, requiredDirectOriginChoices,
    changeDirectOriginChoice, isAuthorizedInitialNativeOrigin } from "../origin-builder-model.js";
import { type OriginUiState } from "../origin-workflow.js";
import { editableConventional, presetEntries, presetRuleNodes, suggestedUpbringing,
    selectPreset, matchingPreview, reconcilePreset, initialMethod, type PresetEntry } from "./model.js";

export interface ConventionalHost<TContext> {
    /** Canonical application-owned state. Never store a second persisted Origin. */
    current(): Readonly<OriginUiState>;
    /** Explicit application capability, not inferred from the open guided creator. */
    canEditDraft(): boolean;
    /** Host must verify character, snapshot, revision, and capability before saving. */
    save(proposal: NativeOriginDraftProposal, expectedRevision: number): Promise<void>;
    retry(): Promise<void>;
    notify(): void;
    methodEditors: readonly MethodEditor<TContext>[];
    methodContext(method: NativeMethod): TContext;
    /** Optional bounded presentation metadata from a native presentation provider. */
    describePreset?(id: string): string | undefined;
    labelRule?(id: string): string | undefined;
}
interface Inspection {
    entry: PresetEntry;
    proposal: NativeOriginDraftProposal;
    preview: NativeOriginPreview;
    revision: number;
    fingerprint: string;
    review?: DependencyChange;
}
const button = "dd-button dd-button--secondary";
const humanize = (id: string): string => id.split(".").at(-1)!.replaceAll(/[-_]/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/\b\w/g, c => c.toUpperCase());
function valueLabel(value: unknown): string | undefined {
    if (typeof value === "string") return humanize(value);
    if (typeof value === "number") return String(value);
    if (typeof value === "boolean") return value ? "Yes" : "No";
    if (Array.isArray(value) && value.every(v => typeof v === "string"))
        return value.map(humanize).join(", ");
    return undefined;
}
function isRecord(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === "object" && !Array.isArray(value);
}
function errorText(error: unknown): string {
    return error instanceof Error ? error.message : "The native Origin request failed.";
}

/** A conventional workspace is scoped to one character and active catalog.
 * Inspection/search/method navigation are transient. Save goes through the
 * host's shared draft workflow; this adapter never invokes activation.
 */
export function createConventionalWorkspace<TContext>(environment: HostEnvironment,
    characterId: string, host: ConventionalHost<TContext>) {
    let entries: readonly PresetEntry[] = [];
    let methods: readonly NativeMethod[] = [];
    let discoveryFingerprint: string | null = null;
    let query = "", inspectedId: string | null = null;
    let inspection: Inspection | null = null;
    let custom = false, chosenMethod: NativeMethod | null = null;
    let busy = false, saving = false, error: string | null = null, methodError: string | null = null;
    let sequence = 0;
    let pending: { proposal: NativeOriginDraftProposal; revision: number; fingerprint: string } | null = null;
    const catalogs = new Map<string, { options: readonly { id: string; name: string }[]; restricted: boolean }>();
    const expandedCatalogs = new Set<string>();

    const notify = () => host.notify();
    const label = (id: string) => host.labelRule?.(id) ?? entries.find(e => e.preset.id === id)?.preset.name ?? humanize(id);
    const currentProposal = () => host.current().draft?.proposal ?? createInitialOriginProposal(characterId);
    const revision = () => host.current().draft?.revision ?? 0;
    function writable(): boolean {
        const s = host.current();
        return host.canEditDraft() && s.status === "ready" && !s.readOnly && !s.savedState && !s.busy &&
            !!s.catalog && (!s.draft || s.draft.fingerprint === s.catalog.fingerprint &&
                s.draft.rulesetId === s.catalog.rulesetId) && editableConventional(currentProposal());
    }
    function currentPreview(): NativeOriginPreview | null {
        const s = host.current();
        return s.draft && s.preview && s.catalog && s.previewDraftRevision === s.draft.revision &&
            matchingPreview(s.preview, s.draft.proposal, s.catalog.rulesetId, s.catalog.fingerprint) ? s.preview : null;
    }
    function activeInspection(): Inspection | null {
        return inspection && inspection.revision === revision() &&
            inspection.fingerprint === host.current().catalog?.fingerprint ? inspection : null;
    }
    async function loadCatalog(metadata: NativeRulesetMetadata, id: string) {
        const existing = catalogs.get(id);
        if (existing) return existing;
        const response = await discoverNativeOriginChoiceCatalog(environment, metadata, id);
        if (host.current().catalog?.fingerprint !== metadata.fingerprint)
            throw new Error("The native rules changed; reload discovery.");
        const value = { options: response.choices.options,
            restricted: response.choices.requiresCharacterAuthority || response.choices.status !== "available" };
        catalogs.set(id, value);
        return value;
    }
    async function load(): Promise<void> {
        const metadata = host.current().catalog;
        if (!metadata || busy) return;
        const token = ++sequence;
        busy = true; error = null; methodError = null; inspection = null; chosenMethod = null;
        catalogs.clear(); expandedCatalogs.clear(); notify();
        try {
            const results = await Promise.allSettled([
                discoverNativeOriginPresets(environment, metadata),
                discoverNativeOriginMethods(environment, metadata)
            ]);
            if (token !== sequence || host.current().catalog?.fingerprint !== metadata.fingerprint) return;
            const presets = results[0];
            if (presets.status === "rejected") throw presets.reason;
            // Direct is the existing canonical proposal, not a custom method choice.
            entries = presetEntries(presets.value.presets, createInitialOriginProposal(characterId).method.methodId);
            const methodResult = results[1];
            methods = methodResult.status === "fulfilled" ? methodResult.value.methods : [];
            methodError = methodResult.status === "rejected" ? errorText(methodResult.reason) : null;
            discoveryFingerprint = metadata.fingerprint;
            inspectedId = currentProposal().selectedAncestryId ?? currentProposal().selectedSpeciesId ?? null;
        } catch (e) {
            if (token === sequence) error = errorText(e);
        } finally {
            if (token === sequence) { busy = false; notify(); }
        }
    }
    async function inspect(id: string): Promise<void> {
        const entry = entries.find(e => e.preset.id === id);
        const metadata = host.current().catalog;
        if (!entry || !metadata || discoveryFingerprint !== metadata.fingerprint || busy || pending) return;
        inspectedId = id; inspection = null; error = null;
        if (!entry.selection || !writable()) { notify(); return; }
        const previous = currentProposal(), priorRevision = revision();
        const target = selectPreset(previous, entry.selection);
        const token = ++sequence;
        busy = true; notify();
        try {
            const evaluate = (proposal: NativeOriginDraftProposal) => previewOriginProposal(environment,
                metadata.rulesetId, metadata.fingerprint, proposal);
            let preview = await evaluate(target);
            if (!matchingPreview(preview, target, metadata.rulesetId, metadata.fingerprint))
                throw new Error("Native preview did not match the inspected selection.");
            let proposal = target, review: DependencyChange | undefined;
            if (target !== previous && previous.manifestations.length > 0) {
                const before = currentPreview();
                if (!before) throw new Error("Refresh the saved draft before changing its configured choices.");
                const options = new Map<string, readonly string[]>();
                for (const requirement of requiredDirectOriginChoices(preview, target)) {
                    if (!requirement.optionCatalogId || options.has(requirement.optionCatalogId)) continue;
                    const choices = await loadCatalog(metadata, requirement.optionCatalogId);
                    if (!choices.restricted) options.set(requirement.optionCatalogId, choices.options.map(o => o.id));
                }
                const change = reconcilePreset(previous, target, before, preview,
                    metadata.rulesetId, metadata.fingerprint, options);
                proposal = change.proposal; review = change.review;
                preview = await evaluate(proposal);
                if (!matchingPreview(preview, proposal, metadata.rulesetId, metadata.fingerprint))
                    throw new Error("The reconciled native preview did not match the proposed selection.");
            }
            if (token !== sequence || revision() !== priorRevision ||
                host.current().catalog?.fingerprint !== metadata.fingerprint || !writable()) return;
            inspection = { entry, proposal, preview, revision: priorRevision, fingerprint: metadata.fingerprint, review };
        } catch (e) {
            if (token === sequence) error = errorText(e);
        } finally {
            if (token === sequence) { busy = false; notify(); }
        }
    }
    async function persist(proposal: NativeOriginDraftProposal, expectedRevision: number): Promise<void> {
        const metadata = host.current().catalog;
        if (!metadata || !writable() || busy || revision() !== expectedRevision ||
            discoveryFingerprint !== metadata.fingerprint) return;
        pending = { proposal, revision: expectedRevision, fingerprint: metadata.fingerprint };
        busy = true; saving = true; error = null; notify();
        try {
            await host.save(proposal, expectedRevision);
            const saved = host.current().draft;
            if (!saved || saved.revision <= expectedRevision || saved.fingerprint !== metadata.fingerprint ||
                saved.proposal.id !== proposal.id ||
                saved.proposal.selectedSpeciesId !== proposal.selectedSpeciesId ||
                (saved.proposal.selectedAncestryId ?? null) !== (proposal.selectedAncestryId ?? null) ||
                saved.proposal.manifestations.length !== proposal.manifestations.length ||
                proposal.manifestations.some(m => !saved.proposal.manifestations.some(n =>
                    n.id === m.id && n.candidateId === m.candidateId &&
                    (n.choices ?? []).length === (m.choices ?? []).length &&
                    (m.choices ?? []).every(c => (n.choices ?? []).some(d =>
                        c.requirementId === d.requirementId && c.selectedOptionIds.length === d.selectedOptionIds.length &&
                        c.selectedOptionIds.every((id, i) => id === d.selectedOptionIds[i]))))))
                throw new Error("The host has not confirmed the requested draft save.");
            pending = null; inspection = null;
            inspectedId = saved.proposal.selectedAncestryId ?? saved.proposal.selectedSpeciesId ?? null;
        } catch (e) {
            error = errorText(e);
        } finally { busy = false; saving = false; notify(); }
    }
    function detail(entry: PresetEntry, proposal?: NativeOriginDraftProposal, preview?: NativeOriginPreview): RuleDetail {
        const facts: DisplayFact[] = [{ label: "Species / Ancestry", value: entry.path.map(p => p.name).join(" / ") }];
        if (entry.reason) facts.push({ label: "Availability", value: entry.reason });
        const suggestion = suggestedUpbringing(entry);
        if (suggestion) facts.push({ label: "Suggested Upbringing (optional)", value: label(suggestion) });
        const features: RuleDetail[] = [];
        if (proposal && preview) {
            const envelope = preview as unknown as Record<string, unknown>;
            const movement = envelope.movementModes;
            if (isRecord(movement) && movement.previewOnly === true && Array.isArray(movement.modes)) {
                for (const mode of movement.modes) {
                    if (isRecord(mode) && typeof mode.mode === "string" && typeof mode.speedFeet === "number")
                        facts.push({ label: humanize(mode.mode) + " speed", value: mode.speedFeet + " ft.",
                            source: typeof mode.sourceId === "string" ? { label: label(mode.sourceId) } : undefined });
                }
            }
            for (const candidate of originPreviewCandidates(preview, proposal)) {
                if (!candidate.manifestedPreview && candidate.eligibility !== "Eligible") continue;
                if (candidate.kind === "Intrinsic" && candidate.manifestedPreview) {
                    const profile = isRecord(preview.result.profile) ? preview.result.profile : null;
                    const underlying = profile && isRecord(profile.underlying) ? profile.underlying : null;
                    const grants = Array.isArray(underlying?.inherentGrants) ? underlying.inherentGrants : [];
                    const grant = grants.find(g => isRecord(g) && Array.isArray(g.sourceCandidateIds) &&
                        g.sourceCandidateIds.includes(candidate.id));
                    const v = isRecord(grant) ? valueLabel(grant.effectiveValue) : undefined;
                    if (v !== undefined) facts.push({ label: label(candidate.characteristic),
                        value: candidate.characteristic === "walkingSpeedFeet" || candidate.characteristic === "darkvisionFeet" ? v + " ft." : v,
                        source: { label: label(candidate.sourceId) } });
                    continue;
                }
                if (candidate.kind !== "Trait") continue;
                const values: DisplayFact[] = [];
                // Show bounded, readable native parameters and authored progression
                // without treating offered parameters as effective character state.
                for (const [key, raw] of Object.entries(candidate.offeredParameters ?? {})) {
                    const v = valueLabel(raw);
                    if (v !== undefined) values.push({ label: humanize(key), value: v });
                }
                if (isRecord(candidate.offeredValue)) {
                    for (const key of ["level", "minimumLevel", "characterLevel", "unlockLevel"] as const) {
                        const v = valueLabel(candidate.offeredValue[key]);
                        if (v !== undefined) values.push({ label: humanize(key), value: v });
                    }
                }
                features.push({ title: candidate.displayName.startsWith("trait.") || candidate.displayName.startsWith("direct:") ? label(candidate.characteristic) : candidate.displayName,
                    description: candidate.description ?? undefined, facts: values,
                    source: { label: label(candidate.sourceId) } });
            }
        }
        return { title: entry.preset.name, description: host.describePreset?.(entry.preset.id),
            facts, features };
    }
    function validation(preview: NativeOriginPreview): readonly ValidationIssue[] {
        return preview.result.issues.map((issue, i) => ({
            id: String(i), severity: issue.severity === "Incomplete" || issue.severity === 0 ? "incomplete" :
                issue.severity === "Conflict" || issue.severity === 2 ? "conflict" :
                issue.severity === "Invalid" || issue.severity === 3 ? "invalid" : "unavailable",
            message: issue.detail
        }));
    }
    function choices(proposal: NativeOriginDraftProposal, preview: NativeOriginPreview): HTMLElement {
        const section = createSectionCard("Required creation choices");
        const candidates = originPreviewCandidates(preview, proposal);
        const requirements = requiredDirectOriginChoices(preview, proposal).filter(requirement =>
            candidates.some(c => c.id === requirement.candidateId && c.eligibility === "Eligible"));
        const expectedRevision = revision();
        if (!requirements.length) section.append(createElement("p", undefined, "No required choices reported."));
        requirements.forEach((requirement, index) => {
            const candidate = candidates.find(c => c.id === requirement.candidateId);
            const catalogId = requirement.optionCatalogId;
            const catalog = catalogId ? catalogs.get(catalogId) : undefined;
            const id = "choice-" + index;
            const options = catalogId ? catalog && !catalog.restricted ?
                catalog.options.map(o => ({ id: o.id, label: o.name })) : [] :
                (requirement.finiteOptions ?? []).map(o => ({ id: o, label: label(o) }));
            const view: ChoiceRequirement = {
                id, label: (candidate ? label(candidate.displayName) : "Characteristic") + " — " + humanize(requirement.requirementId),
                kind: catalogId ? "catalog" : requirement.maximum > 1 ? "counted" : "finite",
                selectedIds: requirement.selectedOptionIds, options: !catalogId || expandedCatalogs.has(id) ? options : [],
                minimum: requirement.minimum, maximum: requirement.maximum,
                catalogId: catalogId ?? undefined
            };
            const locked = !writable() || busy || !!pending || candidate?.eligibility !== "Eligible" || !!catalog?.restricted;
            section.append(renderChoice(view, locked, {
                select: (_id, optionId, selected) => {
                    if (locked || revision() !== expectedRevision || currentPreview() !== preview || !options.some(o => o.id === optionId)) return;
                    const existing = requirement.selectedOptionIds;
                    const next = selected ? requirement.maximum === 1 ? [optionId] :
                        [...new Set([...existing, optionId])] : existing.filter(o => o !== optionId);
                    if (next.length > requirement.maximum) { notify(); return; }
                    void persist(changeDirectOriginChoice(proposal, requirement.candidateId,
                        requirement.requirementId, next), expectedRevision);
                },
                browseCatalog: async (_id, requested) => {
                    const metadata = host.current().catalog;
                    if (!metadata || locked || revision() !== expectedRevision || currentPreview() !== preview || requested !== catalogId) return;
                    busy = true; error = null; notify();
                    try { await loadCatalog(metadata, requested); expandedCatalogs.add(id); }
                    catch (e) { error = errorText(e); }
                    finally { busy = false; notify(); }
                },
                navigate: () => undefined
            }));
            if (catalog?.restricted) section.append(createInlineState(
                "This choice requires a supported native authority or eligibility adapter.", "warning"));
            if (requirement.selectedOptionIds.length) section.append(createElement("p", undefined,
                "Selected: " + requirement.selectedOptionIds.map(label).join(", ")));
        });
        return section;
    }
    function render(): HTMLElement {
        const state = host.current();
        const area = createSectionCard("Character Origins");
        const status: DraftSaveStatus = state.readOnly || state.savedState || !host.canEditDraft() ? "read-only" :
            saving ? "saving" : pending ? "failed" : state.status !== "ready" ? "unavailable" :
                state.draft && state.draft.fingerprint !== state.catalog?.fingerprint ? "stale" :
                    state.draft ? "saved" : "unavailable";
        if (state.status === "ready" && !state.draft && host.canEditDraft() && !pending && !saving && !state.savedState)
            area.append(createElement("p", undefined, "No Origin draft selected."));
        else area.append(renderDraftStatus(status));
        if (error) area.append(createInlineState(error, "error"));
        if (busy && !saving) area.append(createInlineState("Loading native information…", "loading"));
        if (pending) {
            const retry = pending;
            area.append(createButton("Retry draft save", button, () => {
                if (revision() === retry.revision && state.catalog?.fingerprint === retry.fingerprint)
                    void persist(retry.proposal, retry.revision);
                else void host.retry();
            }, busy), createButton("Discard pending change", button, () => { pending = null; error = null; notify(); }, busy));
        }
        if (state.message) area.append(createInlineState(state.message, "warning"));
        if (!state.catalog || state.status !== "ready") return area;
        if (discoveryFingerprint !== state.catalog.fingerprint) {
            area.append(createButton("Load native Species and Ancestries", button, () => void load(), busy));
            return area;
        }
        if (custom) {
            area.append(createButton("Back to Species and Ancestries", button, () => {
                custom = false; chosenMethod = null; notify();
            }));
            if (methodError) area.append(createInlineState(methodError, "warning"),
                createButton("Retry method discovery", button, () => void load(), busy));
            if (chosenMethod) {
                const editor = resolveMethodEditor(chosenMethod, host.methodEditors);
                if (editor && initialMethod(chosenMethod) && host.canEditDraft() && !state.readOnly && !state.savedState)
                    area.append(editor.render(host.methodContext(chosenMethod)));
                else area.append(createInlineState("This method editor is not available for the current character.", "readonly"));
                return area;
            }
            for (const method of methods) {
                if (method.stage === "post-origination") continue;
                const editor = initialMethod(method) ? resolveMethodEditor(method, host.methodEditors) : null;
                const row = createElement("article");
                row.append(createElement("h3", undefined, method.name));
                if (editor) row.append(createButton("Configure", button, () => { chosenMethod = method; notify(); },
                    busy || !!pending || !host.canEditDraft() || state.readOnly || !!state.savedState));
                else row.append(createElement("p", undefined, initialMethod(method) ?
                    "Native preview supported; editor not registered." : "Reference only — not available for initial creation."));
                area.append(row);
            }
            return area;
        }
        area.append(createButton("Construct a Custom Origin", "dd-button", () => { custom = true; notify(); }));
        if (!editableConventional(currentProposal()) && !state.savedState)
            area.append(createInlineState("This saved Origin uses a custom configuration. Its evidence is preserved.", "readonly"));
        const savedIdentity = currentProposal().selectedAncestryId ?? currentProposal().selectedSpeciesId ?? null;
        const selected = entries.find(e => e.preset.id === (inspectedId ?? savedIdentity));
        const inspected = activeInspection();
        const savedPreview = currentPreview();
        const work = createSectionCard("Selection details");
        if (selected) {
            const shown = inspected?.entry.preset.id === selected.preset.id ? inspected : null;
            const same = selected.preset.id === savedIdentity;
            const preview = shown?.preview ?? (same ? savedPreview : null);
            const proposal = shown?.proposal ?? (same ? currentProposal() : undefined);
            work.append(renderRuleDetail(detail(selected, proposal, preview ?? undefined)));
            if (!host.describePreset?.(selected.preset.id))
                work.append(createElement("p", undefined, "Native preset description is not available."));
            if (preview) work.append(renderValidation(validation(preview), () => undefined));
            if (shown && !same) {
                const required = requiredDirectOriginChoices(shown.preview, shown.proposal).filter(requirement =>
                    originPreviewCandidates(shown.preview, shown.proposal).some(c => c.id === requirement.candidateId && c.eligibility === "Eligible"));
                if (required.length) {
                    const summary = createSectionCard("Required choices after selection");
                    const list = createElement("ul");
                    for (const requirement of required) list.append(createElement("li", undefined,
                        humanize(requirement.requirementId) + " — choose " + requirement.minimum +
                        (requirement.maximum === requirement.minimum ? "" : "–" + requirement.maximum)));
                    summary.append(list); work.append(summary);
                }
            }
            if (shown && !same) {
                const save = () => void persist(shown.proposal, shown.revision);
                if (shown.review?.reconsider.length) work.append(renderDependencyReview(shown.review, save,
                    () => { inspection = null; inspectedId = savedIdentity; notify(); }, !writable() || busy || !!pending));
                else work.append(createButton("Select preset", button, save, !writable() || busy || !!pending));
            } else if (same && preview && proposal) {
                work.append(choices(proposal, preview));
                work.append(createElement("p", undefined, isAuthorizedInitialNativeOrigin(preview) ?
                    "Native draft validation complete. No Origin has been granted." :
                    "Draft requires further native review."));
            }
        } else work.append(createElement("p", undefined, "Browse Species and Ancestries to inspect a configuration."));
        const browser = renderRuleBrowser(presetRuleNodes(entries), query, selected?.preset.id ?? null,
            !writable(), q => {
                query = q;
                // Keep focus on search during the local list update.
                const old = area.querySelector(".dd-guided-browser");
                const next = renderRuleBrowser(presetRuleNodes(entries), query, selected?.preset.id ?? null,
                    !writable(), updateQuery, id => void inspect(id));
                old?.replaceWith(next); next.querySelector("input")?.focus();
            }, id => void inspect(id));
        function updateQuery(q: string): void {
            query = q;
            const old = area.querySelector(".dd-guided-browser");
            const next = renderRuleBrowser(presetRuleNodes(entries), query, selected?.preset.id ?? null,
                !writable(), updateQuery, id => void inspect(id));
            old?.replaceWith(next); next.querySelector("input")?.focus();
        }
        area.append(renderWorkspace(work, browser));
        return area;
    }
    return { load, render };
}
