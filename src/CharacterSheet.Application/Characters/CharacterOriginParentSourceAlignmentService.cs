using System.Text.Json;
using System.Text.Json.Serialization;
using CharacterSheet.Application.Persistence;
using CharacterSheet.Application.Site;

namespace CharacterSheet.Application.Characters;

/// <summary>
/// Read-only comparison of proposed mixed-Origin native contributor IDs with
/// separately owner-authorized, currently revalidated individual parent Origins.
/// A match is not proof of biological relationship, reproduction eligibility,
/// a heritable state choice, a DM ruling, or authority to copy any grants.
/// </summary>
public enum CharacterOriginParentSourceAlignmentStatus
{
    Ready,
    Unauthenticated,
    NotFoundOrNotOwned,
    ProjectionUnavailable,
    SheetNotInitialized,
    DraftMissing,
    DraftRevisionConflict,
    AlreadyCommitted,
    UnsupportedDraft,
    InvalidBindings,
    ParentOriginUnverifiable,
    ParentNativeCatalogMismatch,
    ParentSourceMismatch,
    ConcurrentSourceChange
}

public sealed record CharacterOriginParentSourceBinding(
    string ContributorId, Guid ParentCharacterId);

public sealed record CharacterOriginParentSourceMatch(
    string ContributorId, Guid ParentCharacterId, string NativeSourceId,
    long ParentOriginRevision);

/// <summary>
/// A request-local private profile remains useful to a future trusted resolver.
/// No HTTP route returns this material, and source alignment never persists it.
/// </summary>
public sealed record CharacterOriginVerifiedParentSource(
    CharacterOriginParentSourceMatch Match, JsonElement VerifiedInitialProfile);

public sealed record CharacterOriginParentSourceAlignmentResult(
    CharacterOriginParentSourceAlignmentStatus Status,
    long? DraftRevision = null,
    IReadOnlyList<CharacterOriginParentSourceMatch>? Matches = null,
    [property: JsonIgnore] IReadOnlyList<CharacterOriginVerifiedParentSource>? VerifiedSources = null);

public sealed class CharacterOriginParentSourceAlignmentService(
    ISiteCharacterAccessGateway site,
    ICharacterBuildStore builds,
    ICharacterOriginDraftStore drafts,
    ICharacterAuthorizedOriginStore origins,
    CharacterOriginParentNativeReviewService nativeParents)
{
    // An input bound is only a request-size limit, not a claim that any
    // origination process always has this many biological contributors.
    private const int MaxReviewedContributors = 16;

    public async Task<CharacterOriginParentSourceAlignmentResult> ReviewAsync(
        Guid characterId, long expectedDraftRevision,
        IReadOnlyList<CharacterOriginParentSourceBinding>? bindings,
        CancellationToken cancellationToken = default)
    {
        if (characterId == Guid.Empty || expectedDraftRevision < 1 ||
            bindings is null || bindings.Count is < 1 or > MaxReviewedContributors ||
            bindings.Any(binding => binding is null ||
                string.IsNullOrWhiteSpace(binding.ContributorId) ||
                binding.ContributorId.Length > 180 ||
                binding.ParentCharacterId == Guid.Empty ||
                binding.ParentCharacterId == characterId) ||
            bindings.Select(binding => binding.ContributorId)
                .Distinct(StringComparer.Ordinal).Count() != bindings.Count ||
            bindings.Select(binding => binding.ParentCharacterId)
                .Distinct().Count() != bindings.Count)
            return new(CharacterOriginParentSourceAlignmentStatus.InvalidBindings);

        // Never inspect the child's proposal before Site authenticates the
        // child's owner. Campaign DM membership does not grant this access.
        var child = await site.GetAuthorizedCharacterAsync(characterId, cancellationToken);
        if (child.Status != SiteCharacterAccessStatus.Authorized)
            return new(child.Status switch
            {
                SiteCharacterAccessStatus.Unauthenticated =>
                    CharacterOriginParentSourceAlignmentStatus.Unauthenticated,
                SiteCharacterAccessStatus.NotFoundOrNotOwned =>
                    CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned,
                _ => CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable
            });
        if (child.Character?.CharacterId != characterId)
            return new(CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable);

        if (await builds.GetAsync(characterId, cancellationToken) is null)
            return new(CharacterOriginParentSourceAlignmentStatus.SheetNotInitialized);
        if (await origins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginParentSourceAlignmentStatus.AlreadyCommitted);
        var draft = await drafts.GetAsync(characterId, cancellationToken);
        if (draft is null)
            return new(CharacterOriginParentSourceAlignmentStatus.DraftMissing);
        if (draft.Revision != expectedDraftRevision)
            return new(CharacterOriginParentSourceAlignmentStatus.DraftRevisionConflict);
        if (draft.Fingerprint is null)
            return new(CharacterOriginParentSourceAlignmentStatus.ParentNativeCatalogMismatch);

        Dictionary<string, string> offeredSources;
        try
        {
            using var proposal = JsonDocument.Parse(draft.ProposalJson);
            if (!TryNativeBiologicalSources(proposal.RootElement, out offeredSources))
                return new(CharacterOriginParentSourceAlignmentStatus.UnsupportedDraft);
        }
        catch (JsonException)
        {
            return new(CharacterOriginParentSourceAlignmentStatus.UnsupportedDraft);
        }
        if (offeredSources.Count != bindings.Count ||
            bindings.Any(binding => !offeredSources.ContainsKey(binding.ContributorId)))
            return new(CharacterOriginParentSourceAlignmentStatus.InvalidBindings);

        var matches = new List<CharacterOriginParentSourceMatch>();
        var verified = new List<CharacterOriginVerifiedParentSource>();
        var witnesses = new List<CharacterOriginParentInitialEvidence>();
        foreach (var binding in bindings)
        {
            // The parent service checks Site ownership of *both* individual
            // character IDs, validates the first-party event and re-evaluates
            // the exact direct initial Origin against current Rules Core.
            var review = await nativeParents.ReviewAsync(
                characterId, binding.ParentCharacterId, cancellationToken);
            if (review.Status != CharacterOriginParentNativeReviewStatus.Ready ||
                review.Review is null ||
                review.VerifiedInitialProfile is not JsonElement profile ||
                review.PersistenceEvidence is not CharacterOriginParentInitialEvidence witness)
                return new(review.Status switch
                {
                    CharacterOriginParentNativeReviewStatus.Unauthenticated =>
                        CharacterOriginParentSourceAlignmentStatus.Unauthenticated,
                    CharacterOriginParentNativeReviewStatus.NotFoundOrNotOwned =>
                        CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned,
                    CharacterOriginParentNativeReviewStatus.ProjectionUnavailable =>
                        CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable,
                    _ => CharacterOriginParentSourceAlignmentStatus.ParentOriginUnverifiable
                });

            if (review.Review.RulesetId != draft.RulesetId ||
                review.Review.LatestNativeFingerprint != draft.Fingerprint)
                return new(CharacterOriginParentSourceAlignmentStatus.ParentNativeCatalogMismatch);

            var source = offeredSources[binding.ContributorId];
            var underlying = profile.GetProperty("underlying");
            var actualSourceId = source.StartsWith("species.", StringComparison.Ordinal)
                ? OptionalString(underlying, "speciesId")
                : OptionalString(underlying, "ancestryId");
            if (actualSourceId != source)
                return new(CharacterOriginParentSourceAlignmentStatus.ParentSourceMismatch);

            var match = new CharacterOriginParentSourceMatch(
                binding.ContributorId, binding.ParentCharacterId, source,
                review.Review.StoredRevision);
            matches.Add(match);
            // Server-only, request-local: not a manifested contributor grant
            // and never a second persisted effective-character snapshot.
            verified.Add(new(match, profile.Clone()));
            witnesses.Add(witness);
        }

        // The first parent may have changed while another was being reviewed.
        // Reject the whole inspection instead of returning a mixed-age set.
        for (var index = 0; index < verified.Count; index++)
        {
            var source = verified[index];
            var witness = witnesses[index];
            var saved = await origins.GetAsync(
                source.Match.ParentCharacterId, cancellationToken);
            var sourceDraft = await drafts.GetAsync(
                source.Match.ParentCharacterId, cancellationToken);
            var sourceReceipts = await origins.GetEventsAsync(
                source.Match.ParentCharacterId, cancellationToken);
            if (saved is null ||
                saved.Revision != source.Match.ParentOriginRevision ||
                saved.RulesetId != draft.RulesetId ||
                saved.Fingerprint != witness.OriginFingerprint ||
                saved.UpdatedAt != witness.OriginUpdatedAt ||
                saved.SourceDraftRevision != witness.SourceDraftRevision ||
                sourceDraft is null ||
                sourceDraft.Revision != witness.SourceDraftRevision ||
                sourceDraft.RulesetId != saved.RulesetId ||
                sourceDraft.Fingerprint != witness.DraftFingerprint ||
                sourceDraft.UpdatedAt != witness.DraftUpdatedAt ||
                !string.Equals(sourceDraft.ProposalJson, witness.DraftProposalJson,
                    StringComparison.Ordinal) ||
                sourceReceipts.Count != 1 ||
                sourceReceipts[0].CharacterId != source.Match.ParentCharacterId ||
                sourceReceipts[0].CommittedRevision != saved.Revision ||
                sourceReceipts[0].EventId != witness.ReceiptEventId ||
                sourceReceipts[0].CreatedAt != witness.ReceiptCreatedAt ||
                sourceReceipts[0].EventId != "construction:" +
                    source.VerifiedInitialProfile.GetProperty("proposalId").GetString() ||
                sourceReceipts[0].AuthorizationKind != "first-party" ||
                sourceReceipts[0].AuthorizationEvidenceId !=
                    "native-catalog:" + witness.OriginFingerprint ||
                sourceReceipts[0].MethodRuleId != "rule.creature-origination" ||
                sourceReceipts[0].MethodId != "biological" ||
                sourceReceipts[0].ApprovalActorUserId is not null ||
                sourceReceipts[0].ApprovalCampaignId is not null ||
                sourceReceipts[0].ApprovedDecisionsJson is not null)
                return new(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange);
            try
            {
                using var current = JsonDocument.Parse(saved.ProfileJson);
                if (!JsonElement.DeepEquals(current.RootElement,
                    source.VerifiedInitialProfile))
                    return new(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange);
            }
            catch (JsonException)
            {
                return new(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange);
            }
        }

        // A changed child draft or completed Origin invalidates the complete
        // proposal-to-parent association, even though no write happens here.
        var afterDraft = await drafts.GetAsync(characterId, cancellationToken);
        if (afterDraft is null ||
            afterDraft.Revision != draft.Revision ||
            afterDraft.RulesetId != draft.RulesetId ||
            afterDraft.Fingerprint != draft.Fingerprint ||
            !string.Equals(afterDraft.ProposalJson, draft.ProposalJson,
                StringComparison.Ordinal) ||
            await origins.GetAsync(characterId, cancellationToken) is not null)
            return new(CharacterOriginParentSourceAlignmentStatus.ConcurrentSourceChange);

        // A Site owner grant may be revoked while native validation is in
        // flight. Reauthorize the exact child and every source immediately
        // before returning a successful comparison. These are not durable
        // permissions: a future state-changing operation must recheck again.
        foreach (var id in new[] { characterId }.Concat(
                     bindings.Select(binding => binding.ParentCharacterId)))
        {
            var access = await site.GetAuthorizedCharacterAsync(id, cancellationToken);
            if (access.Status != SiteCharacterAccessStatus.Authorized)
                return new(access.Status switch
                {
                    SiteCharacterAccessStatus.Unauthenticated =>
                        CharacterOriginParentSourceAlignmentStatus.Unauthenticated,
                    SiteCharacterAccessStatus.NotFoundOrNotOwned =>
                        CharacterOriginParentSourceAlignmentStatus.NotFoundOrNotOwned,
                    _ => CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable
                });
            if (access.Character?.CharacterId != id)
                return new(CharacterOriginParentSourceAlignmentStatus.ProjectionUnavailable);
        }

        return new(CharacterOriginParentSourceAlignmentStatus.Ready,
            draft.Revision, matches, verified);
    }

    private static string? OptionalString(JsonElement value, string field) =>
        value.TryGetProperty(field, out var found) &&
        found.ValueKind == JsonValueKind.String ? found.GetString() : null;

    /// <summary>
    /// Compare IDs only for a plain biological native-source proposal.
    /// This is not a second implementation of Rules Core reproduction policy.
    /// In particular, there is no two-parent cardinality assumption here.
    /// </summary>
    private static bool TryNativeBiologicalSources(
        JsonElement proposal, out Dictionary<string, string> sources)
    {
        sources = new(StringComparer.Ordinal);
        if (proposal.ValueKind != JsonValueKind.Object ||
            !EnumZeroOrOne(proposal, "intent", 1) ||
            !proposal.TryGetProperty("method", out var method) ||
            method.ValueKind != JsonValueKind.Object ||
            OptionalString(method, "ruleId") != "rule.creature-origination" ||
            OptionalString(method, "methodId") != "biological" ||
            !NullOrMissing(proposal, "selectedSpeciesId") ||
            !NullOrMissing(proposal, "selectedAncestryId") ||
            !EmptyOrMissing(proposal, "candidates") ||
            !EmptyOrMissing(proposal, "manifestations") ||
            !proposal.TryGetProperty("contributors", out var contributors) ||
            contributors.ValueKind != JsonValueKind.Array ||
            contributors.GetArrayLength() is < 1 or > MaxReviewedContributors ||
            proposal.EnumerateObject().Any(field => field.Name is not (
                "id" or "intent" or "method" or "contributors" or "candidates" or
                "manifestations" or "selectedSpeciesId" or "selectedAncestryId")))
            return false;

        foreach (var contributor in contributors.EnumerateArray())
        {
            if (contributor.ValueKind != JsonValueKind.Object ||
                !contributor.TryGetProperty("role", out var role) ||
                !contributor.TryGetProperty("source", out var nativeSource) ||
                role.ValueKind != JsonValueKind.Object ||
                nativeSource.ValueKind != JsonValueKind.Object ||
                !EnumZeroOrOne(role, "kind", 0) ||
                !EnumZeroOrOne(nativeSource, "kind", 0) ||
                !NullOrMissing(contributor, "manifestedProfile") ||
                !NullOrMissing(contributor, "historicalSourceIds") ||
                !NullOrMissing(nativeSource, "version") ||
                role.EnumerateObject().Any(field => field.Name != "kind") ||
                nativeSource.EnumerateObject().Any(field => field.Name is not (
                    "kind" or "id" or "version")) ||
                contributor.EnumerateObject().Any(field => field.Name is not (
                    "id" or "role" or "source" or "manifestedProfile" or "historicalSourceIds")))
                return false;

            var id = OptionalString(contributor, "id");
            var sourceId = OptionalString(nativeSource, "id");
            if (id is not { Length: > 0 and <= 180 } ||
                !ValidSourceId(sourceId) ||
                !sources.TryAdd(id, sourceId!))
                return false;
        }
        return true;
    }

    private static bool ValidSourceId(string? id)
    {
        if (id is not { Length: > 8 and <= 180 } ||
            !(id.StartsWith("species.", StringComparison.Ordinal) ||
              id.StartsWith("ancestry.", StringComparison.Ordinal)) ||
            id[^1] is '.' or '-' ||
            id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            return false;
        return true;
    }

    private static bool EnumZeroOrOne(JsonElement parent, string field, int expected) =>
        parent.TryGetProperty(field, out var kind) &&
        kind.ValueKind == JsonValueKind.Number &&
        kind.TryGetInt32(out var integer) && integer == expected;

    private static bool NullOrMissing(JsonElement parent, string field) =>
        !parent.TryGetProperty(field, out var found) ||
        found.ValueKind == JsonValueKind.Null;

    private static bool EmptyOrMissing(JsonElement parent, string field) =>
        !parent.TryGetProperty(field, out var found) ||
        found.ValueKind == JsonValueKind.Null ||
        found.ValueKind == JsonValueKind.Array && found.GetArrayLength() == 0;
}
