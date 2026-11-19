using System.Text;
using System.Text.Json;

namespace CharacterSheet.Domain.Characters;

/// <summary>
/// A character-owned native free-cast ledger, distinct from the authoritative
/// Origin and the untrusted draft. Only an application service that has
/// revalidated the Rules Core result may prepare a write to this table.
/// </summary>
public sealed class CharacterInnateSpellLedgerState
{
    public const int MaxLedgerBytes = 131_072;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private CharacterInnateSpellLedgerState() { }

    public CharacterInnateSpellLedgerState(Guid characterId, string rulesetId,
        string fingerprint, string originProfileId, string proposalId,
        long originRevision, long ledgerRevision, string ledgerJson,
        DateTimeOffset recordedAt)
    {
        if (characterId == Guid.Empty || originRevision < 1 || ledgerRevision < 0)
            throw new ArgumentException("A committed Origin, character, and ledger revision are required.");
        CharacterId = characterId;
        RulesetId = CharacterOriginDraftState.NormalizeRulesetId(rulesetId);
        Fingerprint = CharacterOriginDraftState.NormalizeFingerprint(fingerprint)
            ?? throw new ArgumentException("The native catalog fingerprint is required.");
        OriginProfileId = Required(originProfileId, 240, nameof(originProfileId));
        ProposalId = Required(proposalId, 240, nameof(proposalId));
        OriginRevision = originRevision;
        LedgerRevision = ledgerRevision;
        LedgerJson = ValidateLedgerJson(ledgerJson, RulesetId, Fingerprint,
            OriginProfileId, ProposalId, LedgerRevision);
        CreatedAt = recordedAt;
        UpdatedAt = recordedAt;
    }

    public Guid CharacterId { get; private set; }
    public string RulesetId { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public string OriginProfileId { get; private set; } = string.Empty;
    public string ProposalId { get; private set; } = string.Empty;
    public long OriginRevision { get; private set; }
    public long LedgerRevision { get; private set; }
    public string LedgerJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Structural validation only, not permission to cast or entitlement
    /// verification. The application service must obtain the authoritative
    /// native result before invoking the persistence store.
    /// </summary>
    public static string ValidateLedgerJson(string ledgerJson, string rulesetId,
        string fingerprint, string originProfileId, string proposalId,
        long ledgerRevision)
    {
        if (string.IsNullOrWhiteSpace(ledgerJson) ||
            Encoding.UTF8.GetByteCount(ledgerJson) > MaxLedgerBytes)
            throw new ArgumentException("Ledger JSON is missing or exceeds the maximum size.");

        LedgerWire? wire;
        try
        {
            wire = JsonSerializer.Deserialize<LedgerWire>(ledgerJson, JsonOptions);
        }
        catch (JsonException error)
        {
            throw new ArgumentException("Invalid native ledger JSON.", nameof(ledgerJson), error);
        }
        if (wire is null ||
            wire.RulesetId != rulesetId ||
            wire.CatalogFingerprint != fingerprint ||
            wire.OriginProfileId != originProfileId ||
            wire.ProposalId != proposalId ||
            wire.Revision != ledgerRevision ||
            wire.CharacterLevel is <= 0 or > 1000 ||
            string.IsNullOrWhiteSpace(wire.ProjectionFingerprint) ||
            wire.ProjectionFingerprint.Length != 64 ||
            !wire.ProjectionFingerprint.All(Uri.IsHexDigit) ||
            wire.RemainingFreeCasts is null ||
            wire.RemainingFreeCasts.Count > 256 ||
            wire.RemainingFreeCasts.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Length > 500 ||
                pair.Value is < 0 or > 1_000_000))
            throw new ArgumentException("Ledger metadata or resource values are inconsistent.", nameof(ledgerJson));
        return ledgerJson;
    }

    public static string ReadProfileId(string profileJson)
    {
        try
        {
            using var json = JsonDocument.Parse(profileJson);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("id", out var id) &&
                !json.RootElement.TryGetProperty("Id", out id) ||
                id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()))
                throw new ArgumentException("The committed Origin profile lacks its native identity.");
            return id.GetString()!;
        }
        catch (JsonException error)
        {
            throw new ArgumentException("Invalid committed Origin profile JSON.", nameof(profileJson), error);
        }
    }

    private sealed record LedgerWire(
        string? RulesetId, string? CatalogFingerprint,
        string? OriginProfileId, string? ProposalId, int CharacterLevel,
        string? ProjectionFingerprint, long Revision,
        Dictionary<string, int>? RemainingFreeCasts);

    private static string Required(string text, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximum)
            throw new ArgumentException("A native Origin identity is required.", name);
        return text;
    }
}

/// <summary>
/// Immutable audit receipt. Event ID and ledger revision each have a unique
/// character-scoped database constraint so a replay cannot publish a new state.
/// </summary>
public sealed class CharacterInnateSpellLedgerReceipt
{
    private CharacterInnateSpellLedgerReceipt() { }

    public CharacterInnateSpellLedgerReceipt(Guid characterId, string eventId,
        long committedRevision, long originRevision, Guid actorUserId,
        string authorizationEvidenceId, string operation,
        DateTimeOffset recordedAt, string? appliedDecisionIdsJson = null)
    {
        if (characterId == Guid.Empty || committedRevision < 0 ||
            originRevision < 1 || actorUserId == Guid.Empty)
            throw new ArgumentException("A verified character, actor, and committed revision are required.");
        CharacterId = characterId;
        EventId = Required(eventId, 240, nameof(eventId));
        CommittedRevision = committedRevision;
        OriginRevision = originRevision;
        ActorUserId = actorUserId;
        AuthorizationEvidenceId = Required(authorizationEvidenceId, 320,
            nameof(authorizationEvidenceId));
        Operation = Required(operation, 64, nameof(operation));
        if (appliedDecisionIdsJson is not null)
        {
            if (Encoding.UTF8.GetByteCount(appliedDecisionIdsJson) > 32_768)
                throw new ArgumentException("Capacity decision evidence is too large.");
            try
            {
                using var doc = JsonDocument.Parse(appliedDecisionIdsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                    doc.RootElement.GetArrayLength() == 0 ||
                    doc.RootElement.EnumerateArray().Any(x =>
                        x.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(x.GetString()) ||
                        x.GetString()!.Length > 240))
                    throw new ArgumentException("Invalid capacity decision ID array.");
            }
            catch (JsonException error)
            {
                throw new ArgumentException("Invalid capacity decision JSON.", nameof(appliedDecisionIdsJson), error);
            }
        }
        AppliedDecisionIdsJson = appliedDecisionIdsJson;
        RecordedAt = recordedAt;
    }

    public Guid CharacterId { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public long CommittedRevision { get; private set; }
    public long OriginRevision { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string AuthorizationEvidenceId { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public string? AppliedDecisionIdsJson { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    private static string Required(string text, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > max)
            throw new ArgumentException("Receipt provenance is required.", field);
        return text.Trim();
    }
}
