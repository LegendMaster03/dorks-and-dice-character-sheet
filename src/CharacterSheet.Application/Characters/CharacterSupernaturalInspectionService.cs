using System.Text.Json;
using System.Text.Json.Nodes;
using CharacterSheet.Application.Persistence;

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
    ICharacterSupernaturalStateStore store)
{
    public async Task<CharacterSupernaturalInspectionResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
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
            return new(CharacterEffectiveStateStatus.Ready, new(
                characterId, build.View.ReadOnly, "unconfigured",
                null, null, null, 0,
                new JsonArray(), new JsonArray(), null, [], false));
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

            return new(CharacterEffectiveStateStatus.Ready, new(
                characterId, build.View.ReadOnly, "stored-unverified",
                saved.RulesetId, saved.Fingerprint, saved.Revision,
                saved.LedgerRevision, sheets, influences, ledger, events, false));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or FormatException)
        {
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);
        }
    }
}
