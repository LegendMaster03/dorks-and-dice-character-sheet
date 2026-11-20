using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterEffectiveStateStatus
{
    Ready, Unauthenticated, NotFoundOrNotOwned, ProjectionUnavailable,
    SheetNotInitialized
}

public sealed record CharacterEffectiveStateView(
    Guid CharacterId, bool ReadOnly, string Status, string? RulesetId,
    string? Fingerprint, long? OriginRevision, long? SupernaturalRevision,
    long ManifestationRevision, string EffectiveSheetId,
    string? ActiveManifestationId, bool EffectiveMechanicsReady,
    JsonNode? NativeProjection, IReadOnlyList<string> Unresolved,
    // Composite partial transformations retain every contributing instance
    // identity. ActiveManifestationId remains the legacy single-form field.
    IReadOnlyList<string>? ActiveManifestationIds = null);

public sealed record CharacterEffectiveStateResult(
    CharacterEffectiveStateStatus Status, CharacterEffectiveStateView? View = null);

/// <summary>
/// Read-only Site-authenticated character-state assembly. Ordinary primary
/// sheets use the bounded native primary projection; stored supernatural
/// sheets use the full native projector only when an independent host verifier
/// accepts the exact current owner record and provenance.
/// </summary>
public sealed class CharacterEffectiveStateService(
    CharacterBuildService builds,
    CharacterStateService routine,
    CharacterOriginStateService origins,
    ICharacterSupernaturalStateStore supernatural,
    INativeRulesCoreGateway core,
    ISiteFreshCharacterAccessGateway freshSite)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterEffectiveStateResult> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        // Cached Tool Host ownership is not sufficient for protected reads.
        var firstLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            id, cancellationToken);
        if (firstLive.Status != SiteCharacterAccessStatus.Authorized)
            return new(FromFreshStatus(firstLive.Status));
        if (firstLive.Character?.CharacterId != id)
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        var build = await builds.GetAsync(id, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null)
            return new(build.Status switch
            {
                CharacterBuildAccessStatus.Unauthenticated => CharacterEffectiveStateStatus.Unauthenticated,
                CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterEffectiveStateStatus.NotFoundOrNotOwned,
                CharacterBuildAccessStatus.SheetNotInitialized => CharacterEffectiveStateStatus.SheetNotInitialized,
                _ => CharacterEffectiveStateStatus.ProjectionUnavailable
            });

        // The first Build fetch proves Site ownership. Its values are not
        // used for native mechanics until a primary dependency fingerprint has
        // been captured. Reload Build inside that window so an advancement
        // update between the first authorization read and other Character
        // stores can never be certified against a digest taken afterward.
        var baselineInputs = await supernatural.GetAuthoritativeInputsFingerprintAsync(
            id, cancellationToken);
        if (baselineInputs is null)
            return new(CharacterEffectiveStateStatus.SheetNotInitialized);
        build = await builds.GetAsync(id, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null)
            return new(build.Status switch
            {
                CharacterBuildAccessStatus.Unauthenticated => CharacterEffectiveStateStatus.Unauthenticated,
                CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterEffectiveStateStatus.NotFoundOrNotOwned,
                CharacterBuildAccessStatus.SheetNotInitialized => CharacterEffectiveStateStatus.SheetNotInitialized,
                _ => CharacterEffectiveStateStatus.ProjectionUnavailable
            });

        var state = await routine.GetAsync(id, cancellationToken);
        if (state.Status != CharacterStateAccessStatus.Ready || state.View is null)
            return new(state.Status switch
            {
                CharacterStateAccessStatus.Unauthenticated => CharacterEffectiveStateStatus.Unauthenticated,
                CharacterStateAccessStatus.NotFoundOrNotOwned => CharacterEffectiveStateStatus.NotFoundOrNotOwned,
                CharacterStateAccessStatus.SheetNotInitialized => CharacterEffectiveStateStatus.SheetNotInitialized,
                _ => CharacterEffectiveStateStatus.ProjectionUnavailable
            });
        var origin = await origins.GetAsync(id, cancellationToken);
        if (origin.Status != CharacterOriginReadStatus.Ready)
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        var persisted = await supernatural.GetAsync(id, cancellationToken);
        var issues = new List<string>
        {
            "class-origin-equipment-grant-applicability-unverified",
            "hit-point-and-class-resource-form-policies-unverified"
        };
        issues.Add(origin.SavedState is null
            ? "authorized-origin-unavailable"
            : "origin-underlying-grants-not-yet-revalidated");
        JsonNode? projection = null;
        string effectiveSheetId = "@primary";
        string? activeManifestationId = null;
        IReadOnlyList<string>? activeManifestationIds = null;
        string? ruleset = persisted?.RulesetId;
        string? fingerprint = persisted?.Fingerprint;
        var supernaturalChanged = false;

        if (persisted is not null)
        {
            // A persisted acquisition is ownership, not entitlement. The
            // second host reads the authoritative record independently before
            // accepting any form or source receipt.
            issues.Add("supernatural-acquisition-authority-unavailable");
            try
            {
                var before = baselineInputs;
                var metadata = await core.GetNativeRulesetAsync(cancellationToken);
                var nativeRuleset = metadata["rulesetId"]?.GetValue<string>();
                var nativeFingerprint = metadata["fingerprint"]?.GetValue<string>();
                if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                    string.IsNullOrWhiteSpace(nativeRuleset) ||
                    string.IsNullOrWhiteSpace(nativeFingerprint))
                    issues.Add("native-ruleset-unavailable");
                else if (persisted.RulesetId != nativeRuleset ||
                    persisted.Fingerprint != nativeFingerprint)
                    issues.Add("supernatural-native-content-changed");
                else if (origin.SavedState is { } originState &&
                    (originState.RulesetId != nativeRuleset ||
                     originState.Fingerprint != nativeFingerprint))
                    issues.Add("authorized-origin-native-content-changed");
                else
                {
                    var primary = CharacterNativePrimaryStateAdapter.Assemble(
                        id, build.View, state.View, origin.SavedState,
                        nativeRuleset, nativeFingerprint);
                    issues.AddRange(primary.Issues);
                    if (before is null || !primary.TransitionInputsVerified ||
                        primary.Mechanics is null)
                        issues.Add("verified-primary-state-unavailable");
                    else
                    {
                        using var sheets = JsonDocument.Parse(persisted.AlternateSheetsJson);
                        using var influences = JsonDocument.Parse(persisted.PersistentInfluencesJson);
                        // Include persisted choices so native read authority
                        // verifies their source and exact active writer set.
                        using var overlap = JsonDocument.Parse(persisted.InfluenceOverlapSelectionsJson);
                        using var ledger = JsonDocument.Parse(persisted.LedgerJson);
                        var request = JsonSerializer.SerializeToElement(new
                        {
                            state = new
                            {
                                characterId = id.ToString("D"),
                                primaryMechanics = primary.Mechanics.Value,
                                alternateSheets = sheets.RootElement,
                                persistentInfluences = influences.RootElement,
                                influenceOverlapSelections = overlap.RootElement
                            },
                            ledger = ledger.RootElement
                        }, Wire);
                        var response = await core.EvaluateVerifiedManifestationStateAsync(
                            request, cancellationToken);
                        var after = await supernatural.GetAuthoritativeInputsFingerprintAsync(
                            id, cancellationToken);
                        // Primary row digests do not include supernatural
                        // ownership. A concurrent transition or acquisition
                        // may change the active form without changing Class,
                        // Origin, equipment or HP inputs.
                        var latest = await supernatural.GetAsync(id, cancellationToken);
                        supernaturalChanged = !SameSupernaturalSnapshot(persisted, latest);
                        if (supernaturalChanged)
                            issues.Add("supernatural-state-changed-during-resolution");
                        else if (after is null || before != after)
                            issues.Add("authoritative-inputs-changed-during-resolution");
                        else if (response["contractVersion"]?.GetValue<int>() != 1 ||
                            response["supportedScope"]?.GetValue<string>() !=
                                "verified-manifestations" ||
                            response["rulesetId"]?.GetValue<string>() != nativeRuleset ||
                            response["fingerprint"]?.GetValue<string>() != nativeFingerprint ||
                            response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                            response["status"]?.GetValue<string>() is not "partial" ||
                            response["projection"] is not JsonObject native ||
                            native["effectiveCharacter"]?["effectiveSheetId"] is not JsonValue selected ||
                            !selected.TryGetValue<string>(out var sheetId) ||
                            !VerifySelectedManifestation(sheetId, native["effectiveCharacter"],
                                sheets.RootElement, ledger.RootElement,
                                out var instanceId, out var compositeIds))
                            issues.Add("native-state-response-unverified");
                        else
                        {
                            // This is an independently authorized effective
                            // read, but Class features, Origin applicability,
                            // equipment and mutable HP policies are still partial.
                            projection = native.DeepClone();
                            effectiveSheetId = sheetId;
                            activeManifestationId = instanceId;
                            activeManifestationIds = compositeIds ?? (instanceId is null
                                ? null : [instanceId]);
                            issues.Remove("supernatural-acquisition-authority-unavailable");
                        }
                    }
                }
            }
            catch (RulesCoreGatewayException)
            {
                issues.Add("verified-manifestation-read-unavailable");
            }
            catch (Exception error) when (error is JsonException or
                InvalidOperationException or FormatException or ArgumentException)
            {
                issues.Add("native-state-response-unverified");
            }
        }
        else
        {
            try
            {
                var before = baselineInputs;
                var metadata = await core.GetNativeRulesetAsync(cancellationToken);
                ruleset = metadata["rulesetId"]?.GetValue<string>();
                fingerprint = metadata["fingerprint"]?.GetValue<string>();

                var primary = CharacterNativePrimaryStateAdapter.Assemble(
                    id, build.View, state.View, origin.SavedState,
                    ruleset ?? "", fingerprint ?? "");
                issues.AddRange(primary.Issues);
                if (primary.Mechanics is null)
                    issues.Add("native-primary-inputs-unresolved");
                else if (before is null || string.IsNullOrWhiteSpace(ruleset) ||
                    string.IsNullOrWhiteSpace(fingerprint) ||
                    metadata["contractVersion"]?.GetValue<int>() != 1)
                    issues.Add("native-ruleset-unavailable");
                else if (origin.SavedState is { } savedOrigin &&
                    (savedOrigin.RulesetId != ruleset ||
                     savedOrigin.Fingerprint != fingerprint))
                    issues.Add("authorized-origin-native-content-changed");
                else
                {
                    // Unsupported independent progression and equipment effects
                    // remain visible in Unresolved while the supported Class
                    // subset is projected by the actual native kernel.
                    var input = JsonSerializer.SerializeToElement(new
                    {
                        state = new
                        {
                            characterId = id.ToString("D"),
                            primaryMechanics = primary.Mechanics.Value,
                            alternateSheets = Array.Empty<object>(),
                            persistentInfluences = Array.Empty<object>()
                        },
                        ledger = new
                        {
                            characterId = id.ToString("D"), revision = 0,
                            instances = Array.Empty<object>(),
                            resources = Array.Empty<object>(),
                            processedEventIds = Array.Empty<string>()
                        }
                    }, Wire);
                    var response = await core.EvaluatePrimaryStateBaselineAsync(input, cancellationToken);
                    var after = await supernatural.GetAuthoritativeInputsFingerprintAsync(id, cancellationToken);
                    // A first supernatural acquisition can occur during an
                    // ordinary baseline calculation. Suppress that stale
                    // primary-only projection rather than showing it as the
                    // current effective character.
                    var latest = await supernatural.GetAsync(id, cancellationToken);
                    supernaturalChanged = !SameSupernaturalSnapshot(persisted, latest);
                    if (supernaturalChanged)
                        issues.Add("supernatural-state-changed-during-resolution");
                    else if (before != after || after is null)
                        issues.Add("authoritative-inputs-changed-during-resolution");
                    else if (response["contractVersion"]?.GetValue<int>() != 1 ||
                        response["supportedScope"]?.GetValue<string>() != "primary-only" ||
                        response["rulesetId"]?.GetValue<string>() != ruleset ||
                        response["fingerprint"]?.GetValue<string>() != fingerprint ||
                        response["status"]?.GetValue<string>() is not "partial" ||
                        response["projection"]?["status"]?.GetValue<string>() == "invalid" ||
                        response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                        response["projection"] is not JsonObject native)
                        issues.Add("native-state-response-unverified");
                    else
                        projection = native.DeepClone();
                }
            }
            catch (Exception error) when (error is RulesCoreGatewayException or
                JsonException or InvalidOperationException or FormatException)
            {
                issues.Add("native-state-evaluation-unavailable");
            }
        }

        // Recheck Site before publishing any private or native-projected data.
        // The primary-data fingerprint does not include Site authorization.
        var lastLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            id, cancellationToken);
        if (lastLive.Status != SiteCharacterAccessStatus.Authorized)
            return new(FromFreshStatus(lastLive.Status));
        if (lastLive.Character is null ||
            !SameSiteScope(firstLive.Character!, lastLive.Character))
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        // Live Site lifecycle, not a cached Build/Origin snapshot, controls
        // whether the returned character is read-only.
        return new(CharacterEffectiveStateStatus.Ready, new(
            id, !lastLive.Character.AllowsOrdinaryEditingByLifecycle,
            projection is null ? "unresolved" : "partial",
            ruleset, fingerprint, origin.SavedState?.Revision,
            supernaturalChanged ? null : persisted?.Revision,
            supernaturalChanged ? 0 : persisted?.LedgerRevision ?? 0,
            effectiveSheetId, activeManifestationId, false, projection, issues,
            activeManifestationIds));
    }

    internal static CharacterEffectiveStateStatus FromFreshStatus(
        SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Unauthenticated =>
            CharacterEffectiveStateStatus.Unauthenticated,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            CharacterEffectiveStateStatus.NotFoundOrNotOwned,
        _ => CharacterEffectiveStateStatus.ProjectionUnavailable
    };

    // Read-time Site consistency, not a cross-service commit fence.
    internal static bool SameSiteScope(
        SiteCharacterProjection first, SiteCharacterProjection last) =>
        first.CharacterId == last.CharacterId &&
        first.LifecycleState == last.LifecycleState &&
        first.ArchivedAt == last.ArchivedAt &&
        first.CampaignIds.Count == first.CampaignIds.Distinct().Count() &&
        last.CampaignIds.Count == last.CampaignIds.Distinct().Count() &&
        first.CampaignIds.ToHashSet().SetEquals(last.CampaignIds);

    /// <summary>
    /// Optimistic second check after the delegated native read. The primary
    /// input fingerprint intentionally excludes the supernatural journal, so
    /// it can not detect concurrent form transitions. The store guarantees
    /// revision increments for every authorized supernatural write. Comparing
    /// both revisions and catalog pins is sufficient to reject a superseded
    /// snapshot; this is not an assertion of a long-lived database lock.
    /// </summary>
    private static bool SameSupernaturalSnapshot(
        CharacterSupernaturalState? before, CharacterSupernaturalState? after) =>
        before is null ? after is null :
        after is not null &&
        before.CharacterId == after.CharacterId &&
        before.ContractVersion == after.ContractVersion &&
        before.Revision == after.Revision &&
        before.LedgerRevision == after.LedgerRevision &&
        before.RulesetId == after.RulesetId &&
        before.Fingerprint == after.Fingerprint &&
        before.DependencyFingerprint == after.DependencyFingerprint;

    internal static bool VerifySelectedManifestation(
        string effectiveSheetId, JsonNode? effective,
        JsonElement sheets, JsonElement ledger, out string? instanceId,
        out IReadOnlyList<string>? compositeIds)
    {
        instanceId = null;
        compositeIds = null;
        if (effective is not JsonObject || sheets.ValueKind != JsonValueKind.Array ||
            ledger.ValueKind != JsonValueKind.Object ||
            !ledger.TryGetProperty("instances", out var instances) ||
            instances.ValueKind != JsonValueKind.Array)
            return false;

        if (effectiveSheetId is not ("@primary" or "@composite") &&
            !sheets.EnumerateArray().Any(sheet =>
                sheet.ValueKind == JsonValueKind.Object &&
                sheet.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String &&
                id.GetString() == effectiveSheetId))
            return false;

        // A claimed primary projection is not current if any independently
        // recorded activation still contributes. Suppressed and ended instances
        // remain in history but do not make an alternate sheet effective.
        var recorded = instances.EnumerateArray().ToArray();
        if (recorded.Any(item =>
            item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(id.GetString()) ||
            !item.TryGetProperty("lifecycle", out _) ||
            !item.TryGetProperty("contribution", out _)))
            return false;
        var contributing = recorded.Where(item =>
            EnumIsActive(item.GetProperty("lifecycle")) &&
            EnumIsContributing(item.GetProperty("contribution"))).ToArray();

        var value = effective["manifestationInstanceId"];
        var combined = effective["contributingManifestationInstanceIds"];
        if (value is null && effectiveSheetId == "@composite")
        {
            if (contributing.Length < 2 || combined is not JsonArray ids ||
                ids.Count != contributing.Length || ids.Any(item =>
                    item is not JsonValue text ||
                    !text.TryGetValue<string>(out var id) ||
                    string.IsNullOrWhiteSpace(id)))
                return false;
            var actual = ids.Select(item => item!.GetValue<string>()).ToArray();
            var expected = contributing.Select(item =>
                item.GetProperty("id").GetString()!).OrderBy(id => id,
                StringComparer.Ordinal).ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal) ||
                !MatchesContributingCoexistence(ledger, contributing))
                return false;
            compositeIds = Array.AsReadOnly(actual);
            return true;
        }
        if (combined is not null)
            return false;
        if (value is null)
            return effectiveSheetId == "@primary" && contributing.Length == 0;
        if (value is not JsonValue selected ||
            !selected.TryGetValue<string>(out var idValue) ||
            string.IsNullOrWhiteSpace(idValue))
            return false;

        // The response's active form must refer to an actually persisted,
        // active and contributing instance of the selected acquired sheet.
        var matching = instances.EnumerateArray().Where(item =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String && id.GetString() == idValue &&
            item.TryGetProperty("sheetId", out var sheet) &&
            sheet.ValueKind == JsonValueKind.String &&
            sheet.GetString() == effectiveSheetId).ToArray();
        if (matching.Length != 1 || effectiveSheetId == "@primary")
            return false;
        var instance = matching[0];
        if (!instance.TryGetProperty("lifecycle", out var life) ||
            !instance.TryGetProperty("contribution", out var contribution) ||
            !EnumIsActive(life) || !EnumIsContributing(contribution) ||
            contributing.Length == 0)
            return false;

        // A single contributing activation selects itself. With multiple
        // contributors, Rules Core selects only when exactly one authorized
        // suppression/replacement interaction covers the entire contributing
        // set. Recheck that decision against persisted IDs, independently of
        // the returned effective-character fields.
        if (contributing.Length > 1 &&
            !MatchesContributingSelection(ledger, contributing, idValue))
            return false;

        instanceId = idValue;
        return true;
    }

    /// <summary>
    /// Validate only the transformation result's correspondence to the
    /// already persisted activation ledger. Mechanics and effect eligibility
    /// are evaluated by Rules Core, not guessed by this presentation adapter.
    /// Every contributor must declare a distinct, typed property slot.
    /// </summary>
    private static bool MatchesContributingCoexistence(
        JsonElement ledger, JsonElement[] contributing)
    {
        var claimedSlots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var instance in contributing)
        {
            if (!instance.TryGetProperty("contributionSlots", out var slots) ||
                slots.ValueKind != JsonValueKind.Array || slots.GetArrayLength() is < 1 or > 128)
                return false;
            foreach (var slot in slots.EnumerateArray())
            {
                if (slot.ValueKind != JsonValueKind.Object ||
                    !slot.TryGetProperty("characteristic", out var characteristic))
                    return false;
                var characteristicId = characteristic.ValueKind switch
                {
                    JsonValueKind.Number when characteristic.TryGetInt32(out var number) &&
                        number is >= 0 and <= 12 => number,
                    JsonValueKind.String => CharacteristicId(characteristic.GetString()),
                    _ => -1
                };
                if (characteristicId < 0) return false;
                string? key = null;
                if (slot.TryGetProperty("key", out var value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        key = value.GetString();
                    else if (value.ValueKind != JsonValueKind.Null)
                        return false;
                }
                if (characteristicId == 11 ? key is not null :
                    string.IsNullOrWhiteSpace(key) || key.Length > 160)
                    return false;
                if (!claimedSlots.Add(characteristicId + ":" + (key ?? "")))
                    return false;
            }
        }

        if (!ledger.TryGetProperty("interactions", out var interactions) ||
            interactions.ValueKind != JsonValueKind.Array)
            return false;
        var ids = contributing.Select(item =>
            item.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        var matches = interactions.EnumerateArray().Where(decision =>
            decision.ValueKind == JsonValueKind.Object &&
            decision.TryGetProperty("kind", out var kind) &&
            (kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out var n) &&
                 n == 0 ||
             kind.ValueKind == JsonValueKind.String &&
                 kind.GetString() == "Coexist") &&
            (!decision.TryGetProperty("selectedInstanceId", out var selected) ||
                selected.ValueKind == JsonValueKind.Null) &&
            decision.TryGetProperty("instanceIds", out var participants) &&
            participants.ValueKind == JsonValueKind.Array &&
            participants.GetArrayLength() == ids.Count &&
            participants.EnumerateArray().All(item =>
                item.ValueKind == JsonValueKind.String &&
                ids.Contains(item.GetString())) &&
            participants.EnumerateArray().Select(item => item.GetString())
                .Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .Take(2).ToArray();
        return matches.Length == 1;
    }

    private static int CharacteristicId(string? name) => name switch
    {
        "AbilityScore" => 0,
        "SkillRanks" => 1,
        "SkillProficient" => 2,
        "SkillMiscModifier" => 3,
        "SkillAbility" => 4,
        "CompetencyRanks" => 5,
        "CompetencyProficient" => 6,
        "CompetencyMiscModifier" => 7,
        "CompetencyAbility" => 8,
        "DefenseContribution" => 9,
        "HitPointGain" => 10,
        "BaseWalkSpeed" => 11,
        "ConditionId" => 12,
        _ => -1
    };

    private static bool MatchesContributingSelection(
        JsonElement ledger, JsonElement[] contributing, string instanceId)
    {
        if (!ledger.TryGetProperty("interactions", out var interactions) ||
            interactions.ValueKind != JsonValueKind.Array)
            return false;

        var activeIds = contributing.Select(item =>
            item.GetProperty("id").GetString()).ToHashSet(StringComparer.Ordinal);
        if (activeIds.Count != contributing.Length || activeIds.Contains(null))
            return false;

        var matching = interactions.EnumerateArray().Where(decision =>
            decision.ValueKind == JsonValueKind.Object &&
            decision.TryGetProperty("instanceIds", out var ids) &&
            ids.ValueKind == JsonValueKind.Array &&
            ids.GetArrayLength() == activeIds.Count &&
            ids.EnumerateArray().All(item =>
                item.ValueKind == JsonValueKind.String &&
                activeIds.Contains(item.GetString())) &&
            ids.EnumerateArray().Select(item => item.GetString())
                .Distinct(StringComparer.Ordinal).Count() == activeIds.Count)
            .Take(2).ToArray();
        if (matching.Length != 1 ||
            !matching[0].TryGetProperty("kind", out var kind) ||
            !IsSelectionInteraction(kind) ||
            !matching[0].TryGetProperty("selectedInstanceId", out var selected) ||
            selected.ValueKind != JsonValueKind.String ||
            selected.GetString() != instanceId)
            return false;

        return true;
    }

    private static bool IsSelectionInteraction(JsonElement kind) =>
        kind.ValueKind == JsonValueKind.Number &&
            kind.TryGetInt32(out var ordinal) && ordinal is 1 or 2 ||
        kind.ValueKind == JsonValueKind.String &&
            kind.GetString() is "Suppress" or "Replace";

    private static bool EnumIsActive(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n == 0 ||
        value.ValueKind == JsonValueKind.String && value.GetString() == "Active";

    private static bool EnumIsContributing(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n == 0 ||
        value.ValueKind == JsonValueKind.String && value.GetString() == "Contributing";
}
