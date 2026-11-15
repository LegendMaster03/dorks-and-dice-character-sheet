using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;

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
    JsonNode? NativeProjection, IReadOnlyList<string> Unresolved);

public sealed record CharacterEffectiveStateResult(
    CharacterEffectiveStateStatus Status, CharacterEffectiveStateView? View = null);

/// <summary>
/// Read-only Site-authenticated character-state assembly. The native adapter
/// currently accepts only ordinary primary sheets. It never infers acquired
/// grants from stored names, evidence strings, or unverified Origin snapshots.
/// </summary>
public sealed class CharacterEffectiveStateService(
    CharacterBuildService builds,
    CharacterStateService routine,
    CharacterOriginStateService origins,
    ICharacterSupernaturalStateStore supernatural,
    INativeRulesCoreGateway core)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterEffectiveStateResult> GetAsync(
        Guid id, CancellationToken cancellationToken = default)
    {
        var build = await builds.GetAsync(id, cancellationToken);
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
        string? ruleset = persisted?.RulesetId;
        string? fingerprint = persisted?.Fingerprint;

        if (persisted is not null)
        {
            // The persistence store is an owner, not an authorizer. A verified
            // native/DM acquisition and replay-safe transition service must be
            // in place before projecting the saved supernatural state.
            issues.Add("supernatural-acquisition-authority-unavailable");
            try
            {
                // A persisted ruleset identity is a pin, not proof that the
                // native catalog still contains the same definitions. Report
                // mismatch even though source authority is not yet installed.
                var metadata = await core.GetNativeRulesetAsync(cancellationToken);
                var nativeRuleset = metadata["rulesetId"]?.GetValue<string>();
                var nativeFingerprint = metadata["fingerprint"]?.GetValue<string>();
                if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                    string.IsNullOrWhiteSpace(nativeRuleset) ||
                    string.IsNullOrWhiteSpace(nativeFingerprint))
                    issues.Add("native-ruleset-unavailable");
                else
                {
                    if (persisted.RulesetId != nativeRuleset ||
                        persisted.Fingerprint != nativeFingerprint)
                        issues.Add("supernatural-native-content-changed");
                    if (origin.SavedState is { } originState &&
                        (originState.RulesetId != nativeRuleset ||
                         originState.Fingerprint != nativeFingerprint))
                        issues.Add("authorized-origin-native-content-changed");
                }
            }
            catch (Exception error) when (error is RulesCoreGatewayException or
                JsonException or InvalidOperationException or FormatException)
            {
                issues.Add("native-ruleset-unavailable");
            }
        }
        else
        {
            try
            {
                var before = await supernatural.GetAuthoritativeInputsFingerprintAsync(id, cancellationToken);
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
                    if (before != after || after is null)
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

        // An unverified stored manifestation is never presented as the active
        // effective form. Keep its revision visible for future reconciliation.
        return new(CharacterEffectiveStateStatus.Ready, new(
            id, build.View.ReadOnly || state.View.ReadOnly || origin.ReadOnly,
            projection is null ? "unresolved" : "partial",
            ruleset, fingerprint, origin.SavedState?.Revision,
            persisted?.Revision, persisted?.LedgerRevision ?? 0,
            "@primary", null, false, projection, issues));
    }
}
