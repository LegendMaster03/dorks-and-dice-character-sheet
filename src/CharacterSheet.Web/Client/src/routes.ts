/**
 * Parses standalone and embedded Character Sheet routes while preserving the Site-issued character
 * identifier.
 */
export type CharacterSheetRoute =
    | { kind: "new" }
    | { kind: "character"; characterId: string }
    | { kind: "dm-origin-review"; campaignId: string; characterId: string }
    | { kind: "invalid"; route: string };

const canonicalGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function parseCharacterSheetRoute(route: string): CharacterSheetRoute {
    if (route === "/new") {
        return { kind: "new" };
    }

    // DM-only review resolves a campaign and target without owner-sheet access.
    const dmMatch = /^\/dm-origin-review\/([^/]+)\/([^/]+)$/.exec(route);
    if (dmMatch && canonicalGuidPattern.test(dmMatch[1]) &&
        canonicalGuidPattern.test(dmMatch[2])) {
        return { kind: "dm-origin-review",
            campaignId: dmMatch[1].toLowerCase(),
            characterId: dmMatch[2].toLowerCase() };
    }

    const match = /^\/characters\/([^/]+)$/.exec(route);
    if (match && canonicalGuidPattern.test(match[1])) {
        return {
            kind: "character",
            characterId: match[1].toLowerCase()
        };
    }

    return { kind: "invalid", route };
}
