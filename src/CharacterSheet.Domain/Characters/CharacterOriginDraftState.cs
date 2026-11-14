using System.Text;
using System.Text.Json;

namespace CharacterSheet.Domain.Characters;

/// <summary>
/// Client-proposed Origin decisions only. A persisted draft is never an
/// authenticated Origin, earned grant record, or authorization to play it.
/// </summary>
public sealed class CharacterOriginDraftState
{
    public const int MaxRulesetIdLength = 160;
    public const int MaxFingerprintLength = 200;
    public const int MaxProposalBytes = 262_144;

    private CharacterOriginDraftState() { }

    public CharacterOriginDraftState(Guid characterId, string rulesetId,
        string? fingerprint, string proposalJson, DateTimeOffset createdAt)
    {
        if (characterId == Guid.Empty)
            throw new ArgumentException("Character ID must be Site-issued.", nameof(characterId));
        CharacterId = characterId;
        RulesetId = NormalizeRulesetId(rulesetId);
        Fingerprint = NormalizeFingerprint(fingerprint);
        ProposalJson = ValidateProposalJson(proposalJson);
        Revision = 1;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid CharacterId { get; private set; }
    public string RulesetId { get; private set; } = string.Empty;
    public string? Fingerprint { get; private set; }
    public string ProposalJson { get; private set; } = string.Empty;
    public long Revision { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeRulesetId(string rulesetId)
    {
        if (string.IsNullOrWhiteSpace(rulesetId) ||
            rulesetId.Trim().Length > MaxRulesetIdLength)
            throw new ArgumentException("A valid Ruleset ID is required.", nameof(rulesetId));
        return rulesetId.Trim();
    }

    public static string? NormalizeFingerprint(string? fingerprint)
    {
        if (fingerprint is null) return null;
        var value = fingerprint.Trim();
        if (value.Length == 0 || value.Length > MaxFingerprintLength)
            throw new ArgumentException("Invalid content fingerprint.", nameof(fingerprint));
        return value;
    }

    public static string ValidateProposalJson(string proposalJson)
    {
        if (string.IsNullOrWhiteSpace(proposalJson) ||
            Encoding.UTF8.GetByteCount(proposalJson) > MaxProposalBytes)
            throw new ArgumentException("The Origin draft exceeds supported limits.", nameof(proposalJson));
        try
        {
            using var parsed = JsonDocument.Parse(proposalJson);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("The Origin draft must be a JSON object.", nameof(proposalJson));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The Origin draft contains invalid JSON.",
                nameof(proposalJson), exception);
        }
        return proposalJson;
    }
}
