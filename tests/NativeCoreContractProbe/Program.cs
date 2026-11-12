using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Characters;
using CharacterSheet.Application.RulesCore;
using RulesCore.Content;
using RulesCore.Mechanics;

if (args.Length != 1) throw new ArgumentException("Supply Rules Core data/rulesets path.");
var catalog = NativeCatalogLoader.Load(
    NativeRulesetLayout.Resolve(args[0], "first-party-001"));
var gateway = new RealEngineGateway(catalog);
var now = DateTimeOffset.UtcNow;
var occurrence = Guid.NewGuid();
var abilities = new Dictionary<string, int>(StringComparer.Ordinal) {
    ["strength"] = 14, ["dexterity"] = 12, ["constitution"] = 14,
    ["intelligence"] = 10, ["wisdom"] = 12, ["charisma"] = 16
};
var knownOptions = new[] {
    "patron.warlock.fiend", "pact.warlock.blade", "invocation.eldritch-sight"
};
foreach (var id in new[] { "class.warlock" }.Concat(knownOptions))
    Ensure(catalog.TryGetById(id, out _), "Missing native catalog entry " + id);

var request = new NativeCharacterRequest(catalog.RulesetId, catalog.Fingerprint,
    abilities, [new NativeAdvancement("class.warlock", 1, occurrence.ToString("D"))],
    SelectedOptions: knownOptions);
var projection = new NativeCharacterProjector(catalog).Project(request);
Ensure(projection.Status != "invalid", "Real native projection rejected Warlock.");
Ensure(projection.Values.TryGetValue("ability.strength.score", out var strength)
    && strength.Status == "resolved" && strength.Value == 14,
    "Core did not resolve the native Strength score.");
Ensure(projection.Values.TryGetValue("proficiency.bonus", out var proficiency)
    && proficiency.Status == "resolved" && proficiency.Value == 2,
    "Core did not resolve Warlock level-one proficiency.");
Ensure(projection.RequiredChoices.Count == 0,
    "Valid Patron, Pact, and Invocation selections did not satisfy choices.");
Ensure(projection.Resources.Any(item => item.Id == "resource.pact-magic" && item.Maximum == 1),
    "Pact Magic tracker was not projected from the native class.");
var pact = projection.Resources.Single(item => item.Id == "resource.pact-magic");
var spent = NativeResourceResolution.Spend(pact, 1, 1);
Ensure(spent.Status == "resolved" && spent.Current == 0,
    "Native Pact Magic expenditure was not resolved.");
var recovered = NativeResourceResolution.Recover(
    pact, spent.Current!.Value, NativeRecoveryEvent.ShortRest);
Ensure(recovered.Status == "resolved" && recovered.Current == pact.Maximum,
    "Native Pact Magic Short Rest recovery was not resolved.");
var insufficient = NativeResourceResolution.Spend(pact, 1, 2);
Ensure(insufficient.Status == "insufficient-resource" &&
    insufficient.Current == 1,
    "Native Pact Magic overdraft was not rejected.");
var unknownSlots = new NativeResource(
    "resource.spell-slot.1", 2, 0, "unsupported", 1);
var unsupportedRecovery = NativeResourceResolution.Recover(
    unknownSlots, 0, NativeRecoveryEvent.LongRest);
Ensure(unsupportedRecovery.Status == "unsupported",
    "Unsupported spell-slot recovery must remain unavailable.");

// Recover the previously approved Loot Tavern manufacturing outcome using
// actual native Core code and the active immutable ruleset snapshot.
var manufacturing = new NativeCraftingResolver(catalog).ResolveManufacturing(
    new NativeManufacturingRequest(catalog.RulesetId, catalog.Fingerprint,
        new NativeCraftingManualCompetency("Blacksmithing", 3, true),
        D20Roll: 15, TargetDc: 20, ManualAbilityModifier: 2));
Ensure(manufacturing.Status == "resolved" &&
    manufacturing.Outcome == "completed" && manufacturing.Total == 20 &&
    manufacturing.Margin == 0 && manufacturing.InputsConsumed,
    "Real Core manufacturing failed to preserve the existing success formula.");
var manufacturingFailure = new NativeCraftingResolver(catalog).ResolveManufacturing(
    new NativeManufacturingRequest(catalog.RulesetId, catalog.Fingerprint,
        new NativeCraftingManualCompetency("Blacksmithing", 3, false),
        D20Roll: 5, TargetDc: 20, ManualAbilityModifier: 2));
Ensure(manufacturingFailure.Outcome == "failed" &&
    manufacturingFailure.RollMode == "disadvantage" &&
    manufacturingFailure.Total == 10 &&
    !manufacturingFailure.ProducesFunctionalOutput,
    "Real Core manufacturing lost failure or disadvantage behavior.");


// Harvesting uses editable native content rather than a hard-coded parallel
// table collection. Verify a real first-party type and a real bestiary monster.
var harvesting = new NativeHarvestingResolver(catalog);
var harvestingCatalog = harvesting.GetCatalog(catalog.Fingerprint);
Ensure(harvestingCatalog.Source.Provider == "Loot Tavern",
    "Native Loot Tavern attribution was not retained.");
var beast = harvestingCatalog.CreatureTypes.Single(t => t.Key == "beast");
Ensure(beast.DefaultComponents.Any(c => c.Key == "pelt"),
    "The real native Harvesting table is incomplete.");
var harvestTable = harvesting.Resolve(new NativeHarvestingTableRequest(
    catalog.RulesetId, catalog.Fingerprint, CreatureType: "beast",
    ManualEdits: new(UpsertComponents: [
        new("pelt", Quantity: 2)
    ])));
Ensure(harvestTable.Components.Single(c => c.Key == "pelt").Quantity == 2,
    "The real native Harvesting component edits were not applied.");
var harvestOutcome = harvesting.Evaluate(new NativeHarvestingOutcomeRequest(
    new NativeHarvestingTableRequest(
        catalog.RulesetId, catalog.Fingerprint, CreatureType: "beast",
        ManualEdits: new(UpsertComponents: [new("pelt", Quantity: 2)])),
    12, 14, false, ["pelt"], "Medium"));
Ensure(harvestOutcome.Components[0].Quantity == 2 &&
    harvestOutcome.Components[0].HarvestDc > 0,
    "Real native Harvesting outcome did not use authored component values.");
var dragonHarvest = harvesting.Resolve(new NativeHarvestingTableRequest(
    catalog.RulesetId, catalog.Fingerprint,
    CreatureConceptKey: "monster.adult-red-dragon"));
Ensure(dragonHarvest.CreatureType == "dragon" && dragonHarvest.CreatureSize == "Huge",
    "Real native bestiary type and size were not resolved for Harvesting.");

var eligibility = new NativeAdvancementEligibility(catalog).Evaluate(
    new NativeEligibilityRequest(catalog.RulesetId, catalog.Fingerprint,
        "class.warlock", request, HitDieValue: 5));
Ensure(eligibility.Eligible == true, "Native class hit-die eligibility failed.");
var optionRequest = request with {
    SelectedOptions = ["patron.warlock.fiend", "pact.warlock.blade"]
};
var invocationEligibility = new NativeAdvancementEligibility(catalog).Evaluate(
    new NativeEligibilityRequest(catalog.RulesetId, catalog.Fingerprint,
        "invocation.eldritch-sight", optionRequest));
Ensure(invocationEligibility.Eligible == true,
    "Native Invocation prerequisite evaluation failed.");

var entry = new CharacterAdvancementEntryView(
    occurrence, 0, "class", "class.warlock", null, now, now, 1);
var build = new CharacterBuildView(Guid.NewGuid(), "in-progress", false, [],
    abilities.Select(pair => new BaseAbilityScoreInputView(
        Guid.NewGuid(), pair.Key, pair.Value, now, now)).ToArray(), [entry]);
var sheet = await NativeCharacterPresentationBridge.ProjectAsync(
    gateway, build, null, CancellationToken.None);
Ensure(sheet.Mechanics?.AbilityValues?.Any(value =>
    value.Key == "strength" && value.EffectiveValue is int n && n == 14) == true,
    "Character Sheet did not consume actual native ability result.");
Ensure(sheet.Mechanics?.Training?.Any(value =>
    value.Key == "proficiency.bonus" && value.EffectiveValue is int n && n == 2) == true,
    "Character Sheet did not consume actual native proficiency.");

var state = new CharacterStateView(Guid.NewGuid(), false, null,
    new CharacterDeathSavesView(0, 0), [], [], [],
    RulesInputs: knownOptions.Select((id, index) => new CharacterRulesInputStateView(
        Guid.NewGuid(), CharacterRulesInputKinds.Choice,
        index switch {
            0 => "native-option.class.warlock.patron",
            1 => "native-option.class.warlock.pact",
            _ => "native-option.class.warlock.invocation.1"
        }, null, null, id, now, now)).ToArray());
var withOptions = await NativeCharacterPresentationBridge.ProjectAsync(
    gateway, build, state, CancellationToken.None);
Ensure(withOptions.Status != "unavailable", "Character Sheet rejected native choices.");
Ensure(gateway.LastSelectedOptions.SequenceEqual(knownOptions),
    "Character Sheet omitted native options from the real Core request.");

// The real native catalog must reach the existing family/composite renderer
// as structure, even when individual numerical totals remain unresolved.
var training = withOptions.Mechanics!.Competencies!;
foreach (var familyId in new[] { "skill.perform", "skill.profession" })
{
    var family = training.Entries.Single(item => item.Key == familyId);
    Ensure(family.IsFamily && family.PresentationCategory == "skill" &&
        family.ChildCompetencyKeys?.Count > 0 && family.RankInputKey is null,
        "Native skill family was flattened or made independently rankable: " + familyId);
    Ensure(training.Entries.Where(item => item.Family == familyId && !item.IsFamily)
        .Select(item => item.Key).Order().SequenceEqual(family.ChildCompetencyKeys!.Order()),
        "Native family membership did not survive the Sheet projection: " + familyId);
}
var perception = training.Relationships!.Single(item => item.ParentKey == "skill.perception");
Ensure(perception.ComponentKeys.SequenceEqual(new[] { "skill.listen", "skill.spot" }) &&
    perception.Composition == "arithmetic-mean",
    "Native Perception lost its independently ranked children.");
Ensure(training.Entries.Single(item => item.Key == "skill.listen").SupportsRanks == true &&
    training.Entries.Single(item => item.Key == "skill.perception").SupportsRanks == false,
    "Native child and composite rank controls were not preserved.");

#if PILLAR2_PHASE5_ORIGIN
// This job must use the *Phase 5* Rules Core branch, not the currently
// deployed main Core. Validate the actual catalog-backed Origin evaluator
// before treating a mocked HTTP response as evidence of executable content.
var firstPartyMethod = new NativeOriginMethodReference(
    "rule.creature-origination", "biological");
Ensure(catalog.TryGetById("species.orc", out _),
    "Selected native Orc is unavailable.");
var nativeDirectOrigin = new NativeOriginProposal(
    "cross-repo-orc", NativeOriginIntent.Direct, firstPartyMethod,
    [], [], [], SelectedSpeciesId: "species.orc");
var orcOrigin = new NativeOriginConstructionEngine(catalog).Evaluate(
    new NativeOriginConstructionRequest(nativeDirectOrigin));
Ensure(orcOrigin.Status == NativeOriginConstructionStatus.Authorized &&
    orcOrigin.Profile is not null &&
    orcOrigin.Profile.Underlying.SpeciesId == "species.orc" &&
    orcOrigin.Profile.Current.SpeciesId == "species.orc" &&
    orcOrigin.Profile.IndependentGrants.Count == 0 &&
    orcOrigin.Profile.History.Count == 1 &&
    orcOrigin.Profile.History[0].Stage == NativeOriginEventStage.Completed &&
    orcOrigin.Profile.Adjudication.Adjudication == NativeOriginAdjudication.Supported,
    "Real Orc native direct-Origin construction was not authorized without DM claims: " +
    string.Join("; ", orcOrigin.Issues.Select(issue => issue.Code + "=" + issue.Path)));

// Human's mandatory native Origin choices are not invented or bypassed. A
// profile object in an incomplete result is not an authorized active Origin.
var humanOrigin = new NativeOriginConstructionEngine(catalog).Evaluate(
    new NativeOriginConstructionRequest(nativeDirectOrigin with
    {
        Id = "cross-repo-human",
        SelectedSpeciesId = "species.human"
    }));
Ensure(humanOrigin.Status != NativeOriginConstructionStatus.Authorized &&
    humanOrigin.Issues.Any(issue =>
        issue.Code == "required-origin-selection-missing"),
    "Human native Origin choices were silently skipped or treated as complete.");
Console.WriteLine("Phase 5 Origin native-contract checks passed: Orc authorized; Human incomplete.");

#endif

Console.WriteLine(
    $"Cross-repository mechanics contract passed: {catalog.RulesetId}, fingerprint {catalog.Fingerprint}, " +
    "Warlock projection, class eligibility, options, skill families/composites, and Sheet presentation.");
return;

static void Ensure(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class RealEngineGateway(NativeCatalogSnapshot catalog) : INativeRulesCoreGateway
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<string> LastSelectedOptions { get; private set; } = [];

    public Task<JsonNode> GetNativeRulesetAsync(CancellationToken ct) =>
        Task.FromResult(JsonSerializer.SerializeToNode(new {
            contractVersion = 1, rulesetId = catalog.RulesetId,
            fingerprint = catalog.Fingerprint,
            capabilities = new { characterProjection = true,
                advancementEligibility = "supported-subset" }
        }, Options)!);

    public Task<JsonNode> SearchNativeOptionsAsync(
        string fingerprint, string category, string? query, int limit,
        int offset, CancellationToken ct)
    {
        if (fingerprint != catalog.Fingerprint)
            throw new InvalidOperationException("Catalog fingerprint mismatch.");
        var page = catalog.Search(query, category, offset, limit);
        return Task.FromResult(JsonSerializer.SerializeToNode(new {
            contractVersion = 1, rulesetId = catalog.RulesetId,
            fingerprint = catalog.Fingerprint, category,
            options = page.Items.Select(item => new {
                id = item.Id, name = item.Name, kind = item.Category
            }).ToArray()
        }, Options)!);
    }

    public Task<JsonNode> EvaluateNativeAdvancementAsync(
        JsonElement json, CancellationToken ct)
    {
        var request = json.Deserialize<NativeEligibilityRequest>(Options)
            ?? throw new InvalidOperationException("Invalid eligibility request.");
        return Task.FromResult(JsonSerializer.SerializeToNode(
            new NativeAdvancementEligibility(catalog).Evaluate(request), Options)!);
    }

    public Task<JsonNode> ProjectNativeCharacterAsync(
        JsonElement json, CancellationToken ct)
    {
        var request = json.Deserialize<NativeCharacterRequest>(Options)
            ?? throw new InvalidOperationException("Invalid character projection request.");
        LastSelectedOptions = request.SelectedOptions ?? [];
        return Task.FromResult(JsonSerializer.SerializeToNode(
            new NativeCharacterProjector(catalog).Project(request, ct), Options)!);
    }
}
