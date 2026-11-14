import assert from "node:assert/strict";
import test from "node:test";
import { parseCharacterSheetRoute } from "../.test-dist/routes.js";

test("parses the new-character route", () => {
    assert.deepEqual(parseCharacterSheetRoute("/new"), { kind: "new" });
});

test("parses a canonical character route", () => {
    assert.deepEqual(
        parseCharacterSheetRoute("/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7"),
        {
            kind: "character",
            characterId: "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7"
        });
});

test("rejects malformed character routes", () => {
    assert.deepEqual(parseCharacterSheetRoute("/characters/not-a-guid"), {
        kind: "invalid",
        route: "/characters/not-a-guid"
    });
    assert.equal(parseCharacterSheetRoute("/characters/8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7/extra").kind, "invalid");
});

test("DM review route retains both campaign and target without owner access", () => {
    const campaignId = "3e50b027-9c43-401f-bb31-ae4ae7409ce4";
    const characterId = "8f62ed58-0f5f-4e71-9a18-8d9dcfe71dc7";
    assert.deepEqual(parseCharacterSheetRoute(
        `/dm-origin-review/${campaignId.toUpperCase()}/${characterId}`), {
        kind: "dm-origin-review", campaignId, characterId
    });
    for (const invalid of [
        `/dm-origin-review/invalid/${characterId}`,
        `/dm-origin-review/${campaignId}/../../secrets`,
        `/dm-origin-review/${campaignId}/${characterId}/extra`
    ]) {
        assert.equal(parseCharacterSheetRoute(invalid).kind, "invalid");
    }
});
