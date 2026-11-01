// The old /api/rules application interface is no longer part of Rules Core.
// Keep harvesting transport shapes while the native harvesting contract is built.
// Unsupported operations fail locally, without attempting legacy HTTP routes.
export class RulesCoreApiError extends Error {
    constructor(message: string, public readonly status: number) {
        super(message);
        this.name = "RulesCoreApiError";
    }
}

export interface HarvestingSourceResponse {
    workKey: string;
    workDisplayName: string;
    provider: string;
    gameEdition: string;
    releaseKind: string;
    publicationDate: string;
    referenceUri: string;
}

export interface HarvestingComponentResponse {
    key: string;
    displayName: string;
    componentDc: number;
    quantity: number | null;
    origin: string | null;
}

export interface HarvestingCreatureTypeResponse {
    key: string;
    displayName: string;
    competencyKey: string;
    competencyDisplayName: string;
    defaultComponents: HarvestingComponentResponse[];
}

export interface HarvestingProcedureCheckResponse {
    mechanicKey: string;
    abilityKey: string;
    abilityDisplayName: string;
    defaultRollMode: string;
}

export interface HarvestingProcedureResponse {
    assessment: HarvestingProcedureCheckResponse;
    carving: HarvestingProcedureCheckResponse;
    totalMechanicKey: string;
    sameActorRollMode: string;
    componentDcAggregation: string;
    awardMode: string;
    helpers: {
        maximumByCreatureSize: Record<string, number>;
        standardHelpActionApplies: boolean;
    };
}

export interface HarvestingRulesCatalogResponse {
    source: HarvestingSourceResponse;
    creatureTypes: HarvestingCreatureTypeResponse[];
    procedure: HarvestingProcedureResponse;
}

export interface HarvestingComponentEditInput {
    key: string;
    displayName?: string | null;
    componentDc?: number | null;
    quantity?: number | null;
}

export interface HarvestingTableResolutionInput {
    creatureConceptKey?: string | null;
    creatureType?: string | null;
    manualEdits?: {
        removeComponentKeys?: string[] | null;
        upsertComponents?: HarvestingComponentEditInput[] | null;
    } | null;
}

export interface HarvestingResolvedTableResponse {
    source: HarvestingSourceResponse;
    creatureType: string;
    creatureTypeDisplayName: string;
    competencyKey: string;
    competencyDisplayName: string;
    components: HarvestingComponentResponse[];
    creatureConceptKey: string | null;
    creatureDisplayName: string | null;
    creatureSize: string | null;
    creatureOverridesApplied: boolean;
    manualEditsApplied: boolean;
}

export interface HarvestingHelperInput {
    proficiencyBonus: number;
    isProficient: boolean;
    participatedForEntireDuration?: boolean;
    isAssessmentParticipant?: boolean;
    isCarvingParticipant?: boolean;
}

export interface HarvestingOutcomeInput {
    table: HarvestingTableResolutionInput;
    assessmentResult: number;
    carvingResult: number;
    sameActor: boolean;
    harvestOrderComponentKeys: string[];
    creatureSize?: string | null;
    helpers?: HarvestingHelperInput[] | null;
}

export interface HarvestingComponentOutcomeResponse {
    key: string;
    displayName: string;
    componentDc: number;
    harvestDc: number;
    quantity: number | null;
    origin: string | null;
    awarded: boolean;
}

export interface HarvestingOutcomeResponse {
    table: HarvestingResolvedTableResponse;
    assessmentRollMode: string;
    carvingRollMode: string;
    creatureSize: string | null;
    assessmentResult: number;
    carvingResult: number;
    helperCount: number;
    helperContribution: number;
    harvestingResult: number;
    components: HarvestingComponentOutcomeResponse[];
}


const nativeHarvestingUnavailable =
    "Harvesting is unavailable until Rules Core exposes a native harvesting contract.";

export async function loadHarvestingRules(
    _environment: unknown,
    _fetcher?: unknown
): Promise<HarvestingRulesCatalogResponse> {
    throw new RulesCoreApiError(nativeHarvestingUnavailable, 501);
}

export async function resolveHarvestingTable(
    _environment: unknown,
    _input: HarvestingTableResolutionInput,
    _campaignId: string | null = null,
    _fetcher?: unknown
): Promise<HarvestingResolvedTableResponse> {
    throw new RulesCoreApiError(nativeHarvestingUnavailable, 501);
}

export async function resolveHarvestingOutcome(
    _environment: unknown,
    _input: HarvestingOutcomeInput,
    _campaignId: string | null = null,
    _fetcher?: unknown
): Promise<HarvestingOutcomeResponse> {
    throw new RulesCoreApiError(nativeHarvestingUnavailable, 501);
}
