using System.Text.Json.Nodes;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Presentation contract for the eventual unified Origins interface.
/// Records of owned Origins and earned events are not proof that an action is
/// currently eligible. Only a separately verified source and Rules Core can
/// authorize an acquisition, advancement or manifestation transition.
/// </summary>
public sealed record CharacterOriginsActionAvailability(
    string Action, string Status, string Reason);

public sealed record CharacterOriginsBackendView(
    int ContractVersion,
    Guid CharacterId,
    bool ReadOnly,
    string OwnershipStatus,
    string? RulesetId,
    string? CatalogFingerprint,
    long? UnderlyingOriginRevision,
    long? SupernaturalRevision,
    long ManifestationRevision,
    JsonNode OwnedAlternateSheets,
    JsonNode PersistentInfluences,
    JsonNode? ManifestationLedger,
    IReadOnlyList<CharacterSupernaturalEventInspection> Events,
    string EffectiveStatus,
    string EffectiveSheetId,
    string? ActiveManifestationId,
    IReadOnlyList<string> ActiveManifestationIds,
    JsonNode? EffectiveProjection,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<CharacterOriginsActionAvailability> Actions);

public sealed record CharacterOriginsBackendResult(
    CharacterEffectiveStateStatus Status,
    CharacterOriginsBackendView? View = null);

/// <summary>
/// Composes two independently Site-authorized existing read services without
/// reimplementing mechanics. Their separate reads must agree on the committed
/// supernatural/catalog revisions; a concurrent write fails closed instead of
/// returning a self-contradictory Origins view.
/// </summary>
public sealed class CharacterOriginsBackendReadService(
    CharacterEffectiveStateService effective,
    CharacterSupernaturalInspectionService inspection)
{
    private static readonly IReadOnlyList<CharacterOriginsActionAvailability> PendingAuthority =
    [
        new("acquire", "not-evaluated", "trusted-source-authority-required"),
        new("advance", "not-evaluated", "trusted-source-authority-required"),
        new("change-expression", "not-evaluated", "trusted-source-authority-required"),
        new("enter-form", "not-evaluated", "trusted-source-authority-required"),
        new("end-form", "not-evaluated", "trusted-source-authority-required"),
        new("switch-form", "not-evaluated", "trusted-source-authority-required"),
        new("suppress-form", "not-evaluated", "trusted-source-authority-required"),
        new("restore-form", "not-evaluated", "trusted-source-authority-required")
    ];

    public async Task<CharacterOriginsBackendResult> GetAsync(
        Guid characterId, CancellationToken cancellationToken = default)
    {
        var current = await effective.GetAsync(characterId, cancellationToken);
        if (current.Status != CharacterEffectiveStateStatus.Ready)
            return new(current.Status);
        var owned = await inspection.GetAsync(characterId, cancellationToken);
        return Compose(current, owned);
    }

    // Pure correlation function for tests. This does not grant authorization.
    internal static CharacterOriginsBackendResult Compose(
        CharacterEffectiveStateResult effective,
        CharacterSupernaturalInspectionResult inspection)
    {
        if (effective.Status != CharacterEffectiveStateStatus.Ready)
            return new(effective.Status);
        if (inspection.Status != CharacterEffectiveStateStatus.Ready)
            return new(inspection.Status);

        var state = effective.View;
        var owned = inspection.View;
        if (state is null || owned is null ||
            state.CharacterId == Guid.Empty || state.CharacterId != owned.CharacterId ||
            state.ReadOnly != owned.ReadOnly ||
            state.SupernaturalRevision != owned.Revision ||
            state.ManifestationRevision != owned.ManifestationRevision)
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        if (owned.Revision is not null)
        {
            if (owned.Revision < 1 ||
                string.IsNullOrWhiteSpace(owned.RulesetId) ||
                string.IsNullOrWhiteSpace(owned.Fingerprint) ||
                owned.RulesetId != state.RulesetId ||
                owned.Fingerprint != state.Fingerprint ||
                owned.ManifestationLedger is null ||
                owned.OwnershipStatusIsInvalid())
                return new(CharacterEffectiveStateStatus.ProjectionUnavailable);
        }
        else if (owned.RulesetId is not null || owned.Fingerprint is not null ||
                 owned.ManifestationLedger is not null ||
                 owned.OwnedSheetsAreNotEmpty())
            return new(CharacterEffectiveStateStatus.ProjectionUnavailable);

        var view = new CharacterOriginsBackendView(
            1,
            state.CharacterId,
            state.ReadOnly,
            owned.Status,
            state.RulesetId,
            state.Fingerprint,
            state.OriginRevision,
            state.SupernaturalRevision,
            state.ManifestationRevision,
            owned.AlternateSheets.DeepClone(),
            owned.Influences.DeepClone(),
            owned.ManifestationLedger?.DeepClone(),
            owned.CompletedEvents.ToArray(),
            state.Status,
            state.EffectiveSheetId,
            state.ActiveManifestationId,
            state.ActiveManifestationIds?.ToArray() ?? [],
            state.NativeProjection?.DeepClone(),
            state.Unresolved.ToArray(),
            PendingAuthority);
        return new(CharacterEffectiveStateStatus.Ready, view);
    }
}

internal static class CharacterOriginsBackendConsistency
{
    internal static bool OwnershipStatusIsInvalid(
        this CharacterSupernaturalInspectionView view) =>
        view.Status != "stored-unverified" ||
        view.AlternateSheets is not JsonArray ||
        view.Influences is not JsonArray ||
        view.ManifestationLedger is not JsonObject;

    internal static bool OwnedSheetsAreNotEmpty(
        this CharacterSupernaturalInspectionView view) =>
        view.Status != "unconfigured" ||
        view.AlternateSheets is not JsonArray sheets || sheets.Count != 0 ||
        view.Influences is not JsonArray influences || influences.Count != 0 ||
        view.CompletedEvents.Count != 0;
}
