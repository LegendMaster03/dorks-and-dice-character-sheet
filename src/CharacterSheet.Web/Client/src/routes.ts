export type CharacterSheetRoute =
    | { kind: "new" }
    | { kind: "character"; characterId: string }
    | { kind: "invalid"; route: string };

const canonicalGuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function parseCharacterSheetRoute(route: string): CharacterSheetRoute {
    if (route === "/new") {
        return { kind: "new" };
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
