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

Console.WriteLine(
    $"Cross-repository mechanics contract passed: {catalog.RulesetId}, fingerprint {catalog.Fingerprint}, " +
    "Warlock projection, class eligibility, options, and Sheet presentation.");
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
