import assert from "node:assert/strict";
import test from "node:test";
import { resolveHostEnvironment } from "../.test-dist/host-environment.js";

test("embedded host attributes override the browser pathname", () => {
    const root = {
        dataset: {
            toolBasePath: "/tools/character-sheet",
            toolRoute: "/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7",
            toolContextUrl: "/tool-host/character-sheet/context"
        }
    };

    assert.deepEqual(resolveHostEnvironment(root, "/ignored"), {
        embedded: true,
        toolBasePath: "/tools/character-sheet",
        toolRoute: "/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7",
        contextUrl: "/tool-host/character-sheet/context"
    });
});

test("standalone hosting uses the browser pathname", () => {
    assert.deepEqual(resolveHostEnvironment({ dataset: {} }, "/new"), {
        embedded: false,
        toolBasePath: "",
        toolRoute: "/new",
        contextUrl: null
    });
});
