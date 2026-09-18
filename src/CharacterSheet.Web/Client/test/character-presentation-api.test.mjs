import test from "node:test";
import assert from "node:assert/strict";
import {
    buildCharacterPresentationUrl,
    loadCharacterPresentation
} from "../.test-dist/character-presentation-api.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const embedded = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false,
    rulesCoreDevelopmentBaseUrl: null
};

test("presentation endpoint uses the Character Sheet Tool Host upstream route", () => {
    assert.equal(
        buildCharacterPresentationUrl(embedded, characterId),
        `/tool-host/character-sheet/api/upstream/api/characters/${characterId}/presentation`);
});

test("presentation client returns advancement and mechanics without calling Rules Core directly", async () => {
    const calls = [];
    const payload = {
        advancement: {
            occurrences: [{
                occurrenceId: "feat-1",
                conceptKey: "feat:alert",
                kind: "feat",
                displayName: "Alert"
            }]
        },
        mechanics: {
            competencies: {
                entries: [{
                    key: "skill.hide",
                    label: "Hide",
                    effectiveValue: "Not configured",
                    kind: "skill"
                }]
            }
        }
    };
    const result = await loadCharacterPresentation(embedded, characterId, async (url, init) => {
        calls.push([String(url), init]);
        return new Response(JSON.stringify(payload), {
            status: 200,
            headers: { "Content-Type": "application/json" }
        });
    });
    assert.equal(calls.length, 1);
    assert.match(calls[0][0], /character-sheet\/api\/upstream/);
    assert.doesNotMatch(calls[0][0], /rules-core/);
    assert.equal(result.advancement.occurrences[0].displayName, "Alert");
    assert.equal(result.mechanics.competencies.entries[0].effectiveValue, "Not configured");
});
