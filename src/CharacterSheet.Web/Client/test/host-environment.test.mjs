import assert from "node:assert/strict";
import test from "node:test";
import { resolveHostEnvironment } from "../.test-dist/host-environment.js";

test("embedded host attributes override the browser pathname and disable standalone adapters", () => {
    const root = {
        dataset: {
            toolBasePath: "/tools/character-sheet",
            toolRoute: "/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7",
            toolContextUrl: "/tool-host/character-sheet/context",
            standaloneDevelopment: "true",
            rulesCoreDevelopmentBaseUrl: "http://unsafe.example"
        }
    };

    assert.deepEqual(resolveHostEnvironment(root, "/ignored"), {
        embedded: true,
        toolBasePath: "/tools/character-sheet",
        toolRoute: "/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7",
        contextUrl: "/tool-host/character-sheet/context",
        standaloneDevelopment: false,
        rulesCoreDevelopmentBaseUrl: null
    });
});

test("standalone hosting uses the browser pathname without a development adapter by default", () => {
    assert.deepEqual(resolveHostEnvironment({ dataset: {} }, "/new"), {
        embedded: false,
        toolBasePath: "",
        toolRoute: "/new",
        contextUrl: null,
        standaloneDevelopment: false,
        rulesCoreDevelopmentBaseUrl: null
    });
});

test("standalone development can expose an explicitly configured Rules Core base", () => {
    assert.deepEqual(resolveHostEnvironment({
        dataset: {
            standaloneDevelopment: "true",
            rulesCoreDevelopmentBaseUrl: "http://localhost:5188///"
        }
    }, "/characters/example"), {
        embedded: false,
        toolBasePath: "",
        toolRoute: "/characters/example",
        contextUrl: null,
        standaloneDevelopment: true,
        rulesCoreDevelopmentBaseUrl: "http://localhost:5188"
    });
});
