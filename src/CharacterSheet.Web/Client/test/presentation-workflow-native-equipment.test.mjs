import assert from "node:assert/strict";
import test from "node:test";
import { createPresentationWorkflow } from "../.test-dist/core/application/presentation-workflow.js";
import {
    createInitialPresentationState,
    reducePresentationState
} from "../.test-dist/state/presentation-state.js";

const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
const environment = {
    embedded: true,
    toolBasePath: "/tools/character-sheet",
    toolRoute: `/characters/${characterId}`,
    contextUrl: "/tool-host/character-sheet/context",
    standaloneDevelopment: false
};
const equipment = {
    contractVersion: 1, rulesetId: "first-party-001",
    fingerprint: "a".repeat(64), status: "needs-choices",
    grants: [], choices: [{ choiceKey: "starting-equipment.initial",
        options: [{ id: "kit", name: "Kit" }] }], issues: []
};

function harness({ guided = false, received = false, unavailablePreview = false } = {}) {
    const previousWindow = globalThis.window;
    const requests = [];
    const actions = [];
    let state = {
        guidedBuilder: { open: guided, activeSection: "class" },
        routine: { state: { rulesInputs: received ? [{
            kind: "booleanFact", key: "starting-equipment.received", booleanValue: true
        }] : [] } },
        presentation: createInitialPresentationState()
    };
    globalThis.window = {
        fetch: async url => {
            const target = String(url);
            requests.push(target);
            if (target.endsWith("/presentation"))
                return Response.json({
                    advancement: { occurrences: [] },
                    mechanics: null,
                    mechanicsStatus: "unconfigured"
                });
            if (target.endsWith("/starting-equipment/preview"))
                return unavailablePreview
                    ? Response.json({ error: "Core unavailable" }, { status: 503 })
                    : Response.json(equipment);
            throw new Error("Unexpected Character Sheet request: " + target);
        }
    };
    const app = {
        getState() { return state; },
        dispatch(action) {
            actions.push(action);
            state = {
                ...state,
                presentation: reducePresentationState(state.presentation, action)
            };
        }
    };
    const workflow = createPresentationWorkflow(app, environment);
    return {
        workflow, actions, requests,
        getState: () => state,
        setGuided(value) {
            state = { ...state, guidedBuilder: { ...state.guidedBuilder, open: value } };
        },
        setReceived(value) {
            state = { ...state, routine: { state: { rulesInputs: value ? [{
                kind: "booleanFact", key: "starting-equipment.received", booleanValue: true
            }] : [] } } };
        },
        restore() { globalThis.window = previousWindow; }
    };
}

test("routine mechanics loads never request starting-equipment preview outside Guided Setup", async () => {
    const testCase = harness();
    try {
        await testCase.workflow.load(characterId);
        await testCase.workflow.load(characterId);
        assert.equal(testCase.requests.filter(url => url.endsWith("/presentation")).length, 2);
        assert.equal(testCase.requests.filter(url => url.endsWith("/preview")).length, 0);
        assert.equal(testCase.getState().presentation.status, "ready");
        assert.equal(testCase.getState().presentation.equipmentPreviewStatus, undefined);
    } finally {
        testCase.restore();
    }
});

test("opening Guided Setup loads native choices and can refresh them after rules changes", async () => {
    const testCase = harness();
    try {
        testCase.setGuided(true);
        await testCase.workflow.load(characterId);
        assert.equal(testCase.requests.length, 2);
        assert.ok(testCase.requests[0].endsWith("/presentation"));
        assert.ok(testCase.requests[1].endsWith("/starting-equipment/preview"));
        assert.equal(testCase.getState().presentation.status, "ready");
        assert.equal(testCase.getState().presentation.equipmentPreviewStatus, "ready");
        assert.equal(testCase.getState().presentation.equipmentPreview.choices[0].choiceKey,
            "starting-equipment.initial");

        await testCase.workflow.load(characterId);
        assert.equal(testCase.requests.filter(url => url.endsWith("/preview")).length, 2);
    } finally {
        testCase.restore();
    }
});

test("awarded ordinary inventory remains independent from later native equipment changes", async () => {
    const testCase = harness({ guided: true, received: true });
    try {
        await testCase.workflow.load(characterId);
        assert.equal(testCase.requests.length, 1);
        assert.equal(testCase.getState().presentation.equipmentPreview, null);
        assert.equal(testCase.getState().presentation.equipmentPreviewStatus, undefined);
    } finally {
        testCase.restore();
    }
});

test("native equipment preview failure does not suppress independent mechanics", async () => {
    const testCase = harness({ guided: true, unavailablePreview: true });
    try {
        await testCase.workflow.load(characterId);
        assert.equal(testCase.getState().presentation.status, "ready");
        assert.equal(testCase.getState().presentation.equipmentPreviewStatus, "error");
        assert.match(testCase.getState().presentation.equipmentPreviewMessage, /Core unavailable/);
        assert.ok(testCase.actions.some(action => action.type === "presentation-loaded"));
    } finally {
        testCase.restore();
    }
});
