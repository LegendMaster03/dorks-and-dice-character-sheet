using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.RulesCore;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Testable, backend-only source decision. A host implementation must consult
/// an independently authorized rule or scoped DM decision. Neither the browser
/// nor a Site delegation ticket may construct this material as entitlement.
/// </summary>
public sealed record TrustedInfluenceOverlapDecisionMaterial(
    JsonElement VerifiedSource, string AuthorizationEvidenceId);

public interface ITrustedCharacterInfluenceOverlapDecisionSource
{
    Task<TrustedInfluenceOverlapDecisionMaterial?> PrepareAsync(
        Guid characterId, SiteCharacterProjection authorizedCharacter,
        CharacterSupernaturalState committed, string intentId, string eventId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Coordinates a single independently verified effect writer decision. Unlike
/// acquisition, the progression and manifestation ledgers remain unchanged.
/// The exact new decision and optional supersession are minted by Rules Core's
/// separately registered source verifier and committed with a receipt under
/// PostgreSQL's character lock. No production source is registered here.
/// </summary>
public sealed class CharacterInfluenceOverlapDecisionService(
    ISiteCharacterAccessGateway site,
    ISiteFreshCharacterAccessGateway freshSite,
    ICharacterSupernaturalStateStore store,
    INativeRulesCoreGateway rules,
    ITrustedCharacterInfluenceOverlapDecisionSource source,
    CharacterBuildService builds,
    CharacterStateService routine,
    CharacterOriginStateService origins)
{
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public async Task<CharacterSupernaturalOwnershipApplyResult> ApplyAsync(
        Guid characterId, long expectedStateRevision, long expectedLedgerRevision,
        string intentId, string eventId, CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || expectedStateRevision < 1 ||
            expectedStateRevision == long.MaxValue || expectedLedgerRevision < 0 ||
            expectedLedgerRevision == long.MaxValue ||
            string.IsNullOrWhiteSpace(intentId) || intentId.Length > 160 ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 160)
            return new(CharacterSupernaturalOwnershipApplyStatus.InvalidRequest);

        var firstLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (firstLive.Status != SiteCharacterAccessStatus.Authorized ||
            firstLive.Character?.CharacterId != characterId)
            return new(FromSite(firstLive.Status));
        if (!firstLive.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterSupernaturalOwnershipApplyStatus.ArchivedReadOnly);

        var scoped = await site.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        if (scoped.Status != SiteCharacterAccessStatus.Authorized ||
            scoped.Character?.CharacterId != characterId ||
            !CharacterEffectiveStateService.SameSiteScope(
                firstLive.Character, scoped.Character))
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);
        if (!scoped.Character.AllowsOrdinaryEditingByLifecycle)
            return new(CharacterSupernaturalOwnershipApplyStatus.ArchivedReadOnly);

        var saved = await store.GetAsync(characterId, cancellationToken);
        if (saved is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized);
        if (saved.Revision != expectedStateRevision)
            return new(CharacterSupernaturalOwnershipApplyStatus.StateRevisionConflict,
                saved.Revision, saved.LedgerRevision);
        if (saved.LedgerRevision != expectedLedgerRevision)
            return new(CharacterSupernaturalOwnershipApplyStatus.LedgerRevisionConflict,
                saved.Revision, saved.LedgerRevision);
        if (saved.ContractVersion != CharacterSupernaturalState.CurrentContractVersion)
            return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);

        var before = await store.GetAuthoritativeInputsFingerprintAsync(
            characterId, cancellationToken);
        if (before is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized);

        string rulesetId;
        string fingerprint;
        try
        {
            var metadata = await rules.GetNativeRulesetAsync(cancellationToken);
            if (metadata["contractVersion"]?.GetValue<int>() != 1 ||
                metadata["rulesetId"] is not JsonValue ruleset ||
                !ruleset.TryGetValue<string>(out rulesetId!) ||
                metadata["fingerprint"] is not JsonValue catalog ||
                !catalog.TryGetValue<string>(out fingerprint!) ||
                string.IsNullOrWhiteSpace(rulesetId) ||
                string.IsNullOrWhiteSpace(fingerprint))
                return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);
        }
        catch (Exception error) when (error is RulesCoreGatewayException or
            JsonException or InvalidOperationException or FormatException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.RulesCoreUnavailable);
        }
        if (saved.RulesetId != rulesetId || saved.Fingerprint != fingerprint)
            return new(CharacterSupernaturalOwnershipApplyStatus.NativeContentChanged);

        var build = await builds.GetAsync(characterId, cancellationToken);
        var current = await routine.GetAsync(characterId, cancellationToken);
        var origin = await origins.GetAsync(characterId, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null ||
            current.Status != CharacterStateAccessStatus.Ready || current.View is null ||
            origin.Status != CharacterOriginReadStatus.Ready ||
            origin.SavedState is { } ownerOrigin &&
            (ownerOrigin.RulesetId != rulesetId || ownerOrigin.Fingerprint != fingerprint))
            return new(CharacterSupernaturalOwnershipApplyStatus.PrimaryInputsUnverified);

        var primary = CharacterNativePrimaryStateAdapter.Assemble(
            characterId, build.View, current.View, origin.SavedState,
            rulesetId, fingerprint);
        if (!primary.TransitionInputsVerified || primary.Mechanics is null)
            return new(CharacterSupernaturalOwnershipApplyStatus.PrimaryInputsUnverified);

        TrustedInfluenceOverlapDecisionMaterial? trusted;
        try
        {
            trusted = await source.PrepareAsync(characterId, scoped.Character,
                saved, intentId, eventId, cancellationToken);
        }
        catch (Exception error) when (error is ArgumentException or
            InvalidOperationException or JsonException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);
        }
        if (trusted is null ||
            !MatchesTrustedSource(trusted, characterId, intentId, eventId,
                rulesetId, fingerprint))
            return new(CharacterSupernaturalOwnershipApplyStatus.SourceUnauthorized);

        JsonElement request;
        long expectedProgressionRevision;
        try
        {
            using var sheets = JsonDocument.Parse(saved.AlternateSheetsJson);
            using var influences = JsonDocument.Parse(saved.PersistentInfluencesJson);
            using var overlap = JsonDocument.Parse(saved.InfluenceOverlapSelectionsJson);
            using var ledger = JsonDocument.Parse(saved.LedgerJson);
            using var progression = JsonDocument.Parse(saved.ProgressionJournalJson);
            expectedProgressionRevision = progression.RootElement
                .GetProperty("revision").GetInt64();
            request = JsonSerializer.SerializeToElement(new
            {
                contractVersion = 1,
                characterId = characterId.ToString("D"),
                eventId, intentId, rulesetId, fingerprint,
                expectedSupernaturalRevision = expectedStateRevision,
                expectedLedgerRevision,
                authoritativeInputsFingerprint = before,
                state = new
                {
                    characterId = characterId.ToString("D"),
                    primaryMechanics = primary.Mechanics.Value,
                    alternateSheets = sheets.RootElement,
                    persistentInfluences = influences.RootElement,
                    influenceOverlapSelections = overlap.RootElement
                },
                ledger = ledger.RootElement,
                progressionJournal = progression.RootElement,
                verifiedSource = trusted.VerifiedSource,
                authorizationEvidenceId = trusted.AuthorizationEvidenceId
            }, Wire);
        }
        catch (Exception error) when (error is JsonException or
            ArgumentException or InvalidOperationException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);
        }

        JsonNode response;
        try
        {
            response = await rules.PlanVerifiedInfluenceOverlapAsync(request,
                cancellationToken);
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
        catch (Exception error) when (error is RulesCoreGatewayException or
            JsonException or InvalidOperationException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.RulesCoreUnavailable);
        }

        if (!TryReadVerifiedPlan(response, characterId, intentId, eventId,
                rulesetId, fingerprint, expectedStateRevision,
                expectedLedgerRevision, expectedProgressionRevision,
                trusted.AuthorizationEvidenceId, out var decisions,
                out var decisionId, out var replacedDecisionId))
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

        CharacterSupernaturalWriteResult committed;
        try
        {
            committed = await store.CommitVerifiedAsync(new VerifiedSupernaturalStateWrite(
                characterId, rulesetId, fingerprint,
                saved.AlternateSheetsJson, saved.PersistentInfluencesJson,
                saved.LedgerJson, expectedStateRevision, expectedLedgerRevision,
                expectedLedgerRevision, before, eventId,
                trusted.AuthorizationEvidenceId, false, DateTimeOffset.UtcNow,
                InfluenceOverlapSelectionsJson: decisions!.ToJsonString(Wire),
                IsOverlapDecision: true,
                NewOverlapDecisionId: decisionId,
                ReplacedOverlapDecisionId: replacedDecisionId),
                cancellationToken);
        }
        catch (Exception error) when (error is ArgumentException or
            InvalidOperationException or JsonException)
        {
            return new(CharacterSupernaturalOwnershipApplyStatus.PlanUnresolved);
        }

        return committed.Status switch
        {
            CharacterSupernaturalWriteStatus.Applied => new(
                CharacterSupernaturalOwnershipApplyStatus.Applied,
                committed.Revision, committed.LedgerRevision),
            CharacterSupernaturalWriteStatus.RevisionConflict => new(
                CharacterSupernaturalOwnershipApplyStatus.StateRevisionConflict,
                committed.Revision, committed.LedgerRevision),
            CharacterSupernaturalWriteStatus.LedgerRevisionConflict => new(
                CharacterSupernaturalOwnershipApplyStatus.LedgerRevisionConflict,
                committed.Revision, committed.LedgerRevision),
            CharacterSupernaturalWriteStatus.DuplicateEvent => new(
                CharacterSupernaturalOwnershipApplyStatus.DuplicateEvent,
                committed.Revision, committed.LedgerRevision),
            CharacterSupernaturalWriteStatus.AuthoritativeInputsChanged => new(
                CharacterSupernaturalOwnershipApplyStatus.AuthoritativeInputsChanged),
            _ => new(CharacterSupernaturalOwnershipApplyStatus.NotInitialized)
        };
    }

    internal static bool MatchesTrustedSource(
        TrustedInfluenceOverlapDecisionMaterial? source, Guid characterId,
        string intentId, string eventId, string rulesetId, string fingerprint)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.AuthorizationEvidenceId) ||
            source.AuthorizationEvidenceId.Length > 320 ||
            source.VerifiedSource.ValueKind != JsonValueKind.Object)
            return false;
        try
        {
            var value = source.VerifiedSource;
            return value.GetProperty("characterId").GetString() ==
                    characterId.ToString("D") &&
                value.GetProperty("intentId").GetString() == intentId &&
                value.GetProperty("eventId").GetString() == eventId &&
                value.GetProperty("rulesetId").GetString() == rulesetId &&
                value.GetProperty("fingerprint").GetString() == fingerprint &&
                value.GetProperty("authorizationEvidenceId").GetString() ==
                    source.AuthorizationEvidenceId &&
                !string.IsNullOrWhiteSpace(value.GetProperty("decisionId").GetString()) &&
                value.GetProperty("sourceOccurrence").ValueKind == JsonValueKind.Object;
        }
        catch (Exception error) when (error is JsonException or
            InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// All transport correlations must match before the trusted persistence
    /// write. The store separately compares the exact prior decision list under
    /// its lock, so a valid plan can not rewrite an unrelated owner or journal.
    /// </summary>
    internal static bool TryReadVerifiedPlan(
        JsonNode? response, Guid characterId, string intentId, string eventId,
        string rulesetId, string fingerprint, long expectedStateRevision,
        long expectedLedgerRevision, long expectedProgressionRevision,
        string evidenceId, out JsonArray? decisions, out string? decisionId,
        out string? replacedDecisionId)
    {
        decisions = null;
        decisionId = null;
        replacedDecisionId = null;
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
                plan["eventId"]?.GetValue<string>() != eventId ||
                plan["intentId"]?.GetValue<string>() != intentId ||
                plan["authorizationEvidenceId"]?.GetValue<string>() != evidenceId ||
                plan["expectedSupernaturalRevision"]?.GetValue<long>() !=
                    expectedStateRevision ||
                plan["expectedManifestationRevision"]?.GetValue<long>() !=
                    expectedLedgerRevision ||
                plan["expectedProgressionRevision"]?.GetValue<long>() !=
                    expectedProgressionRevision ||
                plan["decisionId"] is not JsonValue decision ||
                !decision.TryGetValue<string>(out decisionId) ||
                string.IsNullOrWhiteSpace(decisionId) ||
                plan["proposedState"] is not JsonObject state ||
                state["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                state["primaryMechanics"] is not JsonObject ||
                state["alternateSheets"] is not JsonArray ||
                state["persistentInfluences"] is not JsonArray ||
                state["influenceOverlapSelections"] is not JsonArray proposed ||
                proposed.Count == 0 ||
                proposed[^1]?["id"]?.GetValue<string>() != decisionId ||
                proposed[^1]?["authorizationEvidenceId"]?.GetValue<string>() != evidenceId)
                return false;

            if (plan["replacedDecisionId"] is JsonValue replaced)
            {
                if (!replaced.TryGetValue<string>(out replacedDecisionId) ||
                    string.IsNullOrWhiteSpace(replacedDecisionId))
                    return false;
            }
            else if (plan["replacedDecisionId"] is not null)
                return false;
            decisions = (JsonArray)proposed.DeepClone();
            return true;
        }
        catch (Exception error) when (error is InvalidOperationException or
            FormatException or JsonException or ArgumentException)
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
