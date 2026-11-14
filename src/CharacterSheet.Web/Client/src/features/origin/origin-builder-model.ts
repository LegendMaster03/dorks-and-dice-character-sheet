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
 * Two biological *source references*, not verified identities or fully
 * manifested profiles for actual parent characters. This is an evidence
 * draft only: no inherent grants, inheritance decisions or approvals.
 *
 * The general Origin contract supports arbitrary input roles and numbers;
 * this editor exposes only the common two-biological-parent example.
 */
export type MixedNativeParentSlot = "parent-one" | "parent-two";
export type MixedNativeSourceCategory = "species" | "ancestry";

export interface MixedNativeParentSource {
    slot: MixedNativeParentSlot;
    sourceId: string | null;
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
        proposal.manifestations.length !== 0 ||
        proposal.selectedSpeciesId != null || proposal.selectedAncestryId != null ||
        containsExtraAuthority(proposal) || proposal.contributors.length > 2) return false;

    const slots = new Set<string>();
    return proposal.contributors.every(raw => {
        if (!isRecord(raw) || !isRecord(raw.role) || !isRecord(raw.source) ||
            (raw.id !== "parent-one" && raw.id !== "parent-two") ||
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
    if (slot !== "parent-one" && slot !== "parent-two") {
        throw new Error("An unrecognized biological source slot was supplied.");
    }
    if (selectedSourceId !== null &&
        !/^(species|ancestry)\.[a-z0-9]+(?:[.\-][a-z0-9]+)*$/.test(selectedSourceId)) {
        throw new Error("Only native Species or Ancestry source identities are supported.");
    }

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
    return [
        { slot: "parent-one", sourceId: sourceFor("parent-one") },
        { slot: "parent-two", sourceId: sourceFor("parent-two") }
    ];
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

export function requiredDirectOriginChoices(
    preview: NativeOriginPreview | null,
    draft: NativeOriginDraftProposal | null
): readonly NativeOriginRequiredChoice[] {
    if (preview === null || draft === null || !isDirectOriginProposal(draft) ||
        !isRecord(preview.result.evaluatedProposal) ||
        !Array.isArray(preview.result.evaluatedProposal.candidates)) return [];

    const choices: NativeOriginRequiredChoice[] = [];
    for (const rawCandidate of preview.result.evaluatedProposal.candidates) {
        if (!isRecord(rawCandidate) || typeof rawCandidate.id !== "string" ||
            rawCandidate.contributorId != null || !Array.isArray(rawCandidate.requiredChoices)) continue;
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
