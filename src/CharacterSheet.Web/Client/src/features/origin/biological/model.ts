/** Biological draft operations and read-only native evaluation adapters.
 * No candidate, grant, price, or authority is synthesized here. */
import type { NativeOriginDraftProposal, NativeOriginDraftChoice } from "../origin-api.js";
import type { AllocationView, DependencyChange, NativePreset, ValidationIssue } from "../../guided/contracts.js";

type RecordValue = Record<string, unknown>;
export const record = (value: unknown): value is RecordValue =>
    value !== null && typeof value === "object" && !Array.isArray(value);
const records = (value: unknown): RecordValue[] => Array.isArray(value) ? value.filter(record) : [];
const strings = (value: unknown): string[] => Array.isArray(value) ? value.filter((v): v is string => typeof v === "string") : [];
const text = (value: unknown): string => typeof value === "string" ? value : "";
export const biologicalReference = { ruleId: "rule.creature-origination", methodId: "biological" } as const;
export interface Transmission {
    candidateIds: readonly string[];
    latentConfigurations?: readonly { candidateId: string; choices?: readonly NativeOriginDraftChoice[] }[];
    combinations?: readonly { ruleOwnerId: string; interactionId: string; candidateIds: readonly string[] }[];
}
/** These are ordinary proposed native relationship dispositions, never DM decisions. */
export interface CompositionRelationship {
    id: string; kind: 3; candidateIds: readonly string[]; authoredRuleReference: string;
}
export interface CompositionResolution {
    relationshipId: string; action: 4;
    basis: { adjudication: 0; nativeRuleReference: string };
    replacementCandidateId: string;
    replacementParameters: Readonly<RecordValue>;
    replacementParameterSources: Readonly<Record<string, string>>;
}
export interface BiologicalProposal extends NativeOriginDraftProposal {
    biologicalTransmission: Transmission;
    relationships?: readonly CompositionRelationship[];
    resolutions?: readonly CompositionResolution[];
}
export interface CharacteristicGroup {
    id: string; kind: string; offers: readonly Offer[]; expressions: readonly RecordValue[];
    presentation?: { name: string; description?: string; category?: string; recombinableParameters: readonly string[] };
}
const compositionId = (characteristic: string) => "guided-compose:" + characteristic;
const sameKeys = (a: RecordValue, b: RecordValue) =>
    Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k));

/** Reopen only this editor's narrow native-supported replacement shape.
 * Other persisted relationships require their existing specialist workflow. */
function supportsCompositions(proposal: NativeOriginDraftProposal, candidateIds: readonly string[]): boolean {
    const relations = proposal.relationships ?? [], resolutions = proposal.resolutions ?? [];
    if (!Array.isArray(relations) || !Array.isArray(resolutions) || relations.length !== resolutions.length) return false;
    const ids = new Set<string>();
    return relations.every(rel => {
        if (!record(rel) || typeof rel.authoredRuleReference !== "string" || !rel.authoredRuleReference.startsWith("trait.") ||
            rel.id !== compositionId(rel.authoredRuleReference) || rel.kind !== 3 ||
            ids.has(rel.id as string) || !Array.isArray(rel.candidateIds) || rel.candidateIds.length < 2 ||
            new Set(rel.candidateIds).size !== rel.candidateIds.length ||
            !rel.candidateIds.every(id => typeof id === "string" && candidateIds.includes(id) &&
                proposal.manifestations.some(m => m.candidateId === id))) return false;
        ids.add(rel.id as string);
        const matches = resolutions.filter(res => record(res) && res.relationshipId === rel.id);
        const res = matches[0];
        return matches.length === 1 && record(res) && res.action === 4 && record(res.basis) &&
            res.basis.adjudication === 0 && res.basis.nativeRuleReference === rel.authoredRuleReference &&
            res.basis.tableDecisionId == null && rel.candidateIds.includes(res.replacementCandidateId) &&
            record(res.replacementParameters) && record(res.replacementParameterSources) &&
            sameKeys(res.replacementParameters, res.replacementParameterSources) &&
            Object.keys(res.replacementParameters).length > 0 &&
            Object.values(res.replacementParameterSources).every(id => rel.candidateIds.includes(id));
    });
}
function withoutCompositions(proposal: BiologicalProposal, remove: (relation: CompositionRelationship) => boolean) {
    const removed = new Set((proposal.relationships ?? []).filter(remove).map(r => r.id));
    if (!removed.size) return {};
    return { relationships: proposal.relationships?.filter(r => !removed.has(r.id)),
        resolutions: proposal.resolutions?.filter(r => !removed.has(r.relationshipId)) };
}
export interface Offer {
    id: string;
    contributorId: string;
    sourceId: string;
    occurrence: string;
    kind: string;
    characteristicId: string;
    eligibility: string | number;
    explanation: string;
    value: unknown;
    parameters: unknown;
    requirements: readonly RecordValue[];
    unresolvedEvidence: readonly string[];
    expressionIds: readonly string[];
    state: "available" | "pending" | "latent" | "expressed" | "unresolved";
    inherited: boolean;
    requestedExpression: boolean;
}
export interface BiologicalEvaluation {
    groups: readonly CharacteristicGroup[];
    maxInherited?: number;
    maxExpressed?: number;
    allocation: AllocationView;
    charges: readonly RecordValue[];
    components: readonly RecordValue[];
    issues: readonly ValidationIssue[];
    current: boolean;
    policyRevision?: string;
}
export function newBiologicalProposal(id: string): BiologicalProposal {
    return { id, intent: 1, method: biologicalReference, contributors: [], candidates: [],
        manifestations: [], selectedSpeciesId: null, selectedAncestryId: null,
        biologicalTransmission: { candidateIds: [], latentConfigurations: [], combinations: [] } };
}
export function supportsBiologicalDraft(proposal: NativeOriginDraftProposal): proposal is BiologicalProposal {
    const plan = proposal.biologicalTransmission;
    return (proposal.intent === 1 || proposal.intent === "Mixed") &&
        proposal.method.ruleId === biologicalReference.ruleId &&
        proposal.method.methodId === biologicalReference.methodId &&
        proposal.candidates.length === 0 && proposal.selectedSpeciesId == null &&
        proposal.selectedAncestryId == null && record(plan) &&
        Array.isArray(plan.candidateIds) && plan.candidateIds.every(id => typeof id === "string") &&
        (plan.latentConfigurations == null || Array.isArray(plan.latentConfigurations)) &&
        (plan.combinations == null || Array.isArray(plan.combinations)) &&
        supportsCompositions(proposal, plan.candidateIds) &&
        !["existingConfiguration", "existingEffectiveConfiguration", "existingEffectiveConfigurationBasis",
            "activeTemporaryTransformation", "otherActiveTemporaryTransformations", "temporaryTransformationPrecedence",
            "tableDecisions", "history", "constraints", "inputRequirements", "processCreatureTypeId"]
            .some(key => proposal[key] != null && !(Array.isArray(proposal[key]) && (proposal[key] as unknown[]).length === 0)) &&
        proposal.contributors.every(c => record(c) && record(c.source) && record(c.role) &&
            typeof c.id === "string" && typeof c.source.id === "string" &&
            (c.source.kind === 0 || c.source.kind === "Native") &&
            (c.role.kind === 0 || c.role.kind === "BiologicalParent") &&
            c.manifestedProfile == null && c.historicalSourceIds == null && c.source.version == null);
}
export function nativeContributorAllowed(preset: NativePreset): boolean {
    return preset.selectable && (preset.kind.toLowerCase() === "species" ||
        preset.kind.toLowerCase() === "ancestry") &&
        (!preset.acquisitionMethodIds.length || preset.acquisitionMethodIds.includes("biological"));
}
export function addNativeContributor(proposal: BiologicalProposal, id: string, preset: NativePreset): BiologicalProposal {
    if (!nativeContributorAllowed(preset) || !id || proposal.contributors.some(c => record(c) && c.id === id))
        throw new Error("Select a distinct contributor occurrence and a selectable native source.");
    return { ...proposal, contributors: [...proposal.contributors, {
        id, role: { kind: 0 }, source: { kind: 0, id: preset.id }
    }] };
}
export function allocationView(raw: unknown): AllocationView {
    const label = "Biological allocation";
    if (!record(raw)) return { label, status: "unavailable" };
    const allowance = raw.allowance;
    const used = raw.committedLoad;
    const valid = typeof allowance === "number" && Number.isSafeInteger(allowance) &&
        allowance >= 0 && typeof used === "number" && Number.isSafeInteger(used) && used >= 0;
    const status = valid && raw.status === "within-allowance" && used <= allowance ? "within" :
        valid && raw.status === "over-allowance" && used > allowance ? "over" : "unresolved";
    return { label, status, allowance: typeof allowance === "number" && Number.isSafeInteger(allowance) && allowance >= 0 ? String(allowance) : undefined,
        used: valid && status !== "unresolved" ? String(used) : undefined,
        remaining: valid && status !== "unresolved" ? String(allowance - used) : undefined,
        detail: status === "unresolved" ? "Native evaluation has not resolved the complete cost." :
            status === "over" ? "Over allowance. This draft is not eligible for finalization." : undefined };
}
/** Accept only an envelope matched by the owning draft controller. The native
 * inherited-instance and grant evidence, not checkboxes, determines latency. */
export function evaluateBiological(proposal: BiologicalProposal, envelope: unknown,
    matchesCurrentDraft: boolean): BiologicalEvaluation {
    const empty: BiologicalEvaluation = { groups: [], allocation: allocationView(null),
        components: [], charges: [], issues: [], current: false };
    if (!matchesCurrentDraft || !record(envelope) || envelope.previewOnly !== true ||
        envelope.contentChangedSinceRequest === true || !record(envelope.result)) return empty;
    const result = envelope.result;
    if (!record(result.evaluatedProposal) || result.evaluatedProposal.id !== proposal.id ||
        !record(result.biologicalCombination) || result.biologicalCombination.proposalId !== proposal.id ||
        result.biologicalCombination.explicitTransmissionPlan !== true) return empty;
    const combination = result.biologicalCombination;
    if (!Array.isArray(combination.characteristics)) return empty;
    const genes = records(combination.inheritedInstances);
    const groups: CharacteristicGroup[] = records(combination.characteristics).map(group => ({
        id: text(group.characteristicId), kind: String(group.kind),
        presentation: record(group.presentation) && typeof group.presentation.name === "string" &&
            group.presentation.name.length <= 160 ? {
                name: group.presentation.name,
                description: typeof group.presentation.description === "string" && group.presentation.description.length <= 4000 ? group.presentation.description : undefined,
                category: typeof group.presentation.category === "string" && group.presentation.category.length <= 160 ? group.presentation.category : undefined,
                recombinableParameters: Array.isArray(group.presentation.recombinableParameters) &&
                    group.presentation.recombinableParameters.length <= 32 &&
                    group.presentation.recombinableParameters.every(k => typeof k === "string" && k.length > 0 && k.length <= 160) ?
                    [...new Set(strings(group.presentation.recombinableParameters))] : []
            } : undefined,
        expressions: records(group.expressions),
        offers: records(group.offers).flatMap(item => {
            const c = item.candidate;
            if (!record(c) || !record(c.identity) || !record(c.identity.occurrence) ||
                !record(c.identity.occurrence.source) || !record(c.selectionBasis) ||
                typeof c.id !== "string" || typeof c.contributorId !== "string") return [];
            const id = c.id;
            const inherited = proposal.biologicalTransmission.candidateIds.includes(id);
            const requestedExpression = proposal.manifestations.some(m => m.candidateId === id);
            const instances = genes.filter(g => record(g.gene) && g.gene.sourceCandidateId === id);
            const expressionIds = [...new Set(instances.flatMap(g => strings(g.expressionGrantIds)))];
            const validGene = instances.length > 0 && instances.every(g => Array.isArray(g.expressionGrantIds));
            const state: Offer["state"] = !inherited ? "available" : !validGene ? "unresolved" :
                expressionIds.length ? "expressed" : "latent";
            return [{ id, contributorId: c.contributorId, sourceId: text(c.identity.occurrence.source.id),
                occurrence: text(c.identity.occurrence.occurrencePath), kind: String(group.kind),
                characteristicId: text(group.characteristicId), eligibility: typeof c.selectionBasis.eligibility === "number" ? c.selectionBasis.eligibility : text(c.selectionBasis.eligibility),
                explanation: text(c.selectionBasis.explanation), value: c.offeredValue, parameters: c.offeredParameters,
                requirements: records(c.requiredChoices), unresolvedEvidence: strings(c.unresolvedEvidence),
                expressionIds, state, inherited, requestedExpression }];
        })
    }));
    const represented = new Set(groups.flatMap(g => g.offers.map(o => o.id)));
    const issues: ValidationIssue[] = records(result.issues).map((issue, i) => ({
        id: text(issue.code) + ":" + i, severity: issue.severity === "Invalid" || issue.severity === 3 ? "invalid" : issue.severity === "Conflict" || issue.severity === 2 ? "conflict" : issue.severity === "Incomplete" || issue.severity === 0 ? "incomplete" : "warning", message: text(issue.detail) || "Native evaluation requires review.",
        decision: { id: "inheritance", label: "Review inheritance" }
    }));
    for (const id of proposal.biologicalTransmission.candidateIds) if (!represented.has(id))
        issues.push({ id: "missing:" + id, severity: "unavailable", message: "A saved inherited occurrence is no longer in the eligible offers. Review the saved draft.",
            decision: { id: "contributors", label: "Review contributors" } });
    const rawAllocation = record(envelope.allocation) ? envelope.allocation : result.creationAllocation;
    const allocation = allocationView(rawAllocation);
    const maximum = (value: unknown) => typeof value === "number" && Number.isSafeInteger(value) && value > 0 ? value : undefined;
    return { current: true, groups, allocation, issues,
        maxInherited: maximum(combination.maxInheritedInstancesPerKind),
        maxExpressed: maximum(combination.maxExpressedInstancesPerKind),
        policyRevision: record(rawAllocation) && typeof rawAllocation.policyRevision === "string" ? rawAllocation.policyRevision : undefined,
        components: record(rawAllocation) ? records(rawAllocation.components) : [],
        charges: record(rawAllocation) ? records(rawAllocation.charges) : [] };
}
function requireOffer(evaluation: BiologicalEvaluation, id: string): Offer {
    const offer = evaluation.current ? evaluation.groups.flatMap(g => g.offers).find(o => o.id === id) : undefined;
    if (!offer) throw new Error("Refresh native offers before editing inheritance.");
    return offer;
}
export function setInherited(proposal: BiologicalProposal, evaluation: BiologicalEvaluation,
    candidateId: string, inherit: boolean): BiologicalProposal {
    requireOffer(evaluation, candidateId);
    const plan = proposal.biologicalTransmission;
    return { ...proposal, ...(inherit ? {} : withoutCompositions(proposal, r => r.candidateIds.includes(candidateId))),
        manifestations: inherit ? proposal.manifestations : proposal.manifestations.filter(m => m.candidateId !== candidateId),
        biologicalTransmission: { ...plan, candidateIds: inherit ? [...new Set([...plan.candidateIds, candidateId])] :
            plan.candidateIds.filter(id => id !== candidateId),
            latentConfigurations: inherit ? plan.latentConfigurations : plan.latentConfigurations?.filter(c => c.candidateId !== candidateId),
            combinations: inherit ? plan.combinations : plan.combinations?.filter(c => !c.candidateIds.includes(candidateId)) }
    };
}
/** Moving an instance between requested expression and latency retains its
 * configured choices; native evaluation may reject the requested result. */
export function setExpression(proposal: BiologicalProposal, evaluation: BiologicalEvaluation,
    candidateId: string, express: boolean): BiologicalProposal {
    const offer = requireOffer(evaluation, candidateId);
    const plan = proposal.biologicalTransmission;
    if (!plan.candidateIds.includes(candidateId)) throw new Error("Inherit the occurrence first.");
    const existing = proposal.manifestations.find(m => m.candidateId === candidateId);
    if (!!existing === express) return proposal;
    const latent = plan.latentConfigurations?.find(c => c.candidateId === candidateId);
    const configurations = (plan.latentConfigurations ?? []).filter(c => c.candidateId !== candidateId);
    const manifestations = proposal.manifestations.filter(m => m.candidateId !== candidateId);
    if (express) {
        manifestations.push({ id: "selection:" + candidateId, candidateId, choices: latent?.choices,
            ...((offer.kind === "Intrinsic" || offer.kind === "0") && offer.value != null ? { selectedValue: offer.value } : {}) });
    } else if (existing) {
        if (existing.selectedParameters != null ||
            existing.selectedValue != null && JSON.stringify(existing.selectedValue) !== JSON.stringify(offer.value))
            throw new Error("This expression has configured values requiring native reconciliation.");
        if (existing.choices?.length) configurations.push({ candidateId, choices: existing.choices });
    }
    return { ...proposal, ...(express ? {} : withoutCompositions(proposal, r => r.candidateIds.includes(candidateId))),
        manifestations, biologicalTransmission: { ...plan, latentConfigurations: configurations } };
}
/** Only complete, choice-free, inherited offers of a native-declared
 * composable characteristic are exposed. The Core reconciler is authoritative
 * for the proposed replacement, including value equality and dependencies. */
export function composableOffers(group: CharacteristicGroup): readonly Offer[] {
    if (!group.presentation?.recombinableParameters.length ||
        !group.id.startsWith("trait.") || !["Trait", "1"].includes(group.kind)) return [];
    const allowed = group.presentation.recombinableParameters;
    return group.offers.filter(o => o.inherited && !o.requirements.length && !o.unresolvedEvidence.length &&
        record(o.parameters) && allowed.every(k => Object.hasOwn(o.parameters as RecordValue, k)));
}
export function setComposition(proposal: BiologicalProposal, evaluation: BiologicalEvaluation,
    characteristicId: string, baseCandidateId: string, selectedSources: Readonly<Record<string, string>>): BiologicalProposal {
    const group = evaluation.current ? evaluation.groups.find(g => g.id === characteristicId) : undefined;
    if (!group) throw new Error("Refresh native characteristic offers before composing.");
    const available = composableOffers(group);
    const base = available.find(o => o.id === baseCandidateId);
    const allowed = group.presentation?.recombinableParameters ?? [];
    if (!base || !record(base.parameters) || !allowed.length ||
        Object.keys(selectedSources).some(k => !allowed.includes(k))) throw new Error("Native composition is unavailable.");
    const baseParameters = base.parameters;
    const parameters: RecordValue = Object.fromEntries(Object.entries(baseParameters));
    const sources: Record<string, string> = Object.fromEntries(Object.keys(parameters).map(key => [key, base.id]));
    for (const key of allowed) {
        const donor = available.find(o => o.id === selectedSources[key]);
        if (!donor || !record(donor.parameters) || !sameKeys(baseParameters, donor.parameters) ||
            !Object.hasOwn(donor.parameters, key))
            throw new Error("Select exact, complete native parameter offers.");
        parameters[key] = donor.parameters[key];
        sources[key] = donor.id;
    }
    const participants = [...new Set([base.id, ...Object.values(sources)])];
    if (participants.length < 2) throw new Error("Use the ordinary expression control for one source.");
    const groupIds = new Set(group.offers.map(o => o.id));
    const existing = proposal.manifestations.filter(m => groupIds.has(m.candidateId));
    if (existing.some(m => m.choices?.length || m.selectedParameters != null || m.selectedValue != null) ||
        proposal.biologicalTransmission.latentConfigurations?.some(c => groupIds.has(c.candidateId) && c.choices?.length) ||
        proposal.biologicalTransmission.combinations?.some(c => c.candidateIds.some(id => groupIds.has(id))))
        throw new Error("Existing configured selections or inherited interactions require separate reconciliation.");
    const id = compositionId(group.id);
    return { ...proposal,
        manifestations: [...proposal.manifestations.filter(m => !groupIds.has(m.candidateId)),
            ...participants.map(candidateId => ({ id: "selection:" + candidateId, candidateId }))],
        relationships: [...(proposal.relationships ?? []).filter(r => r.id !== id),
            { id, kind: 3, candidateIds: participants, authoredRuleReference: group.id }],
        resolutions: [...(proposal.resolutions ?? []).filter(r => r.relationshipId !== id),
            { relationshipId: id, action: 4, basis: { adjudication: 0, nativeRuleReference: group.id },
                replacementCandidateId: base.id, replacementParameters: parameters, replacementParameterSources: sources }]
    };
}
export function clearComposition(proposal: BiologicalProposal, characteristicId: string): BiologicalProposal {
    const id = compositionId(characteristicId);
    const relation = proposal.relationships?.find(r => r.id === id);
    if (!relation) return proposal;
    // Return to independent latent instances. Never delete inherited evidence.
    return { ...proposal, ...withoutCompositions(proposal, r => r.id === id),
        manifestations: proposal.manifestations.filter(m => !relation.candidateIds.includes(m.candidateId)) };
}

export function setFiniteChoice(proposal: BiologicalProposal, evaluation: BiologicalEvaluation,
    candidateId: string, requirementId: string, selected: readonly string[]): BiologicalProposal {
    const offer = requireOffer(evaluation, candidateId);
    const req = offer.requirements.find(r => r.id === requirementId);
    if (!offer.inherited || !req || !Array.isArray(req.finiteOptions) || req.optionCatalogId != null ||
        (req.timing !== 0 && req.timing !== "Creation") || typeof req.maximum !== "number" ||
        new Set(selected).size !== selected.length || selected.length > req.maximum ||
        selected.some(id => !req.finiteOptions || !strings(req.finiteOptions).includes(id)))
        throw new Error("This choice is not an available persistent finite selection.");
    const apply = (choices: readonly NativeOriginDraftChoice[] | undefined) => [
        ...(choices ?? []).filter(c => c.requirementId !== requirementId),
        ...(selected.length ? [{ requirementId, selectedOptionIds: selected }] : [])
    ];
    if (offer.requestedExpression) return { ...proposal, manifestations: proposal.manifestations.map(m =>
        m.candidateId === candidateId ? { ...m, choices: apply(m.choices) } : m) };
    const plan = proposal.biologicalTransmission;
    const old = plan.latentConfigurations?.find(c => c.candidateId === candidateId);
    return { ...proposal, biologicalTransmission: { ...plan, latentConfigurations: [
        ...(plan.latentConfigurations ?? []).filter(c => c.candidateId !== candidateId),
        { candidateId, choices: apply(old?.choices) }
    ] } };
}
/** Contributor replacement uses a new occurrence ID. Delete only offers whose
 * exact native evidence identifies the removed occurrence; unknown evidence
 * remains inert in the draft and is flagged for review. */
export function removeContributor(proposal: BiologicalProposal, evaluation: BiologicalEvaluation,
    contributorId: string): { proposal: BiologicalProposal; change: DependencyChange } {
    if (!evaluation.current && (proposal.biologicalTransmission.candidateIds.length || proposal.manifestations.length))
        throw new Error("Refresh native evidence before removing a contributor with saved selections.");
    const affected = new Set(evaluation.groups.flatMap(g => g.offers).filter(o => o.contributorId === contributorId).map(o => o.id));
    const plan = proposal.biologicalTransmission;
    const removed = plan.candidateIds.filter(id => affected.has(id));
    const retained = plan.candidateIds.filter(id => !affected.has(id));
    const name = (id: string) => evaluation.groups.flatMap(g => g.offers).find(o => o.id === id)?.characteristicId ?? "Unresolved saved occurrence";
    const affectedCompositions = (proposal.relationships ?? []).filter(r => r.candidateIds.some(id => affected.has(id)));
    return { proposal: { ...proposal, ...withoutCompositions(proposal, r => r.candidateIds.some(id => affected.has(id))), contributors: proposal.contributors.filter(c => !record(c) || c.id !== contributorId),
        manifestations: proposal.manifestations.filter(m => !affected.has(m.candidateId)),
        biologicalTransmission: { ...plan, candidateIds: retained,
            latentConfigurations: plan.latentConfigurations?.filter(c => !affected.has(c.candidateId)),
            combinations: plan.combinations?.filter(c => !c.candidateIds.some(id => affected.has(id))) } },
        change: { retained: retained.filter(id => evaluation.groups.some(g => g.offers.some(o => o.id === id))).map(name), reconsider: [...removed.map(name), ...retained.filter(id => !evaluation.groups.some(g => g.offers.some(o => o.id === id))).map(name),
            ...affectedCompositions.map(r => "Combined expression — " + r.authoredRuleReference)], available: [] } };
}
