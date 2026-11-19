using CharacterSheet.Application.Characters;

namespace CharacterSheet.Web;

/// <summary>
/// Resolve the actor only from the redeemed Site tool-host context, never
/// from submitted character or receipt data. The owner check is a separate
/// mandatory Site projection verified by the application coordinator.
/// </summary>
public sealed class SiteInnateSpellActorSource(
    IHttpContextAccessor accessor) : ICharacterInnateSpellActorSource
{
    public Guid? GetActorUserId()
    {
        var context = accessor.HttpContext;
        if (context is null) return null;
        var site = HostedToolAuthenticationMiddleware.GetAuthenticationContext(context);
        if (site?.ToolSlug != "character-sheet" ||
            site.SiteMode != "dorks-and-dice" ||
            !Guid.TryParse(site.User?.Id, out var actor) ||
            actor == Guid.Empty)
            return null;
        return actor;
    }
}

/// <summary>
/// The production Rules Core native ledger verifier is not implemented.
/// Explicitly fail closed rather than accepting a browser state snapshot,
/// inventing a proficiency/casting rule, or treating a saved Origin as enough
/// to authorize free-cast expenditure. Replace only when the authenticated
/// Rules Core calculation and event-policy integration have been implemented.
/// </summary>
public sealed class UnavailableInnateSpellLedgerEvaluator :
    ITrustedInnateSpellLedgerEvaluator
{
    public Task<TrustedInnateSpellLedgerCandidate?> CalculateAsync(
        TrustedInnateSpellLedgerEvaluation input,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TrustedInnateSpellLedgerCandidate?>(null);
    }
}
