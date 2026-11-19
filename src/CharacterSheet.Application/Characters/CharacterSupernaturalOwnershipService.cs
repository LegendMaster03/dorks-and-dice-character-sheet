using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterSupernaturalOwnershipOperation
{
    Acquire,
    Advance
}

public enum CharacterSupernaturalOwnershipApplyStatus
{
    Applied, Unauthenticated, NotFoundOrNotOwned, ArchivedReadOnly,
    NotInitialized, InvalidRequest, StateRevisionConflict, LedgerRevisionConflict,
    SourceUnauthorized, PrimaryInputsUnverified, NativeContentChanged,
    AuthoritativeInputsChanged, PlanUnresolved, RulesCoreUnavailable, DuplicateEvent
}

public sealed record CharacterSupernaturalOwnershipApplyResult(
    CharacterSupernaturalOwnershipApplyStatus Status,
    long? StateRevision = null, long? ManifestationRevision = null);

/// <summary>
/// Server-produced source assertion. Its provenance and character binding must
/// be independently checked by the implementation; a client must never supply
/// this material or obtain its constructor through an HTTP model binder.
/// </summary>
public sealed record TrustedSupernaturalOwnershipMaterial(
    JsonElement VerifiedSource, string AuthorizationEvidenceId);

/// <summary>
/// The final Workstream A source adapter is deliberately not registered in
/// production. Synthetic sources can be injected in tests. Null denies.
/// </summary>
public interface ITrustedCharacterSupernaturalOwnershipSource
{
    Task<TrustedSupernaturalOwnershipMaterial?> PrepareAsync(
        Guid characterId, SiteCharacterProjection authorizedCharacter,
        CharacterSupernaturalState? committed,
        CharacterSupernaturalOwnershipOperation operation,
        string intentId, string eventId, CancellationToken cancellationToken);
}

/// <summary>
/// Backend-only acquisition and advancement orchestration. Source authority
/// and all mechanical grants must be evaluated by independently trusted host
/// material and the delegated native Core planner before this service commits
/// their exact outputs. This class is intentionally not reachable by any
/// public mutation route or registered with an unverified production source.
/// </summary>
public sealed class CharacterSupernaturalOwnershipService(
    ISiteCharacterAccessGateway site,
    ISiteFreshCharacterAccessGateway freshSite,
    ICharacterSupernaturalStateStore store,
    INativeRulesCoreGateway rules,
    ITrustedCharacterSupernaturalOwnershipSource source,
    CharacterBuildService builds,
    CharacterStateService routine,
    CharacterOriginStateService origins)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterSupernaturalOwnershipApplyResult> ApplyAsync(
        Guid characterId, CharacterSupernaturalOwnershipOperation operation,
        long expectedStateRevision, long expectedLedgerRevision,
        string intentId, string eventId,
        CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty ||
            !Enum.IsDefined(operation) ||
            expectedStateRevision < 0 || expectedStateRevision == long.MaxValue ||
            expectedLedgerRevision < 0 || expectedLedgerRevision == long.MaxValue ||
            string.IsNullOrWhiteSpace(intentId) || intentId.Length > 160 ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 160 ||
            (operation == CharacterSupernaturalOwnershipOperation.Advance &&
             expectedStateRevision == 0))
            return new(CharacterSupernaturalOwnershipApplyStatus.InvalidRequest);

        var firstLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (firstLive.Status != SiteCharacterAccessStatus.Authorized ||
            firstLive.Character?.CharacterId != characterId)
            return new(FromSite(firstLive.Status));
        if (!firstLive.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterSupernaturalOwnershipApplyStatus.ArchivedReadOnly);

        var scoped = await site.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (scoped.Status != SiteCharacterAccessStatus.Authorized ||
            scoped.Character?.CharacterId != characterId ||
            !CharacterEffectiveStateService.SameSiteScope(
                firstLive.Character, scoped.Character))
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);
        if (!scoped.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterSupernaturalOwnershipApplyStatus.ArchivedReadOnly);

        var saved = await store.GetAsync(characterId, cancellationToken);
        if ((saved?.Revision ?? 0) != expectedStateRevision)
            return new(CharacterSupernaturalOwnershipApplyStatus.StateRevisionConflict,
                saved?.Revision, saved?.LedgerRevision);
        if ((saved?.LedgerRevision ?? 0) != expectedLedgerRevision)
            return new(CharacterSupernaturalOwnershipApplyStatus.LedgerRevisionConflict,
                saved?.Revision, saved?.LedgerRevision);
        if (operation == CharacterSupernaturalOwnershipOperation.Advance && saved is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized);
        if (saved is not null &&
            saved.ContractVersion != CharacterSupernaturalState.CurrentContractVersion)
            return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);

        var before = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (before is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized);

        string rulesetId;
        string catalogFingerprint;
        try
        {
            var metadata = await rules.GetNativeRulesetAsync(cancellationToken);
            if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                metadata["rulesetId"] is not JsonValue ruleset ||
                !ruleset.TryGetValue<string>(out rulesetId!) ||
                metadata["fingerprint"] is not JsonValue fingerprint ||
                !fingerprint.TryGetValue<string>(out catalogFingerprint!) ||
                string.IsNullOrWhiteSpace(rulesetId) ||
                string.IsNullOrWhiteSpace(catalogFingerprint))
                return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);
        }
        catch (Exception e) when (e is RulesCoreGatewayException or JsonException
            or InvalidOperationException or FormatException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.RulesCoreUnavailable);
        }
        if (saved is not null &&
            (saved.RulesetId != rulesetId || saved.Fingerprint != catalogFingerprint))
            return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);

        var build = await builds.GetAsync(characterId, cancellationToken);
        var current = await routine.GetAsync(characterId, cancellationToken);
        var origin = await origins.GetAsync(characterId, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null ||
            current.Status != CharacterStateAccessStatus.Ready || current.View is null ||
            origin.Status != CharacterOriginReadStatus.Ready ||
            origin.SavedState is { } ownerOrigin &&
            (ownerOrigin.RulesetId != rulesetId ||
             ownerOrigin.Fingerprint != catalogFingerprint))
            return new(CharacterSupernaturalOwnershipApplyStatus.PrimaryInputsUnverified);

        var primary = CharacterNativePrimaryStateAdapter.Assemble(
            characterId, build.View, current.View, origin.SavedState,
            rulesetId, catalogFingerprint);
        if (!primary.TransitionInputsVerified || primary.Mechanics is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.PrimaryInputsUnverified);

        TrustedSupernaturalOwnershipMaterial? material;
        try
        {
            material = await source.PrepareAsync(characterId, scoped.Character,
                saved, operation, intentId, eventId, cancellationToken);
        }
        catch (Exception e) when (e is JsonException or ArgumentException
            or InvalidOperationException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);
        }
        if (material is null || !MatchesTrustedSource(material, characterId, operation,
                eventId, rulesetId, catalogFingerprint))
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);

        JsonElement request;
        try
        {
            using var sheets = JsonDocument.Parse(saved?.AlternateSheetsJson ?? "[]");
            using var influences = JsonDocument.Parse(saved?.PersistentInfluencesJson ?? "[]");
            using var ledger = JsonDocument.Parse(saved?.LedgerJson ??
                JsonSerializer.Serialize(new
                {
                    characterId = characterId.ToString("D"), revision = 0,
                    instances = Array.Empty<object>(),
                    resources = Array.Empty<object>(),
                    processedEventIds = Array.Empty<string>()
                }, Wire));
            request = JsonSerializer.SerializeToElement(new
            {
                contractVersion = 1,
                operation = operation.ToString(),
                characterId = characterId.ToString("D"),
                eventId, intentId, rulesetId,
                fingerprint = catalogFingerprint,
                expectedSupernaturalRevision = expectedStateRevision,
                expectedLedgerRevision,
                authoritativeInputsFingerprint = before,
                state = new
                {
                    characterId = characterId.ToString("D"),
                    primaryMechanics = primary.Mechanics.Value,
                    alternateSheets = sheets.RootElement,
                    persistentInfluences = influences.RootElement
                },
                ledger = ledger.RootElement,
                verifiedSource = material.VerifiedSource,
                authorizationEvidenceId = material.AuthorizationEvidenceId
            }, Wire);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);
        }

        JsonNode response;
        try
        {
            // Provisional Workstream A seam: default gateway refuses until the
            // companion native endpoint and its independent source verifier exist.
            response = await rules.PlanVerifiedSupernaturalOwnershipAsync(
                request, cancellationToken);
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode == 409 && error.ErrorCode == "native_content_changed")
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);
        }
        catch (RulesCoreGatewayException error) when (
            error.StatusCode is 400 or 409 or 422)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);
        }
        catch (Exception e) when (e is RulesCoreGatewayException or JsonException
            or InvalidOperationException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.RulesCoreUnavailable);
        }

        if (!TryReadVerifiedPlan(response, characterId, operation,
                eventId, rulesetId, catalogFingerprint,
                expectedStateRevision, expectedLedgerRevision,
                material.AuthorizationEvidenceId,
                out var nextSheets, out var nextInfluences, out var nextLedger))
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);

        var after = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (after is null || after != before)
            return new(CharacterSupernaturalOwnershipApplyStatus.AuthoritativeInputsChanged);

        var lastLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (lastLive.Status != SiteCharacterAccessStatus.Authorized ||
            lastLive.Character?.CharacterId != characterId)
            return new(FromSite(lastLive.Status));
        if (!lastLive.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterSupernaturalOwnershipApplyStatus.ArchivedReadOnly);
        var lastScoped = await site.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (lastScoped.Status != SiteCharacterAccessStatus.Authorized ||
            lastScoped.Character?.CharacterId != characterId ||
            !lastScoped.Character.AllowsOrdinaryEditingByLifecycle ||
            !CharacterEffectiveStateService.SameSiteScope(
                firstLive.Character, lastLive.Character) ||
            !CharacterEffectiveStateService.SameSiteScope(
                scoped.Character, lastScoped.Character))
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);

        CharacterSupernaturalWriteResult commit;
        try
        {
            commit = await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                characterId, rulesetId, catalogFingerprint,
                nextSheets!.ToJsonString(Wire), nextInfluences!.ToJsonString(Wire),
                nextLedger!.ToJsonString(Wire),
                expectedStateRevision, expectedLedgerRevision,
                expectedLedgerRevision, before, eventId,
                material.AuthorizationEvidenceId, false, DateTimeOffset.UtcNow,
                IsProgression: operation == CharacterSupernaturalOwnershipOperation.Advance),
                cancellationToken);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException
            or JsonException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);
        }

        return commit.Status switch
        {
            CharacterSupernaturalWriteStatus.Applied => new(
                CharacterSupernaturalOwnershipApplyStatus.Applied,
                commit.Revision, commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.RevisionConflict => new(
                CharacterSupernaturalOwnershipApplyStatus.StateRevisionConflict,
                commit.Revision, commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.LedgerRevisionConflict => new(
                CharacterSupernaturalOwnershipApplyStatus.LedgerRevisionConflict,
                commit.Revision, commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.DuplicateEvent => new(
                CharacterSupernaturalOwnershipApplyStatus.DuplicateEvent,
                commit.Revision, commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged => new(
                CharacterSupernaturalOwnershipApplyStatus.AuthoritativeInputsChanged),
            _ => new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized)
        };
    }

    internal static bool MatchesTrustedSource(
        TrustedSupernaturalOwnershipMaterial? material,
        Guid characterId, CharacterSupernaturalOwnershipOperation operation,
        string eventId, string rulesetId, string fingerprint)
    {
        if (material is null || string.IsNullOrWhiteSpace(material.AuthorizationEvidenceId) ||
            material.AuthorizationEvidenceId.Length > 320 ||
            material.VerifiedSource.ValueKind != JsonValueKind.Object)
            return false;
        try
        {
            var source = material.VerifiedSource;
            return source.GetProperty("characterId").GetString() == characterId.ToString("D") &&
                source.GetProperty("operation").GetString() == operation.ToString() &&
                source.GetProperty("eventId").GetString() == eventId &&
                source.GetProperty("rulesetId").GetString() == rulesetId &&
                source.GetProperty("fingerprint").GetString() == fingerprint &&
                source.GetProperty("authorizationEvidenceId").GetString() ==
                    material.AuthorizationEvidenceId &&
                source.GetProperty("sourceOccurrence").ValueKind == JsonValueKind.Object;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Strict wire-validation of the provisional delegated result. Structural
    /// validation is not acquisition eligibility: the Core host must verify
    /// the source independently and refuse arbitrary caller-supplied grants.
    /// </summary>
    internal static bool TryReadVerifiedPlan(
        JsonNode? response, Guid characterId,
        CharacterSupernaturalOwnershipOperation operation,
        string eventId, string rulesetId, string fingerprint,
        long expectedStateRevision, long expectedLedgerRevision,
        string evidenceId,
        out JsonArray? alternateSheets, out JsonArray? influences,
        out JsonObject? ledger)
    {
        alternateSheets = null;
        influences = null;
        ledger = null;
        try
        {
            if (response is not JsonObject ||
                response["contractVersion"]?.GetValue<int>() != 1 ||
                response["rulesetId"]?.GetValue<string>() != rulesetId ||
                response["fingerprint"]?.GetValue<string>() != fingerprint ||
                response["status"]?.GetValue<string>() != "planned" ||
                response["committed"]?.GetValue<bool>() != false ||
                response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                response["plan"] is not JsonObject plan ||
                plan["status"]?.GetValue<string>() != "planned" ||
                plan["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                plan["operation"]?.GetValue<string>() != operation.ToString() ||
                plan["eventId"]?.GetValue<string>() != eventId ||
                plan["authorizationEvidenceId"]?.GetValue<string>() != evidenceId ||
                plan["expectedSupernaturalRevision"]?.GetValue<long>() != expectedStateRevision ||
                plan["expectedLedgerRevision"]?.GetValue<long>() != expectedLedgerRevision ||
                plan["alternateSheets"] is not JsonArray sheets ||
                plan["persistentInfluences"] is not JsonArray sources ||
                plan["ledger"] is not JsonObject proposed ||
                proposed["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                proposed["revision"]?.GetValue<long>() != expectedLedgerRevision)
                return false;
            alternateSheets = (JsonArray)sheets.DeepClone();
            influences = (JsonArray)sources.DeepClone();
            ledger = (JsonObject)proposed.DeepClone();
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException
            or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static CharacterSupernaturalOwnershipApplyStatus FromSite(
        SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Unauthenticated =>
            CharacterSupernaturalOwnershipApplyStatus.Unauthenticated,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            CharacterSupernaturalOwnershipApplyStatus.NotFoundOrNotOwned,
        _ => CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized
    };
}
