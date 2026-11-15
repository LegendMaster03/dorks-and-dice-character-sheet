using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterManifestationApplyStatus
{
    Applied, Unauthenticated, NotFoundOrNotOwned, ProjectionUnavailable,
    ArchivedReadOnly, NotInitialized, InvalidRequest, StateRevisionConflict,
    LedgerRevisionConflict, SourceUnauthorized, NativeContentChanged,
    AuthoritativeInputsChanged, PlanUnresolved, RulesCoreUnavailable,
    DuplicateEvent
}

public sealed record CharacterManifestationApplyResult(
    CharacterManifestationApplyStatus Status, long? StateRevision = null,
    long? LedgerRevision = null, JsonNode? EffectivePreview = null);

/// <summary>
/// This type may only be constructed by a separately trusted, host-side source
/// policy which verifies exact acquisition, form eligibility, rules/DM evidence,
/// and primary Origin/Class state. A browser may submit only intent and
/// revision, never these authoritative records or credentials.
/// </summary>
public sealed record TrustedManifestationTransitionMaterial(
    JsonElement PrimaryMechanics,
    JsonElement Transition,
    string AuthorizationEvidenceId);

/// <summary>
/// No production implementation exists until first-party source rules or
/// separately recorded campaign/DM rulings have an independent verifier.
/// A missing/denying policy is not an authorization or default ruleset.
/// </summary>
public interface ITrustedCharacterManifestationTransitionSource
{
    Task<TrustedManifestationTransitionMaterial?> PrepareAsync(
        Guid characterId, SiteCharacterProjection authorizedCharacter,
        CharacterSupernaturalState existing,
        string intentId, string eventId, CancellationToken cancellationToken);
}

/// <summary>
/// Backend-only coordination of trusted source selection, delegated Rules Core
/// transition planning, and one Character Sheet PostgreSQL CAS commit.
/// Does not compute mechanics locally and has no public mutation endpoint.
/// </summary>
public sealed class CharacterManifestationTransitionService(
    ISiteCharacterAccessGateway site,
    ICharacterSupernaturalStateStore store,
    INativeRulesCoreGateway rules,
    ITrustedCharacterManifestationTransitionSource source)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterManifestationApplyResult> ApplyAsync(
        Guid characterId, long expectedStateRevision, long expectedLedgerRevision,
        string intentId, string eventId, CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || expectedStateRevision < 1 ||
            expectedLedgerRevision < 0 || expectedLedgerRevision == long.MaxValue ||
            string.IsNullOrWhiteSpace(intentId) ||
            intentId.Length > 160 || string.IsNullOrWhiteSpace(eventId) ||
            eventId.Length > 160)
            return new(CharacterManifestationApplyStatus.InvalidRequest);

        // Character-owned data is not inspected before Site confirms current
        // account ownership and character lifecycle.
        var access = await site.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        if (access.Status != SiteCharacterAccessStatus.Authorized ||
            access.Character?.CharacterId != characterId)
            return new(access.Status switch
            {
                SiteCharacterAccessStatus.Unauthenticated => CharacterManifestationApplyStatus.Unauthenticated,
                SiteCharacterAccessStatus.NotFoundOrNotOwned => CharacterManifestationApplyStatus.NotFoundOrNotOwned,
                _ => CharacterManifestationApplyStatus.ProjectionUnavailable
            });
        if (!access.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterManifestationApplyStatus.ArchivedReadOnly);

        var saved = await store.GetAsync(characterId, cancellationToken);
        if (saved is null)
            return new(CharacterManifestationApplyStatus.NotInitialized);
        if (saved.ContractVersion != CharacterSupernaturalState.CurrentContractVersion)
            return new(CharacterManifestationApplyStatus.NativeContentChanged);
        if (saved.Revision != expectedStateRevision)
            return new(CharacterManifestationApplyStatus.StateRevisionConflict,
                saved.Revision, saved.LedgerRevision);
        if (saved.LedgerRevision != expectedLedgerRevision)
            return new(CharacterManifestationApplyStatus.LedgerRevisionConflict,
                saved.Revision, saved.LedgerRevision);

        var before = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (before is null)
            return new(CharacterManifestationApplyStatus.NotInitialized);

        TrustedManifestationTransitionMaterial? verified;
        try
        {
            verified = await source.PrepareAsync(characterId, access.Character,
                saved, intentId, eventId, cancellationToken);
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return new(CharacterManifestationApplyStatus.SourceUnauthorized);
        }
        if (verified is null ||
            string.IsNullOrWhiteSpace(verified.AuthorizationEvidenceId) ||
            verified.AuthorizationEvidenceId.Length > 320 ||
            !MatchesVerifiedMaterial(verified, saved, characterId, expectedLedgerRevision, eventId))
            return new(CharacterManifestationApplyStatus.SourceUnauthorized);

        // Build the planner input from the current authoritative owner record.
        // The trusted policy provides only the primary snapshot and eligible
        // transition. It can not replace persisted sheets, grants, influences
        // or journal content on this request.
        JsonElement request;
        try
        {
            using var sheets = JsonDocument.Parse(saved.AlternateSheetsJson);
            using var influences = JsonDocument.Parse(saved.PersistentInfluencesJson);
            using var ledger = JsonDocument.Parse(saved.LedgerJson);
            request = JsonSerializer.SerializeToElement(new
            {
                state = new
                {
                    characterId = characterId.ToString("D"),
                    primaryMechanics = verified.PrimaryMechanics,
                    alternateSheets = sheets.RootElement,
                    persistentInfluences = influences.RootElement
                },
                ledger = ledger.RootElement,
                transition = verified.Transition
            }, Wire);
        }
        catch (JsonException)
        {
            return new(CharacterManifestationApplyStatus.PlanUnresolved);
        }

        JsonNode response;
        try
        {
            response = await rules.PlanVerifiedManifestationAsync(request, cancellationToken);
        }
        catch (Exception error) when (error is RulesCoreGatewayException or JsonException
            or InvalidOperationException)
        {
            return new(CharacterManifestationApplyStatus.RulesCoreUnavailable);
        }

        JsonNode? proposed;
        JsonNode? effective;
        try
        {
            if (response["contractVersion"]?.GetValue<int>() != 1 ||
                response["rulesetId"]?.GetValue<string>() != saved.RulesetId ||
                response["fingerprint"]?.GetValue<string>() != saved.Fingerprint ||
                response["status"]?.GetValue<string>() != "planned" ||
                response["committed"]?.GetValue<bool>() != false ||
                response["effectiveMechanicsReady"]?.GetValue<bool>() != false ||
                response["plan"]?["status"]?.GetValue<string>() != "planned" ||
                response["plan"]?["expectedRevision"]?.GetValue<long>() != expectedLedgerRevision)
                return new(CharacterManifestationApplyStatus.PlanUnresolved);

            proposed = response["plan"]?["proposed"];
            effective = response["plan"]?["proposedEffective"];
            if (proposed is not JsonObject ||
                effective is not JsonObject ||
                proposed["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                proposed["revision"]?.GetValue<long>() != expectedLedgerRevision + 1 ||
                effective["status"]?.GetValue<string>() is "invalid" or "unresolved" ||
                effective["effectiveSheetId"] is null)
                return new(CharacterManifestationApplyStatus.PlanUnresolved);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or FormatException)
        {
            return new(CharacterManifestationApplyStatus.PlanUnresolved);
        }

        // Core planning itself does not commit or lock this Character. The
        // PostgreSQL store must check both the read snapshot and the complete
        // primary dependency fingerprint under its own serializable transaction.
        var after = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (before != after || after is null)
            return new(CharacterManifestationApplyStatus.AuthoritativeInputsChanged);

        CharacterSupernaturalWriteResult commit;
        try
        {
            commit = await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                characterId, saved.RulesetId, saved.Fingerprint,
                saved.AlternateSheetsJson, saved.PersistentInfluencesJson,
                proposed.ToJsonString(Wire),
                expectedStateRevision, expectedLedgerRevision,
                expectedLedgerRevision + 1, before, eventId,
                verified.AuthorizationEvidenceId, true, DateTimeOffset.UtcNow),
                cancellationToken);
        }
        catch (Exception error) when (error is JsonException or ArgumentException
            or InvalidOperationException)
        {
            return new(CharacterManifestationApplyStatus.PlanUnresolved);
        }

        return commit.Status switch
        {
            CharacterSupernaturalWriteStatus.Applied => new(
                CharacterManifestationApplyStatus.Applied, commit.Revision,
                commit.LedgerRevision, effective.DeepClone()),
            CharacterSupernaturalWriteStatus.DuplicateEvent => new(
                CharacterManifestationApplyStatus.DuplicateEvent, commit.Revision,
                commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.RevisionConflict => new(
                CharacterManifestationApplyStatus.StateRevisionConflict, commit.Revision,
                commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.LedgerRevisionConflict => new(
                CharacterManifestationApplyStatus.LedgerRevisionConflict, commit.Revision,
                commit.LedgerRevision),
            CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged => new(
                CharacterManifestationApplyStatus.AuthoritativeInputsChanged),
            _ => new(CharacterManifestationApplyStatus.NotInitialized)
        };
    }

    private static bool MatchesVerifiedMaterial(
        TrustedManifestationTransitionMaterial material,
        CharacterSupernaturalState saved,
        Guid id, long revision, string eventId)
    {
        try
        {
            var primary = material.PrimaryMechanics;
            var request = material.Transition;
            return primary.ValueKind == JsonValueKind.Object &&
                request.ValueKind == JsonValueKind.Object &&
                primary.TryGetProperty("rulesetId", out var ruleset) &&
                ruleset.GetString() == saved.RulesetId &&
                primary.TryGetProperty("fingerprint", out var fingerprint) &&
                fingerprint.GetString() == saved.Fingerprint &&
                request.TryGetProperty("characterId", out var character) &&
                character.GetString() == id.ToString("D") &&
                request.TryGetProperty("expectedRevision", out var expected) &&
                expected.GetInt64() == revision &&
                request.TryGetProperty("eventId", out var eventValue) &&
                eventValue.GetString() == eventId &&
                request.TryGetProperty("steps", out var steps) &&
                steps.ValueKind == JsonValueKind.Array &&
                steps.GetArrayLength() is > 0 and <= 512;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or FormatException)
        {
            return false;
        }
    }
}
