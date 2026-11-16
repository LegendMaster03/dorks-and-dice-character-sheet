using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

public sealed record CharacterSupernaturalInspectionView(
    Guid CharacterId, bool ReadOnly, string Status,
    string? RulesetId, string? Fingerprint, long? Revision,
    long ManifestationRevision, JsonNode AlternateSheets, JsonNode Influences,
    JsonNode? ManifestationLedger,
    IReadOnlyList<CharacterSupernaturalEventInspection> CompletedEvents,
    bool EffectiveMechanicsVerified);

public sealed record CharacterSupernaturalEventInspection(
    string EventId, long Revision, long ManifestationRevision,
    string Kind, string EvidenceId);

public sealed record CharacterSupernaturalInspectionResult(
    CharacterEffectiveStateStatus Status, CharacterSupernaturalInspectionView? View = null);

/// <summary>
/// An authenticated ownership inspection, NOT a mechanics projection or form
/// activation. Inactive persistent sheets may be examined without claiming
/// they are currently manifested or their grants are playable.
/// </summary>
public sealed class CharacterSupernaturalInspectionService(
    CharacterBuildService builds,
    ICharacterSupernaturalStateStore store,
    ISiteFreshCharacterAccessGateway freshSite)
{
    public async Task<CharacterSupernaturalInspectionResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        // Inspection exposes private influences and receipts even when Core
        // is never contacted; refresh Site authorization before storage.
        var firstLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (firstLive.Status != SiteCharacterAccessStatus.Authorized)
            return new(CharacterEffectiveStateService.FromFreshStatus(firstLive.Status));
        if (firstLive.Character?.CharacterId != characterId)
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        var build = await builds.GetAsync(characterId, cancellationToken);
        if (build.Status != CharacterBuildAccessStatus.Ready || build.View is null)
            return new(build.Status switch
            {
                CharacterBuildAccessStatus.Unauthenticated => CharacterEffectiveStateStatus.Unauthenticated,
                CharacterBuildAccessStatus.NotFoundOrNotOwned => CharacterEffectiveStateStatus.NotFoundOrNotOwned,
                CharacterBuildAccessStatus.SheetNotInitialized => CharacterEffectiveStateStatus.SheetNotInitialized,
                _ => CharacterEffectiveStateStatus.ProjectionUnavailable
            });
        var saved = await store.GetAsync(characterId, cancellationToken);
        if (saved is null)
            return await CompleteReadAsync(characterId, firstLive.Character!, new(
                characterId, false, "unconfigured",
                null, null, null, 0,
                new JsonArray(), new JsonArray(), null, [], false), cancellationToken);
        try
        {
            if (saved.ContractVersion != CharacterSheet.Domain.Characters.CharacterSupernaturalState.CurrentContractVersion)
                return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

            // Bounded JSON was checked when saved, but storage may have been
            // externally modified; malformed documents must not become active.
            var sheets = JsonNode.Parse(saved.AlternateSheetsJson) as JsonArray;
            var influences = JsonNode.Parse(saved.PersistentInfluencesJson) as JsonArray;
            var ledger = JsonNode.Parse(saved.LedgerJson) as JsonObject;
            if (sheets is null || influences is null || ledger is null ||
                ledger["characterId"]?.GetValue<string>() != characterId.ToString("D") ||
                ledger["revision"]?.GetValue<long>() != saved.LedgerRevision)
                return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

            var events = (await store.GetEventsAsync(characterId, cancellationToken))
                .Select(value => new CharacterSupernaturalEventInspection(
                    value.EventId, value.CommittedRevision, value.LedgerRevision,
                    value.Kind, value.AuthorizationEvidenceId)).ToArray();

            return await CompleteReadAsync(characterId, firstLive.Character!, new(
                characterId, false, "stored-unverified",
                saved.RulesetId, saved.Fingerprint, saved.Revision,
                saved.LedgerRevision, sheets, influences, ledger, events, false),
                cancellationToken);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or FormatException)
        {
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);
        }
    }

    private async Task<CharacterSupernaturalInspectionResult> CompleteReadAsync(
        Guid characterId, SiteCharacterProjection first,
        CharacterSupernaturalInspectionView view, CancellationToken cancellationToken)
    {
        var lastLive = await freshSite.GetFreshAuthorizedCharacterAsync(
            characterId, cancellationToken);
        if (lastLive.Status != SiteCharacterAccessStatus.Authorized)
            return new(CharacterEffectiveStateService.FromFreshStatus(lastLive.Status));
        if (lastLive.Character is null ||
            !CharacterEffectiveStateService.SameSiteScope(first, lastLive.Character))
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);
        // An archived owner can still inspect, but never as an editable owner.
        return new(CharacterEffectiveStateStatus.Ready,
            view with { ReadOnly = !lastLive.Character.AllowsOrdinaryEditingByLifecycle });
    }
}
