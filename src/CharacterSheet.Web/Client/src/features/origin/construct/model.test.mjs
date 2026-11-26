import test from "node:test";
import assert from "node:assert/strict";
import { emptyConstructDraft, addComponent, editComponent, removeComponent, constructionAllocation,
    decodeAssembly, decodeConfiguration, setManifestation, hasPhysicalDeclaration, hasProposedContact,
    setPhysicalConnection, setExplicitWiring, setPowerConnection, hasPowerConnection } from "../../../../.construct-test-dist/features/origin/construct/model.js";

test("exact instances support duplicate definitions and retain unaffected settings", () => {
    let draft = emptyConstructDraft("origin", null);
    draft = addComponent(draft, "one", "component.a");
    draft = addComponent(draft, "two", "component.a");
    draft = editComponent(draft, "two", { materials: { shell: "material.b" }, constructionVariantId: "small" });
    const preserved = draft.assembly.components[1];
    const changed = editComponent(draft, "one", { designId: "design.a" });
    assert.equal(changed.assembly.components[1], preserved);
    assert.throws(() => addComponent(draft, "one", "component.a"));
    assert.equal(draft.assembly.initialAllowance, null);
});
test("removal only invalidates exact incident links and parent relationships", () => {
    let draft = emptyConstructDraft("origin", 123);
    for (const id of ["one", "two", "three"]) draft = addComponent(draft, id, "component.a");
    draft = editComponent(draft, "two", { parentInstanceId: "one", mountInterfaceId: "socket", connectedTo: ["one", "three"],
        materials: { shell: "material.b" } });
    draft = setManifestation(draft, "candidate", { id: "manifest", candidateId: "candidate" });
    const prior = draft.assembly.components[2];
    draft = { ...draft, assembly: { ...draft.assembly, powerConnections: [
        { fromInstanceId: "one", toInstanceId: "two" }, { fromInstanceId: "two", toInstanceId: "three" }] } };
    const changed = removeComponent(draft, "one");
    assert.deepEqual(changed.assembly.components[0].connectedTo, ["three"]);
    assert.equal(changed.assembly.components[0].parentInstanceId, null);
    assert.equal(changed.assembly.components[0].mountInterfaceId, null);
    assert.deepEqual(changed.assembly.components[0].materials, { shell: "material.b" });
    assert.equal(changed.assembly.components[1], prior);
    assert.equal(changed.assembly.powerConnections.length, 1);
    assert.equal(changed.proposal, draft.proposal);
});
const assembly = {
    status: 4, knownConstructionCost: 100, components: [], shutdownConsequences: [], issues: [],
    powerNetworks: [{ id: "a", componentInstanceIds: [], requestedDemand: 3, availableOutput: 4,
        operationalDemand: 3, operationalOutput: 4, requestedHeadroom: 1, isOverallocated: false,
        hasUnresolvedDemand: false, hasUnresolvedOutput: false }], requiredConstructionResources: { "material.a": 2 },
    isBodyMassComplete: false, knownRetainedMaterialMassPounds: 21
};
test("native quotation displays known cost without inferring unknown allowance or incomplete cost", () => {
    const result = decodeAssembly(assembly);
    assert.equal(constructionAllocation(123, result).remaining, "23");
    assert.equal(constructionAllocation(null, result).remaining, undefined);
    const partial = decodeAssembly({ ...assembly, status: 2 });
    assert.equal(constructionAllocation(123, partial).remaining, undefined);
    const over = decodeAssembly({ ...assembly, status: 1, issues: [
        { code: "construct-allowance-exceeded", path: "initialAllowance", severity: 0, detail: "Over" }] });
    assert.equal(constructionAllocation(90, over).status, "over");
    assert.equal(constructionAllocation(90, over).remaining, undefined);
});
test("native flags are validated; absent operating power does not become a zero", () => {
    assert.throws(() => decodeAssembly({ ...assembly, isBodyMassComplete: "false" }));
    assert.throws(() => decodeAssembly({ ...assembly, knownConstructionCost: -1 }));
    assert.throws(() => decodeAssembly({ ...assembly, powerNetworks: [{ ...assembly.powerNetworks[0], availableOutput: null }] }));
    assert.equal(decodeAssembly({ ...assembly, powerNetworks: null }).powerNetworks, null);
});
test("configuration discovery retains native variant, mode, and compatibility identities", () => {
    const config = decodeConfiguration({ definitionId: "component.a", name: "Component", materialSlots: ["shell"],
        defaultRecipeId: "recipe.a", constructionVariants: [{ id: "small", recipeId: "recipe.b", bodySize: "small" }],
        modeIds: ["active"], baseCapabilities: ["movement"], requiresAnyOf: [["power", "alternate"]],
        mountInterfaces: [], acceptsMountInterfaces: ["socket"], powerSourceRequired: false, requiresContinuousPower: true,
        designs: [], enhancements: [], powerSources: [], issues: [] });
    assert.equal(config.constructionVariants[0].recipeId, "recipe.b");
    assert.deepEqual(config.requiresAnyOf, [["power", "alternate"]]);
    assert.throws(() => decodeConfiguration({ ...config, powerSourceRequired: 0 }));
});

test("physical links and explicit wiring remain distinct undirected choices", () => {
    let draft = emptyConstructDraft("origin", null);
    for (const id of ["one", "two", "three"]) draft = addComponent(draft, id, "component.a");
    draft = setPhysicalConnection(draft, "one", "two", true);
    assert.equal(hasPhysicalDeclaration(draft, "two", "one"), true);
    assert.equal(draft.assembly.powerConnections, undefined);
    draft = setExplicitWiring(draft, true);
    assert.deepEqual(draft.assembly.powerConnections, []);
    assert.throws(() => setPowerConnection(draft, "one", "three", true));
    draft = setPowerConnection(draft, "one", "two", true);
    assert.equal(setPowerConnection(draft, "two", "one", true), draft);
    assert.equal(hasPowerConnection(draft, "two", "one"), true);
    draft = setPhysicalConnection(draft, "two", "one", false);
    assert.equal(hasPhysicalDeclaration(draft, "one", "two"), false);
    assert.equal(hasPowerConnection(draft, "one", "two"), true);
    draft = setPowerConnection(draft, "two", "one", false);
    assert.deepEqual(draft.assembly.powerConnections, []);
    draft = editComponent(draft, "two", { parentInstanceId: "three", mountInterfaceId: "socket" });
    assert.equal(hasProposedContact(draft, "three", "two"), true);
    draft = setPowerConnection(draft, "two", "three", true);
    assert.equal(draft.assembly.powerConnections.length, 1);
    draft = setExplicitWiring(draft, false);
    assert.equal(draft.assembly.powerConnections, undefined);
    assert.equal(draft.assembly.components[1].parentInstanceId, "three");
});

test("manufacturing stage and external price evidence is native, including unknown prices", () => {
    const quote = { status: "partial", recipes: [{ instanceId: "one", sourceDefinitionId: "component.a",
        recipeId: "recipe.a", stages: [{ stepId: "form", competencyId: "competency.a", minimumRanks: 2,
            dependencies: [], inputs: [{ materialId: "material.a", amount: 2, retainedInProduct: true }],
            outputs: [{ materialId: "material.b", amount: 1, retainedInProduct: false }] }] }],
        ingredients: [{ definitionId: "material.a", quantity: 2, unitCost: null, knownCost: null }] };
    const result = decodeAssembly({ ...assembly, manufacturingQuote: quote, status: 2 });
    assert.equal(result.manufacturingQuote.recipes[0].instanceId, "one");
    assert.equal(result.manufacturingQuote.recipes[0].stages[0].inputs[0].retainedInProduct, true);
    assert.equal(result.manufacturingQuote.ingredients[0].knownCost, null);
    assert.equal(constructionAllocation(123, result).remaining, undefined);
    assert.throws(() => decodeAssembly({ ...assembly, manufacturingQuote: { ...quote, status: "resolved" } }));
    assert.throws(() => decodeAssembly({ ...assembly, manufacturingQuote: { ...quote, status: "unavailable" } }));
    const resolved = decodeAssembly({ ...assembly, status: 1, issues: [{ code: "construct-allowance-exceeded",
        path: "initialAllowance", severity: 0, detail: "Over" }],
        manufacturingQuote: { ...quote, status: "resolved",
            ingredients: [{ definitionId: "material.a", quantity: 2, unitCost: 50, knownCost: 100 }] } });
    assert.equal(constructionAllocation(90, resolved).remaining, "-10");
    assert.equal(constructionAllocation(90, resolved).status, "over");
});
