import assert from "node:assert/strict";
import test from "node:test";
import { CharacterSheetApiError } from "../.test-dist/character-api.js";
import { createRoutineStateWorkflow } from "../.test-dist/core/application/routine-state-workflow.js";

const id = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${id}`,
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false
};

test("a concurrent native spend refreshes saved state and never replays the action", async () => {
    const originalWindow = globalThis.window;
    const refreshed = {
        characterId: id, readOnly: false, currentHitPoints: null,
        deathSaves: { successes: 0, failures: 0 },
        inventoryItemOccurrences: [], rulesInputs: [{
            id: "00000000-0000-0000-0000-000000000001",
            kind: "resource", key: "resource.pact-magic",
            integerValue: 0, booleanValue: null, textValue: null,
            createdAt: "now", updatedAt: "now"
        }], conditions: [], notes: []
    };
    const requests = [];
    globalThis.window = { fetch: async (url) => {
        requests.push(String(url));
        return Response.json(refreshed);
    } };
    try {
        const actions = [];
        let state = {
            routine: {
                status: "ready",
                state: { ...refreshed, rulesInputs: refreshed.rulesInputs.map(input => ({
                    ...input, integerValue: 1
                })) },
                mutation: null
            }
        };
        const app = {
            getState: () => state,
            dispatch(action) {
                actions.push(action);
                if (action.type === "routine-loaded") {
                    state = { ...state, routine: {
                        ...state.routine, state: action.state
                    } };
                }
            }
        };
        const workflow = createRoutineStateWorkflow(app, environment);
        let attempted = 0;
        const success = await workflow.mutate("native-resource-action", async () => {
            attempted++;
            throw new CharacterSheetApiError("Resource changed since evaluation.", 409);
        }, "resource:resource.pact-magic");
        assert.equal(success, false);
        assert.equal(attempted, 1);
        assert.deepEqual(requests, [
            `/tool-host/character-sheet/api/upstream/api/characters/${id}/state`
        ]);
        assert.equal(state.routine.state.rulesInputs[0].integerValue, 0);
        assert.deepEqual(actions.map(action => action.type), [
            "routine-mutation-started", "routine-loaded", "routine-mutation-failed"
        ]);
        assert.match(actions.at(-1).message, /changed since/i);
    } finally {
        globalThis.window = originalWindow;
    }
});
