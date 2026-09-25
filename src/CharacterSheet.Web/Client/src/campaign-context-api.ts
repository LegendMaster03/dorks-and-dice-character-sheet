import type { HostEnvironment } from "./host-environment.js";
import type { FetchLike } from "./character-api.js";

export interface CampaignParticipantContextResponse {
    participantId: string;
    displayName: string;
    userId?: string | null;
}

export interface CampaignCharacterContextResponse {
    characterId: string;
    ownerUserId: string;
    name: string;
}

export interface CampaignContextResponse {
    campaignId: string;
    name: string;
    requestingUserRoles: string[];
    participants: CampaignParticipantContextResponse[];
    characters: CampaignCharacterContextResponse[];
}

export async function loadHostedCampaignContext(
    environment: HostEnvironment,
    campaignId: string,
    fetcher: FetchLike = window.fetch.bind(window)
): Promise<CampaignContextResponse> {
    if (!environment.embedded || environment.contextUrl === null) {
        throw new Error("Campaign roster context is available only when Character Sheet is hosted by Dorks & Dice.");
    }

    const queryOrFragmentIndex = environment.contextUrl.search(/[?#]/);
    const contextPath = queryOrFragmentIndex >= 0
        ? environment.contextUrl.slice(0, queryOrFragmentIndex)
        : environment.contextUrl;
    if (!contextPath.endsWith("/context")) {
        throw new Error("Character Sheet is missing a valid Tool Host context URL.");
    }

    const hostBase = contextPath.slice(0, -"/context".length);
    const response = await fetcher(
        `${hostBase}/api/campaigns/${encodeURIComponent(campaignId)}/context`,
        {
            method: "GET",
            headers: { Accept: "application/json" }
        });

    if (!response.ok) {
        throw new Error("Campaign roster context is unavailable.");
    }

    return await response.json() as CampaignContextResponse;
}
