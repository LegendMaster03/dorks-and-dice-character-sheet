import test from "node:test";
import assert from "node:assert/strict";
import { matchingRuleNodes, resolveMethodEditor, isCurrentEvaluation } from "../.test-dist/features/guided/contracts.js";

test("hierarchical search retains exact ancestors and nonselectability", () => {
    const nodes = [{ id: "root", label: "Root", selectable: false },
        { id: "a", parentId: "root", label: "Deep", selectable: false },
        { id: "b", parentId: "a", label: "Match", selectable: true },
        { id: "other", label: "Other", selectable: true }];
    assert.deepEqual(matchingRuleNodes(nodes, "match"), nodes.slice(0, 3));
    assert.deepEqual(matchingRuleNodes(nodes, "none"), []);
});
test("method registry refuses reference-only and ambiguous editors", () => {
    const reference = { ruleId: "rule", methodId: "method" };
    const editor = { reference, decisions: [], render() {} };
    const method = { reference, support: "initial-preview" };
    assert.equal(resolveMethodEditor(method, [editor]), editor);
    assert.equal(resolveMethodEditor(method, [editor, editor]), null);
    assert.equal(resolveMethodEditor({ ...method, support: "reference-only" }, [editor]), null);
    assert.equal(resolveMethodEditor({ ...method, support: "verified-reconstruction-plan" }, [editor]), null);
});
test("evaluation requires exact draft revision and catalog identity", () => {
    const draft = { revision: 4, rulesetId: "rule", fingerprint: "one" };
    const evaluation = { draftRevision: 4, rulesetId: "rule", fingerprint: "one", previewOnly: true };
    assert.equal(isCurrentEvaluation(draft, evaluation), true);
    assert.equal(isCurrentEvaluation(draft, { ...evaluation, draftRevision: 3 }), false);
    assert.equal(isCurrentEvaluation(draft, { ...evaluation, fingerprint: "two" }), false);
    assert.equal(isCurrentEvaluation(draft, { ...evaluation, rulesetId: "other" }), false);
    assert.equal(isCurrentEvaluation(draft, { ...evaluation, previewOnly: false }), false);
});
