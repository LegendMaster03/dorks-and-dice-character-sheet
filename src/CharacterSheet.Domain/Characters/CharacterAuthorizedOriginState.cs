using System.Text;
using System.Text.Json;

namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Server-verified Origin projection. Unlike a CharacterOriginDraftState, this
/// row may be consumed as authorized Origin state, but it does not by itself
/// authenticate Class, Background, Upbringing, inventory or supernatural grants.
/// Only the trusted application evaluator may create or replace this record.
/// </summary>
public sealed class CharacterAuthorizedOriginState
{
    public const int MaxProfileBytes = 1_048_576;
    private CharacterAuthorizedOriginState() { }

    public CharacterAuthorizedOriginState(Guid characterId, string rulesetId,
        string fingerprint, string profileJson, long? sourceDraftRevision,
        DateTimeOffset recordedAt, string? evaluatedProjectionJson = null)
    {
        if (characterId == Guid.Empty)
            throw new ArgumentException("Site Character ID is required.", nameof(characterId));
        CharacterId = characterId;
        RulesetId = CharacterOriginDraftState.NormalizeRulesetId(rulesetId);
        Fingerprint = CharacterOriginDraftState.NormalizeFingerprint(fingerprint)
            ?? throw new ArgumentException("A native catalog fingerprint is required.", nameof(fingerprint));
        ProfileJson = ValidateProfileJson(profileJson);
        EvaluatedProjectionJson = ValidateProjectionJson(evaluatedProjectionJson);
        if (sourceDraftRevision is < 1)
            throw new ArgumentOutOfRangeException(nameof(sourceDraftRevision));
        SourceDraftRevision = sourceDraftRevision;
        Revision = 1;
        CreatedAt = recordedAt;
        UpdatedAt = recordedAt;
    }

    public Guid CharacterId { get; private set; }
    public string RulesetId { get; private set; } = string.Empty;
    public string Fingerprint { get; private set; } = string.Empty;
    public string ProfileJson { get; private set; } = string.Empty;
    /// <summary>
    /// The original Rules Core source-attributed projection at this revision.
    /// It is only usable while the current native fingerprint still matches;
    /// a stored projection by itself is not an effective entitlement.
    /// </summary>
    public string? EvaluatedProjectionJson { get; private set; }
    public long Revision { get; private set; }
    public long? SourceDraftRevision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static string? ValidateProjectionJson(string? json)
    {
        if (json is null) return null;
        if (string.IsNullOrWhiteSpace(json) ||
            Encoding.UTF8.GetByteCount(json) > 131_072)
            throw new ArgumentException("Invalid evaluated Origin projection.", nameof(json));
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("An evaluated projection must be a JSON object.", nameof(json));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Invalid evaluated Origin projection JSON.", nameof(json), exception);
        }
        return json;
    }

    public static string ValidateProfileJson(string profileJson)
    {
        if (string.IsNullOrWhiteSpace(profileJson) ||
            Encoding.UTF8.GetByteCount(profileJson) > MaxProfileBytes)
            throw new ArgumentException("The evaluated Origin profile exceeds supported limits.", nameof(profileJson));
        try
        {
            using var parsed = JsonDocument.Parse(profileJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("An evaluated Origin profile must be a JSON object.", nameof(profileJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Invalid evaluated Origin JSON.", nameof(profileJson), exception);
        }
        return profileJson;
    }
}

/// <summary>
/// Immutable, character-scoped receipt for one successfully committed completed
/// Origin event. This is provenance, not an independent source of rules.
/// </summary>
public sealed class CharacterOriginEventReceipt
{
    private CharacterOriginEventReceipt() { }

    public CharacterOriginEventReceipt(Guid characterId, string eventId,
        long revision, string methodRuleId, string methodId,
        string authorizationKind, string authorizationEvidenceId,
        DateTimeOffset recordedAt, Guid? approvalActorUserId = null,
        Guid? approvalCampaignId = null, string? approvedDecisionsJson = null)
    {
        if (characterId == Guid.Empty || revision < 1)
            throw new ArgumentException("A valid character and committed revision are required.");
        CharacterId = characterId;
        EventId = Required(eventId, 240, nameof(eventId));
        CommittedRevision = revision;
        MethodRuleId = Required(methodRuleId, 180, nameof(methodRuleId));
        MethodId = Required(methodId, 180, nameof(methodId));
        AuthorizationKind = Required(authorizationKind, 32, nameof(authorizationKind));
        AuthorizationEvidenceId = Required(authorizationEvidenceId, 320,
            nameof(authorizationEvidenceId));
        if (approvalActorUserId.HasValue != approvalCampaignId.HasValue ||
            approvalActorUserId.HasValue != (approvedDecisionsJson is not null) ||
            approvalActorUserId == Guid.Empty || approvalCampaignId == Guid.Empty)
            throw new ArgumentException("DM approval audit requires a complete actor, campaign and ruling set.");
        if (approvedDecisionsJson is not null)
        {
            if (Encoding.UTF8.GetByteCount(approvedDecisionsJson) > 131_072)
                throw new ArgumentException("DM ruling audit exceeds the maximum size.");
            try
            {
                using var rulings = JsonDocument.Parse(approvedDecisionsJson);
                if (rulings.RootElement.ValueKind != JsonValueKind.Array ||
                    rulings.RootElement.GetArrayLength() == 0)
                    throw new ArgumentException("DM ruling audit must contain decisions.");
            }
            catch (JsonException error)
            {
                throw new ArgumentException("Invalid DM ruling audit JSON.", nameof(approvedDecisionsJson), error);
            }
        }
        ApprovalActorUserId = approvalActorUserId;
        ApprovalCampaignId = approvalCampaignId;
        ApprovedDecisionsJson = approvedDecisionsJson;
        CreatedAt = recordedAt;
    }

    public Guid CharacterId { get; private set; }
    public string EventId { get; private set; } = string.Empty;
    public long CommittedRevision { get; private set; }
    public string MethodRuleId { get; private set; } = string.Empty;
    public string MethodId { get; private set; } = string.Empty;
    public string AuthorizationKind { get; private set; } = string.Empty;
    public string AuthorizationEvidenceId { get; private set; } = string.Empty;
    /// <summary>Exact immutable DM approval audit; null for native-only events.</summary>
    public Guid? ApprovalActorUserId { get; private set; }
    public Guid? ApprovalCampaignId { get; private set; }
    public string? ApprovedDecisionsJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private static string Required(string value, int maximum, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maximum)
            throw new ArgumentException("An exact authorization identity is required.", parameter);
        return value.Trim();
    }
}
