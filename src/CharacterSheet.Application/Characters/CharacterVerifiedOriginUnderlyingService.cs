using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterVerifiedOriginUnderlyingStatus
{
    VerifiedPartial,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    SheetNotInitialized,
    OriginUnavailable,
    NativeContentChanged,
    SourceUnverified,
    AuthoritativeInputsChanged,
    RulesCoreUnavailable
}

public sealed record CharacterVerifiedOriginUnderlyingResult(
    CharacterVerifiedOriginUnderlyingStatus Status,
    long? OriginRevision = null,
    JsonNode? Underlying = null,
    JsonNode? Current = null);

/// <summary>
/// Authorized read-only coordination for the existing Origin Underlying model.
/// The saved profile is only a candidate: Rules Core must consult an independent
/// host authority provider for source occurrences, committed provenance and DM
/// evidence, and must review the profile against the current native catalog.
/// This service never issues an inherited grant, builds a new Origin, or
/// activates a supernatural form. Missing production authority fails closed.
/// </summary>
public sealed class CharacterVerifiedOriginUnderlyingService(
    CharacterOriginStateService origins,
    ICharacterSupernaturalStateStore ownerStore,
    INativeRulesCoreGateway rules)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterVerifiedOriginUnderlyingResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty)
            return new(CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned);

        // Site ownership must be established before any character-owned
        // database fingerprint or Origin record is inspected.
        var initialAccess = await origins.GetAsync(characterId, cancellationToken);
        if (initialAccess.Status != CharacterOriginReadStatus.Ready)
            return new(initialAccess.Status switch
            {
                CharacterOriginReadStatus.Unauthenticated =>
                    CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
                CharacterOriginReadStatus.NotFoundOrNotOwned =>
                    CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                CharacterOriginReadStatus.SheetNotInitialized =>
                    CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized,
                _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
            });

        // Reload inside the digest window, so an Origin change between
        // initial Site access and fingerprint acquisition is not certified.
        var before = await ownerStore.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        var origin = await origins.GetAsync(characterId, cancellationToken);
        if (origin.Status != CharacterOriginReadStatus.Ready)
            return new(origin.Status switch
            {
                CharacterOriginReadStatus.Unauthenticated =>
                    CharacterVerifiedOriginUnderlyingStatus.Unauthenticated,
                CharacterOriginReadStatus.NotFoundOrNotOwned =>
                    CharacterVerifiedOriginUnderlyingStatus.NotFoundOrNotOwned,
                CharacterOriginReadStatus.SheetNotInitialized =>
                    CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized,
                _ => CharacterVerifiedOriginUnderlyingStatus.ProjectionUnavailable
            });
        if (before is null)
            return new(CharacterVerifiedOriginUnderlyingStatus.SheetNotInitialized);
        if (origin.SavedState is not { } saved)
            return new(CharacterVerifiedOriginUnderlyingStatus.OriginUnavailable);
        if (saved.CharacterId != characterId || saved.Revision < 1)
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

        try
        {
            var native = await rules.GetNativeRulesetAsync(cancellationToken);
            if (native["contractVersion"]?.GetValue<int>() != 1 ||
                native["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                native["fingerprint"]?.GetValue<string>() != saved.Fingerprint)
                return new(CharacterVerifiedOriginUnderlyingStatus.NativeContentChanged);

            using var profile = JsonDocument.Parse(saved.ProfileJson);
            if (profile.RootElement.ValueKind != JsonValueKind.Object ||
                !profile.RootElement.TryGetProperty("underlying", out var underlying) ||
                !profile.RootElement.TryGetProperty("current", out var current) ||
                underlying.ValueKind != JsonValueKind.Object ||
                current.ValueKind != JsonValueKind.Object)
                return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

            var response = await rules.VerifyNativeOriginUnderlyingAsync(
                JsonSerializer.SerializeToElement(new
                {
                    characterId,
                    originRevision = saved.Revision,
                    rulesetId = saved.RulesetId,
                    fingerprint = saved.Fingerprint,
                    profile = profile.RootElement
                }, Wire), cancellationToken);

            // The host's verdict does not excuse a concurrent mutation or
            // inconsistent returned configuration. Compare the exact source
            // structures, not a species ID, ancestry label or display name.
            var after = await ownerStore.GetAuthoritativeInputsFingerprintAsync(
                characterId, cancellationToken);
            if (after is null || before != after)
                return new(CharacterVerifiedOriginUnderlyingStatus.AuthoritativeInputsChanged);
            if (response["contractVersion"]?.GetValue<int>() != 1 ||
                response["supportedScope"]?.GetValue<string>() !=
                    "verified-origin-underlying" ||
                response["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                response["fingerprint"]?.GetValue<string>() != saved.Fingerprint ||
                response["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                response["originRevision"]?.GetValue<long>() != saved.Revision ||
                response["status"]?.GetValue<string>() != "partial" ||
                response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                response["underlying"] is not JsonObject verifiedUnderlying ||
                response["current"] is not JsonObject verifiedCurrent ||
                !JsonElement.DeepEquals(underlying,
                    JsonSerializer.SerializeToElement(verifiedUnderlying, Wire)) ||
                !JsonElement.DeepEquals(current,
                    JsonSerializer.SerializeToElement(verifiedCurrent, Wire)))
                return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);

            return new(CharacterVerifiedOriginUnderlyingStatus.VerifiedPartial,
                saved.Revision, verifiedUnderlying.DeepClone(), verifiedCurrent.DeepClone());
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode == 409 && error.ErrorCode == "native_content_changed")
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.NativeContentChanged);
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode is 400 or 403 or 409 or 422)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);
        }
        catch (RulesCoreGatewayException)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.RulesCoreUnavailable);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return new(CharacterVerifiedOriginUnderlyingStatus.SourceUnverified);
        }
    }
}
