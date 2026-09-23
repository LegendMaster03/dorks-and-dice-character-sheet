import assert from "node:assert/strict";
import test from "node:test";
import { createInitialState } from "../.test-dist/app-state.js";
import { createApplication } from "../.test-dist/render-lifecycle.js";

test("render lifecycle runs only through explicit render or state dispatch", () => {
    const revisions = [];
    const application = createApplication(
        createInitialState({ kind: "new" }),
        state => revisions.push(state.renderRevision)
    );

    assert.deepEqual(revisions, []);
    application.render();
    application.dispatch({ type: "rerender" });
    application.dispatch({ type: "rerender" }, { render: false });

    assert.deepEqual(revisions, [0, 1]);
    assert.equal(application.getState().renderRevision, 2);

    application.render();
    assert.deepEqual(revisions, [0, 1, 2]);
});
