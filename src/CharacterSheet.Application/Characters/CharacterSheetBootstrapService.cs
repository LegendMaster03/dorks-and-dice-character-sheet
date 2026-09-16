using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;
using CharacterSheet.Domain.Characters;

namespace CharacterSheet.Application.Characters;

public enum CharacterSheetBootstrapStatus
{
    Ready,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    Unauthenticated,
    Archived
}

public sealed record CharacterSheetRootState(
    int SchemaVersion,
    string BuilderStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CharacterSheetBootstrapView(
    Guid CharacterId,
    string Name,
    string Lifecycle,
    DateTimeOffset? ArchivedAt,
    IReadOnlyCollection<Guid> CampaignIds,
    bool HasRichSheet,
    CharacterSheetRootState? Sheet);

public sealed record CharacterSheetBootstrapResult(
    CharacterSheetBootstrapStatus Status,
    CharacterSheetBootstrapView? View = null);

public sealed class CharacterSheetBootstrapService(
    ISiteCharacterAccessGateway siteCharacterAccess,
    ICharacterSheetStore characterSheetStore)
{
    public async Task<CharacterSheetBootstrapResult> GetAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var character = access.Character!;
        var root = await characterSheetStore.GetAsync(characterId, cancellationToken);
        return new CharacterSheetBootstrapResult(
            CharacterSheetBootstrapStatus.Ready,
            ToView(character, root));
    }

    public async Task<CharacterSheetBootstrapResult> InitializeAsync(
        Guid characterId,
        CancellationToken cancellationToken = default)
    {
        var access = await siteCharacterAccess.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        var denied = MapDeniedAccess(access.Status);
        if (denied is not null)
        {
            return denied;
        }

        var character = access.Character!;
        if (!character.AllowsOrdinaryEditingByLifecycle)
        {
            var existing = await characterSheetStore.GetAsync(characterId, cancellationToken);
            return new CharacterSheetBootstrapResult(
                CharacterSheetBootstrapStatus.Archived,
                ToView(character, existing));
        }

        var root = await characterSheetStore.GetOrCreateAsync(characterId, cancellationToken);
        return new CharacterSheetBootstrapResult(
            CharacterSheetBootstrapStatus.Ready,
            ToView(character, root));
    }

    private static CharacterSheetBootstrapResult? MapDeniedAccess(SiteCharacterAccessStatus status) => status switch
    {
        SiteCharacterAccessStatus.Authorized => null,
        SiteCharacterAccessStatus.NotFoundOrNotOwned =>
            new(CharacterSheetBootstrapStatus.NotFoundOrNotOwned),
        SiteCharacterAccessStatus.ProjectionUnavailable =>
            new(CharacterSheetBootstrapStatus.ProjectionUnavailable),
        SiteCharacterAccessStatus.Unauthenticated =>
            new(CharacterSheetBootstrapStatus.Unauthenticated),
        _ => new(CharacterSheetBootstrapStatus.ProjectionUnavailable)
    };

    private static CharacterSheetBootstrapView ToView(
        SiteCharacterProjection character,
        CharacterSheetRoot? root) =>
        new(
            character.CharacterId,
            character.Name,
            character.LifecycleState.ToString(),
            character.ArchivedAt,
            character.CampaignIds.ToArray(),
            root is not null,
            root is null
                ? null
                : new CharacterSheetRootState(
                    root.SchemaVersion,
                    root.BuilderStatus.ToString(),
                    root.CreatedAt,
                    root.UpdatedAt));
}
