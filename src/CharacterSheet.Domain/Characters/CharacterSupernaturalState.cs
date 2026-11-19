using System.Text;
using System.Text.Json;

namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Character-owned, versioned persistence of verified supernatural state. The
/// primary character, Origin, Class history, and routine state are not copied.
/// JSON is a storage contract, never evidence that any grant was earned.
/// </summary>
public sealed class CharacterSupernaturalState
{
    public const int CurrentContractVersion = 1;
    public const int MaxDocumentBytes = 1_048_576;

    private CharacterSupernaturalState() { }

    public CharacterSupernaturalState(Guid characterId, string rulesetId, string fingerprint,
        string alternateSheetsJson, string persistentInfluencesJson, string ledgerJson,
        long ledgerRevision, string dependencyFingerprint, DateTimeOffset at)
    {
        if (characterId == Guid.Empty) throw new ArgumentException("Character ID is required.");
        CharacterId = characterId;
        SetContents(rulesetId, fingerprint, alternateSheetsJson,
            persistentInfluencesJson, ledgerJson, ledgerRevision, dependencyFingerprint);
        ContractVersion = CurrentContractVersion;
        Revision = 1;
        CreatedAt = at;
        UpdatedAt = at;
    }

    public Guid CharacterId { get; private set; }
    public int ContractVersion { get; private set; }
    public string RulesetId { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public string AlternateSheetsJson { get; private set; } = "[]";
    public string PersistentInfluencesJson { get; private set; } = "[]";
    public string LedgerJson { get; private set; } = "{}";
    public long LedgerRevision { get; private set; }
    public string DependencyFingerprint { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void ApplyVerified(string rulesetId, string fingerprint,
        string alternateSheetsJson, string persistentInfluencesJson,
        string ledgerJson, long ledgerRevision, string dependencyFingerprint,
        long expectedRevision, DateTimeOffset at)
    {
        if (Revision != expectedRevision || expectedRevision == long.MaxValue)
            throw new InvalidOperationException("Stale supernatural state revision.");
        SetContents(rulesetId, fingerprint, alternateSheetsJson,
            persistentInfluencesJson, ledgerJson, ledgerRevision, dependencyFingerprint);
        Revision++;
        UpdatedAt = at;
    }

    private void SetContents(string rulesetId, string fingerprint,
        string alternateSheetsJson, string persistentInfluencesJson,
        string ledgerJson, long ledgerRevision, string dependencyFingerprint)
    {
        if (string.IsNullOrWhiteSpace(rulesetId) || rulesetId.Length > 160 ||
            string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length > 200 ||
            string.IsNullOrWhiteSpace(dependencyFingerprint) ||
            dependencyFingerprint.Length != 64 ||
            !dependencyFingerprint.All(Uri.IsHexDigit) || ledgerRevision < 0)
            throw new ArgumentException("Invalid ruleset, revision or verified input fingerprint.");
        RulesetId = rulesetId;
        Fingerprint = fingerprint;
        AlternateSheetsJson = CheckJson(alternateSheetsJson, JsonValueKind.Array, "alternateSheets");
        PersistentInfluencesJson = CheckJson(persistentInfluencesJson, JsonValueKind.Array, "influences");
        LedgerJson = CheckJson(ledgerJson, JsonValueKind.Object, "manifestationLedger");
        LedgerRevision = ledgerRevision;
        DependencyFingerprint = dependencyFingerprint.ToLowerInvariant();
    }

    public static string CheckJson(string json, JsonValueKind kind, string field)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
            throw new ArgumentException($"Invalid or oversized {field}.");
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != kind)
                throw new ArgumentException($"{field} must be a JSON {kind}.");
            return json;
        }
        catch (JsonException error)
        {
            throw new ArgumentException($"Malformed {field} JSON.", error);
        }
    }
}

/// <summary>
/// Immutable database-enforced event identity. This receipt is not a source of
/// acquisition authority; authoritative adjudication must precede the write.
/// </summary>
public sealed class CharacterSupernaturalEventReceipt
{
    private CharacterSupernaturalEventReceipt() { }

    public CharacterSupernaturalEventReceipt(Guid characterId, string eventId,
        long committedRevision, long ledgerRevision, string kind,
        string authorizationEvidenceId, DateTimeOffset createdAt,
        string? transitionRequestJson = null,
        string? expressionChangeJson = null)
    {
        if (characterId == Guid.Empty || committedRevision < 1 || ledgerRevision < 0 ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 160 ||
            string.IsNullOrWhiteSpace(kind) || kind.Length > 32 ||
            string.IsNullOrWhiteSpace(authorizationEvidenceId) ||
            authorizationEvidenceId.Length > 320)
            throw new ArgumentException("Incomplete supernatural event receipt.");
        CharacterId = characterId;
        EventId = eventId;
        CommittedRevision = committedRevision;
        LedgerRevision = ledgerRevision;
        Kind = kind;
        AuthorizationEvidenceId = authorizationEvidenceId;
        TransitionRequestJson = transitionRequestJson is null ? null :
            CharacterSupernaturalState.CheckJson(transitionRequestJson,
                JsonValueKind.Object, "transitionRequest");
        ExpressionChangeJson = expressionChangeJson is null ? null :
            CharacterSupernaturalState.CheckJson(expressionChangeJson,
                JsonValueKind.Object, "expressionChange");
        CreatedAt = createdAt;
    }

    public Guid CharacterId { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public long CommittedRevision { get; private set; }
    public long LedgerRevision { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string AuthorizationEvidenceId { get; private set; } = string.Empty;

    /// <summary>
    /// Exact verified transition request, including ordered steps, per-step
    /// authority evidence and optional interaction replacements. Null for
    /// acquisition and historical receipts predating the additive audit
    /// migration. Persistence is not independent proof of authorization.
    /// </summary>
    public string? TransitionRequestJson { get; private set; }

    /// <summary>
    /// Immutable expression-change evidence. A receipt records the exact
    /// before/after expression but never authenticates its own source.
    /// </summary>
    public string? ExpressionChangeJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
