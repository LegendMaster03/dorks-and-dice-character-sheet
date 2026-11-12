using System.Text.Json;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

public enum CharacterOriginParentSnapshotStatus
{
    Ready,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    CharacterSheetNotInitialized,
    ParentOriginMissing,
    StoredProfileUnverifiable
}

/// <summary>
/// Restricted inspection of another character's last committed Origin.
/// A stored Current configuration is historical evidence, not a revalidated
/// parent profile, source-character relationship or inheritable grant list.
/// Nothing returned here can authorize a mixed Origin or be supplied to Core
/// as a trusted ManifestedProfile without a future server-side verifier.
/// </summary>
public sealed record CharacterOriginParentIdentity(
    string? SpeciesId, string? AncestryId, string? ProfileId);

public sealed record CharacterOriginParentSnapshot(
    Guid ParentCharacterId,
    string RulesetId,
    string Fingerprint,
    long Revision,
    CharacterOriginParentIdentity Underlying,
    CharacterOriginParentIdentity Current);

public sealed record CharacterOriginParentSnapshotResult(
    CharacterOriginParentSnapshotStatus Status,
    CharacterOriginParentSnapshot? Snapshot = null);

/// <summary>
/// Both target and proposed parent must be independently visible through the
/// *same request's* Site owner projection. Checking the target alone would
/// allow a guessed parent ID to expose a different owner's stored profile.
/// This never treats campaign membership as evidence of parent ownership.
/// </summary>
public sealed class CharacterOriginParentSnapshotService(
    ISiteCharacterAccessGateway site,
    ICharacterBuildStore builds,
    ICharacterAuthorizedOriginStore origins)
{
    public async Task<CharacterOriginParentSnapshotResult> InspectAsync(
        Guid characterId, Guid parentCharacterId,
        CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || parentCharacterId == Guid.Empty)
            return new(CharacterOriginParentSnapshotStatus.NotFoundOrNotOwned);

        var target = await site.GetAuthorizedCharacterAsync(
            characterId, cancellationToken);
        var denied = Denied(target.Status);
        if (denied is not null)
            return new(denied.Value);
        if (target.Character?.CharacterId != characterId)
            return new(CharacterOriginParentSnapshotStatus.ProjectionUnavailable);

        // Never query parent persistence until Site independently confirms
        // this request can view the proposed parent character.
        var parent = await site.GetAuthorizedCharacterAsync(
            parentCharacterId, cancellationToken);
        denied = Denied(parent.Status);
        if (denied is not null)
            return new(denied.Value);
        if (parent.Character?.CharacterId != parentCharacterId)
            return new(CharacterOriginParentSnapshotStatus.ProjectionUnavailable);

        if (await builds.GetAsync(characterId, cancellationToken) is null ||
            await builds.GetAsync(parentCharacterId, cancellationToken) is null)
            return new(CharacterOriginParentSnapshotStatus.CharacterSheetNotInitialized);

        var saved = await origins.GetAsync(parentCharacterId, cancellationToken);
        if (saved is null)
            return new(CharacterOriginParentSnapshotStatus.ParentOriginMissing);

        // The event receipt must match the committed snapshot. A truncated or
        // malformed stored profile does not become trusted ancestry evidence.
        var events = await origins.GetEventsAsync(parentCharacterId, cancellationToken);
        if (saved.Revision < 1 || events.Count != saved.Revision ||
            events.Count == 0 ||
            events[^1].CharacterId != parentCharacterId ||
            events[^1].CommittedRevision != saved.Revision)
            return new(CharacterOriginParentSnapshotStatus.StoredProfileUnverifiable);

        try
        {
            using var profile = JsonDocument.Parse(saved.ProfileJson);
            var document = profile.RootElement;
            if (document.ValueKind != JsonValueKind.Object ||
                !HasString(document, "id") ||
                !document.TryGetProperty("underlying", out var underlying) ||
                !document.TryGetProperty("current", out var current) ||
                !IsConfigurationShape(underlying) ||
                !IsConfigurationShape(current))
                return new(CharacterOriginParentSnapshotStatus.StoredProfileUnverifiable);

            return new(CharacterOriginParentSnapshotStatus.Ready,
                new(parentCharacterId, saved.RulesetId, saved.Fingerprint,
                    saved.Revision, Identity(underlying), Identity(current)));
        }
        catch (JsonException)
        {
            return new(CharacterOriginParentSnapshotStatus.StoredProfileUnverifiable);
        }
    }

    private static CharacterOriginParentIdentity Identity(JsonElement config) =>
        new(Text(config, "speciesId"), Text(config, "ancestryId"),
            Text(config, "profileId"));

    private static bool IsConfigurationShape(JsonElement config) =>
        config.ValueKind == JsonValueKind.Object &&
        config.TryGetProperty("inherentGrants", out var grants) &&
        grants.ValueKind == JsonValueKind.Array &&
        IsStringOrNull(config, "speciesId") &&
        IsStringOrNull(config, "ancestryId") &&
        IsStringOrNull(config, "profileId");

    private static bool IsStringOrNull(JsonElement source, string name) =>
        !source.TryGetProperty(name, out var value) ||
        value.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static bool HasString(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static string? Text(JsonElement source, string name) =>
        source.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static CharacterOriginParentSnapshotStatus? Denied(
        SiteCharacterAccessStatus status) =>
        status switch
        {
            SiteCharacterAccessStatus.Authorized => null,
            SiteCharacterAccessStatus.Unauthenticated =>
                CharacterOriginParentSnapshotStatus.Unauthenticated,
            SiteCharacterAccessStatus.NotFoundOrNotOwned =>
                CharacterOriginParentSnapshotStatus.NotFoundOrNotOwned,
            _ => CharacterOriginParentSnapshotStatus.ProjectionUnavailable
        };
}
