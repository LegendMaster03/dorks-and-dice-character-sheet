/**
 * Pure Origin draft operations. These only describe player selections;
 * Rules Core must adjudicate every resulting proposal.
 */
import type {
    NativeOriginDraftProposal,
    NativeOriginDraftManifestation,
    NativeOriginPreview
} from "./origin-api.js";

export interface NativeOriginRequiredChoice {
    candidateId: string;
    requirementId: string;
    label: string;
    finiteOptions: readonly string[] | null;
    optionCatalogId: string | null;
    minimum: number;
    maximum: number;
    selectedOptionIds: readonly string[];
}

type JsonRecord = Record<string, unknown>;
const isRecord = (value: unknown): value is JsonRecord =>
    value !== null && typeof value === "object" && !Array.isArray(value);

export function createInitialOriginProposal(characterId: string): NativeOriginDraftProposal {
    return {
        id: `initial-${characterId}`,
        intent: 0,
        method: { ruleId: "rule.creature-origination", methodId: "biological" },
        contributors: [],
        candidates: [],
        manifestations: [],
        selectedSpeciesId: null,
        selectedAncestryId: null
    };
}

export function isDirectOriginProposal(proposal: NativeOriginDraftProposal): boolean {
    return proposal.intent === 0 || proposal.intent === "Direct";
}

/**
 * Biological native *source references*, not verified identities or fully
 * manifested profiles for actual parent characters. This is an evidence
 * draft only: no inherent grants, inheritance decisions or approvals.
 *
 * The general Origin contract supports arbitrary input roles and numbers.
 * Four is a basic editor limit, not an origination rule or parent maximum.
 */
export type MixedNativeParentSlot =
    "parent-one" | "parent-two" | "source-3" | "source-4";
const mixedSourceSlots: readonly MixedNativeParentSlot[] = [
    "parent-one", "parent-two", "source-3", "source-4"
];
export type MixedNativeSourceCategory = "species" | "ancestry";

export interface MixedNativeParentSource {
    slot: MixedNativeParentSlot;
    sourceId: string | null;
}

/** Additional slots are optional draft exemplars, never claimed parent identities. */
export function nextMixedNativeSourceSlot(
    proposal: NativeOriginDraftProposal
): MixedNativeParentSlot | null {
    if (!isEditableMixedNativeOrigin(proposal)) return null;
    return mixedSourceSlots.slice(2).find(slot =>
        !proposal.contributors.some(raw => isRecord(raw) && raw.id === slot)) ?? null;
}

function nativeBiologicalMethod(proposal: NativeOriginDraftProposal): boolean {
    return proposal.method?.ruleId === "rule.creature-origination" &&
        proposal.method.methodId === "biological";
}

function containsExtraAuthority(proposal: NativeOriginDraftProposal): boolean {
    return [
        "tableDecisions", "resolutions", "relationships", "inputRequirements",
        "constraints", "history", "existingConfiguration",
        "existingEffectiveConfiguration", "existingEffectiveConfigurationBasis",
        "activeTemporaryTransformation", "otherActiveTemporaryTransformations",
        "temporaryTransformationPrecedence", "processCreatureTypeId"
    ].some(field => {
        const value = proposal[field];
        return value !== undefined && value !== null &&
            !(Array.isArray(value) && value.length === 0);
    });
}

export function isEditableDirectNativeOrigin(
    proposal: NativeOriginDraftProposal
): boolean {
    return isDirectOriginProposal(proposal) &&
        nativeBiologicalMethod(proposal) &&
        proposal.contributors.length === 0 && proposal.candidates.length === 0 &&
        !containsExtraAuthority(proposal);
}

export function isEditableMixedNativeOrigin(
    proposal: NativeOriginDraftProposal
): boolean {
    if (!(proposal.intent === 1 || proposal.intent === "Mixed") ||
        !nativeBiologicalMethod(proposal) || proposal.candidates.length !== 0 ||
        proposal.selectedSpeciesId != null || proposal.selectedAncestryId != null ||
        containsExtraAuthority(proposal) || proposal.contributors.length > mixedSourceSlots.length)
        return false;

    // Only source-local selection references and unproven choices are editable.
    // No offered values, parameter overrides, DM evidence or grants enter this path.
    const selectedIds = new Set<string>();
    if (!proposal.manifestations.every(m =>
        isRecord(m) && typeof m.candidateId === "string" && m.candidateId.length > 0 &&
        m.candidateId.length <= 512 && m.id === `selection:${m.candidateId}` &&
        !selectedIds.has(m.candidateId) &&
        Object.keys(m).every(k => ["id", "candidateId", "choices"].includes(k)) &&
        (m.choices === undefined || Array.isArray(m.choices) &&
            m.choices.length <= 32 && m.choices.every(choice =>
                isRecord(choice) && typeof choice.requirementId === "string" &&
                Array.isArray(choice.selectedOptionIds) &&
                choice.selectedOptionIds.length <= 32 &&
                choice.selectedOptionIds.every((id: unknown) => typeof id === "string") &&
                new Set(choice.selectedOptionIds).size === choice.selectedOptionIds.length &&
                Object.keys(choice).every(k =>
                    ["requirementId", "selectedOptionIds"].includes(k)))) &&
        (selectedIds.add(m.candidateId), true))) return false;

    const slots = new Set<string>();
    return proposal.contributors.every(raw => {
        if (!isRecord(raw) || !isRecord(raw.role) || !isRecord(raw.source) ||
            typeof raw.id !== "string" ||
            !mixedSourceSlots.includes(raw.id as MixedNativeParentSlot) ||
            slots.has(raw.id) ||
            !(raw.role.kind === 0 || raw.role.kind === "BiologicalParent") ||
            !(raw.source.kind === 0 || raw.source.kind === "Native") ||
            typeof raw.source.id !== "string" ||
            !/^(species|ancestry)\.[a-z0-9]+(?:[.\-][a-z0-9]+)*$/.test(raw.source.id) ||
            raw.manifestedProfile != null || raw.historicalSourceIds != null ||
            raw.source.version != null) return false;
        slots.add(raw.id);
        return true;
    });
}

/** Switching intent discards all old manifested choices and source evidence. */
export function changeNativeOriginMode(
    proposal: NativeOriginDraftProposal, mode: "direct" | "mixed"
): NativeOriginDraftProposal {
    if (!isEditableDirectNativeOrigin(proposal) &&
        !isEditableMixedNativeOrigin(proposal)) {
        throw new Error("This Origin contains evidence that the basic editor can not replace.");
    }
    return {
        id: proposal.id,
        intent: mode === "direct" ? 0 : 1,
        method: { ruleId: "rule.creature-origination", methodId: "biological" },
        contributors: [],
        candidates: [],
        manifestations: [],
        selectedSpeciesId: null,
        selectedAncestryId: null
    };
}

/** The references are typed native options, but not authenticated parent profiles. */
export function changeMixedNativeParent(
    proposal: NativeOriginDraftProposal,
    slot: MixedNativeParentSlot,
    selectedSourceId: string | null
): NativeOriginDraftProposal {
    if (!isEditableMixedNativeOrigin(proposal)) {
        throw new Error("Mixed native source references require a plain biological evidence draft.");
    }
    if (!mixedSourceSlots.includes(slot)) {
        throw new Error("An unrecognized biological source slot was supplied.");
    }
    if (selectedSourceId !== null &&
        !/^(species|ancestry)\.[a-z0-9]+(?:[.\-][a-z0-9]+)*$/.test(selectedSourceId)) {
        throw new Error("Only native Species or Ancestry source identities are supported.");
    }

    const existing = proposal.contributors.find(raw =>
        isRecord(raw) && raw.id === slot);
    if ((isRecord(existing) && isRecord(existing.source) ?
        existing.source.id : null) === selectedSourceId) return proposal;

    const contributors = proposal.contributors.filter(raw =>
        isRecord(raw) && raw.id !== slot);
    if (selectedSourceId !== null) {
        contributors.push({
            id: slot,
            role: { kind: 0 },
            source: { kind: 0, id: selectedSourceId }
        });
    }
    return {
        ...proposal,
        // A changed parent cannot reuse earlier manifestation evidence,
        // native relationship suggestions, or independently claimed outcomes.
        contributors: contributors.sort((a, b) =>
            String((a as Record<string, unknown>).id).localeCompare(
                String((b as Record<string, unknown>).id))),
        manifestations: [],
        candidates: [],
        selectedSpeciesId: null,
        selectedAncestryId: null
    };
}

export function mixedNativeParentSources(
    proposal: NativeOriginDraftProposal
): readonly MixedNativeParentSource[] {
    const sourceFor = (slot: MixedNativeParentSlot): string | null => {
        const raw = proposal.contributors.find(value =>
            isRecord(value) && value.id === slot);
        return isRecord(raw) && isRecord(raw.source) &&
            typeof raw.source.id === "string" ? raw.source.id : null;
    };
    return mixedSourceSlots.filter(slot =>
        slot === "parent-one" || slot === "parent-two" ||
        proposal.contributors.some(raw => isRecord(raw) && raw.id === slot))
        .map(slot => ({ slot, sourceId: sourceFor(slot) }));
}

/**
 * Read-only projection of what native Rules Core offered for this exact proposal.
 * A candidate is not selected just because it appears in evaluatedProposal, and
 * neither a selection nor an evaluated preview is a committed characteristic.
 */
export interface NativeOriginCandidateView {
    id: string;
    contributorId: string | null;
    sourceId: string;
    occurrencePath: string;
    characteristic: string;
    displayName: string;
    description: string | null;
    kind: string;
    eligibility: string;
    explanation: string | null;
    selectedDraft: boolean;
    /** Present in evaluatedProposal.manifestations; not necessarily an earned grant. */
    evaluatedManifestation: boolean;
    /** Present in the preview profile grant list only; still not committed. */
    manifestedPreview: boolean;
    offeredValue: unknown;
    offeredParameters: Readonly<Record<string, unknown>> | null;
    selectedParameters: Readonly<Record<string, unknown>> | null;
    unresolvedEvidence: readonly string[];
    requiredChoiceCount: number;
}

/**
 * The native source deconstructor preserves the draft's contributor identities,
 * even when an Ancestry contributes offers from an ancestor Species. Compare the
 * contributor snapshot, not a false leaf-source equivalence rule.
 *
 * This only binds the preview to the current player proposal. Rules Core still
 * authenticates the candidate origin, its inheritance eligibility, and grants.
 */
export function mixedNativePreviewMatchesDraft(
    preview: NativeOriginPreview | null, proposal: NativeOriginDraftProposal
): boolean {
    if (!isEditableMixedNativeOrigin(proposal) ||
        preview?.previewOnly !== true ||
        !isRecord(preview.result?.evaluatedProposal)) return false;
    const evaluated = preview.result.evaluatedProposal;
    if (evaluated.id !== proposal.id ||
        !(evaluated.intent === proposal.intent ||
            (evaluated.intent === 1 && proposal.intent === "Mixed") ||
            (evaluated.intent === "Mixed" && proposal.intent === 1)) ||
        !isRecord(evaluated.method) ||
        evaluated.method.ruleId !== proposal.method.ruleId ||
        evaluated.method.methodId !== proposal.method.methodId ||
        !Array.isArray(evaluated.contributors) ||
        evaluated.contributors.length !== proposal.contributors.length ||
        !Array.isArray(evaluated.manifestations)) return false;

    const byId = new Map<string, JsonRecord>();
    for (const raw of evaluated.contributors) {
        if (!isRecord(raw) || typeof raw.id !== "string" ||
            byId.has(raw.id)) return false;
        byId.set(raw.id, raw);
    }
    for (const original of proposal.contributors) {
        if (!isRecord(original) || !isRecord(original.role) ||
            !isRecord(original.source)) return false;
        const current = byId.get(original.id as string);
        if (!current || !isRecord(current.role) || !isRecord(current.source) ||
            current.role.kind !== original.role.kind ||
            current.source.kind !== original.source.kind ||
            current.source.id !== original.source.id ||
            (current.source.version ?? null) !== (original.source.version ?? null))
            return false;
    }
    const selections = new Map<string, JsonRecord>();
    for (const raw of evaluated.manifestations) {
        if (!isRecord(raw) || typeof raw.candidateId !== "string" ||
            selections.has(raw.candidateId)) return false;
        selections.set(raw.candidateId, raw);
    }
    if (selections.size !== proposal.manifestations.length) return false;
    for (const original of proposal.manifestations) {
        const current = selections.get(original.candidateId);
        if (!current || current.id !== original.id ||
            !Array.isArray(current.choices) && (original.choices?.length ?? 0) > 0)
            return false;
        const selectedChoices = Array.isArray(current.choices) ?
            current.choices.filter(isRecord) : [];
        if ((original.choices ?? []).some(choice =>
            !selectedChoices.some(c =>
                c.requirementId === choice.requirementId &&
                Array.isArray(c.selectedOptionIds) &&
                c.selectedOptionIds.length === choice.selectedOptionIds.length &&
                c.selectedOptionIds.every((id, i) =>
                    id === choice.selectedOptionIds[i])))) return false;
    }
    return true;
}

export function originPreviewCandidates(
    preview: NativeOriginPreview | null, proposal: NativeOriginDraftProposal | null
): readonly NativeOriginCandidateView[] {
    if (preview?.previewOnly !== true || proposal === null ||
        !isRecord(preview.result?.evaluatedProposal)) return [];
    const evaluated = preview.result.evaluatedProposal;
    if (evaluated.id !== proposal.id || !Array.isArray(evaluated.candidates) ||
        (isEditableMixedNativeOrigin(proposal) &&
            !mixedNativePreviewMatchesDraft(preview, proposal))) return [];
    const evaluatedManifestations = new Set(Array.isArray(evaluated.manifestations)
        ? evaluated.manifestations.filter(isRecord)
            .map(m => m.candidateId).filter((id): id is string => typeof id === "string")
        : []);
    const resultProfile = isRecord(preview.result.profile) ? preview.result.profile : null;
    const underlying = resultProfile && isRecord(resultProfile.underlying)
        ? resultProfile.underlying : null;
    const grantSets = [underlying?.inherentGrants, resultProfile?.independentGrants];
    const previewGrants = new Set<string>();
    for (const grants of grantSets) {
        if (!Array.isArray(grants)) continue;
        for (const raw of grants) {
            if (!isRecord(raw) || !Array.isArray(raw.sourceCandidateIds)) continue;
            for (const id of raw.sourceCandidateIds)
                if (typeof id === "string") previewGrants.add(id);
        }
    }
    const selected = new Map(proposal.manifestations.map(m => [m.candidateId, m]));
    const result: NativeOriginCandidateView[] = [];
    const seen = new Set<string>();
    for (const raw of evaluated.candidates) {
        if (!isRecord(raw) || typeof raw.id !== "string" ||
            seen.has(raw.id) || !isRecord(raw.identity) ||
            !isRecord(raw.identity.occurrence) ||
            !isRecord(raw.identity.occurrence.source)) return [];
        const identity = raw.identity;
        const occurrence = identity.occurrence as JsonRecord;
        const source = occurrence.source as JsonRecord;
        const contributorId = raw.contributorId == null ? null :
            typeof raw.contributorId === "string" ? raw.contributorId : undefined;
        const sourceId = source.id;
        const path = occurrence.occurrencePath;
        if (typeof sourceId !== "string" || typeof path !== "string" ||
            !path.startsWith("/") ||
            !(source.kind === 0 || source.kind === "Native") ||
            contributorId === undefined ||
            raw.id !== `${contributorId === null ? "direct" :
                "contributor:" + contributorId}:${sourceId}:${path}` ||
            (contributorId !== null && !proposal.contributors.some(entry =>
                isRecord(entry) && entry.id === contributorId))) return [];
        seen.add(raw.id);
        const basis = isRecord(raw.selectionBasis) ? raw.selectionBasis : {};
        const kind = identity.kind === 0 || identity.kind === "Intrinsic" ? "Intrinsic" :
            identity.kind === 1 || identity.kind === "Trait" ? "Trait" : "Other";
        const eligibility = ["Unreviewed", "Eligible", "Restricted", "Unresolved",
            "DmAuthorized"][typeof basis.eligibility === "number" ?
                basis.eligibility : -1] ??
            (typeof basis.eligibility === "string" ? basis.eligibility : "Unreviewed");
        const selection = selected.get(raw.id);
        result.push({
            id: raw.id,
            contributorId,
            sourceId,
            occurrencePath: path,
            characteristic: [identity.mechanicalDefinitionId, identity.traitConceptId,
                identity.intrinsicSlotId].find(x => typeof x === "string") as string ??
                (typeof occurrence.occurrencePath === "string" ?
                    occurrence.occurrencePath : raw.id),
            displayName: isRecord(raw.offeredValue) &&
                typeof raw.offeredValue.name === "string"
                ? raw.offeredValue.name : (typeof identity.mechanicalDefinitionId === "string"
                    ? identity.mechanicalDefinitionId : raw.id),
            description: isRecord(raw.offeredValue) &&
                typeof raw.offeredValue.description === "string"
                ? raw.offeredValue.description : null,
            kind, eligibility,
            explanation: typeof basis.explanation === "string" ?
                basis.explanation : null,
            selectedDraft: selection !== undefined,
            evaluatedManifestation: evaluatedManifestations.has(raw.id),
            manifestedPreview: previewGrants.has(raw.id),
            offeredValue: raw.offeredValue ?? null,
            offeredParameters: isRecord(raw.offeredParameters) ?
                raw.offeredParameters : null,
            selectedParameters: isRecord(selection?.selectedParameters) ?
                selection.selectedParameters : null,
            unresolvedEvidence: Array.isArray(raw.unresolvedEvidence) ?
                raw.unresolvedEvidence.filter((v): v is string => typeof v === "string") : [],
            requiredChoiceCount: Array.isArray(raw.requiredChoices) ?
                raw.requiredChoices.length : 0
        });
    }
    return result;
}

/**
 * Select only a native candidate returned by the current preview for a
 * matching source contributor. Never copy the candidate, its offered values,
 * or its purported eligibility into a user-authored draft.
 */
export function changeMixedNativeCandidate(
    proposal: NativeOriginDraftProposal, preview: NativeOriginPreview,
    candidateId: string, selected: boolean
): NativeOriginDraftProposal {
    if (!isEditableMixedNativeOrigin(proposal))
        throw new Error("This Origin is not an editable mixed source draft.");
    const prior = proposal.manifestations.filter(m => m.candidateId !== candidateId);
    const alreadySelected = prior.length !== proposal.manifestations.length;
    // An offer can disappear after native content changes or source validation.
    // Removing an existing inert draft selection must not require a fresh offer.
    if (!selected) {
        if (!alreadySelected) throw new Error("This candidate is not selected in the draft.");
        return { ...proposal, manifestations: prior };
    }
    if (alreadySelected) return proposal;
    const candidate = originPreviewCandidates(preview, proposal).find(c =>
        c.id === candidateId);
    const contributor = proposal.contributors.find(raw =>
        isRecord(raw) && raw.id === candidate?.contributorId);
    // A selected Ancestry contributes native offers from its entire lineage.
    // The occurrence may name an ancestor Species, not the leaf source ID.
    if (!candidate || !isRecord(contributor) || !isRecord(contributor.source) ||
        !(contributor.source.kind === 0 || contributor.source.kind === "Native") ||
        candidate.contributorId === null ||
        !/^(species|ancestry)\./.test(candidate.sourceId))
        throw new Error("This native candidate does not belong to a selected contributor.");
    return { ...proposal, manifestations:
        [...prior, { id: `selection:${candidateId}`, candidateId }] };
}

/**
 * A mixed choice is just a saved request. It is not first-party eligibility
 * evidence, and the browser never authorizes mixed traits or their parameters.
 */
export function changeMixedNativeFiniteChoice(
    proposal: NativeOriginDraftProposal, preview: NativeOriginPreview,
    candidateId: string, requirementId: string,
    selectedOptionIds: readonly string[]
): NativeOriginDraftProposal {
    if (!isEditableMixedNativeOrigin(proposal))
        throw new Error("This Origin is not an editable mixed source draft.");
    const selected = proposal.manifestations.find(m => m.candidateId === candidateId);
    const choice = requiredDraftOriginChoices(preview, proposal).find(c =>
        c.candidateId === candidateId && c.requirementId === requirementId);
    if (!selected || !choice || choice.finiteOptions === null ||
        choice.optionCatalogId !== null ||
        selectedOptionIds.length > choice.maximum ||
        new Set(selectedOptionIds).size !== selectedOptionIds.length ||
        !selectedOptionIds.every(id => choice.finiteOptions?.includes(id)))
        throw new Error("The choice is not a finite native offer on this selected candidate.");
    const choices = (selected.choices ?? []).filter(c => c.requirementId !== requirementId);
    if (selectedOptionIds.length > 0)
        choices.push({ requirementId, selectedOptionIds: [...selectedOptionIds] });
    return { ...proposal, manifestations: proposal.manifestations.map(m =>
        m.candidateId === candidateId ? { ...m, choices } : m) };
}

/** Any initial mixed construction requires a separate verified ruling path. */
export function canActivateDirectNativeOrigin(
    proposal: NativeOriginDraftProposal | null,
    preview: NativeOriginPreview | null
): boolean {
    return proposal !== null && isEditableDirectNativeOrigin(proposal) &&
        isAuthorizedInitialNativeOrigin(preview);
}

export function changeDirectOriginIdentity(
    proposal: NativeOriginDraftProposal,
    target: "species" | "ancestry",
    selectedId: string | null
): NativeOriginDraftProposal {
    if (!isDirectOriginProposal(proposal) || proposal.contributors.length !== 0 ||
        proposal.candidates.length !== 0) {
        throw new Error("This proposal requires a separately authorized mixed or custom Origin editor.");
    }
    // Repeating the same selection must not destroy its configured choices.
    if (target === "species" && selectedId === (proposal.selectedSpeciesId ?? null) ||
        target === "ancestry" && selectedId === (proposal.selectedAncestryId ?? null))
        return proposal;
    // A new identity can have different traits, candidate IDs and required
    // choices. Never carry manifestations from a previous selected source.
    return target === "species"
        ? {
            ...proposal,
            selectedSpeciesId: selectedId,
            selectedAncestryId: null,
            manifestations: []
        }
        : {
            ...proposal,
            selectedAncestryId: selectedId,
            manifestations: []
        };
}

export function changeDirectOriginChoice(
    proposal: NativeOriginDraftProposal,
    candidateId: string,
    requirementId: string,
    selectedOptionIds: readonly string[]
): NativeOriginDraftProposal {
    if (!isDirectOriginProposal(proposal) || proposal.contributors.length !== 0 ||
        proposal.candidates.length !== 0) {
        throw new Error("Only a direct first-party Origin can use this choice editor.");
    }
    const prior = proposal.manifestations.find(m => m.candidateId === candidateId);
    const other = proposal.manifestations.filter(m => m.candidateId !== candidateId);
    const choices = (prior?.choices ?? []).filter(c => c.requirementId !== requirementId);
    if (selectedOptionIds.length > 0) {
        choices.push({ requirementId, selectedOptionIds: [...selectedOptionIds] });
    }
    if (choices.length === 0 && prior?.selectedValue === undefined &&
        prior?.selectedParameters === undefined) {
        return { ...proposal, manifestations: other };
    }
    const selected: NativeOriginDraftManifestation = {
        ...(prior ?? {
            id: `selection:${candidateId}`,
            candidateId
        }),
        choices
    };
    return { ...proposal, manifestations: [...other, selected] };
}

export function requiredDraftOriginChoices(
    preview: NativeOriginPreview | null,
    draft: NativeOriginDraftProposal | null
): readonly NativeOriginRequiredChoice[] {
    if (preview === null || draft === null ||
        !(isDirectOriginProposal(draft) || isEditableMixedNativeOrigin(draft)) ||
        !isRecord(preview.result.evaluatedProposal) ||
        (typeof preview.result.evaluatedProposal.id === "string" &&
            preview.result.evaluatedProposal.id !== draft.id) ||
        !Array.isArray(preview.result.evaluatedProposal.candidates) ||
        (isEditableMixedNativeOrigin(draft) &&
            !mixedNativePreviewMatchesDraft(preview, draft))) return [];
    const direct = isDirectOriginProposal(draft);

    const choices: NativeOriginRequiredChoice[] = [];
    for (const rawCandidate of preview.result.evaluatedProposal.candidates) {
        if (!isRecord(rawCandidate) || typeof rawCandidate.id !== "string" ||
            (direct ? rawCandidate.contributorId != null :
                !draft.manifestations.some(m => m.candidateId === rawCandidate.id)) ||
            !Array.isArray(rawCandidate.requiredChoices)) continue;
        const prior = draft.manifestations.find(m => m.candidateId === rawCandidate.id);
        for (const requirement of rawCandidate.requiredChoices) {
            if (!isRecord(requirement) || typeof requirement.id !== "string" ||
                !Number.isInteger(requirement.minimum) ||
                !Number.isInteger(requirement.maximum) ||
                // Creation is enum 0 on the native numeric JSON wire format.
                !(requirement.timing === 0 || requirement.timing === "Creation")) continue;
            const finite = Array.isArray(requirement.finiteOptions) &&
                requirement.finiteOptions.every(v => typeof v === "string")
                ? requirement.finiteOptions as string[] : null;
            const source = isRecord(rawCandidate.identity) &&
                isRecord(rawCandidate.identity.occurrence)
                ? rawCandidate.identity.occurrence : null;
            const sourceName = source && isRecord(source.source) &&
                typeof source.source.id === "string" ? source.source.id : "Origin";
            const selected = prior?.choices?.find(c =>
                c.requirementId === requirement.id)?.selectedOptionIds ?? [];
            choices.push({
                candidateId: rawCandidate.id,
                requirementId: requirement.id,
                label: `${sourceName} · ${requirement.id}`,
                finiteOptions: finite,
                optionCatalogId: typeof requirement.optionCatalogId === "string"
                    ? requirement.optionCatalogId : null,
                minimum: requirement.minimum as number,
                maximum: requirement.maximum as number,
                selectedOptionIds: selected
            });
        }
    }
    return choices.sort((a, b) =>
        a.label.localeCompare(b.label) || a.candidateId.localeCompare(b.candidateId));
}

export function requiredDirectOriginChoices(
    preview: NativeOriginPreview | null, draft: NativeOriginDraftProposal | null
): readonly NativeOriginRequiredChoice[] {
    return draft !== null && isDirectOriginProposal(draft)
        ? requiredDraftOriginChoices(preview, draft) : [];
}

/** A preview object is not enough: incomplete results can still carry a profile. */
export function isAuthorizedInitialNativeOrigin(preview: NativeOriginPreview | null): boolean {
    if (preview === null || preview.previewOnly !== true ||
        !Array.isArray(preview.result?.issues) ||
        preview.result.issues.length !== 0) return false;
    return preview.result.status === "Authorized" || preview.result.status === 4;
}

/** Underlying identity is authoritative only for a committed Origin, not a draft. */
export function savedUnderlyingOriginIdentity(profile: unknown):
    { speciesId: string | null; ancestryId: string | null } | null {
    if (!isRecord(profile) || !isRecord(profile.underlying)) return null;
    const underlying = profile.underlying;
    return {
        speciesId: typeof underlying.speciesId === "string"
            ? underlying.speciesId : null,
        ancestryId: typeof underlying.ancestryId === "string"
            ? underlying.ancestryId : null
    };
}

export function hasLegacySpeciesSelections(
    selections: readonly { category: string }[]
): boolean {
    return selections.some(selection =>
        /^(species|subspecies|racespecies)$/i.test(selection.category));
}
