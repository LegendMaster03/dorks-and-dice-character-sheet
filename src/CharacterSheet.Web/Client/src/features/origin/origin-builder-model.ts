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

export function hasLegacySpeciesSelections(
    selections: readonly { category: string }[]
): boolean {
    return selections.some(selection =>
        /^(species|subspecies|racespecies)$/i.test(selection.category));
}
