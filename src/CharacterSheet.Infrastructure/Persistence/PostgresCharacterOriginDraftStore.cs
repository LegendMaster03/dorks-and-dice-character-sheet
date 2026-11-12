using CharacterSheet.Application.Persistence;
using CharacterSheet.Domain.Characters;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CharacterSheet.Infrastructure.Persistence;

/// <summary>
/// Compare-and-swap persistence for non-active Origin drafts. Database uniqueness
/// and conditional updates prevent racing requests from silently losing work.
/// </summary>
public sealed class PostgresCharacterOriginDraftStore(CharacterSheetDbContext dbContext)
    : ICharacterOriginDraftStore
{
    public Task<CharacterOriginDraftState?> GetAsync(Guid characterId,
        CancellationToken cancellationToken = default) =>
        dbContext.CharacterOriginDrafts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CharacterId == characterId, cancellationToken);

    public async Task<CharacterOriginDraftState?> SaveAsync(Guid characterId,
        string rulesetId, string? fingerprint, string proposalJson,
        long expectedRevision, DateTimeOffset changedAt,
        CancellationToken cancellationToken = default)
    {
        if (expectedRevision < 0 || expectedRevision == long.MaxValue)
            return null;
        var normalizedRuleset = CharacterOriginDraftState.NormalizeRulesetId(rulesetId);
        var normalizedFingerprint = CharacterOriginDraftState.NormalizeFingerprint(fingerprint);
        var validJson = CharacterOriginDraftState.ValidateProposalJson(proposalJson);

        if (expectedRevision == 0)
        {
            var draft = new CharacterOriginDraftState(characterId, normalizedRuleset,
                normalizedFingerprint, validJson, changedAt);
            dbContext.CharacterOriginDrafts.Add(draft);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return draft;
            }
            catch (DbUpdateException error) when (
                error.InnerException is PostgresException postgres &&
                postgres.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // Another writer created the first draft. Do not overwrite it.
                dbContext.Entry(draft).State = EntityState.Detached;
                return null;
            }
        }

        var applied = await dbContext.CharacterOriginDrafts
            .Where(item => item.CharacterId == characterId &&
                item.Revision == expectedRevision)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.RulesetId, normalizedRuleset)
                .SetProperty(item => item.Fingerprint, normalizedFingerprint)
                .SetProperty(item => item.ProposalJson, validJson)
                .SetProperty(item => item.Revision, expectedRevision + 1)
                .SetProperty(item => item.UpdatedAt, changedAt),
                cancellationToken);
        return applied == 1 ? await GetAsync(characterId, cancellationToken) : null;
    }
}
